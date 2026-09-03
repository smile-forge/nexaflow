using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using System.Runtime.InteropServices;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// The resident half of <c>nfi</c>: one process per product tree, holding each working tree's graph in
/// memory so the second command costs milliseconds instead of a second and a half of reading.
/// <para>
/// <b>There is no way to start this deliberately, and that is the design.</b> It is reached only by
/// <see cref="DaemonClient"/> spawning it, and it refuses to run without the nonce that spawn sets in the
/// environment. A resident process that a person — or an assistant reading the help — can start by hand is
/// a process that gets started twice, or started stale, or started against the wrong root, and then quietly
/// answers questions from state nobody meant it to have. Callers use <c>nfi</c> exactly as they always did;
/// this is not part of the interface.
/// </para>
/// <para>
/// Requests run concurrently, serialised per working tree. That is the boundary the work actually has:
/// agents run one to a worktree, so two of them are asking about different graphs and have no business
/// queueing behind each other — while two asking about the <i>same</i> graph must queue, because a command
/// that mutates it interleaving with one that reads it is how a warm process starts lying.
/// </para>
/// </summary>
internal static class DaemonServer
{
    /// <summary>The hidden first argument. Not in the usage text, not in the verb switch, and inert without
    /// <see cref="SpawnNonceVariable"/> — three locks on a door nobody should be opening.</summary>
    internal const string ModeArgument = "__serve";

    /// <summary>Set by the spawning client and checked here. Its value is not a secret and does not need to
    /// be: it is a statement that a client asked for this process, which a person typing the argument by
    /// hand has not made.</summary>
    internal const string SpawnNonceVariable = "NFI_DAEMON_SPAWN";

    /// <summary>How long the process stays up with nothing asked of it. Long enough to span the pauses in a
    /// working session, short enough that a forgotten one is not forgotten for the afternoon.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(20);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What has been taken and where it has got to — the only thing the status path reads, which is
    /// why it can be answered while the work it describes is holding a lock.</summary>
    private static readonly WorkLedger Ledger = new();
    private static long _lastActivityTicks = DateTime.UtcNow.Ticks;
    private static int _inFlight;

    /// <summary>Runs until idle. <paramref name="args"/> is the hidden mode argument, the pipe, and the root.</summary>
    internal static int Run(string[] args)
    {
        if (Environment.GetEnvironmentVariable(SpawnNonceVariable) is not { Length: > 0 })
        {
            Console.Error.WriteLine(
                "error: this is nfi's internal resident mode and is not meant to be started by hand. Just run "
              + "nfi normally — it starts and reuses one of these on its own.");
            return 2;
        }
        if (args.Length < 3) return 2;

        var pipe = args[1];
        var root = args[2];

        // One per pipe, and the pipe is per root per build: two clients racing to spawn produce one daemon
        // and one process that finds the door already answered and leaves without a word.
        using var only = new Mutex(initiallyOwned: false, "Local\\" + pipe, out _);
        if (!only.WaitOne(TimeSpan.Zero)) return 0;

        try
        {
            DaemonLog.Open(pipe);
            DaemonLog.Say("-", "daemon", $"up for {root}");

            RequestScope.Install();
            using var host = new InitiativesHost(root);

            // Read once, up front, because that is what starts the watcher: the tree is small, and without this
            // nothing here would ever notice it change — the verbs each load their own copy and tell no one.
            _ = host.Tree;
            Serve(pipe, host);
            return 0;
        }
        finally { only.ReleaseMutex(); }
    }

    private static void Serve(string pipe, InitiativesHost host)
    {
        using var stopping = new CancellationTokenSource();

        while (!stopping.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipe, PipeDirection.InOut,
                                                   NamedPipeServerStream.MaxAllowedServerInstances,
                                                   PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Waiting with a deadline rather than forever, so idling out is the same code path as being told
            // to stop rather than a timer racing the connection handlers for the process.
            var connect = server.WaitForConnectionAsync(stopping.Token);
            if (!connect.Wait(UntilIdle()))
            {
                server.Dispose();

                // Nothing knocked, but a long command may still be running: idle means idle.
                if (Volatile.Read(ref _inFlight) > 0) continue;
                break;
            }

            Interlocked.Increment(ref _inFlight);
            _ = Task.Run(() => Handle(server, host, stopping));
        }

        // Let whatever is in flight finish before the state it changed goes with the process.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref _inFlight) > 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);
        host.Flush();
        DaemonLog.Say("-", "daemon", "idle, stopping");
        DaemonLog.Close();
    }

