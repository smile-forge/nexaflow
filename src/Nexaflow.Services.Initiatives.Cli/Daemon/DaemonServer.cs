using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Hosting.Ipc;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// The resident half of <c>nfi</c>: one process per product tree, holding the graph in memory so the second
/// command costs milliseconds instead of a second and a half of reading.
/// <para>
/// <b>There is no way to start this deliberately, and that is the design.</b> It is reached only by
/// <see cref="DaemonClient"/> spawning it, and it refuses to run without the nonce that spawn sets in the
/// environment. A resident process that a person — or an assistant reading the help — can start by hand is
/// a process that gets started twice, or started stale, or started against the wrong root, and then quietly
/// answers questions from state nobody meant it to have. Callers use <c>nfi</c> exactly as they always did;
/// this is not part of the interface.
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
    /// working session, short enough that a forgotten one is not a forgotten one for the afternoon.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(20);

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
            using var host = new InitiativesHost(root);
            Serve(pipe, host);
            return 0;
        }
        finally { only.ReleaseMutex(); }
    }

    private static void Serve(string pipe, InitiativesHost host)
    {
        var lastActivity = DateTime.UtcNow;
        var stopping     = false;

        while (!stopping)
        {
            using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
                                                         PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Waiting with a deadline rather than forever, so idling out is the same code path as being
            // asked to stop rather than a timer racing the connection handler for the process.
            var connect = server.WaitForConnectionAsync();
            var idleFor = IdleTimeout - (DateTime.UtcNow - lastActivity);
            if (idleFor <= TimeSpan.Zero || !connect.Wait(idleFor)) break;

            try
            {
                if (DaemonProtocol.Read<DaemonRequest>(server) is not { } request) continue;

                lastActivity = DateTime.UtcNow;
                stopping     = request.Stop;

                DaemonProtocol.Write(server, Execute(request, host));
                server.Flush();
                server.WaitForPipeDrain();
            }
            catch (IOException) { /* the client hung up mid-exchange; the next one is unaffected */ }
            finally
            {
                if (server.IsConnected) try { server.Disconnect(); } catch (IOException) { }
                // Whatever the command changed is on disk before the next caller can ask for it, so an
                // abrupt end costs a load rather than a rebuild.
                host.Flush();
            }
        }

        host.Flush();
    }

    /// <summary>
    /// Runs one command exactly as a one-shot process would, and captures what it printed.
    /// <para>
    /// The verbs write to the console and read the current directory, because they were written to be a
    /// program; rather than rewrite thirty of them onto injected streams, the console and the directory are
    /// pointed somewhere else for the duration. That is process-global state, which is precisely why the
    /// server answers one connection at a time — the alternative is two commands interleaving their output
    /// into each other's response, and no amount of speed is worth that.
    /// </para>
    /// </summary>
    private static DaemonResponse Execute(DaemonRequest request, InitiativesHost host)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var outWas = Console.Out;
        var errWas = Console.Error;
        var dirWas = Directory.GetCurrentDirectory();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Program.StandardInput = request.Stdin;
            Program.Host          = host;

            try { Directory.SetCurrentDirectory(request.WorkingDirectory); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* keep ours */ }

            var code = request.Stop && request.Args.Length == 0 ? 0 : Program.Execute(request.Args);
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
            Console.SetOut(outWas);
            Console.SetError(errWas);
            Program.StandardInput = null;
            Program.Host          = null;
            try { Directory.SetCurrentDirectory(dirWas); } catch (IOException) { }
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
