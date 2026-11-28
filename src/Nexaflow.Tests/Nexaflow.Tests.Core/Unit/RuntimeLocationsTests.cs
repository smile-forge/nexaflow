using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A runtime's mounts: its workspace's connections and the ones found on this machine on its file system, each with its
/// pooled session, following both as they change and keeping a session across a switch to a workspace that has the same
/// connection.
/// </summary>
[TestClass]
public sealed class RuntimeLocationsTests
{
    private MemoryLocationHost _host = null!;
    private LocationFileSystem _vfs = null!;
    private RuntimeLocations _locations = null!;
    private WorkspaceConnections _connections = null!;
    private ConnectionProfile _nas = null!;
    private FindsHosts _finder = null!;
    private DiscoveredLocations _discovered = null!;

    [TestInitialize]
    public void Setup()
    {
        // A host of this test's own name, so parallel tests never share one.
        _host = new MemoryLocationHost("nas-" + Guid.NewGuid().ToString("N")[..8], TimeSpan.Zero);
        MemoryLocationHost.Publish(_host);
        _nas = new ConnectionProfile("memory-" + _host.Name, "memory", "NAS", _host.Name);

        var registry = new LocationProviderRegistry();
        _finder = new FindsHosts();
        registry.Register(_finder);
        var secrets = new StoredCredentialSource(new NoSecrets());

        _vfs         = new LocationFileSystem(VirtualFileSystem.Instance);
        _discovered  = new DiscoveredLocations(registry);
        _locations   = new RuntimeLocations(_vfs, new LocationSessionPool(registry, secrets), _discovered);
        _connections = new WorkspaceConnections(TestTemp.CreateSubdirectory("nexa-runtime-").FullName, TestRuntimes.Config, new NoSecrets());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _locations.DisposeAsync();
        MemoryLocationHost.Withdraw(_host.Name);
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    /// <summary>The in-memory backend, finding on this machine whatever the test says it finds.</summary>
    private sealed class FindsHosts : ILocationProvider, ILocationDiscovery
    {
        private readonly MemoryLocationProvider _memory = new();
        private ConnectionProfile[] _found = [];

        public string Kind => _memory.Kind;

        public LocationCapabilities Capabilities => _memory.Capabilities;

        public ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
            => _memory.ConnectAsync(profile, credentials, ct);

        public ValueTask<IReadOnlyList<ConnectionProfile>> DiscoverAsync(CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<ConnectionProfile>>(Volatile.Read(ref _found));

        public event Action? Changed;

        public void Find(params ConnectionProfile[] found)
        {
            Volatile.Write(ref _found, found);
            Changed?.Invoke();
        }
    }

    private string Root => VirtualMount.RootFor(_nas.Id);

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public async Task AWorkspacesConnectionIsMounted_AndAFileOnItReadsThroughTheRuntimeFileSystem()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);

        CollectionAssert.AreEqual(new[] { _nas.Id }, _vfs.Locations.Select(l => l.Id).ToArray());
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");
        Assert.AreEqual("remote", await _vfs.ReadAllTextAsync($@"{Root}\notes.md"));
    }

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public void AConnectionSavedOrRemovedLaterIsMountedOrUnmounted()
    {
        _locations.Follow(_connections);
        Assert.AreEqual(0, _vfs.Locations.Count);

        _connections.Save(_nas);
        Assert.AreEqual(1, _vfs.Locations.Count);

        _connections.Remove(_nas.Id);
        Assert.AreEqual(0, _vfs.Locations.Count);
    }

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public async Task SwitchingToAWorkspaceWithTheSameConnectionKeepsItsSession()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");

        var other = new WorkspaceConnections(TestTemp.CreateSubdirectory("nexa-runtime-other-").FullName, TestRuntimes.Config, new NoSecrets());
        other.Save(_nas with { Label = "NAS at home" });
        _locations.Follow(other);
        Assert.AreEqual("remote", await _vfs.ReadAllTextAsync($@"{Root}\notes.md"));

