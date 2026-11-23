namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary>What one caller asked to have built: projects of one working tree, the targets to run in each, and the
/// global properties they are evaluated with.</summary>
/// <param name="CodeRoot">The working tree the projects belong to — what decides who may share a session with whom.</param>
/// <param name="Projects">Full paths of the projects or solutions, each built as an entry of its own.</param>
/// <param name="Targets">The targets to run in every entry. The outputs reported are the last one's.</param>
/// <param name="Properties">Global properties, as <c>-p:</c> gives them on a command line.</param>
/// <param name="Restore">Whether each entry is restored first, as <c>dotnet build</c> does.</param>
internal sealed record BuildOrder(
    string CodeRoot,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, string> Properties,
    bool Restore)
{
    /// <summary>Item types each entry hands back once built, in <see cref="BuiltProject.Items"/> — how a design-time
    /// build's compiler command line is read.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>The stamp of everything restoring an entry reads (<see cref="RestoreInputs"/>), null where that cannot be
    /// named. With it, an entry whose stamp is what it was at its last successful restore is not restored again.</summary>
    public Func<string, string?>? RestoreStamp { get; init; }

    /// <summary>The entries the pool found nothing changed for since their last restore: set by the pool, read by the
    /// engine, which builds them without restoring.</summary>
    public IReadOnlySet<string> RestoreUnchanged { get; init; } = new HashSet<string>();
}

/// <summary>One entry of a build: whether it built, what its last target returned — for <c>Build</c>, the assembly it
/// produced — the items it was asked to hand back, and what became of its restore.</summary>
/// <param name="Restore"><see cref="BuiltProject.Restored"/>, <see cref="BuiltProject.Unchanged"/>,
/// <see cref="BuiltProject.RestoreFailed"/>, or empty when no restore was asked for.</param>
internal sealed record BuiltProject(
    string Project,
    bool Succeeded,
    IReadOnlyList<string> Outputs,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Items = null,
    string Restore = "")
{
    public const string Restored      = "restored";
    public const string Unchanged     = "unchanged";
    public const string RestoreFailed = "failed";
}

/// <summary>An error or warning a build raised, with its file relative to the tree it was built in where it lies
/// inside it.</summary>
internal sealed record BuildDiagnostic(string Severity, string File, int Line, int Column, string Code, string Message,
                                       string Project);

/// <summary>What became of a <see cref="BuildOrder"/>.</summary>
/// <param name="Problem">Why nothing was built at all — no SDK for the tree, the caller left, the pool shut —
/// rather than a build that ran and failed, whose reasons are its <paramref name="Diagnostics"/>.</param>
internal sealed record BuildReport(
    bool Succeeded,
    IReadOnlyList<BuiltProject> Projects,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    double WaitedSeconds,
    double TookSeconds,
    int Nodes,
    string? Problem = null)
{
    public static BuildReport Refused(string problem, double waited = 0) => new(false, [], [], waited, 0, 0, problem);
}

/// <summary>
/// The build engine the pool schedules: a scheduler with worker processes behind it, opened for a session, given
/// work, and told when to let its idle workers go. Kept apart from MSBuild so the schedule is provable without one —
/// <see cref="HostedMSBuild"/> is the real one.
/// </summary>
internal interface IBuildEngine : IDisposable
{
    /// <summary>The worker processes a session may use at most.</summary>
    int Nodes { get; }

    /// <summary>Null when this engine can build the tree at <paramref name="codeRoot"/>, otherwise why not.</summary>
    string? Admit(string codeRoot);

    void BeginSession();

    /// <summary>Runs <paramref name="order"/> inside the open session. Never throws: a failure is in the report.</summary>
    Task<BuildReport> Start(BuildOrder order);

    /// <summary>Stops everything running in the open session.</summary>
    void CancelSession();

    /// <summary>Closes the session once nothing is running in it; its workers stay up to be reused.</summary>
    void EndSession();

    /// <summary>Ends every idle worker process.</summary>
    void ReleaseNodes();
}

