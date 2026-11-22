using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// A folder on a location in the file browser: its permissions, owner and group shown where the location reports them,
/// names that differ only by case kept apart where the location's names do, and a folder that cannot be read saying why,
/// with a way to read it again.
/// </summary>
[TestClass]
[CoversNode("locations-browse")]
public sealed class LocationBrowsingTests
{
    private const string Root = "::memory-lab";

    private MemoryLocationHost _host = null!;
    private LocationSessionPool _pool = null!;
    private LocationSet _set = null!;
    private LocationFileSystem _vfs = null!;
    private IShellServices _shell = null!;

    /// <summary>The in-memory backend, reporting POSIX permissions as a WSL distro does.</summary>
    private sealed class ReportsPermissions(ILocationProvider memory) : ILocationProvider
    {
        public string Kind => memory.Kind;

        public LocationCapabilities Capabilities => memory.Capabilities | LocationCapabilities.Permissions | LocationCapabilities.CaseSensitive;

        public ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
            => memory.ConnectAsync(profile, credentials, ct);
    }

    private sealed class SameNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    /// <summary>Makes the location one Windows also shows as a folder, as a distro is.</summary>
    private string ReachableAsAFolder()
    {
        var folder = TestTemp.CreateSubdirectory("nexa-share-").FullName;
        System.IO.Directory.CreateDirectory(Path.Combine(folder, "docs"));
        _vfs.RegisterLocation(_set.Mounts.Single() with { NativeRoots = [folder], NativePaths = new SameNames() });
        return folder;
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _host = new MemoryLocationHost("lab-" + Guid.NewGuid().ToString("N")[..8], TimeSpan.Zero, caseSensitive: true);
        var registry = new LocationProviderRegistry();
        registry.Register(new ReportsPermissions(new MemoryLocationProvider(_host)));
        _pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set  = _pool.Acquire([new ConnectionProfile("memory-lab", "memory", "Lab", _host.Name)]);
        _vfs  = new LocationFileSystem(VirtualFileSystem.Instance);
        _vfs.RegisterLocation(_set.Mounts.Single());

        await _vfs.CreateDirectoryAsync($@"{Root}\docs");
        await _vfs.WriteAllTextAsync($@"{Root}\docs\a.txt", "lower");
        await _vfs.WriteAllTextAsync($@"{Root}\docs\A.txt", "upper");

        _shell = Substitute.For<IShellServices>();
        _shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _vfs.UnregisterLocation("memory-lab");
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }

    private async Task<FileSystemViewModel> AtAsync(string folder)
    {
        var vm = new FileSystemViewModel(folder, _shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), _vfs);
        await vm.Browsing;
        await vm.EntriesLoaded;
        return vm;
    }

    [TestMethod]
    public async Task AFolderOnALocationThatReportsPermissions_ShowsThem()
    {
        var onLocation = await AtAsync($@"{Root}\docs");
        Assert.IsTrue(onLocation.ShowsPosixColumns);

        var onDisk = await AtAsync(TestTemp.CreateSubdirectory("nexa-posix-").FullName);
        Assert.IsFalse(onDisk.ShowsPosixColumns, "a folder on this disk reports no permissions");
    }

    [TestMethod]
    public async Task NamesThatDifferOnlyByCase_AreTwoRows_AndStaySoWhenReadAgain()
    {
        var vm = await AtAsync($@"{Root}\docs");
        CollectionAssert.AreEquivalent(new[] { "a.txt", "A.txt" }, vm.Entries.Select(e => e.Name).ToArray());

        await _vfs.WriteAllTextAsync($@"{Root}\docs\b.txt", "new");
        vm.Refresh();
        await vm.EntriesLoaded;

        CollectionAssert.AreEquivalent(new[] { "a.txt", "A.txt", "b.txt" }, vm.Entries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public async Task AFolderThatCannotBeRead_SaysWhy_AndTryingAgainReadsIt()
    {
        await _vfs.CreateDirectoryAsync($@"{Root}\later");
        var vm = await AtAsync($@"{Root}\later");
        Assert.IsFalse(vm.HasListingFailure);

        // Taken away while it is open — the distro shut down, the folder removed from inside Linux — and read again.
        await _vfs.DeleteAsync($@"{Root}\later", recursive: true);
        vm.Refresh();
        await vm.EntriesLoaded;
        Assert.IsTrue(vm.HasListingFailure, "a folder that cannot be read says so rather than showing an empty list");
        Assert.IsFalse(string.IsNullOrEmpty(vm.ListingFailureText));

        await _vfs.CreateDirectoryAsync($@"{Root}\later");
        await _vfs.WriteAllTextAsync($@"{Root}\later\here.txt", "here");
        vm.RetryListingCommand.Execute(null);
        await vm.EntriesLoaded;

        Assert.IsFalse(vm.HasListingFailure);
        Assert.AreEqual("here.txt", vm.Entries.Single().Name);
    }

    [TestMethod]
    [DataRow(0b111_101_101, true, false, "drwxr-xr-x", DisplayName = "a folder anyone may enter")]
    [DataRow(0b110_100_100, false, false, "-rw-r--r--", DisplayName = "a file only its owner writes")]
    [DataRow(0b100_111_101_101, false, false, "-rwsr-xr-x", DisplayName = "set-user-id on a runnable file")]
    [DataRow(0b010_110_100_000, false, false, "-rw-r-S---", DisplayName = "set-group-id on a file its group cannot run")]
    [DataRow(0b001_111_111_111, true, false, "drwxrwxrwt", DisplayName = "a sticky folder")]
    [DataRow(0b111_111_111, false, true, "lrwxrwxrwx", DisplayName = "a link")]
    public void PermissionsReadAsLsWritesThem(int mode, bool folder, bool link, string expected)
    {
        var entry = new FileSystemEntry
        {
            Name          = "x",
            IsDirectory   = folder,
            PosixMode     = (UnixFileMode)mode,
            SymlinkTarget = link ? "/elsewhere" : null,
        };
        Assert.AreEqual(expected, entry.PermissionsLabel);
    }

    [TestMethod]
    public void AnEntryWithNoPermissions_ShowsNone()
        => Assert.AreEqual(string.Empty, new FileSystemEntry { Name = "x" }.PermissionsLabel);

    [TestMethod]
    public async Task AFolderWindowsCanAlsoShow_IsStillWatchedThroughTheLocation()
    {
        ReachableAsAFolder();
        var vm = await AtAsync($@"{Root}\docs");

        vm.AttachFolderWatching();

        _shell.DidNotReceiveWithAnyArgs().WatchFolder(default!, default!);
    }

    [TestMethod]
    public async Task TheAssistantsMove_TakesTheNameItWasGiven_NotTheOneWindowsWouldRead()
    {
        ReachableAsAFolder();
        await _vfs.WriteAllTextAsync($@"{Root}\docs\notes", "plain");
        await _vfs.WriteAllTextAsync($@"{Root}\docs\notes%2E", "dotted");
        var vm = await AtAsync($@"{Root}\docs");

        await new Nexaflow.Features.WindowsFileSystem.ClientTools.MoveTool(vm).InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["source"] = "notes.", ["destination"] = "moved" }, CancellationToken.None);

        Assert.AreEqual("dotted", await _vfs.ReadAllTextAsync($@"{Root}\docs\moved"));
        Assert.AreEqual("plain", await _vfs.ReadAllTextAsync($@"{Root}\docs\notes"), "its neighbour is untouched");
    }

    [TestMethod]
    public async Task TheAssistant_ReachesNoSiblingThatDiffersOnlyByCase()
    {
        ReachableAsAFolder();
        await _vfs.CreateDirectoryAsync($@"{Root}\Docs");
        await _vfs.WriteAllTextAsync($@"{Root}\Docs\secret", "not given");
        var vm = await AtAsync($@"{Root}\docs");

        await new Nexaflow.Features.WindowsFileSystem.ClientTools.MoveTool(vm).InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["source"] = "/Docs/secret", ["destination"] = "taken" }, CancellationToken.None);

        Assert.AreEqual("not given", await _vfs.ReadAllTextAsync($@"{Root}\Docs\secret"));
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\docs\taken"));
    }

    [TestMethod]
    public async Task TheAssistantsDelete_NeverPromisesARecycleBinTheLocationHasNot()
    {
        ReachableAsAFolder();
        var vm = await AtAsync($@"{Root}\docs");

        await new Nexaflow.Features.WindowsFileSystem.ClientTools.DeleteTool(vm).InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["name"] = "a.txt" }, CancellationToken.None);

        Assert.AreEqual("lower", await _vfs.ReadAllTextAsync($@"{Root}\docs\a.txt"), "approved as recyclable, so not deleted for good");
    }
}