        Assert.AreEqual(1, _host.Connects);
        Assert.AreEqual(0, _host.Disconnects);
        Assert.AreEqual("NAS at home", _vfs.Locations.Single().Label);

        _connections.Remove(_nas.Id);
        Assert.AreEqual(1, _vfs.Locations.Count, "the workspace left behind is no longer followed");
    }

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public void AConnectionNoBackendReachesIsMountedNowhere_AndSaysWhy()
    {
        _connections.Save(new ConnectionProfile("sftp-box", "sftp", "Box", "box"));
        _locations.Follow(_connections);

        Assert.AreEqual(0, _vfs.Locations.Count);
        StringAssert.Contains(_locations.Unreachable.Single().Reason, "sftp");
    }

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public void AConnectionWhoseIdIsAlreadyAMountsIsMountedNowhere()
    {
        var clash = "clash-" + _host.Name;
        VirtualFileSystem.Instance.RegisterMount(new VirtualMount(clash, "Clash", TestTemp.Root));
        try
        {
            _connections.Save(_nas with { Id = clash });
            _locations.Follow(_connections);

            Assert.AreEqual(0, _vfs.Locations.Count);
            Assert.AreEqual(clash, _locations.Unreachable.Single().Connection.Id);
        }
        finally { VirtualFileSystem.Instance.UnregisterMount(clash); }
    }

    [TestMethod]
    [CoversNode("io-locations-pool")]
    public async Task DisposingUnmountsEverything_AndClosesTheSessions()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");

        await _locations.DisposeAsync();

        Assert.AreEqual(0, _vfs.Locations.Count);
        Assert.AreEqual(1, _host.Disconnects);
    }

    [TestMethod]
    [CoversNode("io-locations-discovery")]
    public async Task AConnectionFoundOnThisMachineIsMounted_AndNeverSaved()
    {
        _locations.Follow(_connections);
        _finder.Find(_nas);
        await _discovered.StartAsync();

        Assert.IsTrue(_vfs.Locations.Any(l => l.Id == _nas.Id), "what is found is mounted");
        Assert.AreEqual(0, _connections.All.Count, "nothing found is written to the workspace");
        Assert.IsTrue(await _vfs.ExistsAsync(Root), "and it is reached through the runtime's file system");
    }

    [TestMethod]
    [CoversNode("io-locations-discovery")]
    public async Task ASavedConnectionKeepsItsIdOverAFoundOne()
    {
        _connections.Save(_nas with { Label = "Saved" });
        _locations.Follow(_connections);
        _finder.Find(_nas with { Label = "Found" });
        await _discovered.StartAsync();

        var mounted = _vfs.Locations.Single(l => l.Id == _nas.Id);
        Assert.AreEqual("Saved", mounted.Label);
    }

    [TestMethod]
    [CoversNode("io-locations-discovery")]
    public async Task AFoundConnectionThatGoesIsUnmounted()
    {
        _locations.Follow(_connections);
        _finder.Find(_nas);
        await _discovered.StartAsync();
        Assert.IsTrue(_vfs.Locations.Any(l => l.Id == _nas.Id));

        _finder.Find();
        await _discovered.RefreshAsync();

        Assert.IsFalse(_vfs.Locations.Any(l => l.Id == _nas.Id), "a distro uninstalled leaves no location behind");
    }

    [TestMethod]
    [CoversNode("io-locations-discovery")]
    public void WhatCannotBeMountedIsListedOnTheFileSystemWithWhy()
    {
        _connections.Save(new ConnectionProfile("nowhere-1", "nowhere", "Nowhere", "host"));
        _locations.Follow(_connections);

        var listed = ((ILocationDirectory)_vfs).Unreachable.Single();
        Assert.AreEqual("nowhere-1", listed.Connection.Id);
        StringAssert.Contains(listed.Reason, "nowhere");
    }
}
