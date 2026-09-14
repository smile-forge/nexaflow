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
using System.Reflection;

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

    /// <summary>How often the watchdog looks at what is running.</summary>
    private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(15);

    /// <summary>A command running longer than this gets a line in the log each time the watchdog looks, so a
    /// long one and a stuck one can be told apart afterwards — the log used to show a start and then nothing.</summary>
    private static readonly TimeSpan Noteworthy = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a command may go on running after its caller has gone before the process gives up on it.
    /// <para>
    /// Every long-running verb stops of its own accord once its caller leaves (see
    /// <see cref="RequestScope.Cancellation"/>), so one still running this long afterwards is not slow, it is
    /// stuck — and it holds its tree's lock, so every later command on that tree would wait behind it forever.
    /// A thread cannot be stopped from outside, so the process is: it answers everything queued, writes what it
    /// can, and exits, and the next command starts a fresh one. A command whose caller is still waiting is never
    /// touched, however long it takes — that caller is the one who gets to decide.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan WedgeGrace = TimeSpan.FromSeconds(60);

    /// <summary>One lock per working tree — see <see cref="Execute"/>.</summary>
    internal static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What has been taken and where it has got to — the only thing the status path reads, which is
    /// why it can be answered while the work it describes is holding a lock.</summary>
    internal static readonly WorkLedger Ledger = new();

    /// <summary>The commands this process has taken and not finished with, for the watchdog.</summary>
    private static readonly ConcurrentDictionary<string, Taken> Active = new(StringComparer.Ordinal);

    /// <summary>Cancelled when the process is going down because a command wedged, so that everything still
    /// waiting for a turn is answered rather than left waiting for a lock that will not come back.</summary>
    private static readonly CancellationTokenSource Restarting = new();
    private static string? _restartReason;
    private static long _lastActivityTicks = DateTime.UtcNow.Ticks;
    private static int _inFlight;

    /// <summary>A command in progress: when it got its turn, and whether — and since when — its caller has gone.</summary>
    private sealed class Taken(string ticket, string command)
    {
        public readonly string Ticket  = ticket;
        public readonly string Command = command;
        public long Started;
        public long Left;

        /// <summary>Set once the answer is being written, after which a closed connection is the ordinary end of
        /// the exchange rather than the caller leaving.</summary>
        public int Answering;
    }

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
        // and one process that finds the door already answered and leaves without a word. A process that died
        // holding it leaves it abandoned, which is ours to take rather than a reason to refuse to start.
        using var only = new Mutex(initiallyOwned: false, "Local\\" + pipe, out _);
        bool owned;
        try { owned = only.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return 0;

        try
        {
            DaemonLog.Open(pipe);
            DaemonLog.Say("-", "daemon", $"up for {root}");

            // One resident per tree: the processes older builds started for it are asked to finish and go.
            Succession.Claim(root, pipe);

            // The one line that tells a crash from a hang afterwards. Without it both look like a start and then
            // nothing, and the only way to tell them apart was to guess.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                DaemonLog.Say("-", "crash", (e.ExceptionObject?.ToString() ?? "unknown").ReplaceLineEndings(" | "));

            RequestScope.Install();
            using var host = new InitiativesHost(root);

            // Read once, up front, because that is what starts the watcher: the tree is small, and without this
            // nothing here would ever notice it change — the verbs each load their own copy and tell no one.
            _ = host.Tree;
            Serve(pipe, host);
            return 0;
        }
        finally
        {
            Succession.Leave(root, pipe);
            try { only.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    /// <summary>The accept loop, until idle, told to stop, or restarting past a wedged command.</summary>
    internal static void Serve(string pipe, InitiativesHost host)
    {
        using var stopping = new CancellationTokenSource();
        using var ending   = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, Restarting.Token);
        using var watchdog = new Timer(_ => Watch(), null, WatchEvery, WatchEvery);

        while (!ending.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipe, PipeDirection.InOut,
                                                   NamedPipeServerStream.MaxAllowedServerInstances,
                                                   PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Waiting with a deadline rather than forever, so idling out is the same code path as being told
            // to stop rather than a timer racing the connection handlers for the process.
            bool connected;
            try { connected = server.WaitForConnectionAsync(ending.Token).Wait(NextWait()); }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException) { connected = false; }

            if (!connected)
            {
                server.Dispose();
                if (ending.IsCancellationRequested) break;

                // Nothing knocked, but a long command may still be running: idle means idle.
                if (Volatile.Read(ref _inFlight) > 0) continue;
                break;
            }

            Interlocked.Increment(ref _inFlight);
            _ = Task.Run(() => Handle(server, host, stopping));
        }

        if (Restarting.IsCancellationRequested) Restart(host);

        // Let whatever is in flight finish before the state it changed goes with the process.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref _inFlight) > 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);
        host.Flush();
        DaemonLog.Say("-", "daemon", "idle, stopping");
        DaemonLog.Close();
    }

    /// <summary>
    /// How long the accept loop may wait. Until the process has been idle long enough to end — or, once it has
    /// but something is still running, one watchdog interval: waiting "the time left", which was zero, spun the
    /// loop creating and discarding pipes as fast as it could for as long as that command ran.
    /// </summary>
    private static TimeSpan NextWait()
    {
        var idle = DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        var left = IdleTimeout - idle;
        if (left > TimeSpan.Zero) return left;
        return Volatile.Read(ref _inFlight) > 0 ? WatchEvery : TimeSpan.Zero;
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

            var taken = Active[work.Ticket] = new Taken(work.Ticket, WorkLedger.Describe(request.Args));
            using var gone = new CancellationTokenSource();
            WatchForHangUp(server, gone, taken);

            // Null when the caller left before its turn came: there is no one to answer.
            if (Execute(request, host, work, taken, gone.Token) is not { } response) return;

            Volatile.Write(ref taken.Answering, 1);
            DaemonProtocol.Write(server, response);
            Settle(server);
        }
        catch (IOException) { /* the client hung up mid-exchange; no other caller is affected */ }
        catch (ObjectDisposedException) { }
        finally
        {
            if (work is not null)
            {
                Active.TryRemove(work.Ticket, out _);
                Ledger.Done(work);
                DaemonLog.Say(work.Ticket, "done", $"{Ledger.StatusOf(work.Ticket).RanSeconds:F2}s");
            }

            try { if (server.IsConnected) server.Disconnect(); } catch (IOException) { }
            server.Dispose();
            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>
    /// Notices the caller going away. After the request a client sends nothing more on a command connection, so
    /// a read that completes at all means the other end has closed it — end of stream, or a broken pipe.
    /// <para>
    /// Without this, work nobody would ever read the answer to still ran. A client killed by a harness timeout
    /// left its command queued; the retry queued behind it; and a ninety-second build retried twice was four and a
    /// half minutes of every command on the tree looking hung. Now a queued command is dropped before it starts
    /// and a running one is told to stop.
    /// </para>
    /// </summary>
    private static void WatchForHangUp(NamedPipeServerStream server, CancellationTokenSource gone, Taken taken)
    {
        var one = new byte[1];
        Task<int> read;
        try { read = server.ReadAsync(one).AsTask(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { return; }

        _ = read.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result > 0) return;   // a byte nobody sends; not a hang-up
            if (Volatile.Read(ref taken.Answering) == 1) return;     // the exchange ending, not the caller leaving

            Volatile.Write(ref taken.Left, DateTime.UtcNow.Ticks);
            DaemonLog.Say(taken.Ticket, "left", "its caller hung up before the answer - stopping it");
            try { gone.Cancel(); } catch (ObjectDisposedException) { /* the answer went out first */ }
        }, TaskScheduler.Default);
    }

    /// <summary>Gets the last frame all the way there before the connection is taken down under it.</summary>
    private static void Settle(NamedPipeServerStream server)
    {
        server.Flush();
        if (OperatingSystem.IsWindows()) server.WaitForPipeDrain();
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
    /// <returns>Null when the caller left while the command was waiting for its turn.</returns>
    private static DaemonResponse? Execute(DaemonRequest request, InitiativesHost host, WorkItem work, Taken taken,
                                           CancellationToken gone)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Serialised per working tree, because that is where the shared mutable state is: one graph, which
        // one command may read while another edits. Different trees hold different graphs and proceed
        // together — and two callers on the same tree waiting for each other is the consistency, not a cost.
        var gate = Locks.GetOrAdd(request.CodeRoot ?? "", _ => new SemaphoreSlim(1, 1));
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(gone, Restarting.Token))
        {
            try { gate.Wait(waiting.Token); }
            catch (OperationCanceledException)
            {
                if (_restartReason is { } reason)
                {
                    DaemonLog.Say(work.Ticket, "refused", "the process is restarting");
                    return new DaemonResponse(2, "",
                        $"error: nfi's resident process is restarting, because {reason}. Run the command again - it "
                      + $"starts a fresh one.{Environment.NewLine}");
                }

                DaemonLog.Say(work.Ticket, "dropped", "its caller left while it was waiting for its turn");
                return null;
            }
        }

        // Running rather than queued from here, which is the distinction anyone asking after this needs:
        // waiting for a turn and taking a long time are different problems with different answers.
        Ledger.Running(work);
        Volatile.Write(ref taken.Started, DateTime.UtcNow.Ticks);
        DaemonLog.Say(work.Ticket, "start", $"waited {Ledger.StatusOf(work.Ticket).WaitedSeconds:F2}s");

        try
        {
            using var scope = RequestScope.Begin(stdout, stderr, new RequestContext(request.WorkingDirectory)
            {
                Shell        = request.Shell,
                Stdin        = request.Stdin,
                Host         = host,
                Cancellation = gone,
            });

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
            gate.Release();

            // Whatever the command changed is on disk before the next caller can ask for it, so an abrupt
            // end costs a load rather than a rebuild. This tree's alone: flushing every tree took every tree's
            // lock, so one tree's answer waited behind whatever another tree was in the middle of.
            try { host.Flush(request.CodeRoot); } catch (IOException) { }
        }
    }

    /// <summary>
    /// What the watchdog does every <see cref="WatchEvery"/>: note what has been running a while, and give up on
    /// a command that has outlived its caller by <see cref="WedgeGrace"/>.
    /// </summary>
    private static void Watch()
    {
        try
        {
            var now = DateTime.UtcNow.Ticks;
            foreach (var taken in Active.Values)
            {
                var started = Volatile.Read(ref taken.Started);
                if (started == 0) continue;                           // still waiting for its turn

                var ran  = TimeSpan.FromTicks(now - started);
                var left = Volatile.Read(ref taken.Left);
                if (ran < Noteworthy) continue;

                DaemonLog.Say(taken.Ticket, "running",
                    $"{ran.TotalSeconds:F0}s" + (left > 0 ? $", its caller left {TimeSpan.FromTicks(now - left).TotalSeconds:F0}s ago" : ""));

                if (left > 0 && now - left > WedgeGrace.Ticks
                    && Interlocked.CompareExchange(ref _restartReason,
                           $"`{taken.Command}` stopped responding ({ran.TotalSeconds:F0}s, its caller long gone)", null) is null)
                {
                    DaemonLog.Say(taken.Ticket, "wedged",
                        $"{taken.Command}: still running {TimeSpan.FromTicks(now - left).TotalSeconds:F0}s after its caller "
                      + "left - restarting the process");
                    Restarting.Cancel();
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // A watchdog that takes the process down is worse than none.
            DaemonLog.Say("-", "watchdog", e.Message);
        }
    }

    /// <summary>
    /// Ends the process past a wedged command. Whatever was waiting has been answered by now or is about to be;
    /// what can be written is written, within a bound, because flushing the wedged tree may need the very lock
    /// its command is holding; and then the process exits rather than returning, since returning would dispose
    /// the host and block on that same lock. The next command starts a fresh process.
    /// </summary>
    private static void Restart(InitiativesHost host)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && Active.Values.Any(t => Volatile.Read(ref t.Left) == 0))
            Thread.Sleep(50);

        try { Task.Run(host.Flush).Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }

        DaemonLog.Say("-", "daemon", $"restarting: {_restartReason}");
        DaemonLog.Close();
        Environment.Exit(3);
    }

    /// <summary>
    /// Where the daemon is started from. The root is carried as an argument and every path a command names is
    /// measured from the <b>caller's</b> directory (see <c>CallerPath</c>), so the process's own directory is
    /// orientation and nothing depends on it.
    /// <para>
    /// It is guarded because an argument must never be the reason a daemon cannot start. A root that named a
    /// file — which is what <c>nfi batch &lt;script&gt;</c> produced, its script being read as the root —
    /// made Windows refuse the spawn outright, and what the caller saw was a Win32Exception about a working
    /// directory in place of the verb's own account of a bad argument.
    /// </para>
    /// </summary>
    internal static string SpawnDirectory(string root) =>
        Directory.Exists(root) ? root : AppContext.BaseDirectory;

    /// <summary>
    /// The command line that starts one of these, for the client that is about to.
    /// <para>
    /// The process may not be our own executable. The installer's release gate runs the validator as
    /// <c>dotnet nfi.dll validate …</c>, and there <see cref="Environment.ProcessPath"/> is the shared host —
    /// so spawning "the way we were started" handed <c>__serve</c> back to <c>dotnet</c>, which read it as a
    /// command name and said no such thing exists. What the client then saw was a daemon that had exited
    /// immediately, which is indistinguishable from one that crashed, and the release build failed on it.
    /// When we are hosted, the assembly goes back in front of the arguments.
    /// </para>
    /// <para>
    /// stdout and stderr are redirected and the client must then <i>drain</i> them: a child whose pipe buffer
    /// fills with nobody reading blocks on its next write, and a resident process that has stopped answering
    /// because it tried to print something is a hang with no visible cause. The client keeps the tail for the
    /// one case that needs it — saying why the process died when it did.
    /// </para>
    /// </summary>
    internal static ProcessStartInfo SpawnInfo(string exe, string pipe, string root)
    {
        var self   = Assembly.GetEntryAssembly()?.Location;
        var hosted = self is { Length: > 0 }
                  && !string.Equals(Path.GetFileNameWithoutExtension(exe),
                                    Path.GetFileNameWithoutExtension(self), StringComparison.OrdinalIgnoreCase);

        // Staged either way — it is our own assemblies that a build has to overwrite, and the host it runs
        // under is the SDK's and no concern of ours.
        var info = new ProcessStartInfo(hosted ? exe : Stage(pipe, exe))
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = SpawnDirectory(root),
        };

        if (hosted) info.ArgumentList.Add(Stage(pipe, self!));

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