    /// <summary>How long the accept loop may wait before the process has been idle long enough to end.</summary>
    private static TimeSpan UntilIdle()
    {
        var idle = DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        var left = IdleTimeout - idle;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>
    /// One connection, start to finish.
    /// <para>
    /// A command is acknowledged before anything that can block, so that from the client's point of view silence
    /// afterwards is always about the work and never about whether anyone is listening. That ordering is the
    /// whole point: it is what lets a caller tell a command that is taking a while from a process that has
    /// stopped answering, without either end holding a list of which commands are allowed to be slow.
    /// </para>
    /// </summary>
    private static void Handle(NamedPipeServerStream server, InitiativesHost host, CancellationTokenSource stopping)
    {
        WorkItem? work = null;
        try
        {
            if (DaemonProtocol.Read<DaemonRequest>(server) is not { } request) return;

            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

            // Answered from the ledger and from nothing else. It must never take the workspace lock: a status
            // that queued behind the work it is reporting on would take exactly as long as the thing being asked
            // about, and arrive once the answer no longer mattered.
            if (request.Ask == DaemonAsk.Working)
            {
                DaemonProtocol.Write(server, new DaemonWorkList(Ledger.All()));
                Settle(server);
                return;
            }

            if (request.Ask == DaemonAsk.Status)
            {
                DaemonProtocol.Write(server, Ledger.StatusOf(request.Ticket));
                Settle(server);
                return;
            }

            work = Ledger.Accept(request.Ticket, request.Args, request.CodeRoot ?? "");
            DaemonLog.Say(work.Ticket, "accept", WorkLedger.Describe(request.Args));
            DaemonProtocol.Write(server, new DaemonAck(work.Ticket, true, null));

            if (request.Stop) stopping.Cancel();

            DaemonProtocol.Write(server, Execute(request, host, work));
            Settle(server);
        }
        catch (IOException) { /* the client hung up mid-exchange; no other caller is affected */ }
        catch (ObjectDisposedException) { }
        finally
        {
            if (work is not null)
            {
                Ledger.Done(work);
                DaemonLog.Say(work.Ticket, "done", $"{Ledger.StatusOf(work.Ticket).RanSeconds:F2}s");
            }

            try { if (server.IsConnected) server.Disconnect(); } catch (IOException) { }
            server.Dispose();
            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>Gets the last frame all the way there before the connection is taken down under it.</summary>
    private static void Settle(NamedPipeServerStream server)
    {
        server.Flush();
        server.WaitForPipeDrain();
    }

    /// <summary>
    /// Runs one command exactly as a one-shot process would, and captures what it printed.
    /// <para>
    /// The verbs write to the console and read the current directory because they were written to be a
    /// program. Rather than rewrite thirty of them onto injected streams, both now flow with the request
    /// (see <see cref="RequestScope"/>) rather than with the process — which is what allows two callers on
    /// different working trees to be served at once without their output landing in each other's answer.
    /// </para>
    /// </summary>
    private static DaemonResponse Execute(DaemonRequest request, InitiativesHost host, WorkItem work)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Serialised per working tree, because that is where the shared mutable state is: one graph, which
        // one command may read while another edits. Different trees hold different graphs and proceed
        // together — and two callers on the same tree waiting for each other is the consistency, not a cost.
        var gate = Locks.GetOrAdd(request.CodeRoot ?? "", _ => new SemaphoreSlim(1, 1));
        gate.Wait();

        // Running rather than queued from here, which is the distinction anyone asking after this needs:
        // waiting for a turn and taking a long time are different problems with different answers.
        Ledger.Running(work);
        DaemonLog.Say(work.Ticket, "start", $"waited {Ledger.StatusOf(work.Ticket).WaitedSeconds:F2}s");

        try
        {
            using var scope = RequestScope.Begin(stdout, stderr, request.WorkingDirectory);
            Program.StandardInput = request.Stdin;
            Program.Host          = host;

            var code = request.Args.Length == 0 ? 0 : Program.Execute(request.Args);
            return new DaemonResponse(code, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            // A verb that throws must not take the daemon with it: the next caller would pay a cold start
            // for someone else's bad argument.
            return new DaemonResponse(1, stdout.ToString(),
                                      stderr + $"error: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        finally
        {
            Program.StandardInput = null;
            Program.Host          = null;
            gate.Release();

            // Whatever the command changed is on disk before the next caller can ask for it, so an abrupt
            // end costs a load rather than a rebuild.
            try { host.Flush(); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The command line that starts one of these, for the client that is about to.
    /// <para>
    /// stdout and stderr are redirected but the client must then <i>drain</i> them: a child whose pipe buffer
    /// fills with nobody reading blocks on its next write, and a resident process that has stopped answering
    /// because it tried to print something is a hang with no visible cause. The client keeps the tail for the
    /// one case that needs it — saying why the process died when it did.
    /// </para>
    /// </summary>
    internal static ProcessStartInfo SpawnInfo(string exe, string pipe, string root)
    {
        var info = new ProcessStartInfo(Stage(pipe, exe))
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = root,
        };
        info.ArgumentList.Add(ModeArgument);
        info.ArgumentList.Add(pipe);
        info.ArgumentList.Add(root);
        info.Environment[SpawnNonceVariable] = Guid.NewGuid().ToString("N");
        return info;
    }

    /// <summary>
    /// Starts the daemon without handing it the caller's console.
    /// <para>
    /// Redirecting the child's own stdout and stderr is not enough. Windows gives a new process every handle its
    /// parent has marked inheritable, so a daemon spawned from <c>nfi … | tail</c> also received a duplicate of
    /// the <i>shell's</i> pipe. It never wrote to it and never closed it — and a pipe with a living writer never
    /// reaches end-of-file, so the shell went on waiting for a command that had already finished, for as long as
    /// the daemon lived. What that looks like is a command that hangs for twenty minutes and then succeeds, with
    /// the work having been done in the first second; and nothing in the client, which has long since exited, is
    /// there to be found staring at it. Every long "hang" this design produced was this.
    /// </para>
    /// <para>
    /// So the three standard handles are made non-inheritable across the spawn and put back as they were. The
    /// child still gets pipes of its own for stdout and stderr, because those are its and closing them is its
    /// business — it is the caller's that it must not be holding.
    /// </para>
    /// </summary>
    internal static Process? StartDetached(ProcessStartInfo info)
    {
        if (!OperatingSystem.IsWindows()) return Process.Start(info);

        var handles = new[] { GetStdHandle(-10), GetStdHandle(-11), GetStdHandle(-12) };
        var restore = new uint[handles.Length];

        for (var i = 0; i < handles.Length; i++)
        {
            restore[i] = uint.MaxValue;
            if (handles[i] == IntPtr.Zero || handles[i] == new IntPtr(-1)) continue;
            if (!GetHandleInformation(handles[i], out var flags)) continue;

            if (SetHandleInformation(handles[i], Inheritable, 0)) restore[i] = flags;
        }

        try { return Process.Start(info); }
        finally
        {
            for (var i = 0; i < handles.Length; i++)
                if (restore[i] != uint.MaxValue)
                    SetHandleInformation(handles[i], Inheritable, restore[i] & Inheritable);
        }
    }

    private const uint Inheritable = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    /// <summary>
    /// Where the daemon actually runs from: a copy of the build output, never the build output itself.
    /// <para>
    /// A resident .NET process holds its assemblies open, so a daemon started out of <c>bin/</c> locks the exact
    /// files the next build has to overwrite. The failure then lands on whoever typed <c>dotnet build</c>, as a
    /// copy error naming a process they did not start and were never told existed — which is the precise
    /// opposite of a thing that happens transparently. Running from a copy costs one write of the output per
    /// build and removes the whole class of problem.
    /// </para>
    /// <para>
    /// The directory is named for the pipe, which already encodes the product root and the binary, so it is
    /// self-invalidating: a rebuild stages somewhere new rather than over the top of a daemon that is using it.
    /// Staging prunes its dead siblings on the way past, and a live daemon's copy defends itself — its files are
    /// open, the delete fails, and it is left alone.
    /// </para>
    /// </summary>
    private static string Stage(string pipe, string exe)
    {
        try
        {
            var home  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Smile", "nfi", "daemon");
            var here  = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var there = Path.Combine(home, pipe);
            var copy  = Path.Combine(there, Path.GetFileName(exe));

            Directory.CreateDirectory(home);
            foreach (var stale in Directory.EnumerateDirectories(home))
            {
                if (string.Equals(Path.GetFileName(stale), pipe, StringComparison.OrdinalIgnoreCase)) continue;
                if (InUse(Path.Combine(stale, Path.GetFileName(exe)))) continue;

                try { Directory.Delete(stale, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            // Already staged for this exact binary, since the pipe says which one it is.
            if (File.Exists(copy)) return copy;

            foreach (var file in Directory.EnumerateFiles(here, "*", SearchOption.AllDirectories))
            {
                var landing = Path.Combine(there, Path.GetRelativePath(here, file));
                Directory.CreateDirectory(Path.GetDirectoryName(landing)!);
                File.Copy(file, landing, overwrite: true);
            }

            return File.Exists(copy) ? copy : exe;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Worth having and not worth failing over: in place, the daemon still works and the only cost is
            // that a build while one is running has to be told to stop first.
            return exe;
        }
    }

    /// <summary>
    /// Whether a staged copy still has a daemon living in it.
    /// <para>
    /// Asked before deleting rather than discovered during it, because a recursive delete removes what it can
    /// and only then reports the file it could not — so a daemon of the previous build, which is exactly what
    /// is there to be pruned, would be left running on top of a directory with pieces missing, and would fail
    /// later at whichever assembly it had not happened to load yet. Trying to open the executable for writing
    /// asks the question outright and costs nothing.
    /// </para>
    /// </summary>
    private static bool InUse(string exe)
    {
        if (!File.Exists(exe)) return false;

        try
        {
            using var _ = new FileStream(exe, FileMode.Open, FileAccess.Write, FileShare.None);
            return false;
        }
        catch (IOException)               { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    // ── The ledger ──────────────────────────────────────────────────────────
    //
    // What has been taken and where it has got to. Small — one entry per command in the last few minutes — and
    // read by the status path only, which is why it can be answered while the work it describes holds a lock.
}
