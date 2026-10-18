using System.Runtime.InteropServices;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Sandbox.Agent;
using Nexaflow.Tests.Sandbox.Session;
using Nexaflow.Tests.Sandbox.Verbs;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The host's end of a session: starts the sandbox with this checkout mapped into it, keeps its window open beside the
/// work, and carries agent verbs across as request files, printing what comes back.
/// </summary>
internal static class SandboxHost
{
    private const int DefaultMemoryMB = 16384;

    /// <summary>A first boot is about forty seconds; a loaded machine can take several times that.</summary>
    private static readonly TimeSpan AgentStartTimeout = TimeSpan.FromMinutes(4);

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The agent rewrites its heartbeat every two seconds, so this long without one means it is gone.</summary>
    private static readonly TimeSpan HeartbeatLimit = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    private static readonly Dictionary<Verb, Func<VerbArguments, int>> Handlers = new()
    {
        [VerbCatalogue.Start] = Start,
        [VerbCatalogue.Show] = Show,
        [VerbCatalogue.Status] = _ => Status(),
        [VerbCatalogue.Stop] = _ => Stop(),
    };

    private static string Checkout => TestSampleData.RepoRoot;

    public static int Execute(Verb verb, VerbArguments arguments)
    {
        UiTestGate.EnsureDpiAware();   // monitor work areas and window bounds in physical pixels
        return Handlers[verb](arguments);
    }

    /// <summary>Sends an agent verb, as typed, to the current session's agent and prints its answer.</summary>
    public static int Forward(Verb verb, string[] arguments)
    {
        var session = LiveSession();
        var request = new SandboxRequest($"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{verb.Name}-{Guid.NewGuid():N}"[..40], verb.Name, arguments);
        SessionFolder.WriteJson(session.RequestFile(request.Id), request);

        var response = verb == VerbCatalogue.Run ? FollowRun(session, request.Id) : AwaitResponse(session, request.Id, AnswerTimeout);
        if (response is null) return 0;   // stopped following a run that goes on

        (response.Ok ? Console.Out : Console.Error).WriteLine(response.Text);
        if (response.Artifact is { } artifact) Console.WriteLine(Path.Combine(session.Root, artifact));
        return response.ExitCode ?? (response.Ok ? 0 : 1);
    }