/// <summary>
/// One set of build worker processes for every caller of the resident process — every session, every working tree —
/// rather than a set per <c>dotnet build</c>, each the size of the machine and each left behind for a quarter of an
/// hour when its build ends.
/// <para>
/// Builds share a <b>session</b>: one scheduler, one cap on workers, and a project two builds both need built once.
/// A session also remembers what it has built, which is why a working tree builds at most once in each — a second
/// build of the same tree would be handed the first one's results, from before whatever was edited in between, and
/// two builds of one tree at once would be writing the same <c>obj</c> folders anyway. So a build joins the session
/// that is open unless its tree has already built in it; then the session takes nobody new, drains, and the build
/// opens the next one.
/// </para>
/// <para>
/// Workers outlive the session, because starting them is what an incremental build mostly costs, and are let go
/// when no build has come for <see cref="Linger"/> — or when this process ends.
/// </para>
/// <para>
/// A pool also remembers what each entry's restore read when it last ran (<see cref="BuildOrder.RestoreStamp"/>), and
/// leaves out a restore with nothing new to read.
/// </para>
/// </summary>
internal sealed class BuildPool : IDisposable
{
    /// <summary>
    /// How long idle workers are kept for the next build. Builds from a working session come minutes apart; a
    /// worker started cold makes a no-op build of a large suite take about twice as long as a warm one.
    /// </summary>
    internal static readonly TimeSpan Linger = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The workers every build shares. Half the machine: an incremental build of the largest suite is no faster on
    /// all 32 cores than on 8, a full one is bound by its project graph long before it runs out of workers, and the
    /// rest of the machine is the other sessions' — and their tests'.
    /// </summary>
    internal static int DefaultNodes => Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// The workers design-time builds share. A design-time build is one project with its references left unbuilt, and the
    /// compile host asks for them one at a time; two lets two trees' edit checks run at once.
    /// </summary>
    internal const int DesignTimeNodes = 2;

    private readonly IBuildEngine _engine;
    private readonly TimeSpan _linger;
    private readonly Action<string> _log;
    private readonly object _gate = new();   // a monitor, not a Lock: waiting for a session to drain is Monitor.Wait
    private readonly Timer _lingering;

    // Each entry's restore stamp when it last restored, per global properties: the restores that need not run again.
    private readonly Dictionary<string, string> _restoredAt = new(StringComparer.OrdinalIgnoreCase);

    private Session? _session;
    private bool _releasing;
    private bool _disposed;

    private sealed class Session
    {
        public readonly HashSet<string> Trees = new(StringComparer.OrdinalIgnoreCase);
        public int Running;
        public bool Draining;
    }

