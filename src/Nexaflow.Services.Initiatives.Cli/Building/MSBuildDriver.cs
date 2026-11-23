using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary>
/// MSBuild's <see cref="BuildManager"/>, driven for <see cref="BuildPool"/>. Every project is built in a worker process,
/// none in this one: the resident process keeps only the scheduler, so a task's assemblies, its memory and anything it
/// leaves running stay out of the process answering everyone's questions.
/// <para>
/// A build is what <c>dotnet build</c> does — each entry restored first, in a submission of its own whose evaluation is
/// thrown away, then built — with the entries of one order built side by side in one session.
/// </para>
/// </summary>
internal sealed class MSBuildDriver(string name, int nodes, bool reuseNodes) : IBuildEngine
{
    // As `-restore` asks for it: a restore's evaluation must not be the build's, which has to see what it restored.
    private const BuildRequestDataFlags RestoreFlags = BuildRequestDataFlags.ClearCachesAfterBuild
                                                     | BuildRequestDataFlags.SkipNonexistentTargets
                                                     | BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports
                                                     | BuildRequestDataFlags.FailOnUnresolvedSdk;

    private readonly BuildManager _manager = Create(name);
    private readonly SubmissionRouter _router = new();

    // One at a time across the process: a BuildManager's constructor sets up a process-wide serialization contract without a
    // lock, and two pools whose first builds start together would otherwise construct theirs at once and one would throw.
    private static readonly Lock Constructing = new();

    private static BuildManager Create(string name)
    {
        lock (Constructing) return new BuildManager(name);
    }

    public int Nodes => nodes;

    /// <summary>Always: <see cref="HostedMSBuild"/> admitted the tree before this was created.</summary>
    public string? Admit(string codeRoot) => null;

    public void BeginSession() => _manager.BeginBuild(new BuildParameters
    {
        MaxNodeCount      = nodes,
        EnableNodeReuse   = reuseNodes,
        DisableInProcNode = true,
        Loggers           = [_router],
    });

