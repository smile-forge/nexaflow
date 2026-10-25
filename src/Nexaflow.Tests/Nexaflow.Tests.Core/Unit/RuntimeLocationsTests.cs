using System;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A runtime's mounts: its workspace's connections on its file system, each with its pooled session, following the
/// connections as they change and keeping a session across a switch to a workspace that has the same connection.
/// </summary>
[TestClass]
[CoversNode("io-locations-pool")]
public sealed class RuntimeLocationsTests
{
    private MemoryLocationHost _host = null!;
    private LocationFileSystem _vfs = null!;
    private RuntimeLocations _locations = null!;
    private WorkspaceConnections _connections = null!;
    private ConnectionProfile _nas = null!;

    [TestInitialize]
    public void Setup()
    {
        // A host of this test's own name, so parallel tests never share one.
        _host = new MemoryLocationHost("nas-" + Guid.NewGuid().ToString("N")[..8], TimeSpan.Zero);
        MemoryLocationHost.Publish(_host);
        _nas = new ConnectionProfile("memory-" + _host.Name, "memory", "NAS", _host.Name);

        var registry = new LocationProviderRegistry();
        registry.Register(new MemoryLocationProvider());
        var secrets = new StoredCredentialSource(new NoSecrets());

        _vfs         = new LocationFileSystem(VirtualFileSystem.Instance);
        _locations   = new RuntimeLocations(_vfs, new LocationSessionPool(registry, secrets));
        _connections = new WorkspaceConnections(TestTemp.CreateSubdirectory("nexa-runtime-").FullName, new NoSecrets());
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

    private string Root => VirtualMount.RootFor(_nas.Id);

    [TestMethod]
    public async Task AWorkspacesConnectionIsMounted_AndAFileOnItReadsThroughTheRuntimeFileSystem()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);

        CollectionAssert.AreEqual(new[] { _nas.Id }, _vfs.Locations.Select(l => l.Id).ToArray());
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");
        Assert.AreEqual("remote", await _vfs.ReadAllTextAsync($@"{Root}\notes.md"));
    }

    [TestMethod]
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
    public async Task SwitchingToAWorkspaceWithTheSameConnectionKeepsItsSession()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");

        var other = new WorkspaceConnections(TestTemp.CreateSubdirectory("nexa-runtime-other-").FullName, new NoSecrets());
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
    public void AConnectionNoBackendReachesIsMountedNowhere_AndSaysWhy()
    {
        _connections.Save(new ConnectionProfile("sftp-box", "sftp", "Box", "box"));
        _locations.Follow(_connections);

        Assert.AreEqual(0, _vfs.Locations.Count);
        StringAssert.Contains(_locations.Unreachable.Single().Reason, "sftp");
    }

    [TestMethod]
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
    public async Task DisposingUnmountsEverything_AndClosesTheSessions()
    {
        _connections.Save(_nas);
        _locations.Follow(_connections);
        await _vfs.WriteAllTextAsync($@"{Root}\notes.md", "remote");

        await _locations.DisposeAsync();

        Assert.AreEqual(0, _vfs.Locations.Count);
        Assert.AreEqual(1, _host.Disconnects);
    }
}
