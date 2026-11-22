using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A location this machine also reaches as a Windows folder — a WSL distro under <c>\\wsl.localhost</c>: its files are
/// handed to a path-only reader at that folder's path with no copy, a path typed in either Windows form opens the location,
/// and a name Windows cannot hold maps to the name the folder shows for it. A location without one stays remote.
/// </summary>
[TestClass]
[CoversNode("io-locations-routing")]
public sealed class LocationNativePathTests
{
    private const string Root = "::wsl-probe";

    private LocationFileSystem _vfs = null!;
    private MemoryLocationProvider _provider = null!;

    /// <summary>As WSL's 9P stands in for a character no Windows name can hold: one from the private-use area.</summary>
    private sealed class PrivateUseNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection)
            => [$@"\\wsl.localhost\{connection.Host}", $@"\\wsl$\{connection.Host}"];

        public string ToNativeName(string name) => name.Replace(':', '\uF03A');

        public string FromNativeName(string nativeName) => nativeName.Replace('\uF03A', ':');
    }

    [TestInitialize]
    public void Setup()
    {
        _provider = new MemoryLocationProvider(new MemoryLocationHost("probe", TimeSpan.Zero));
        var profile = new ConnectionProfile("wsl-probe", "wsl", "Probe", "Probe");
        var names   = new PrivateUseNames();
        var mount   = new LocationMount(profile, _provider.Capabilities | LocationCapabilities.CaseSensitive,
                                        new MemoryLocationSessionSource(_provider, profile, new StoredCredentials(null)))
        {
            NativeRoots = names.GetNativeRoots(profile),
            NativePaths = names,
        };

        _vfs = new LocationFileSystem(new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName));
        _vfs.RegisterLocation(mount);
    }

    [TestMethod]
    public void AFileOnALocationThisMachineReaches_IsFullyOnDiskAtItsNativePath()
    {
        Assert.AreEqual(VirtualBacking.PassThrough, _vfs.GetBacking($@"{Root}\home\adam\notes.md"));
        Assert.AreEqual(@"\\wsl.localhost\Probe\home\adam\notes.md", _vfs.ResolveOnDisk($@"{Root}\home\adam\notes.md"));
        Assert.AreEqual(@"\\wsl.localhost\Probe", _vfs.TryResolveReal(Root));
    }

    [TestMethod]
    public void ANameWindowsCannotHold_IsHandedOverAsTheNameTheFolderShows()
        => Assert.AreEqual("\\\\wsl.localhost\\Probe\\home\\a\uF03Ab.txt", _vfs.TryResolveReal($@"{Root}\home\a%3Ab.txt"));

    [TestMethod]
    public void APathTypedInEitherWindowsForm_OpensTheLocation()
    {
        Assert.AreEqual($@"{Root}\home\a%3Ab.txt", _vfs.TryToVirtual("\\\\wsl$\\Probe\\home\\a\uF03Ab.txt"));
        Assert.AreEqual($@"{Root}\etc", _vfs.TryToVirtual(@"\\wsl.localhost\Probe\etc"));
        Assert.AreEqual(Root, _vfs.TryToVirtual(@"\\WSL.LOCALHOST\probe"), "a share's name is read as Windows reads it");
        Assert.IsFalse((_vfs.TryToVirtual(@"\\wsl.localhost\Probey\etc") ?? "").StartsWith(Root, StringComparison.Ordinal),
                       "another distro whose name starts the same is not this one");
    }

    [TestMethod]
    public async Task MaterializingHandsOverTheNativePath_WithNoCopy()
        => Assert.AreEqual(@"\\wsl.localhost\Probe\srv\clip.mp4", await _vfs.MaterializeFileAsync($@"{Root}\srv\clip.mp4"));

    [TestMethod]
    public async Task ALocationReachedOnlyThroughItsSession_StaysRemote()
    {
        var profile = new ConnectionProfile("memory-far", "memory", "Far", "far");
        _vfs.RegisterLocation(new LocationMount(profile, _provider.Capabilities,
                                                new MemoryLocationSessionSource(_provider, profile, new StoredCredentials(null))));

        Assert.AreEqual(VirtualBacking.Remote, _vfs.GetBacking(@"::memory-far\x.txt"));
        Assert.IsNull(_vfs.TryResolveReal(@"::memory-far\x.txt"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => await _vfs.MaterializeFileAsync(@"::memory-far\x.txt"));
    }

    [TestMethod]
    public void NamesCompareAsTheFileSystemHoldingThemReadsThem()
    {
        Assert.AreEqual(StringComparison.Ordinal, _vfs.GetNameComparison($@"{Root}\home"));
        Assert.AreEqual(StringComparison.OrdinalIgnoreCase, _vfs.GetNameComparison(@"C:\Users"));
    }

    [TestMethod]
    [CoversNode("io-locations-directory")]
    public void AMountedLocationIsListedForPages_ByItsRootAndDisplayForm()
    {
        var listed = ((ILocationDirectory)_vfs).Mounted.Single();

        Assert.AreEqual("wsl", listed.Kind);
        Assert.AreEqual(Root, listed.Root);
        Assert.AreEqual("wsl://Probe/", listed.DisplayPath);
    }

    [TestMethod]
    public async Task ThisDiskHasNoLocationShell_AndALocationWithoutOneRefuses()
    {
        Assert.IsNull(await _vfs.GetTerminalLauncherAsync(@"C:\Users"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => await _vfs.GetTerminalLauncherAsync($@"{Root}\home"));
    }

    [TestMethod]
    [DataRow(@"home\notes%2E", DisplayName = "a name ending in a dot")]
    [DataRow(@"home\notes%20", DisplayName = "a name ending in a space")]
    [DataRow(@"home\folder%2E\inner.txt", DisplayName = "under a folder ending in a dot")]
    public void ANameWindowsWouldTrim_HasNoNativePath_SoNoReaderIsHandedItsNeighbour(string remainder)
    {
        Assert.IsNull(_vfs.TryResolveReal($@"{Root}\{remainder}"), "Windows reads 'notes.' as 'notes', another file");
        Assert.AreEqual(VirtualBacking.Remote, _vfs.GetBacking($@"{Root}\{remainder}"));
    }

    // ── Naming an entry ─────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("a:b.txt", "a%3Ab.txt")]
    [DataRow("%41", "%2541", DisplayName = "a percent sign is a character, not an escape")]
    [DataRow("notes.", "notes%2E", DisplayName = "a trailing dot is kept")]
    [DataRow("c:evil", "c%3Aevil", DisplayName = "a name like a drive stays in the folder")]
    public void AnEntryNamedOnALocation_IsTheLocationsEntryOfExactlyThatName(string name, string segment)
    {
        Assert.AreEqual($@"{Root}\home\{segment}", _vfs.ChildPath($@"{Root}\home", name));
        Assert.AreEqual($@"{Root}\home\{segment}", _vfs.ChildPath(@"\\wsl.localhost\Probe\home", name),
                        "named in the Windows folder showing the location, it is still the location's entry");
    }

    [TestMethod]
    [DataRow("a:b?.txt", true)]
    [DataRow("notes. ", true)]
    [DataRow("", false)]
    [DataRow(".", false)]
    [DataRow("..", false)]
    [DataRow("a/b", false)]
    [DataRow("a\0b", false)]
    public void ALocationTakesAnyNameLinuxDoes(string name, bool allowed)
        => Assert.AreEqual(allowed, _vfs.CanName($@"{Root}\home", name), name);

    [TestMethod]
    [DataRow("notes.txt", true)]
    [DataRow("a:b.txt", false)]
    [DataRow("what?", false)]
    [DataRow("notes.", false, DisplayName = "Windows would drop the dot")]
    [DataRow("notes ", false, DisplayName = "Windows would drop the space")]
    [DataRow("..", false)]
    public void ThisDiskTakesTheNamesWindowsKeepsAsTyped(string name, bool allowed)
    {
        var folder = TestTemp.CreateSubdirectory("nexa-naming-").FullName;
        Assert.AreEqual(allowed, _vfs.CanName(folder, name), name);
        if (allowed) Assert.AreEqual(Path.Combine(folder, name), _vfs.ChildPath(folder, name));
    }
}
