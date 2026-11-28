using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Localization;
using Nexaflow.Features.WindowsFileSystem.Model;

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
        var vm = new FileBrowserRuntime(_shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), _vfs).Browse(folder);
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

        await new Nexaflow.Features.WindowsFileSystem.ClientTools.MoveTool(vm.Model).InvokeAsync(
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

        await new Nexaflow.Features.WindowsFileSystem.ClientTools.MoveTool(vm.Model).InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["source"] = "/Docs/secret", ["destination"] = "taken" }, CancellationToken.None);

        Assert.AreEqual("not given", await _vfs.ReadAllTextAsync($@"{Root}\Docs\secret"));
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\docs\taken"));
    }

    [TestMethod]
    public async Task TheAssistantsDelete_OnALocation_IsAskedForAsForGood_ThenDeletesThroughTheLocation()
    {
        var share = ReachableAsAFolder();
        _shell.Runs();
        var vm    = await AtAsync($@"{Root}\docs");
        await _vfs.CreateDirectoryAsync($@"{Root}\docs\old");
        vm.Refresh();
        await vm.EntriesLoaded;
        var tool  = new Nexaflow.Features.WindowsFileSystem.ClientTools.DeleteTool(vm.Model);
        var file  = new System.Text.Json.Nodes.JsonObject { ["name"] = "a.txt" };

        var asked  = tool.Announce(file)!;
        var folder = tool.Announce(new System.Text.Json.Nodes.JsonObject { ["name"] = "old" })!;
        var result = await tool.InvokeAsync(file, CancellationToken.None);

        Assert.AreEqual(Str.Format("WindowsFileSystem.Announce.FileForGoodFormat", "a.txt"), asked.Target, "the approval says it is for good");
        Assert.AreEqual(Str.Format("WindowsFileSystem.Announce.FolderForGoodFormat", "old"), folder.Target, "and that it is a folder");
        Assert.IsTrue(result.Success, result.ToString());
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\docs\a.txt"), "deleted, as the person's own Delete does there");
        Assert.AreEqual("upper", await _vfs.ReadAllTextAsync($@"{Root}\docs\A.txt"), "only the name asked for, matched exactly");
        Assert.AreEqual(0, System.IO.Directory.GetFiles(share, "*", System.IO.SearchOption.AllDirectories).Length,
                        "nothing went through the Windows folder that shows the location");
    }

    // ── Names typed for a location ──────────────────────────────────────────

    /// <summary>The path of the entry a location lists by <paramref name="name"/>, as the file system names it.</summary>
    private static string Named(string folder, string name) => $@"{folder}\{LocationPath.EncodeSegment(name)}";

    [TestMethod]
    public async Task Rename_ShowsALocationsNameAsItIs_AndNamesTheFileExactlyAsTyped()
    {
        await _vfs.WriteAllTextAsync(Named($@"{Root}\docs", "a:b.txt"), "colon");
        string? shown = null;
        Action<string>? confirm = null;
        _shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<string>>(), Arg.Any<Action>()))
              .Do(call => { shown = call.ArgAt<string>(2); confirm = call.ArgAt<Action<string>>(3); });

        await new Nexaflow.Features.WindowsFileSystem.FileActions.RenameFile(_shell, _vfs, new Nexaflow.Features.WindowsFileSystem.Services.FileCompanions(_shell)).PerformActionAsync(Named($@"{Root}\docs", "a:b.txt"));
        Assert.AreEqual("a:b.txt", shown, "the name as Linux has it, not the segment that stands for it");

        var renamed = _shell.ReceivedCalls().Count();
        confirm!("c:d %41.txt");
        for (int i = 0; i < 100 && _shell.ReceivedCalls().Count() == renamed; i++) await Task.Delay(20);

        Assert.AreEqual("colon", await _vfs.ReadAllTextAsync(Named($@"{Root}\docs", "c:d %41.txt")), "named exactly as typed: '%41' is not 'A'");
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\docs\c%3Ad A.txt"));
    }

    [TestMethod]
    public async Task TheAssistantsRename_NamesTheFileExactlyAsAsked()
    {
        var vm = await AtAsync($@"{Root}\docs");

        var result = await new Nexaflow.Features.WindowsFileSystem.ClientTools.RenameTool(vm.Model).InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["name"] = "a.txt", ["new_name"] = "x:y %41.txt" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ToString());
        Assert.AreEqual("lower", await _vfs.ReadAllTextAsync(Named($@"{Root}\docs", "x:y %41.txt")));
    }

    [TestMethod]
    public async Task New_NamesTheItemExactlyAsTyped_ALinuxNameIncluded()
    {
        var folder = await new Nexaflow.Features.WindowsFileSystem.FileActions.NewFolderCreateAction(_vfs).CreateItemAsync($@"{Root}\docs", "n:m %41.");

        Assert.AreEqual(Named($@"{Root}\docs", "n:m %41."), folder);
        Assert.IsTrue(await _vfs.IsDirectoryAsync(Named($@"{Root}\docs", "n:m %41.")));
    }

    [TestMethod]
    public async Task ANameWindowsCannotHold_IsShownAsTheLocationHasIt_InTheListAndTheTree()
    {
        await _vfs.WriteAllTextAsync(_vfs.ChildPath($@"{Root}\docs", "at 12:30.txt"), "half past");
        await _vfs.CreateDirectoryAsync(_vfs.ChildPath($@"{Root}\docs", "q:3"));

        var vm  = await AtAsync($@"{Root}\docs");
        var row = vm.Entries.Single(e => e.Name == "at 12%3A30.txt");
        Assert.AreEqual("at 12:30.txt", row.ShownName, "the row shows the name, not the segment its path holds");

        var node = new FileSystemTreeNode("docs", $@"{Root}\docs", _vfs);
        await node.LoadChildrenAsync();
        CollectionAssert.Contains(node.Children.Select(c => c.Name).ToArray(), "q:3");
        Assert.AreEqual($@"{Root}\docs\q%3A3", node.Children.Single(c => c.Name == "q:3").FullPath);
    }

    [TestMethod]
    public void ALink_ShowsWhereItPoints_AfterItsName()
    {
        var link = new FileSystemEntry { Name = "python3", SymlinkTarget = "/usr/bin/python3.12" };
        var file = new FileSystemEntry { Name = "notes.txt" };

        Assert.IsTrue(link.IsLink);
        Assert.AreEqual("→ /usr/bin/python3.12", link.LinkTargetLabel, "as ls -l shows it");
        Assert.IsFalse(file.IsLink);
        Assert.AreEqual(string.Empty, file.LinkTargetLabel);
        Assert.AreEqual("notes.txt", file.ShownName, "on this disk the name is shown as it is");
    }
}
