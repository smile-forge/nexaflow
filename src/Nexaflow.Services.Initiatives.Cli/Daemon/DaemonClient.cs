using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using System.Text;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// How every <c>nfi</c> invocation actually runs: hand the command to the resident process for this tree,
/// starting one first if nothing answers.
/// <para>
/// The caller is not told any of this. There is no flag to opt out, no note about a daemon starting, and no
/// verb to manage one — a command prints what it always printed and exits with what it always exited with.
/// The only visible difference is that the second one is fast.
/// </para>
/// </summary>
internal static class DaemonClient
{
    /// <summary>Long enough for a process to start and open a pipe on a loaded machine; short enough that a
    /// genuine failure is reported rather than waited through.</summary>
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(20);

    /// <summary>Runs <paramref name="args"/> on the daemon and reproduces its output here.</summary>
    internal static int Run(string[] args, string productRoot, string? codeRoot)
    {
        var pipe = DaemonProtocol.PipeName(productRoot, DaemonProtocol.BuildStamp());

        var request = new DaemonRequest(args, codeRoot, Directory.GetCurrentDirectory(), ReadStdinIfWanted(args));
        var reply   = Send(pipe, request, connectMs: 200)
                   ?? StartThenSend(pipe, productRoot, request);

        Console.Out.Write(reply.Out);
        Console.Error.Write(reply.Error);
        return reply.ExitCode;
    }

    /// <summary>
    /// One attempt: connect, say the thing, hear the answer. Null when nothing is listening — which on the
    /// first call of the day is the ordinary case, not a fault.
    /// </summary>
    private static DaemonResponse? Send(string pipe, DaemonRequest request, int connectMs)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
            client.Connect(connectMs);

            DaemonProtocol.Write(client, request);
            return DaemonProtocol.Read<DaemonResponse>(client);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a daemon and keeps trying until it answers. A failure here is fatal by design: falling back to
    /// running in-process would mean the command silently costing a cold start every time, which is the state
    /// this exists to end — and it would hide whatever is stopping the daemon from starting.
    /// </summary>
    private static DaemonResponse StartThenSend(string pipe, string productRoot, DaemonRequest request)
    {
        var exe = Environment.ProcessPath;
        if (exe is not { Length: > 0 })
            Fail("nfi cannot locate its own executable, so it cannot start its resident process.");

        Process? spawned = null;
        var      said    = new StringBuilder();

        try
        {
            spawned = Process.Start(DaemonServer.SpawnInfo(exe!, pipe, productRoot));

            // Drain both streams, or a child that prints anything blocks on a full pipe buffer and stops
            // answering with nothing to show for it. Kept only so a death can be explained.
            if (spawned is not null)
            {
                spawned.OutputDataReceived += (_, e) => Keep(said, e.Data);
                spawned.ErrorDataReceived  += (_, e) => Keep(said, e.Data);
                spawned.BeginOutputReadLine();
                spawned.BeginErrorReadLine();
            }
        }
        catch (Exception ex)
        {
            Fail($"nfi could not start its resident process ({ex.GetType().Name}: {ex.Message}).");
        }

        var deadline = DateTime.UtcNow + StartupBudget;
        while (DateTime.UtcNow < deadline)
        {
            if (Send(pipe, request, connectMs: 250) is { } reply) return reply;

            // It died rather than declined: say what it said, which is the only place the reason exists.
            if (spawned is { HasExited: true })
                Fail($"nfi's resident process exited immediately (exit {spawned.ExitCode})"
                   + (said.Length == 0 ? "" : ": " + said.ToString().Trim())
                   + ". Run the same command again once that is resolved.");

            Thread.Sleep(100);
        }

        Fail($"nfi's resident process did not answer within {StartupBudget.TotalSeconds:F0}s on pipe {pipe}.");
        return null!;   // Fail throws
    }

    private static void Keep(StringBuilder said, string? line)
    {
        if (line is null or "") return;
        lock (said) if (said.Length < 2000) said.AppendLine(line);
    }

    private static void Fail(string message) => throw new DaemonUnavailableException(message);

    /// <summary>
    /// Standard input, but only for a command that asked for it. The daemon has no console, so whatever is
    /// piped in has to be read here and carried — and reading it unconditionally would hang every command
    /// run from a terminal with nothing to pipe.
    /// </summary>
    private static string? ReadStdinIfWanted(string[] args)
    {
        if (!args.Any(a => a is "--stdin" or "--find-stdin")) return null;
        return Console.In.ReadToEnd();
    }
}

/// <summary>The daemon could not be reached and could not be started. Fatal: there is no in-process fallback,
/// on purpose, because one would turn a broken daemon into a permanent silent slowdown.</summary>
internal sealed class DaemonUnavailableException(string message) : Exception(message);