    public BuildPool(IBuildEngine engine, TimeSpan linger, Action<string>? log = null)
    {
        _engine    = engine;
        _linger    = linger;
        _log       = log ?? (_ => { });
        _lingering = new Timer(_ => ReleaseIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Builds <paramref name="order"/> and waits for it. When <paramref name="cancellation"/> fires — the caller has
    /// gone — the session is cancelled if this build is all it holds, and otherwise left to finish for the others in
    /// it; either way this returns at once.
    /// </summary>
    public BuildReport Build(BuildOrder order, CancellationToken cancellation)
    {
        if (_engine.Admit(order.CodeRoot) is { } refusal) return BuildReport.Refused(refusal);

        var waiting = System.Diagnostics.Stopwatch.StartNew();
        Session session;
        using (cancellation.Register(() => { lock (_gate) Monitor.PulseAll(_gate); }))
        lock (_gate)
        {
            while (true)
            {
                if (_disposed) return BuildReport.Refused("nfi's resident process is stopping", waiting.Elapsed.TotalSeconds);
                if (cancellation.IsCancellationRequested) return BuildReport.Refused("its caller left", waiting.Elapsed.TotalSeconds);

                if (_session is null && !_releasing)
                {
                    _lingering.Change(Timeout.Infinite, Timeout.Infinite);
                    _engine.BeginSession();
                    _session = new Session();
                    _log("session opened");
                    break;
                }
                if (_session is { Draining: false } open)
                {
                    if (!open.Trees.Contains(order.CodeRoot)) break;
                    open.Draining = true;   // this tree built in it already: let it finish, and build in the next one
                }
                Monitor.Wait(_gate);
            }

            session = _session!;
            session.Trees.Add(order.CodeRoot);
            session.Running++;
        }

        var waited = waiting.Elapsed.TotalSeconds;
        order = WithoutUnchangedRestores(order);

        // Counted as running from here, so it has to be finished from here, whatever starting it does.
        Task<BuildReport> run;
        try { run = _engine.Start(order); }
        catch (Exception e) { run = Task.FromResult(BuildReport.Refused($"the build could not start: {e.Message}", waited)); }
        _ = run.ContinueWith(_ => Finished(session), CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously,
                             TaskScheduler.Default);

        try { run.Wait(cancellation); }
        catch (OperationCanceledException)
        {
            bool alone;
            lock (_gate)
            {
                alone = session.Running == 1;
                if (alone) session.Draining = true;
            }
            if (alone) _engine.CancelSession();
            _log(alone ? "caller left; its session cancelled" : "caller left; its build finishes for the others in its session");
            return BuildReport.Refused("its caller left", waited);
        }

        var report = run.Result with { WaitedSeconds = waited };
        RememberRestores(order, report);
        return report;
    }

    // Stamped once the session is the order's, not while it waited for one: what it reads is what it is about to restore from.
    private BuildOrder WithoutUnchangedRestores(BuildOrder order)
    {
        if (!order.Restore || order.RestoreStamp is not { } stampOf) return order;

        var unchanged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in order.Projects)
            if (stampOf(project) is { } stamp)
                lock (_gate)
                    if (_restoredAt.TryGetValue(RestoreKey(order, project), out var last) && last == stamp) unchanged.Add(project);

        return unchanged.Count == 0 ? order : order with { RestoreUnchanged = unchanged };
    }

    // Stamped after the restore, which writes the project.assets.json the stamp asks after.
    private void RememberRestores(BuildOrder order, BuildReport report)
    {
        if (order.RestoreStamp is not { } stampOf) return;
        foreach (var built in report.Projects.Where(p => p.Restore == BuiltProject.Restored))
            if (stampOf(built.Project) is { } stamp)
                lock (_gate) _restoredAt[RestoreKey(order, built.Project)] = stamp;
    }

    private static string RestoreKey(BuildOrder order, string project) =>
        Path.GetFullPath(project) + "|" + string.Join(";", order.Properties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                                                                          .Select(p => $"{p.Key}={p.Value}"));

    private void Finished(Session session)
    {
        lock (_gate)
        {
            if (--session.Running > 0) return;
            session.Draining = true;
        }

        try { _engine.EndSession(); }
        catch (Exception e) { _log($"closing the session failed: {e.Message}"); }

        lock (_gate)
        {
            _session = null;
            if (!_disposed) _lingering.Change(_linger, Timeout.InfiniteTimeSpan);
            Monitor.PulseAll(_gate);
        }
        _log("session closed");
    }

    private void ReleaseIdle()
    {
        lock (_gate)
        {
            if (_session is not null || _releasing || _disposed) return;
            _releasing = true;
        }

        try
        {
            _engine.ReleaseNodes();
            _log($"no build for {_linger.TotalMinutes:F0} min; idle workers released");
        }
        catch (Exception e) { _log($"releasing idle workers failed: {e.Message}"); }
        finally
        {
            lock (_gate)
            {
                _releasing = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Waits for the open session to close, then lets every idle worker go. Called once nothing is asked of the
    /// pool any more.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.PulseAll(_gate);
            while (_session is not null || _releasing) Monitor.Wait(_gate);
        }

        _lingering.Dispose();
        try { _engine.ReleaseNodes(); }
        catch (Exception e) { _log($"releasing workers failed: {e.Message}"); }
        _engine.Dispose();
    }
}

/// <summary>The resident process's two pools: <see cref="Builds"/> for what callers build, and <see cref="DesignTime"/> for
/// the design-time builds edit checks wait on.</summary>
internal sealed record BuildPools(BuildPool Builds, BuildPool DesignTime);
