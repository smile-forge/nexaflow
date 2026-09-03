using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Hosting.Ipc;

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
    }

    /// <summary>How long the accept loop may wait before the process has been idle long enough to end.</summary>
    private static TimeSpan UntilIdle()
    {
        var idle = DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        var left = IdleTimeout - idle;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    private static void Handle(NamedPipeServerStream server, InitiativesHost host, CancellationTokenSource stopping)
    {
        try
        {
            if (DaemonProtocol.Read<DaemonRequest>(server) is not { } request) return;

            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            if (request.Stop) stopping.Cancel();

            DaemonProtocol.Write(server, Execute(request, host));
            server.Flush();
            server.WaitForPipeDrain();
        }
        catch (IOException) { /* the client hung up mid-exchange; no other caller is affected */ }
        catch (ObjectDisposedException) { }
        finally
        {
            try { if (server.IsConnected) server.Disconnect(); } catch (IOException) { }
            server.Dispose();
            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _inFlight);
        }
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
    private static DaemonResponse Execute(DaemonRequest request, InitiativesHost host)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Serialised per working tree, because that is where the shared mutable state is: one graph, which
        // one command may read while another edits. Different trees hold different graphs and proceed
        // together — and two callers on the same tree waiting for each other is the consistency, not a cost.
        var gate = Locks.GetOrAdd(request.CodeRoot ?? "", _ => new SemaphoreSlim(1, 1));
        gate.Wait();

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
        var info = new ProcessStartInfo(exe)
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
}
