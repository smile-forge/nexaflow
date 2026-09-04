using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

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

    /// <summary>
    /// How long the daemon has to say it heard us. This is not a limit on the work — the acknowledgement is sent
    /// before any of that begins — so it can be short: a process that has taken a connection and not answered in
    /// ten seconds is not busy, it is stuck, and every second waited past that is a second spent hiding it.
    /// </summary>
    private static readonly TimeSpan AckBudget = TimeSpan.FromSeconds(10);

    /// <summary>How long a status query has to come back before the daemon counts as unresponsive. Answering one
    /// touches nothing but a dictionary, so this is generous by an order of magnitude and still decisive.</summary>
    private static readonly TimeSpan StatusBudget = TimeSpan.FromSeconds(10);

    /// <summary>How long the client stays quiet before checking that the work it waits on is still alive.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(5);

    /// <summary>How long a command may take before the wait is worth mentioning. Nearly every one answers in
    /// under a second, and a progress report for those would be noise rather than information.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Runs <paramref name="args"/> on the daemon and reproduces its output here.</summary>
    internal static int Run(string[] args, string productRoot, string? codeRoot)
    {
        var request = DaemonRequest.Command(DaemonRequest.NewTicket(), args, codeRoot,
                                            Directory.GetCurrentDirectory(), ReadStdinIfWanted(args));

        var pipe  = DaemonProtocol.PipeName(productRoot, DaemonProtocol.BuildStamp());
        var reply = Send(pipe, request, connectMs: 200)
                 ?? StartThenSend(pipe, productRoot, request);

        Console.Out.Write(reply.Out);
        Console.Error.Write(reply.Error);
        return reply.ExitCode;
    }

    /// <summary>
    /// Says what the resident process for this tree is doing, and where to watch it.
    /// <para>
    /// This is the one command that is <i>about</i> the daemon rather than merely served by it, and it exists
    /// because the alternative was inferring the answer. When a command seems stuck, the questions are: is
    /// anything there, what is it working on, and how long has it been at it. Each of those is a fact the daemon
    /// already holds, and none of them was reachable. The status query cannot queue — it reads a dictionary —
    /// so this answers even while every worker is busy, which is exactly when it is asked.
    /// </para>
    /// </summary>
    internal static int Report(string productRoot, bool stop)
    {
        var pipe = DaemonProtocol.PipeName(productRoot, DaemonProtocol.BuildStamp());

        Console.WriteLine($"root  {productRoot}");
        Console.WriteLine($"pipe  {pipe}");
        Console.WriteLine($"log   {DaemonLog.PathFor(pipe)}");

        if (stop)
        {
            var bye = Send(pipe, new DaemonRequest { Stop = true, Ticket = DaemonRequest.NewTicket() },
                           connectMs: 500);
            Console.WriteLine(bye is null ? "state nothing was running" : "state asked to stop");
            return 0;
        }

        if (Working(pipe) is not { } work)
        {
            Console.WriteLine("state not running — the next command will start one");
            return 0;
        }

        Console.WriteLine($"state answering, {work.Length} command(s) on the books");
        foreach (var one in work)
            Console.WriteLine($"      {one.Ticket}  {one.State,-8}  {Age(one)}  {one.Command}"
                            + (one.Behind is { } behind ? $"   (behind {behind})" : ""));

        return 0;
    }

    private static string Age(DaemonWorkStatus work) => work.State switch
    {
        WorkState.Queued => $"waiting {work.WaitedSeconds,6:F1}s",
        _                => $"running {work.RanSeconds,6:F1}s",
    };

    /// <summary>Everything the daemon has on the books, or null when nothing answers.</summary>
    private static DaemonWorkStatus[]? Working(string pipe)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
            client.Connect(500);

            DaemonProtocol.Write(client, new DaemonRequest { Ask = DaemonAsk.Working });
            return Within(() => DaemonProtocol.Read<DaemonWorkList>(client), StatusBudget)?.Work;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException
                                    or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// One attempt: connect, say the thing, hear that it was heard, then wait as long as the work takes.
    /// <para>
    /// The stages deliberately do not share a timeout, because they fail for unrelated reasons and want
    /// unrelated answers. Failing to connect means there is no daemon, and is answered by starting one. Being
    /// connected to and then not acknowledged means a daemon that is running and no longer accepting work —
    /// starting another cannot fix that, and it has to be said rather than waited through. And past the
    /// acknowledgement there is no deadline at all, because how long a command ought to take is not something
    /// this end can know.
    /// </para>
    /// </summary>
    private static DaemonResponse? Send(string pipe, DaemonRequest request, int connectMs)
    {
        NamedPipeClientStream client;
        try
        {
            client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
            client.Connect(connectMs);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            DaemonProtocol.Write(client, request);

            var ack = Within(() => DaemonProtocol.Read<DaemonAck>(client), AckBudget);
            if (ack is null)
                Fail($"nfi's resident process took the connection but did not acknowledge "
                   + $"'{Describe(request)}' within {AckBudget.TotalSeconds:F0}s. It is running and no longer "
                   + "accepting work; end that process and run the command again.");

            if (!ack.Accepted)
                Fail($"nfi's resident process declined the command: {ack.Reason ?? "no reason given"}.");

            return Await(pipe, client, request);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Torn down mid-exchange. On the first attempt of the day this races an idling daemon closing its
            // pipe, and the caller's next move — start one and ask again — is the right one either way.
            return null;
        }
        finally { client.Dispose(); }
    }

    /// <summary>
    /// Waits for the answer, checking on the work whenever it has been quiet for a while.
    /// <para>
    /// Nothing here limits how long the work may take, and nothing should: a graph build takes as long as it
    /// takes, and a client that gave up on one would be wrong in precisely the cases that matter. What is
    /// limited is the daemon's ability to keep saying what it is doing. While it answers a status query it is
    /// alive and the wait is legitimate however long it runs; the moment it stops answering one, this is a hang
    /// and gets reported as a hang. That is the whole reason the acknowledgement exists — it removes the need
    /// for either end to hold a list of which commands are allowed to be slow, which would be wrong the day
    /// someone added a verb to it.
    /// </para>
    /// </summary>
    private static DaemonResponse Await(string pipe, Stream client, DaemonRequest request)
    {
        var reading = Task.Run(() => DaemonProtocol.Read<DaemonResponse>(client));
        var since   = DateTime.UtcNow;
        var said    = false;

        while (!reading.Wait(Heartbeat))
        {
            var status = Ask(pipe, request.Ticket);
            if (status is not null && status.State != WorkState.Unknown)
            {
                said |= Mention(status, DateTime.UtcNow - since);
                continue;
            }

            // A poll and the answer it was about cross on the wire routinely — the work finishes in the gap
            // between the question and the reply — so a missing status is only a hang if the answer does not
            // then turn up either.
            if (!reading.Wait(Heartbeat)) Fail(Wedged(request, status));
        }

        if (said) Erase();

        return reading.Result
            ?? throw new DaemonUnavailableException(
                   $"nfi's resident process hung up while running '{Describe(request)}' without answering.");
    }

    /// <summary>
    /// Says what is being waited for, once it has gone on long enough to be worth saying.
    /// <para>
    /// Only to a real console, and rewritten in place rather than accumulated: this is for the person watching
    /// an unexpectedly long command, and it must not reach a caller that is capturing output, for whom an extra
    /// line on stderr is a change in what the command returned. Which is also why it is not printed at once —
    /// nearly every command answers in under a second, and a progress report for those would be noise.
    /// </para>
    /// </summary>
    private static bool Mention(DaemonWorkStatus status, TimeSpan waited)
    {
        if (waited < Patience || Console.IsErrorRedirected) return false;

        var doing = status.State == WorkState.Queued && status.Behind is { } behind
            ? $"queued behind {behind}"
            : "running";

        Console.Error.Write($"\rnfi: {doing} — {status.Command} ({waited.TotalSeconds:F0}s)".PadRight(Width));
        return true;
    }

    /// <summary>Takes the progress line back off the console, so what the command actually printed is all that
    /// is left behind.</summary>
    private static void Erase()
    {
        Console.Error.Write("\r".PadRight(Width) + "\r");
    }

    /// <summary>Wide enough to overwrite the previous progress line, and narrow enough not to wrap.</summary>
    private static int Width => Math.Max(40, Console.IsErrorRedirected ? 80 : Console.WindowWidth - 1);

    /// <summary>Asks after one ticket, on a connection of its own so the answer cannot queue behind the very
    /// work it is about. Null when the daemon did not answer, which is the whole signal.</summary>
    private static DaemonWorkStatus? Ask(string pipe, string ticket)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
            client.Connect(1000);

            DaemonProtocol.Write(client, DaemonRequest.Status(ticket));
            return Within(() => DaemonProtocol.Read<DaemonWorkStatus>(client), StatusBudget);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException
                                    or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>What to say when the daemon has stopped answering: which command, how long, and whether the
    /// problem is that it went quiet or that it has forgotten the work entirely.</summary>
    private static string Wedged(DaemonRequest request, DaemonWorkStatus? status) =>
        status is null
            ? $"nfi's resident process stopped answering while running '{Describe(request)}' "
            + $"(ticket {request.Ticket}). It is wedged rather than slow — it did not answer a status query "
            + $"within {StatusBudget.TotalSeconds:F0}s either. End that process and run the command again."
            : $"nfi's resident process no longer knows about '{Describe(request)}' (ticket {request.Ticket}) "
            + "and never sent its answer, so it has most likely restarted underneath this command. "
            + "Run the command again.";

    /// <summary>
    /// A read with a deadline. Pipe streams do not honour <see cref="Stream.ReadTimeout"/>, and a connection
    /// whose read timed out is being abandoned anyway, so the read is simply left behind — with its eventual
    /// failure observed, so that giving up on a connection is not also an unhandled exception somewhere.
    /// </summary>
    private static T? Within<T>(Func<T?> read, TimeSpan budget) where T : class
    {
        var task = Task.Run(read);
        if (task.Wait(budget)) return task.Result;

        _ = task.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
        return null;
    }

    /// <summary>The command line, short enough to sit in a message someone has to read.</summary>
    private static string Describe(DaemonRequest request)
    {
        var line = string.Join(' ', request.Args);
        return line.Length <= 80 ? line : line[..77] + "...";
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
            spawned = DaemonServer.StartDetached(DaemonServer.SpawnInfo(exe!, pipe, productRoot));

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

    [DoesNotReturn]
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
