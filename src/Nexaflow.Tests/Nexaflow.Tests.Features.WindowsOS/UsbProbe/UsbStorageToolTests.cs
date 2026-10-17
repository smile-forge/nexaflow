using System.IO;
using Nexaflow.Features.UsbProbe.StorageTools;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Storage;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Stores;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The mass-storage tool over a "volume" that is a folder the test owns, browsed through the shared store browser: offered
/// for disks only, a folder tree and a list kept on the same folder, copies both ways always read back and matched, every
/// change asked for first, and nothing reached outside the device's own volumes.
/// </summary>
[TestClass]
[CoversNode("usbprobe-storage")]
public class UsbStorageToolTests
{
    sealed class FolderStorage(string root) : IUsbStorageLocator
    {
        public IReadOnlyList<UsbDisk> DisksOf(UsbFunctionNode function) =>
            [new UsbDisk("disk", "SanDisk Ultra USB Device", 3, 32_000_000_000,
                [new UsbVolume(@"\\?\Volume{test}\", [root], "STICK", "exFAT", 32_000_000_000, 14_000_000_000, false, null)], null)];
    }

    string _stick = "", _pc = "";
    RecordingHost _host = null!;

    [TestInitialize]
    public void Setup()
    {
        var root = Directory.CreateTempSubdirectory("nexaflow-usbstorage-").FullName;
        _stick = Directory.CreateDirectory(Path.Combine(root, "stick")).FullName + "\\";
        _pc = Directory.CreateDirectory(Path.Combine(root, "pc")).FullName;
        File.WriteAllText(Path.Combine(_stick, "notes.txt"), "on the stick");
        Directory.CreateDirectory(Path.Combine(_stick, "Photos", "2024"));
        File.WriteAllBytes(Path.Combine(_stick, "Photos", "a.jpg"), [1, 2, 3, 4]);
        _host = new RecordingHost();
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(Path.GetDirectoryName(_pc)!, recursive: true);

    async Task<UsbStorageTool> ToolAsync()
    {
        var tool = (UsbStorageTool)new UsbStorageToolProvider(new FolderStorage(_stick)).Offer(UsbProbeSamples.Stick(), _host).Single();
        await tool.ActivateAsync(CancellationToken.None);
        await WaitFor(() => tool.Browser.Entries.Count > 0);
        return tool;
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.IsTrue(condition(), "it did not settle in two seconds");
    }

    static string[] Names(UsbStorageTool tool) => [.. tool.Browser.Entries.Select(e => e.Name)];

    static StoreRow Row(UsbStorageTool tool, string name) => tool.Browser.Entries.Single(e => e.Name == name);

    [TestMethod]
    public void OnlyADisk_IsOffered()
    {
        var provider = new UsbStorageToolProvider(new FolderStorage(_stick));
        Assert.AreEqual(1, provider.Offer(UsbProbeSamples.Stick(), _host).Count);
        Assert.AreEqual(0, provider.Offer(UsbProbeSamples.Lg(), _host).Count, "a monitor's HID and serial functions are no disk");
    }

    [TestMethod]
    public async Task ItBrowses_TheVolume_NeverAboveItsRoot()
    {
        var tool = await ToolAsync();

        StringAssert.Contains(tool.Browser.CurrentRoot!.Name, "STICK");
        StringAssert.Contains(tool.Status, "free of");
        CollectionAssert.AreEqual(new[] { "Photos", "notes.txt" }, Names(tool), "folders first");
        Assert.IsFalse(tool.Browser.UpCommand.CanExecute(null), "nothing above the volume's root");

        await tool.Browser.OpenCommand.ExecuteAsync(Row(tool, "Photos"));
        CollectionAssert.AreEqual(new[] { "2024", "a.jpg" }, Names(tool));
        Assert.IsTrue(tool.Browser.UpCommand.CanExecute(null));

        await tool.Browser.UpCommand.ExecuteAsync(null);
        Assert.AreEqual(_stick, tool.Browser.FolderPath);
        Assert.IsTrue(Row(tool, "Photos").IsSelected, "coming back up, the folder just left is the one selected");
    }

    [TestMethod]
    public async Task TheTree_AndThePathBar_FollowTheFolderOpen()
    {
        var tool = await ToolAsync();
        var volume = tool.Browser.Roots.Single();
        Assert.IsTrue(volume.IsRoot);

        await tool.Browser.OpenCommand.ExecuteAsync(Row(tool, "Photos"));
        await tool.Browser.OpenCommand.ExecuteAsync(Row(tool, "2024"));

        Assert.IsTrue(volume.IsExpanded);
        var photos = volume.ChildWithId(Path.Combine(_stick, "Photos"))!;
        Assert.IsTrue(photos.IsExpanded);
        Assert.IsTrue(photos.ChildWithId(Path.Combine(_stick, "Photos", "2024"))!.IsSelected, "the tree selects the folder open");
        CollectionAssert.AreEqual(new[] { volume.Name, "Photos", "2024" }, tool.Browser.Crumbs.Select(c => c.Label).ToArray());

        await tool.Browser.OpenCommand.ExecuteAsync(tool.Browser.Crumbs[0]);
        Assert.AreEqual(_stick, tool.Browser.FolderPath, "a step of the path bar opens its folder");

        photos.IsSelected = true;
        await WaitFor(() => tool.Browser.FolderPath == photos.Path);
        CollectionAssert.AreEqual(new[] { "2024", "a.jpg" }, Names(tool), "selecting a folder in the tree opens it");
    }

    [TestMethod]
    public async Task TheList_SortsByAColumn_FoldersAlwaysFirst()
    {
        var tool = await ToolAsync();
        File.WriteAllBytes(Path.Combine(_stick, "big.bin"), new byte[4096]);
        await tool.Browser.RefreshCommand.ExecuteAsync(null);

        tool.Browser.SortCommand.Execute("Size");
        CollectionAssert.AreEqual(new[] { "Photos", "big.bin", "notes.txt" }, Names(tool), "largest first");
        tool.Browser.SortCommand.Execute("Size");
        CollectionAssert.AreEqual(new[] { "Photos", "notes.txt", "big.bin" }, Names(tool), "the same column again turns it round");
        Assert.AreEqual("Size", tool.Browser.SortColumn);
    }

    [TestMethod]
    public async Task AFile_OpensInTheAppsViewer()
    {
        var tool = await ToolAsync();
        await tool.Browser.OpenCommand.ExecuteAsync(Row(tool, "notes.txt"));
        CollectionAssert.AreEqual(new[] { Path.Combine(_stick, "notes.txt") }, _host.OpenedFiles);
    }

    [TestMethod]
    public async Task CopiesOffTheDevice_AreAlwaysReadBackAndMatched()
    {
        var tool = await ToolAsync();
        var op = tool.Operations.Single(o => o.Name == "copy_from_device");
        Assert.IsTrue(op.ChangesSomething);

        var said = await op.RunAsync(new Dictionary<string, string>
        {
            ["paths"] = $"[\"{Path.Combine(_stick, "notes.txt").Replace("\\", "\\\\")}\", \"{Path.Combine(_stick, "Photos").Replace("\\", "\\\\")}\"]",
            ["destination"] = _pc,
        }, CancellationToken.None);

        Assert.AreEqual("on the stick", File.ReadAllText(Path.Combine(_pc, "notes.txt")));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(_pc, "Photos", "a.jpg")));
        StringAssert.Contains(said, "2 read back and matched");
        Assert.AreEqual(said, tool.Browser.LastResult);
        Assert.IsTrue(_host.Logged.Contains(said), "the Diagnostics tab hears of it");
    }

    [TestMethod]
    public async Task CopiesOntoTheDevice_AskFirst()
    {
        var tool = await ToolAsync();
        var file = Path.Combine(_pc, "report.pdf");
        File.WriteAllText(file, "report");
        _host.PickedFile = file;

        _host.Confirm = false;
        await tool.Browser.AddFilesCommand.ExecuteAsync(null);
        Assert.IsFalse(File.Exists(Path.Combine(_stick, "report.pdf")), "declined, nothing written");
        StringAssert.Contains(_host.Confirmations.Single(), "report.pdf");

        _host.Confirm = true;
        await tool.Browser.AddFilesCommand.ExecuteAsync(null);
        Assert.AreEqual("report", File.ReadAllText(Path.Combine(_stick, "report.pdf")));
        Assert.IsTrue(Row(tool, "report.pdf").IsSelected, "the folder shows what arrived, selected");
        StringAssert.Contains(tool.Browser.LastResult, "read back and matched");
    }

    [TestMethod]
    public async Task SeveralFiles_CopyOntoTheDevice_InOneAsk()
    {
        var tool = await ToolAsync();
        var a = Path.Combine(_pc, "a.txt");
        var b = Path.Combine(_pc, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        _host.PickedFiles = [a, b];

        await tool.Browser.AddFilesCommand.ExecuteAsync(null);

        StringAssert.Contains(_host.Confirmations.Single(), "2 items", "one question for the lot");
        Assert.AreEqual("a", File.ReadAllText(Path.Combine(_stick, "a.txt")));
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(_stick, "b.txt")));
        StringAssert.Contains(tool.Browser.LastResult, "2 read back and matched");
    }

    [TestMethod]
    public async Task DroppedFiles_CopyIntoTheFolderUnderThePointer_AskingFirst()
    {
        var tool = await ToolAsync();
        var file = Path.Combine(_pc, "dropped.txt");
        File.WriteAllText(file, "dropped");

        await tool.Browser.DropAsync([file], Row(tool, "Photos"));

        Assert.AreEqual(1, _host.Confirmations.Count);
        Assert.AreEqual("dropped", File.ReadAllText(Path.Combine(_stick, "Photos", "dropped.txt")));
    }

    [TestMethod]
    public async Task ANewFolder_IsNamedByThePerson_AndSelected()
    {
        var tool = await ToolAsync();
        _host.Answer = "Scans";
        await tool.Browser.NewFolderCommand.ExecuteAsync(null);

        Assert.IsTrue(Directory.Exists(Path.Combine(_stick, "Scans")));
        Assert.IsTrue(Row(tool, "Scans").IsSelected);

        _host.Answer = null;
        await tool.Browser.NewFolderCommand.ExecuteAsync(null);
        Assert.AreEqual(2, tool.Browser.Entries.Count(e => e.IsFolder), "cancelled, nothing made");

        _host.Answer = "a/b";
        await tool.Browser.NewFolderCommand.ExecuteAsync(null);
        StringAssert.Contains(tool.Browser.LastResult, "cannot be a file or folder name");
    }

    [TestMethod]
    public async Task Renaming_KeepsTheRowSelected_AndTheOpenFolderFollows()
    {
        var tool = await ToolAsync();
        _host.Answer = "readme.txt";
        await tool.Browser.RenameCommand.ExecuteAsync(Row(tool, "notes.txt"));
        Assert.IsTrue(File.Exists(Path.Combine(_stick, "readme.txt")));
        Assert.IsTrue(Row(tool, "readme.txt").IsSelected);

        await tool.Browser.OpenCommand.ExecuteAsync(Row(tool, "Photos"));
        var photos = tool.Browser.Roots.Single().ChildWithId(Path.Combine(_stick, "Photos"))!;
        Assert.IsFalse(tool.Browser.RenameCommand.CanExecute(tool.Browser.Roots.Single()), "a volume's root has no name of its own to change");
        _host.Answer = "Pictures";
        await tool.Browser.RenameCommand.ExecuteAsync(photos);
        Assert.AreEqual(Path.Combine(_stick, "Pictures"), tool.Browser.FolderPath, "renaming the folder open keeps it open under its new name");
    }

    [TestMethod]
    public async Task Deleting_AsksFirst_AndSaysItCannotBeUndone()
    {
        var tool = await ToolAsync();
        Row(tool, "notes.txt").IsSelected = true;
        Row(tool, "Photos").IsSelected = true;

        _host.Confirm = false;
        await tool.Browser.DeleteCommand.ExecuteAsync(null);
        Assert.IsTrue(File.Exists(Path.Combine(_stick, "notes.txt")), "declined, nothing deleted");
        StringAssert.Contains(_host.Confirmations.Single(), "2 items");
        StringAssert.Contains(_host.Confirmations.Single(), "removed for good");

        _host.Confirm = true;
        await tool.Browser.DeleteCommand.ExecuteAsync(null);
        Assert.AreEqual(0, tool.Browser.Entries.Count);
        Assert.IsFalse(tool.Browser.DeleteCommand.CanExecute(tool.Browser.Roots.Single()), "a volume's root is never deleted");
    }

    [TestMethod]
    public async Task CopyPath_IsTheSelection_OrTheFolderOpen()
    {
        var tool = await ToolAsync();
        Assert.AreEqual(_stick, tool.Browser.PathTextOf(null), "nothing selected: the folder open");
        Row(tool, "notes.txt").IsSelected = true;
        Row(tool, "Photos").IsSelected = true;
        Assert.AreEqual($"{Path.Combine(_stick, "Photos")}{Environment.NewLine}{Path.Combine(_stick, "notes.txt")}", tool.Browser.PathTextOf(null));
        Assert.AreEqual(_stick, tool.Browser.PathTextOf(tool.Browser.Crumbs[0]));
    }

    [TestMethod]
    public async Task TheAgent_ReachesTheDevicesVolumes_AndNothingElse()
    {
        var tool = await ToolAsync();
        var list = tool.Operations.Single(o => o.Name == "list_folder");
        var copy = tool.Operations.Single(o => o.Name == "copy_to_device");

        StringAssert.Contains(await list.RunAsync(new Dictionary<string, string> { ["path"] = Path.Combine(_stick, "Photos") }, CancellationToken.None), "a.jpg");
        StringAssert.Contains(await list.RunAsync(new Dictionary<string, string> { ["path"] = _pc }, CancellationToken.None), "is not on");
        StringAssert.Contains(await copy.RunAsync(new Dictionary<string, string> { ["paths"] = _stick + "notes.txt", ["folder"] = _pc }, CancellationToken.None),
            "is not on", "copy_to_device writes to the device only");
        Assert.IsFalse(tool.Operations.Single(o => o.Name == "list_volumes").ChangesSomething);
        StringAssert.Contains(tool.Describe(), "STICK");
    }

    [TestMethod]
    public async Task ItsDebugDetails_AreTheDisksAndVolumes_WithoutTheUsersNames()
    {
        var tool = (UsbStorageTool)new UsbStorageToolProvider(new FolderStorage(_stick)).Offer(UsbProbeSamples.Stick(), _host).Single();
        var details = await tool.DebugDetailsAsync(new UsbDebugMask(["4C530001"]), CancellationToken.None);
        var volume = details["disks"]![0]!["volumes"]![0]!;

        Assert.AreEqual("exFAT", (string?)volume["fileSystem"]);
        StringAssert.StartsWith((string)volume["label"]!, "NAME-");
        Assert.AreEqual(1, (int?)volume["folderMounts"], "a folder it is mounted on is counted, not named");
        Assert.IsFalse(details.ToJsonString().Contains("STICK") || details.ToJsonString().Contains("stick"), "neither the label nor the folder's path");
    }
}
