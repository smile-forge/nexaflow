using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A runtime's file system: <c>::{connectionId}\…</c> reaches the connected host, every other path the shared file
/// system, and a feature never has to know which. The sync members are refused a remote path by name, so a caller
/// that would block on the network finds out at the call, not as a frozen window.
/// </summary>
[TestClass]
[CoversNode("io-locations-routing")]
public sealed class LocationFileSystemTests
{
    private const string Root = "::memory-nas";

    private MemoryLocationHost _host = null!;
    private LocationMount _location = null!;
    private VirtualFileSystem _shared = null!;
    private LocationFileSystem _vfs = null!;
    private string _local = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _shared = new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName);
        _local  = TestTemp.CreateSubdirectory("nexa-local-").FullName;
        File.WriteAllText(Path.Combine(_local, "here.txt"), "local");
        _shared.RegisterMount(new VirtualMount("docs", "Documents", _local));

        _host = new MemoryLocationHost("nas", TimeSpan.Zero);
        var provider = new MemoryLocationProvider(_host);
        var profile  = new ConnectionProfile("memory-nas", "memory", "NAS", "nas") { User = "adam" };
        _location = new LocationMount(profile, provider.Capabilities,
                                      new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null)));

        _vfs = new LocationFileSystem(_shared);
        _vfs.RegisterLocation(_location);
    }

    private async Task<List<LocationEntry>> ListAsync(string path)
    {
        var entries = new List<LocationEntry>();
        await foreach (var entry in _vfs.EnumerateAsync(path)) entries.Add(entry);
        return entries;
    }

    // ── Routing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ARemotePathReachesTheConnectedHost()
    {
        await _vfs.CreateDirectoryAsync($@"{Root}\home");
        await _vfs.WriteAllTextAsync($@"{Root}\home\notes.md", "remote");

        Assert.AreEqual("remote", await _vfs.ReadAllTextAsync($@"{Root}\home\notes.md"));
        Assert.IsTrue(await _vfs.IsDirectoryAsync($@"{Root}\home"));
        Assert.IsTrue(_host.With(nodes => nodes.ContainsKey("/home/notes.md")));
    }

    [TestMethod]
    public async Task LocalAndMountedPathsPassToTheSharedFileSystem()
    {
        Assert.AreEqual("local", await _vfs.ReadAllTextAsync(@"::docs\here.txt"));
        Assert.AreEqual("local", _vfs.ReadAllText(Path.Combine(_local, "here.txt")));
        Assert.AreEqual(VirtualBacking.PassThrough, _vfs.GetBacking(@"::docs\here.txt"));
    }

    [TestMethod]
    public void ASyncMemberRefusesARemotePathAndSaysWhichOne()
    {
        var refusal = Assert.ThrowsExactly<LocationRequiresAsyncException>(() => _vfs.Exists($@"{Root}\x.txt"));

        Assert.AreEqual(nameof(IVirtualFileSystem.Exists), refusal.Member);
        Assert.AreEqual($@"{Root}\x.txt", refusal.Path);
        Assert.ThrowsExactly<LocationRequiresAsyncException>(() => _vfs.EnumerateEntries(Root));
        Assert.ThrowsExactly<LocationRequiresAsyncException>(() => _vfs.CreateArchive(Path.Combine(_local, "a.zip"), [$@"{Root}\x.txt"]));
    }

    [TestMethod]
    public void ALetGoLocationNoLongerRoutes()
    {
        _vfs.UnregisterLocation("memory-nas");

        Assert.IsFalse(_vfs.Exists($@"{Root}\x.txt"));   // a stale id is a missing path, not a remote one
        Assert.AreEqual(VirtualBacking.Real, _vfs.GetBacking($@"{Root}\x.txt"));
    }

    // ── Names ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AServerNameNoWindowsPathCanHoldListsAsASegmentAndShowsAsItself()
    {
        await _vfs.WriteAllTextAsync($@"{Root}\a%3Ab.md", "colon");

        var listed = (await ListAsync(Root)).Single();
        Assert.AreEqual("a%3Ab.md", listed.Name);
        Assert.AreEqual("a:b.md", _vfs.GetDisplayName(Path.Combine(Root, listed.Name)));
        Assert.AreEqual("colon", await _vfs.ReadAllTextAsync(Path.Combine(Root, listed.Name)));
        Assert.IsTrue(_host.With(nodes => nodes.ContainsKey("/a:b.md")));
    }

    [TestMethod]
    public void TheRootShowsTheConnectionsLabelAndBreadcrumbsDecodeEachName()
    {
        Assert.AreEqual("NAS", _vfs.GetDisplayName(Root));

        var crumbs = _vfs.GetBreadcrumbs($@"{Root}\home\a%3Ab");
        CollectionAssert.AreEqual(
            new[] { ("NAS", Root), ("home", $@"{Root}\home"), ("a:b", $@"{Root}\home\a%3Ab") },
            crumbs.ToArray());
        Assert.AreEqual($@"{Root}\home", _vfs.GetParentPath($@"{Root}\home\a%3Ab"));
        Assert.IsNull(_vfs.GetParentPath(Root));
    }

    [TestMethod]
    public void TheAddressBarShowsTheLocationsUriAndReadsItBack()
    {
        var shown = _vfs.GetDisplayPath($@"{Root}\home\a%3Ab.md");

        Assert.AreEqual("memory://adam@nas/home/a:b.md", shown);
        Assert.AreEqual($@"{Root}\home\a%3Ab.md", _vfs.TryParseDisplayPath(shown));
        Assert.AreEqual(Root, _vfs.TryParseDisplayPath("memory://nas"));
        Assert.IsNull(_vfs.TryParseDisplayPath("memory://elsewhere/home"));
        Assert.AreEqual(_local, _vfs.GetDisplayPath(_local));
    }

    // ── What may be done with a remote path ─────────────────────────────────

    [TestMethod]
    public void ARemotePathIsBackedRemoteAndHasNoRealPath()
    {
        Assert.AreEqual(VirtualBacking.Remote, _vfs.GetBacking($@"{Root}\x.txt"));
        Assert.IsFalse(_vfs.GetBacking($@"{Root}\x.txt").IsFullyOnDisk());
        Assert.IsNull(_vfs.TryResolveReal($@"{Root}\x.txt"));
        Assert.AreEqual(_location.Capabilities, _vfs.GetCapabilities($@"{Root}\x.txt"));
    }

    [TestMethod]
    public async Task ARemoteFileHasNoCopyOnThisDiskToHandOver()
    {
        await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => await _vfs.MaterializeFileAsync($@"{Root}\x.txt"));
    }

    [TestMethod]
    public async Task ARemoteFileIsNeverAnArchiveToBrowse()
    {
        await _vfs.WriteAllBytesAsync($@"{Root}\bundle.zip", [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);

        Assert.IsFalse(await _vfs.IsContainerAsync($@"{Root}\bundle.zip"));
        Assert.IsNull(await _vfs.DescribeArchiveAsync($@"{Root}\bundle.zip"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            async () => await _vfs.ExtractAllAsync($@"{Root}\bundle.zip", _local));
    }

    [TestMethod]
    public async Task AMoveStaysWithinOneLocation()
    {
        await _vfs.WriteAllTextAsync($@"{Root}\a.txt", "a");

        await _vfs.MoveAsync($@"{Root}\a.txt", $@"{Root}\b.txt", replace: false);
        Assert.AreEqual("a", await _vfs.ReadAllTextAsync($@"{Root}\b.txt"));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            async () => await _vfs.MoveAsync($@"{Root}\b.txt", Path.Combine(_local, "b.txt"), replace: false));
    }

    [TestMethod]
    public async Task AWriteOverAChangedRemoteFileIsAConflict()
    {
        await _vfs.WriteAllTextAsync($@"{Root}\doc.md", "as read");
        var asRead = await _vfs.GetEntryAsync($@"{Root}\doc.md");
        await _vfs.WriteAllTextAsync($@"{Root}\doc.md", "theirs");

        await using var write = await _vfs.BeginWriteAsync($@"{Root}\doc.md", WriteMode.Replace, asRead);
        await write.Content.WriteAsync(Encoding.UTF8.GetBytes("mine"));
        var conflict = await Assert.ThrowsExactlyAsync<LocationException>(async () => await write.CommitAsync(CancellationToken.None));

        Assert.AreEqual(LocationError.Conflict, conflict.Error);
    }

    [TestMethod]
    public async Task ALocationWithoutWatchIsPolled()
        => Assert.IsNull(await _vfs.WatchChangesAsync(Root, recursive: false));

    // ── Registration ────────────────────────────────────────────────────────

    [TestMethod]
    public void ReRegisteringTheSameLocationStaysSilent()
    {
        int raised = 0;
        _vfs.LocationsChanged += () => raised++;

        _vfs.RegisterLocation(_location);

        Assert.AreEqual(0, raised);
        Assert.AreEqual(1, _vfs.Locations.Count);
    }

    [TestMethod]
    public void AnIdNamesExactlyOnePlace()
    {
        var clash = _location with { Connection = _location.Connection with { Id = "docs" } };

        Assert.ThrowsExactly<ArgumentException>(() => _vfs.RegisterLocation(clash));
        Assert.ThrowsExactly<ArgumentException>(() => _vfs.RegisterMount(new VirtualMount("memory-nas", "x", _local)));
        Assert.ThrowsExactly<ArgumentException>(() => _vfs.RegisterLocation(
            _location with { Connection = _location.Connection with { Id = "a:b" } }));
    }
}
