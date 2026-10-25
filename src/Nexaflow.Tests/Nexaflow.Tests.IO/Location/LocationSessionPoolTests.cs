using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// One runtime's live sessions: one per connection, shared by every set that holds it, kept across a set swapped for a
/// fresh one, closed once quiet for the idle time with nothing left open, and reconnected after a dropped link — all of it
/// out of sight of whoever reads a file.
/// </summary>
[TestClass]
[CoversNode("io-locations-pool")]
public sealed class LocationSessionPoolTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);

    private MemoryLocationHost _host = null!;
    private ManualClock _clock = null!;
    private LocationProviderRegistry _registry = null!;
    private LocationSessionPool _pool = null!;

    [TestInitialize]
    public void Setup()
    {
        _host     = new MemoryLocationHost("nas", TimeSpan.Zero);
        _clock    = new ManualClock();
        _registry = new LocationProviderRegistry();
        _registry.Register(new MemoryLocationProvider(_host));
        _pool     = new LocationSessionPool(_registry, new StoredCredentials(null), Idle, _clock);
    }

    [TestCleanup]
    public async Task Cleanup() => await _pool.DisposeAsync();

    private static ConnectionProfile Nas(string label = "NAS") => new("memory-nas", "memory", label, "nas") { User = "adam" };

    private static async Task<ILocationSession> SessionOf(LocationSet set) => await set.Mounts[0].Sessions.GetSessionAsync(default);

    [TestMethod]
    public async Task TwoSetsHoldingOneConnectionShareOneSession()
    {
        var first  = _pool.Acquire([Nas()]);
        var second = _pool.Acquire([Nas()]);

        Assert.AreSame(await SessionOf(first), await SessionOf(second));
        Assert.AreEqual(1, _host.Connects);
        Assert.AreEqual(1, _pool.EntryCount);
    }

    [TestMethod]
    public async Task HoldingTheFreshSetBeforeLettingTheOldGo_KeepsTheSession_EvenThroughARename()
    {
        var old = _pool.Acquire([Nas()]);
        await (await SessionOf(old)).GetEntryAsync("/", default);

        var fresh = _pool.Acquire([Nas("Home NAS")]);
        _pool.Release(old);
        await (await SessionOf(fresh)).GetEntryAsync("/", default);

        Assert.AreEqual(1, _host.Connects, "a label is for people; the endpoint is the same");
        Assert.AreEqual(0, _host.Disconnects);
        Assert.AreEqual("Home NAS", fresh.Mounts[0].Label);
    }

    [TestMethod]
    public async Task LettingGoOfTheLastSetClosesTheSession()
    {
        var set = _pool.Acquire([Nas()]);
        await SessionOf(set);

        _pool.Release(set);

        Assert.AreEqual(1, _host.Disconnects);
        Assert.AreEqual(0, _pool.EntryCount);
    }

    [TestMethod]
    public void AnotherEndpointIsAnotherSession()
    {
        _pool.Acquire([Nas()]);
        _pool.Acquire([Nas() with { User = "eve" }]);

        Assert.AreEqual(2, _pool.EntryCount);
    }

    [TestMethod]
    public void AConnectionNoBackendReachesIsListedUnreachable_AndMountedNowhere()
    {
        var set = _pool.Acquire([new ConnectionProfile("sftp-box", "sftp", "Box", "box")]);

        Assert.AreEqual(0, set.Mounts.Count);
        StringAssert.Contains(set.Unreachable.Single().Reason, "sftp");
        Assert.AreEqual(0, _pool.EntryCount);
    }

    [TestMethod]
    public async Task AQuietSessionClosesAfterItsIdleTime_AndTheNextUseConnectsAgain()
    {
        var set = _pool.Acquire([Nas()]);
        await (await SessionOf(set)).GetEntryAsync("/", default);

        _clock.Advance(Idle - TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, _host.Disconnects, "not idle long enough yet");

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, _host.Disconnects);

        Assert.IsNotNull(await (await SessionOf(set)).GetEntryAsync("/", default));
        Assert.AreEqual(2, _host.Connects);
    }

    [TestMethod]
    public async Task AnOpenStreamKeepsTheSessionFromIdling()
    {
        _host.With(nodes => nodes["/a.txt"] = new MemoryLocationHost.Node(IsDirectory: false, "hello"u8.ToArray(), DateTime.Now));
        var set = _pool.Acquire([Nas()]);
        var stream = await (await SessionOf(set)).OpenReadAsync("/a.txt", ReadIntent.Whole, default);

        _clock.Advance(Idle * 2);
        Assert.AreEqual(0, _host.Disconnects, "a read still under way is use");

        await stream.DisposeAsync();
        _clock.Advance(Idle);
        Assert.AreEqual(1, _host.Disconnects);
    }

    [TestMethod]
    public async Task ASessionWhoseLinkDroppedIsLetGo_AndTheNextUseReconnects()
    {
        var set = _pool.Acquire([Nas()]);
        var dropped = await SessionOf(set);

        _host.Drop();
        var failure = await Assert.ThrowsExactlyAsync<LocationException>(async () => await dropped.GetEntryAsync("/", default));
        Assert.AreEqual(LocationError.Disconnected, failure.Error);

        var again = await SessionOf(set);
        Assert.AreNotSame(dropped, again);
        Assert.IsNotNull(await again.GetEntryAsync("/", default));
        Assert.AreEqual(2, _host.Connects);
    }

    [TestMethod]
    public async Task CallersAskingAtOnceShareOneConnect()
    {
        var slow     = new MemoryLocationHost("slow", TimeSpan.FromMilliseconds(50));
        var registry = new LocationProviderRegistry();
        registry.Register(new MemoryLocationProvider(slow));
        await using var pool = new LocationSessionPool(registry, new StoredCredentials(null), Idle, _clock);
        var set = pool.Acquire([new ConnectionProfile("memory-slow", "memory", "Slow", "slow")]);

        var sessions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => set.Mounts[0].Sessions.GetSessionAsync(default).AsTask()));

        Assert.AreEqual(1, sessions.Distinct().Count());
        Assert.AreEqual(1, slow.Connects);
    }

    [TestMethod]
    public async Task DisposingThePoolClosesEverySession()
    {
        var set = _pool.Acquire([Nas()]);
        await SessionOf(set);

        await _pool.DisposeAsync();

        Assert.AreEqual(1, _host.Disconnects);
        var failure = await Assert.ThrowsExactlyAsync<LocationException>(async () => await SessionOf(set));
        Assert.AreEqual(LocationError.Disconnected, failure.Error);
    }

    [TestMethod]
    public async Task AFileReadThroughTheRuntimeFileSystemNeverSeesTheSessionComeAndGo()
    {
        var set = _pool.Acquire([Nas()]);
        var vfs = new LocationFileSystem(new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-pooltemp-").FullName));
        vfs.RegisterLocation(set.Mounts[0]);

        await vfs.WriteAllTextAsync(@"::memory-nas\notes.md", "remote");
        _clock.Advance(Idle);
        Assert.AreEqual(1, _host.Disconnects, "the session closed while quiet");

        Assert.AreEqual("remote", await vfs.ReadAllTextAsync(@"::memory-nas\notes.md"));
        Assert.AreEqual(2, _host.Connects);
    }

    /// <summary>A clock that moves only when told, firing each timer that falls due.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers) _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            List<ManualTimer> due;
            lock (_timers) due = [.. _timers.Where(t => t.Due is { } at && at <= _now)];
            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void Fire()
            {
                Due = null;
                callback(state);
            }

            public void Dispose() => Due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