    public Task<BuildReport> Start(BuildOrder order) =>
        Task.Factory.StartNew(() => Run(order), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public void CancelSession() => _manager.CancelAllSubmissions();

    public void EndSession() => _manager.EndBuild();

    public void ReleaseNodes() => _manager.ShutdownAllNodes();

    public void Dispose() => _manager.Dispose();

    private BuildReport Run(BuildOrder order)
    {
        var clock      = Stopwatch.StartNew();
        var raised     = new Raised(order.CodeRoot);
        var properties = order.Properties.ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.OrdinalIgnoreCase);
        var routed     = new List<int>();
        var restore    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (order.Restore)
            {
                // One after another: restores of entries that share projects would write the same project.assets.json.
                foreach (var project in order.Projects)
                {
                    if (order.RestoreUnchanged.Contains(project))
                    {
                        restore[project] = BuiltProject.Unchanged;
                        continue;
                    }

                    var restoring = new Dictionary<string, string?>(properties, StringComparer.OrdinalIgnoreCase)
                    {
                        ["MSBuildRestoreSessionId"]      = Guid.NewGuid().ToString("D"),
                        ["ExcludeRestorePackageImports"] = "true",
                    };
                    var restored = Submit(new BuildRequestData(project, restoring, null, ["Restore"], null, RestoreFlags), raised, routed)
                                   .GetAwaiter().GetResult();
                    if (restored.OverallResult != BuildResultCode.Success)
                    {
                        raised.Exception(project, restored.Exception);
                        return Report(order, [new BuiltProject(project, false, [], Restore: BuiltProject.RestoreFailed)], raised, clock);
                    }
                    restore[project] = BuiltProject.Restored;
                }
            }

            var building = order.Projects
                                .Select(project => (Project: project, Result: Submit(Request(order, project, properties), raised, routed)))
                                .ToList();
            Task.WaitAll([.. building.Select(b => b.Result)]);

            var built = new List<BuiltProject>(building.Count);
            foreach (var (project, result) in building)
            {
                raised.Exception(project, result.Result.Exception);
                built.Add(new BuiltProject(project, result.Result.OverallResult == BuildResultCode.Success,
                                           Outputs(result.Result, order.Targets[^1]), Items(result.Result, order.Items),
                                           restore.GetValueOrDefault(project, "")));
            }
            return Report(order, built, raised, clock);
        }
        catch (Exception e)
        {
            raised.Exception(order.Projects.FirstOrDefault() ?? "", e);
            return Report(order, [.. order.Projects.Select(p => new BuiltProject(p, false, [], Restore: restore.GetValueOrDefault(p, "")))],
                          raised, clock);
        }
        finally
        {
            foreach (var id in routed) _router.Unroute(id);
        }
    }

    // Asked for its items only when some are wanted: handing back a project's state is work the worker otherwise skips.
    private static BuildRequestData Request(BuildOrder order, string project, Dictionary<string, string?> properties) =>
        order.Items.Count == 0
            ? new BuildRequestData(project, properties, null, [.. order.Targets], null)
            : new BuildRequestData(project, properties, null, [.. order.Targets], null, BuildRequestDataFlags.ProvideSubsetOfStateAfterBuild,
                                   new RequestedProjectState { ItemFilters = order.Items.ToDictionary(i => i, _ => new List<string>()) });

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? Items(BuildResult result, IReadOnlyList<string> types) =>
        types.Count == 0 || result.ProjectStateAfterBuild is not { } state
            ? null
            : types.ToDictionary(t => t, t => (IReadOnlyList<string>)[.. state.GetItems(t).Select(i => i.EvaluatedInclude)]);

    // Completes once the submission's result is in and its last event has been logged — MSBuild holds a submission open
    // until its root project's ProjectFinished is through the logging service, so nothing it raised arrives after this.
    private Task<BuildResult> Submit(BuildRequestData request, Raised raised, List<int> routed)
    {
        var submission = _manager.PendBuildRequest(request);
        _router.Route(submission.SubmissionId, raised);
        routed.Add(submission.SubmissionId);

        var done = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        submission.ExecuteAsync(s => done.SetResult(s.BuildResult!), null);
        return done.Task;
    }

    private static IReadOnlyList<string> Outputs(BuildResult result, string target) =>
        result.ResultsByTarget.TryGetValue(target, out var outcome) ? [.. outcome.Items.Select(i => i.ItemSpec)] : [];

    private BuildReport Report(BuildOrder order, List<BuiltProject> built, Raised raised, Stopwatch clock) =>
        new(built.Count == order.Projects.Count && built.All(b => b.Succeeded), built, raised.All(), 0,
            clock.Elapsed.TotalSeconds, nodes);

    /// <summary>What one order's submissions raised, gathered from whichever worker raised it.</summary>
    private sealed class Raised(string codeRoot)
    {
        private readonly object _gate = new();
        private readonly List<BuildDiagnostic> _raised = [];
        private readonly HashSet<(string, string, int, int, string, string)> _seen = [];

        public void Add(string severity, string? file, int line, int column, string? code, string? message, string? project)
        {
            var diagnostic = new BuildDiagnostic(severity, Relative(file ?? ""), line, column, code ?? "", message ?? "",
                                                 Relative(project ?? ""));
            lock (_gate)
            {
                // Once, however many times it was raised: a project built for several frameworks raises it for each,
                // and a file several projects compile, in each of them.
                if (_seen.Add((severity, diagnostic.File, line, column, diagnostic.Code, diagnostic.Message)))
                    _raised.Add(diagnostic);
            }
        }

        public void Exception(string project, Exception? exception)
        {
            if (exception is not null) Add("error", project, 0, 0, exception.GetType().Name, exception.Message, project);
        }

        /// <summary>Errors, then warnings, each in the order they were raised.</summary>
        public IReadOnlyList<BuildDiagnostic> All()
        {
            lock (_gate) return [.. _raised.Where(d => d.Severity == "error"), .. _raised.Where(d => d.Severity != "error")];
        }

        private string Relative(string path)
        {
            if (path.Length == 0 || !Path.IsPathFullyQualified(path)) return path.Replace('\\', '/');
            var relative = Path.GetRelativePath(codeRoot, path);
            return (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative).Replace('\\', '/');
        }
    }

    /// <summary>
    /// The one logger a session has, handing each error and warning to the order whose submission raised it. Loggers
    /// belong to the session, not the submission, and a session holds several callers' builds at once.
    /// </summary>
    private sealed class SubmissionRouter : ILogger
    {
        private readonly ConcurrentDictionary<int, Raised> _routes = new();

        // Errors and warnings reach a logger whatever its verbosity; asking for nothing more keeps every message
        // a worker would otherwise send across.
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;

        public string? Parameters { get; set; }

        public void Route(int submission, Raised raised) => _routes[submission] = raised;

        public void Unroute(int submission) => _routes.TryRemove(submission, out _);

        public void Initialize(IEventSource source)
        {
            source.ErrorRaised += (_, e) => To(e.BuildEventContext)?.Add("error", e.File, e.LineNumber, e.ColumnNumber, e.Code,
                                                                          e.Message, e.ProjectFile);
            source.WarningRaised += (_, e) => To(e.BuildEventContext)?.Add("warning", e.File, e.LineNumber, e.ColumnNumber, e.Code,
                                                                            e.Message, e.ProjectFile);
        }

        public void Shutdown() { }

        private Raised? To(BuildEventContext? context) =>
            context is not null && _routes.TryGetValue(context.SubmissionId, out var raised) ? raised : null;
    }
}
