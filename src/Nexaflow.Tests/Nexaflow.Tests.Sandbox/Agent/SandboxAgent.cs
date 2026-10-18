using System.Diagnostics;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Sandbox.Host;
using Nexaflow.Tests.Sandbox.Session;
using Nexaflow.Tests.Sandbox.Verbs;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// The sandbox's end of a session, started when its user signs in. It takes requests from the session folder one at a
/// time, on this thread — UI Automation and input stay on one STA thread — while a journey run goes on in its own process
/// and answers when it ends. Input verbs are refused while a run is driving the desktop, so a person's click never lands
/// in the middle of a journey's.
/// </summary>
internal sealed class SandboxAgent : IDisposable
{
    /// <summary>How long the loop waits for the request watch before looking anyway, should a notification go missing.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(2);

    /// <summary>The journeys glide the pointer fast, so a click is not two seconds of travel; the agent's clicks match them.</summary>
    private const double PointerPixelsPerMillisecond = 20;

    private readonly SessionFolder _session;
    private readonly UIA3Automation _automation = new();
    private readonly AppUnderTest _app;
    private readonly DesktopDriver _desktop;
    private readonly Dictionary<Verb, Func<SandboxRequest, VerbArguments, SandboxResponse?>> _handlers;
    private readonly Lock _gate = new();
    private readonly Timer _heartbeat;
    private JourneyRun? _run;
    private bool _stopping;

    private SandboxAgent(SessionFolder session)
    {
        _session = session;
        _app = new AppUnderTest(_automation);
        _desktop = new DesktopDriver(_automation);
        _handlers = new()
        {
            [VerbCatalogue.Run] = StartRun,
            [VerbCatalogue.Cancel] = (request, _) => CancelRun(request),
            [VerbCatalogue.Launch] = (request, arguments) => Answer(request, _app.Launch(arguments.Value("tab"), TabParams(arguments))),
            [VerbCatalogue.Close] = (request, _) => Answer(request, _app.Close()),
            [VerbCatalogue.Shot] = Screenshot,
            [VerbCatalogue.Tree] = (request, arguments) =>
                Answer(request, _desktop.Tree(arguments.Positional(0) is { } target ? ElementTarget.Parse(target) : null, arguments.IntValue("depth") ?? 8)),
            [VerbCatalogue.Click] = (request, arguments) =>
                Answer(request, _desktop.Click(ElementTarget.Parse(arguments.Positionals[0]), arguments.Flag("right"), arguments.Flag("double"))),
            [VerbCatalogue.Type] = (request, arguments) =>
                Answer(request, _desktop.Type(arguments.Positionals[0], arguments.Value("into") is { } into ? ElementTarget.Parse(into) : null)),
            [VerbCatalogue.Key] = (request, arguments) => Answer(request, _desktop.Key(arguments.Positionals[0])),
            [VerbCatalogue.Stop] = (request, _) => StopServing(request),
        };
        _heartbeat = new Timer(_ => Beat(), null, TimeSpan.Zero, HeartbeatEvery);
    }

    public static int Serve(VerbArguments arguments)
    {
        var root = arguments.Value("session") ?? throw new SandboxException("agent: --session <folder> is required.");
        UiTestGate.EnsureDpiAware();
        Mouse.MovePixelsPerMillisecond = PointerPixelsPerMillisecond;

        using var agent = new SandboxAgent(new SessionFolder(root));
        agent.Serve();
        return 0;
    }

    private void Serve()
    {
        Log($"agent {Environment.ProcessId} up");
        var added = VisualCppRuntime.Install(_session.Redist);
        Log(added.Count == 0 ? "Visual C++ runtime already present" : $"installed the Visual C++ runtime: {string.Join(", ", added)}");

        // Inside the sandbox, a mapped folder's listing is cached for the better part of twenty seconds unless something
        // watches it: the watch both wakes the loop and keeps the listing current.
        using var arrived = new AutoResetEvent(initialState: true);
        using var watcher = new FileSystemWatcher(_session.Requests, "*.json") { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, _) => arrived.Set();
        watcher.Renamed += (_, _) => arrived.Set();
        watcher.EnableRaisingEvents = true;

        while (!_stopping)
        {
            arrived.WaitOne(Poll);
            foreach (var file in _session.PendingRequests().ToList())
            {
                var clock = Stopwatch.StartNew();
                var request = SessionFolder.ReadJson<SandboxRequest>(file);
                var read = clock.ElapsedMilliseconds;
                File.Delete(file);
                var deleted = clock.ElapsedMilliseconds;
                if (request is null) continue;
                var response = Handle(request);
                var handled = clock.ElapsedMilliseconds;
                if (response is not null) Write(response);
                Log($"{request.Id}: read {read} ms, deleted {deleted} ms, handled {handled} ms, answered {clock.ElapsedMilliseconds} ms");
                if (_stopping) break;
            }
        }
        Log("agent stopping");
    }

