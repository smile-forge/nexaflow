using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// What the backends find on this machine: one connection per id, kept current as a backend says its set changed, and a
/// backend that cannot look this time keeps what it found last rather than making its locations vanish.
/// </summary>
[TestClass]
[CoversNode("io-locations-discovery")]
public sealed class DiscoveredLocationsTests
{
    private sealed class Finder(string kind) : ILocationProvider, ILocationDiscovery
    {
        private ConnectionProfile[] _found = [];
        private volatile bool _broken;

        public string Kind => kind;

        public LocationCapabilities Capabilities => LocationCapabilities.None;

        public ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ConnectionProfile>> DiscoverAsync(CancellationToken ct)
            => _broken
                ? ValueTask.FromException<IReadOnlyList<ConnectionProfile>>(new IOException("the registry is locked"))
                : ValueTask.FromResult<IReadOnlyList<ConnectionProfile>>(Volatile.Read(ref _found));

        public event Action? Changed;

        public void Find(params ConnectionProfile[] found)
        {
            _broken = false;
            Volatile.Write(ref _found, found);
            Changed?.Invoke();
        }

        public void Break()
        {
            _broken = true;
            Changed?.Invoke();
        }
    }

    private static ConnectionProfile Distro(string id) => new(id, "wsl", id, id);

    [TestMethod]
    public async Task WhatEveryBackendFindsIsListed_OnePerId()
    {
        var registry = new LocationProviderRegistry();
        var first    = new Finder("wsl");
        var second   = new Finder("other");
        registry.Register(first);
        registry.Register(second);
        first.Find(Distro("wsl-ubuntu"), Distro("wsl-debian"));
        second.Find(Distro("wsl-ubuntu") with { Label = "Shadow" });

        var discovered = new DiscoveredLocations(registry);
        await discovered.StartAsync();

        CollectionAssert.AreEquivalent(new[] { "wsl-ubuntu", "wsl-debian" }, discovered.All.Select(c => c.Id).ToArray());
        Assert.AreEqual("wsl-ubuntu", discovered.All.Single(c => c.Id == "wsl-ubuntu").Label, "the first backend to find an id keeps it");
    }

    [TestMethod]
    public async Task ABackendSayingItsSetChanged_IsLookedAtAgain()
    {
        var registry = new LocationProviderRegistry();
        var finder   = new Finder("wsl");
        registry.Register(finder);
        var discovered = new DiscoveredLocations(registry);
        await discovered.StartAsync();
        int changes = 0;
        discovered.Changed += () => Interlocked.Increment(ref changes);

        finder.Find(Distro("wsl-alpine"));
        await discovered.RefreshAsync();

        Assert.AreEqual("wsl-alpine", discovered.All.Single().Id);
        Assert.IsTrue(changes >= 1);
    }

    [TestMethod]
    public async Task ABackendThatCannotLook_KeepsWhatItFoundLast()
    {
        var registry = new LocationProviderRegistry();
        var finder   = new Finder("wsl");
        registry.Register(finder);
        finder.Find(Distro("wsl-ubuntu"));
        var discovered = new DiscoveredLocations(registry);
        await discovered.StartAsync();

        finder.Break();
        await discovered.RefreshAsync();

        Assert.AreEqual("wsl-ubuntu", discovered.All.Single().Id);
    }

    [TestMethod]
    public async Task ABackendRegisteredLater_IsLookedAtToo()
    {
        var registry   = new LocationProviderRegistry();
        var discovered = new DiscoveredLocations(registry);
        await discovered.StartAsync();
        Assert.AreEqual(0, discovered.All.Count);

        var finder = new Finder("wsl");
        finder.Find(Distro("wsl-ubuntu"));
        registry.Register(finder);
        await discovered.RefreshAsync();

        Assert.AreEqual("wsl-ubuntu", discovered.All.Single().Id);
    }
}