    private static int Start(VerbArguments arguments)
    {
        if (SessionFolder.Current(Checkout) is { } previous
            && SessionFolder.ReadJson<HostRecord>(previous.HostRecordFile) is { } record
            && WsbCli.Running().Contains(record.SandboxId))
            throw new SandboxException($"This checkout's sandbox is already running ({previous.Root}). `stop` it first.");

        // Everything the sandbox will need, checked here rather than a minute into a boot.
        _ = BuiltOutput.App;
        _ = BuiltOutput.JourneyRunner;
        var agent = Environment.ProcessPath ?? throw new SandboxException("Cannot tell where this exe is.");
        if (Path.GetRelativePath(Checkout, agent).StartsWith("..", StringComparison.Ordinal))
            throw new SandboxException($"Run the copy of this exe built in the checkout ({Checkout}); the sandbox only sees the checkout.");

        var monitor = MonitorDirectory.Choose(arguments.Value("monitor"));
        var configuration = new SandboxConfiguration(
            Checkout, HostDotnet(), arguments.IntValue("memory") ?? DefaultMemoryMB, !arguments.Flag("offline"), arguments.Flag("audio"));

        var session = SessionFolder.CreateNew(Checkout);
        VisualCppRuntime.Stage(session.Redist);

        // Started minimised, so its console never sits over the app it is driving.
        var logon = $"cmd.exe /c start \"Nexaflow sandbox agent\" /min \"{configuration.InSandbox(agent)}\" "
                    + $"{VerbCatalogue.Agent.Name} --session \"{configuration.InSandbox(session.Root)}\"";
        var xml = configuration.ToXml(logon);
        File.WriteAllText(session.ConfigurationFile, xml);

        Console.WriteLine($"Starting Windows Sandbox with {configuration.MemoryMB} MB; session {session.Root}");
        var id = WsbCli.Start(xml);
        SessionFolder.WriteJson(session.HostRecordFile, new HostRecord(id, DateTimeOffset.Now, monitor.Device));

        SandboxWindow.Open(id, monitor, WindowTimeout);
        Console.WriteLine($"Its window is on {monitor}. Leave it open: closed, the sandbox has no desktop.");

        var deadline = DateTime.UtcNow + AgentStartTimeout;
        while (!AgentAlive(session))
        {
            if (DateTime.UtcNow > deadline)
                throw new SandboxException(
                    $"The agent did not start within {AgentStartTimeout.TotalMinutes:0} minutes. Look at the sandbox's window, and at {session.AgentLog}.");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
        // The window resizes itself once the user is signed in, which undoes the placement; the agent up means that is done.
        SandboxWindow.MoveTo(monitor);
        Console.WriteLine("Ready. `run <filter>` runs journeys; `launch`, `shot`, `tree`, `click`, `type` and `key` drive the app.");
        return 0;
    }

    private static int Show(VerbArguments arguments)
    {
        var session = SessionFolder.Current(Checkout) ?? throw NoSession();
        var record = SessionFolder.ReadJson<HostRecord>(session.HostRecordFile) ?? throw NoSession();
        var monitor = MonitorDirectory.Choose(arguments.Value("monitor") ?? record.Monitor);

        if (!SandboxWindow.MoveTo(monitor))
            SandboxWindow.Open(record.SandboxId, monitor, WindowTimeout);
        if (arguments.Value("monitor") is not null)
            SessionFolder.WriteJson(session.HostRecordFile, record with { Monitor = monitor.Device });
        Console.WriteLine($"The sandbox's window is on {monitor}.");
        return 0;
    }

    private static int Status()
    {
        if (SessionFolder.Current(Checkout) is not { } session)
        {
            Console.WriteLine("No sandbox session for this checkout. `start` one.");
            return 0;
        }

        var record = SessionFolder.ReadJson<HostRecord>(session.HostRecordFile);
        var running = record is not null && WsbCli.Running().Contains(record.SandboxId);
        var agent = SessionFolder.ReadJson<AgentStatus>(session.AgentStatusFile);

        Console.WriteLine($"session  {session.Root}");
        Console.WriteLine($"sandbox  {(record is null ? "never started" : $"{record.SandboxId}, {(running ? "running" : "gone")}")}");
        Console.WriteLine($"window   {(SandboxWindow.IsOpen ? "open" : "closed — `show` reopens it")}");
        Console.WriteLine(agent is null
            ? "agent    not started"
            : $"agent    pid {agent.Pid}, last heard {(DateTimeOffset.Now - agent.Heartbeat).TotalSeconds:0}s ago");
        if (agent?.RunId is { } run)
            Console.WriteLine($"run      {run} ({agent.RunFilter ?? "every journey"}) — {session.RunFolder(run)}");
        if (agent?.AppPid is { } pid)
            Console.WriteLine($"app      Nexaflow, pid {pid}");
        return 0;
    }

    private static int Stop()
    {
        var session = SessionFolder.Current(Checkout) ?? throw NoSession();
        var record = SessionFolder.ReadJson<HostRecord>(session.HostRecordFile);

        if (AgentAlive(session))
        {
            // Let the agent close the app and cancel a run, so their logs and reports are whole; then pull the plug regardless.
            var request = new SandboxRequest($"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-stop", VerbCatalogue.Stop.Name, []);
            SessionFolder.WriteJson(session.RequestFile(request.Id), request);
            try { AwaitResponse(session, request.Id, TimeSpan.FromSeconds(30)); }
            catch (SandboxException ex) { Console.Error.WriteLine(ex.Message); }
        }

        if (record is not null && WsbCli.Running().Contains(record.SandboxId))
            WsbCli.Stop(record.SandboxId);
        SessionFolder.ClearCurrent(Checkout);
        Console.WriteLine($"Stopped. The session's results stay in {session.Root}.");
        return 0;
    }

    /// <summary>Follows a run's console as it is written until the run answers; Ctrl+C stops following and leaves it running.</summary>
    private static SandboxResponse? FollowRun(SessionFolder session, string id)
    {
        var detached = false;
        ConsoleCancelEventHandler detach = (_, e) =>
        {
            e.Cancel = true;
            detached = true;
        };
        Console.CancelKeyPress += detach;
        try
        {
            var log = Path.Combine(session.RunFolder(id), JourneyRun.ConsoleLogName);
            long shown = 0;
            while (!detached)
            {
                shown = ShowFrom(log, shown);
                if (SessionFolder.ReadJson<SandboxResponse>(session.ResponseFile(id)) is { } response)
                {
                    ShowFrom(log, shown);
                    return response;
                }
                EnsureAgentAlive(session);
                Thread.Sleep(TimeSpan.FromMilliseconds(500));
            }
            Console.WriteLine("Stopped following. The run goes on in the sandbox: `status` shows it, `cancel` stops it.");
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= detach;
        }
    }

    /// <summary>Prints what <paramref name="path"/> gained past <paramref name="offset"/>; returns the new offset.</summary>
    private static long ShowFrom(string path, long offset)
    {
        if (!File.Exists(path)) return offset;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= offset) return offset;
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
        return stream.Length;
    }

    private static SandboxResponse AwaitResponse(SessionFolder session, string id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (SessionFolder.ReadJson<SandboxResponse>(session.ResponseFile(id)) is { } response) return response;
            if (DateTime.UtcNow > deadline)
                throw new SandboxException($"The agent did not answer within {timeout.TotalSeconds:0}s. See {session.AgentLog}.");
            EnsureAgentAlive(session);
            Thread.Sleep(Poll);
        }
    }

    private static SessionFolder LiveSession()
    {
        var session = SessionFolder.Current(Checkout) ?? throw NoSession();
        EnsureAgentAlive(session);
        return session;
    }

    private static void EnsureAgentAlive(SessionFolder session)
    {
        if (!AgentAlive(session))
            throw new SandboxException(
                $"The sandbox's agent is not answering. If its window was closed, `show` reopens it; otherwise see {session.AgentLog}, or `stop` and `start` again.");
    }

    private static bool AgentAlive(SessionFolder session) =>
        SessionFolder.ReadJson<AgentStatus>(session.AgentStatusFile) is { } status
        && DateTimeOffset.Now - status.Heartbeat < HeartbeatLimit;

    private static SandboxException NoSession() => new("No sandbox is running for this checkout. `start` one.");

    /// <summary>The host's .NET install: the running runtime lives at <c>&lt;dotnet&gt;\shared\Microsoft.NETCore.App\&lt;version&gt;</c>.</summary>
    private static string HostDotnet()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return File.Exists(Path.Combine(root, "dotnet.exe"))
            ? root
            : throw new SandboxException($"Cannot find the .NET install to share with the sandbox (looked in {root}).");
    }
}