    private SandboxResponse? Handle(SandboxRequest request)
    {
        try
        {
            var verb = VerbCatalogue.Find(request.Verb);
            var arguments = VerbArguments.Parse(verb, request.Arguments);
            if (!_handlers.TryGetValue(verb, out var handler))
                return Refuse(request, $"`{verb.Name}` is done on the host, not by the agent.");
            if (verb.SendsInput && CurrentRun is { } run)
                return Refuse(request, $"Journey run {run.Id} is driving the desktop, and `{verb.Name}` would take its input. Wait for it, or `cancel` it.");
            return handler(request, arguments);
        }
        catch (SandboxException ex)
        {
            return Refuse(request, ex.Message);
        }
        catch (Exception ex)
        {
            Log($"{request.Id} failed: {ex}");
            return Refuse(request, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private SandboxResponse? StartRun(SandboxRequest request, VerbArguments arguments)
    {
        // The journeys launch their own app, and two on one desktop take each other's clicks.
        var closed = _app.IsRunning ? _app.Close() : null;

        var environment = SessionFolder.ReadJson<Dictionary<string, string>>(_session.EnvironmentFile) ?? [];
        var run = new JourneyRun(request.Id, arguments.Positional(0), _session.RunFolder(request.Id), environment);
        lock (_gate) _run = run;
        Log($"run {run.Id} started ({run.Filter ?? "every journey"})");
        run.Begin(exitCode =>
        {
            lock (_gate) _run = null;
            Log($"run {run.Id} exited {exitCode}");
            var text = closed is null ? run.Summary(exitCode) : $"{closed}{Environment.NewLine}{run.Summary(exitCode)}";
            Write(new SandboxResponse(request.Id, exitCode == 0, text, Path.GetRelativePath(_session.Root, run.Folder), exitCode));
        });
        return null;   // answered when the run ends
    }

    private SandboxResponse CancelRun(SandboxRequest request)
    {
        if (CurrentRun is not { } run) return Refuse(request, "No journey run is in progress.");
        run.Cancel();
        return Answer(request, $"Cancelled run {run.Id}.");
    }

    private SandboxResponse Screenshot(SandboxRequest request, VerbArguments arguments)
    {
        var path = Path.Combine(_session.Shots, request.Id + ".png");
        _desktop.Screenshot(path, arguments.Flag("window"));
        return new SandboxResponse(request.Id, true, "Screenshot:", Path.GetRelativePath(_session.Root, path));
    }

    private SandboxResponse StopServing(SandboxRequest request)
    {
        CurrentRun?.Cancel();
        var closed = _app.IsRunning ? _app.Close() : "No app was open.";
        _stopping = true;
        return Answer(request, closed);
    }

    private JourneyRun? CurrentRun
    {
        get { lock (_gate) return _run; }
    }

    private static IReadOnlyDictionary<string, string> TabParams(VerbArguments arguments) =>
        arguments.Values("param").Select(pair => pair.Split('=', 2)).ToDictionary(
            parts => parts[0],
            parts => parts.Length == 2 ? parts[1] : throw new SandboxException($"--param takes key=value, not '{parts[0]}'."));

    private void Beat()
    {
        try
        {
            var run = CurrentRun;
            SessionFolder.WriteJson(_session.AgentStatusFile,
                new AgentStatus(Environment.ProcessId, DateTimeOffset.Now, run?.Id, run?.Filter, _app.Pid));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"heartbeat not written: {ex.Message}");
        }
    }

    private static SandboxResponse Answer(SandboxRequest request, string text) => new(request.Id, true, text);

    private static SandboxResponse Refuse(SandboxRequest request, string text) => new(request.Id, false, text);

    private void Write(SandboxResponse response) => SessionFolder.WriteJson(_session.ResponseFile(response.Id), response);

    private void Log(string line)
    {
        lock (_gate) File.AppendAllText(_session.AgentLog, $"{DateTimeOffset.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
    }

    public void Dispose()
    {
        _heartbeat.Dispose();
        _app.Dispose();
        _automation.Dispose();
    }
}
