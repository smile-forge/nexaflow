using System.Collections.Concurrent;
using Nexaflow.Services.Initiatives.Cli.Building;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// The schedule every caller's builds share in the resident process: who joins the open session, who waits for the next
/// one, what a caller leaving does to the others, and when the workers are let go. Proved against an engine that builds
/// nothing, so each rule is a sequence of starts and finishes rather than a race between real builds.
/// </summary>
[TestClass]
[CoversNode("nfi-build")]
public class BuildPoolTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NotYet   = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public void TwoTrees_BuildInOneSession()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));

        var first  = Build(pool, "tree-a");
        var second = Build(pool, "tree-b");
        Assert.IsTrue(engine.WaitForStarts(2), "the second tree joins the session the first opened");
        Assert.AreEqual(1, engine.Sessions);

        engine.Finish("tree-a");
        engine.Finish("tree-b");
        Assert.IsTrue(first.Wait(Patience) && second.Wait(Patience));
        Assert.IsTrue(engine.WaitFor(() => engine.Ended == 1), "the session closes once both have finished");
    }

    [TestMethod]
    public void OneTree_BuildsAgainOnlyInASessionOfItsOwn()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));

        var first = Build(pool, "tree-a");
        Assert.IsTrue(engine.WaitForStarts(1));

        var again = Build(pool, "tree-a");
        Thread.Sleep(NotYet);
        Assert.AreEqual(1, engine.Started.Count, "a session that built the tree would hand its second build the first one's results");

        engine.Finish("tree-a");
        Assert.IsTrue(first.Wait(Patience));
        Assert.IsTrue(engine.WaitForStarts(2), "it builds once the session that built it has closed");
        Assert.AreEqual(2, engine.Sessions);

        engine.Finish("tree-a");
        Assert.IsTrue(again.Wait(Patience));
    }

    [TestMethod]
    public void ASessionSomeoneWaitsOn_TakesNobodyNew()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));

        var first = Build(pool, "tree-a");
        Assert.IsTrue(engine.WaitForStarts(1));
        var again = Build(pool, "tree-a");
        Thread.Sleep(NotYet);

        var other = Build(pool, "tree-b");
        Thread.Sleep(NotYet);
        Assert.AreEqual(1, engine.Started.Count, "joining a draining session would keep it open past the build waiting on it");

        engine.Finish("tree-a");
        Assert.IsTrue(engine.WaitForStarts(3), "both start in the next session");
        Assert.AreEqual(2, engine.Sessions);

        engine.Finish("tree-a");
        engine.Finish("tree-b");
        Assert.IsTrue(Task.WaitAll([first, again, other], Patience));
    }

    [TestMethod]
    public void ACallerLeaving_CancelsASessionItHasToItself()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));
        using var leaving = new CancellationTokenSource();

        var build = Build(pool, "tree-a", leaving.Token);
        Assert.IsTrue(engine.WaitForStarts(1));
        leaving.Cancel();

        Assert.IsTrue(build.Wait(Patience), "the caller is answered at once");
        Assert.IsNotNull(build.Result.Problem);
        Assert.AreEqual(1, engine.Cancelled);
    }

    [TestMethod]
    public void ACallerLeaving_LeavesASharedSessionToTheOthers()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));
        using var leaving = new CancellationTokenSource();

        var gone   = Build(pool, "tree-a", leaving.Token);
        var staying = Build(pool, "tree-b");
        Assert.IsTrue(engine.WaitForStarts(2));
        leaving.Cancel();

        Assert.IsTrue(gone.Wait(Patience));
        Assert.AreEqual(0, engine.Cancelled, "cancelling the session would cancel the other tree's build too");

        engine.Finish("tree-b");
        Assert.IsTrue(staying.Wait(Patience));
        Assert.IsTrue(staying.Result.Succeeded);
        engine.Finish("tree-a");   // the build its caller left still runs to its end, and holds the session until it does
    }

    [TestMethod]
    public void IdleWorkers_AreLetGoAfterTheLinger_AndNotWhileABuildRuns()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromMilliseconds(200));

        var build = Build(pool, "tree-a");
        Assert.IsTrue(engine.WaitForStarts(1));
        Thread.Sleep(NotYet + NotYet);
        Assert.AreEqual(0, engine.Released, "the linger starts when the session closes");

        engine.Finish("tree-a");
        Assert.IsTrue(build.Wait(Patience));
        Assert.IsTrue(engine.WaitFor(() => engine.Released == 1), "released once no build came for the linger");
    }

    [TestMethod]
    public void ATreeTheEngineCannotBuild_IsRefusedWithoutASession()
    {
        var engine = new ScriptedEngine { Refusal = "no SDK" };
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));

        var report = pool.Build(Order("tree-a"), CancellationToken.None);

        Assert.AreEqual("no SDK", report.Problem);
        Assert.AreEqual(0, engine.Sessions);
    }

    [TestMethod]
    public void ABuildThatCannotStart_StillClosesItsSession()
    {
        var engine = new ScriptedEngine { StartFails = true };
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));

        var report = pool.Build(Order("tree-a"), CancellationToken.None);

        Assert.IsNotNull(report.Problem);
        Assert.IsTrue(engine.WaitFor(() => engine.Ended == 1), "a session held open by a build that never ran would stop every build after it");
    }

    [TestMethod]
    public void Disposing_LetsTheWorkersGo()
    {
        var engine = new ScriptedEngine();
        var pool   = new BuildPool(engine, TimeSpan.FromHours(1));
        var build  = Build(pool, "tree-a");
        Assert.IsTrue(engine.WaitForStarts(1));
        engine.Finish("tree-a");
        Assert.IsTrue(build.Wait(Patience));

        pool.Dispose();

        Assert.AreEqual(1, engine.Released);
        Assert.IsTrue(engine.Disposed);
    }

    [TestMethod]
    public void ARestore_IsLeftOut_UntilWhatItReadsChanges()
    {
        var engine = new ScriptedEngine();
        using var pool = new BuildPool(engine, TimeSpan.FromHours(1));
        var stamp = "first";
        var order = Order("tree-a") with { Restore = true, RestoreStamp = _ => stamp };

        BuildOnce(pool, engine, order);
        Assert.AreEqual(0, engine.LastOrder!.RestoreUnchanged.Count, "nothing is known of its last restore");

        BuildOnce(pool, engine, order);
        Assert.AreEqual(1, engine.LastOrder!.RestoreUnchanged.Count, "restored already, and nothing it reads has changed");

        stamp = "second";
        BuildOnce(pool, engine, order);
        Assert.AreEqual(0, engine.LastOrder!.RestoreUnchanged.Count, "something it reads changed");

        BuildOnce(pool, engine, order with { RestoreStamp = _ => null });
        Assert.AreEqual(0, engine.LastOrder!.RestoreUnchanged.Count, "what it reads cannot be named, so it restores");
    }

    private static void BuildOnce(BuildPool pool, ScriptedEngine engine, BuildOrder order)
    {
        var build = Task.Factory.StartNew(() => pool.Build(order, CancellationToken.None), CancellationToken.None,
                                          TaskCreationOptions.LongRunning, TaskScheduler.Default);
        engine.Finish(order.CodeRoot);
        Assert.IsTrue(build.Wait(Patience));
        Assert.IsTrue(engine.WaitFor(() => engine.Ended == engine.Sessions), "its session closed, so the next build opens one");
    }

    private static Task<BuildReport> Build(BuildPool pool, string tree, CancellationToken cancellation = default) =>
        Task.Factory.StartNew(() => pool.Build(Order(tree), cancellation), CancellationToken.None, TaskCreationOptions.LongRunning,
                              TaskScheduler.Default);

    private static BuildOrder Order(string tree) =>
        new(tree, [$"{tree}/app.csproj"], ["Build"], new Dictionary<string, string>(), Restore: false);

    /// <summary>An engine whose builds finish when the test says, one tree at a time, in the order they started.</summary>
    private sealed class ScriptedEngine : IBuildEngine
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(BuildOrder Order, TaskCompletionSource<BuildReport> Run)>> _running = new();
        private int _sessions, _ended, _cancelled, _released;

        public string? Refusal { get; init; }
        public bool StartFails { get; init; }
        public ConcurrentQueue<string> Started { get; } = new();
        public int Sessions  => Volatile.Read(ref _sessions);
        public int Ended     => Volatile.Read(ref _ended);
        public int Cancelled => Volatile.Read(ref _cancelled);
        public int Released  => Volatile.Read(ref _released);
        public bool Disposed { get; private set; }
        public BuildOrder? LastOrder { get; private set; }

        public int Nodes => 2;
        public string? Admit(string codeRoot) => Refusal;
        public void BeginSession() => Interlocked.Increment(ref _sessions);
        public void EndSession() => Interlocked.Increment(ref _ended);
        public void ReleaseNodes() => Interlocked.Increment(ref _released);
        public void Dispose() => Disposed = true;

        public void CancelSession()
        {
            Interlocked.Increment(ref _cancelled);
            foreach (var queue in _running.Values)
                while (queue.TryDequeue(out var running)) running.Run.TrySetResult(Report(running.Order, false));
        }

        public Task<BuildReport> Start(BuildOrder order)
        {
            if (StartFails) throw new InvalidOperationException("no session");
            var run = new TaskCompletionSource<BuildReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            LastOrder = order;
            _running.GetOrAdd(order.CodeRoot, _ => new()).Enqueue((order, run));
            Started.Enqueue(order.CodeRoot);
            return run.Task;
        }

        public void Finish(string tree)
        {
            Assert.IsTrue(WaitFor(() => _running.TryGetValue(tree, out var q) && !q.IsEmpty), $"{tree} is running");
            Assert.IsTrue(_running[tree].TryDequeue(out var running));
            running.Run.SetResult(Report(running.Order, true));
        }

        public bool WaitForStarts(int count) => WaitFor(() => Started.Count >= count);

        public bool WaitFor(Func<bool> condition) => SpinWait.SpinUntil(condition, Patience);

        // Each entry restored, as an engine reports it, unless the pool said its restore could be left out.
        private static BuildReport Report(BuildOrder order, bool succeeded) =>
            new(succeeded, [.. order.Projects.Select(p => new BuiltProject(p, succeeded, [], Restore:
                    !order.Restore ? "" : order.RestoreUnchanged.Contains(p) ? BuiltProject.Unchanged : BuiltProject.Restored))],
                [], 0, 0, 2);
    }
}
