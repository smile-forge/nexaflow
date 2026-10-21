using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The open folder kept up to date while its view is on screen: watched through the shell, listed again in place when the
/// shell says it changed, re-aimed when the reader moves to another folder, let go when the view goes, and caught up on
/// what changed meanwhile when it comes back.
/// </summary>
[TestClass]
[CoversNode("winfs-folder-watch")]
public class FolderWatchTests
{
    private string _scratch = string.Empty;
    private IShellServices _shell = null!;
    private readonly List<(string Path, Action<FolderChange> OnChanged, IDisposable Handle)> _watches = [];

    [TestInitialize]
    public void Setup()
    {
        _scratch = TestTemp.CreateSubdirectory("folderwatch-").FullName;
        Directory.CreateDirectory(Path.Combine(_scratch, "sub"));
        File.WriteAllText(Path.Combine(_scratch, "first.txt"), "x");

        _shell = Substitute.For<IShellServices>();
        _shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        _shell.WatchFolder(Arg.Any<string>(), Arg.Any<Action<FolderChange>>()).Returns(call =>
        {
            var handle = Substitute.For<IDisposable>();
            _watches.Add((call.ArgAt<string>(0), call.ArgAt<Action<FolderChange>>(1), handle));
            return handle;
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task TheOpenFolderIsWatched_OnlyWhileItsViewIsLoaded()
    {
        var vm = await OpenAsync();
        Assert.AreEqual(0, _watches.Count, "nothing is watched before the view is on screen");

        vm.AttachFolderWatching();
        Assert.AreEqual(_scratch, _watches.Single().Path);

        vm.DetachFolderWatching();
        _watches.Single().Handle.Received(1).Dispose();
    }

    [TestMethod]
    public async Task AFileTheShellTellsOf_IsListed_WithoutARefresh()
    {
        var vm = await OpenAsync();
        vm.AttachFolderWatching();

        var made = Path.Combine(_scratch, "made.txt");
        File.WriteAllText(made, "x");
        _watches.Single().OnChanged(new FolderChange(new HashSet<string> { made }, Whole: false));

        await UntilAsync(() => vm.Entries.Any(entry => entry.Name == "made.txt"));
        Assert.IsTrue(vm.Entries.Any(entry => entry.Name == "first.txt"), "what was there stays");
    }

    [TestMethod]
    public async Task MovingToAnotherFolder_WatchesThatOne_AndLetsTheLastGo()
    {
        var vm = await OpenAsync();
        vm.AttachFolderWatching();

        var sub = Path.Combine(_scratch, "sub");
        vm.NavigateTo(sub);

        Assert.AreEqual(2, _watches.Count);
        _watches[0].Handle.Received(1).Dispose();
        Assert.AreEqual(sub, _watches[1].Path);
    }

    [TestMethod]
    public async Task ComingBack_CatchesUpOnWhatChangedWhileAway()
    {
        var vm = await OpenAsync();
        vm.AttachFolderWatching();
        vm.DetachFolderWatching();

        File.WriteAllText(Path.Combine(_scratch, "meanwhile.txt"), "x");
        vm.AttachFolderWatching();

        await UntilAsync(() => vm.Entries.Any(entry => entry.Name == "meanwhile.txt"));
    }

    [TestMethod]
    public async Task OutsideASyncedFolder_ThereIsNoStatusColumn()
    {
        var vm = await OpenAsync();

        Assert.IsFalse(vm.ShowsCloudStatus);
        Assert.IsTrue(vm.Entries.All(entry => entry.CloudState == Nexaflow.IO.Common.Cloud.CloudFileState.None));
    }

    private async Task<FileSystemViewModel> OpenAsync()
    {
        var vm = new FileSystemViewModel(_scratch, _shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());
        await UntilAsync(() => !vm.IsLoadingEntries && vm.Entries.Count > 0);
        return vm;
    }

    private static async Task UntilAsync(Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++) await Task.Delay(25);
        Assert.IsTrue(done(), "the folder did not settle");
    }
}
