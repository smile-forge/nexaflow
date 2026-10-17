using System.IO;
using Nexaflow.Features.UsbProbe.PortableTools;
using Nexaflow.IO.Common.Stores;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// A phone's files over MTP, on a phone that is a store in memory: offered for an MTP function only, nothing shared
/// while the phone is locked and its storage there as soon as it is unlocked, paths given the way down rather than as a
/// disk path, and a file on the phone fetched to this PC — checked — to open.
/// </summary>
[TestClass]
[CoversNode("usbprobe-portable")]
public class PortableDeviceToolTests
{
    /// <summary>A phone: opaque ids, no paths, storage shared only once unlocked, and an event when that changes.</summary>
    sealed class FakePhone : IFileStore
    {
        sealed record Item(string Id, string Parent, string Name, bool Folder, byte[] Data);

        readonly Dictionary<string, Item> _items = [];
        int _next;

        public bool Unlocked { get; private set; }

        public void Unlock()
        {
            Unlocked = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public FakePhone(bool unlocked)
        {
            Unlocked = unlocked;
            Add("s1", "", "Internal storage", true);
            var dcim = Add("d1", "s1", "DCIM", true);
            Add("f1", dcim.Id, "photo.jpg", false, [7, 7, 7]);
        }

        Item Add(string id, string parent, string name, bool folder, byte[]? data = null) => _items[id] = new Item(id, parent, name, folder, data ?? []);

        public string Name => "Galaxy S21";
        public event EventHandler? Changed;

        public Task<IReadOnlyList<StoreRoot>> RootsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoreRoot>>(Unlocked ? [new StoreRoot("s1", "Internal storage", "Generic hierarchical", 1_000_000, 400_000, false)] : []);

        public Task<IReadOnlyList<StoreEntry>> ListAsync(string folderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoreEntry>>([.. _items.Values.Where(i => i.Parent == folderId).Select(Entry)]);

        public Task<Stream> OpenReadAsync(string fileId, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(_items[fileId].Data));

        public async Task<StoreEntry> WriteAsync(string folderId, string name, long length, DateTime? modified, Stream content, CancellationToken ct)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            return Entry(Add($"o{++_next}", folderId, name, false, buffer.ToArray()));
        }

        public Task<StoreEntry> CreateFolderAsync(string folderId, string name, CancellationToken ct) => Task.FromResult(Entry(Add($"o{++_next}", folderId, name, true)));
        public Task<StoreEntry> RenameAsync(StoreEntry entry, string name, CancellationToken ct) => Task.FromResult(Entry(_items[entry.Id] = _items[entry.Id] with { Name = name }));
        public Task DeleteAsync(StoreEntry entry, CancellationToken ct) { _items.Remove(entry.Id); return Task.CompletedTask; }
        public string? LocalPathOf(string id) => null;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;

        static StoreEntry Entry(Item i) => new(i.Id, i.Name, i.Folder, i.Data.Length, new DateTime(2026, 10, 1));
    }

    const string MtpFunction = @"USB\VID_04E8&PID_6860&MS_COMP_MTP&SAMSUNG_Android\8&130557e0&2&0000";

    RecordingHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new RecordingHost();

    static IO.Usb.Inventory.UsbDeviceNode Phone() => UsbProbeSamples.Lg() with
    {
        Functions =
        [
            new(MtpFunction, [0], "Phone", IO.Usb.Inventory.UsbDevNodeDriver.None with { Service = "WUDFWpdMtp" },
                IO.Usb.Inventory.UsbFunctionMode.WindowsClass, IO.Usb.Inventory.UsbPowerState.D0, [], null, []) { FunctionInterface = 0 },
        ],
    };

    async Task<PortableDeviceTool> ToolAsync(FakePhone phone)
    {
        var provider = new PortableToolProvider(id => id == MtpFunction ? [@"\\?\usb#phone"] : [], _ => phone);
        var tool = (PortableDeviceTool)provider.Offer(Phone(), _host).Single();
        await tool.ActivateAsync(CancellationToken.None);
        return tool;
    }

    [TestMethod]
    public void OnlyAnMtpFunction_IsOffered()
    {
        var provider = new PortableToolProvider(_ => [], _ => new FakePhone(true));
        Assert.AreEqual(1, provider.Offer(Phone(), _host).Count);
        Assert.AreEqual(0, provider.Offer(UsbProbeSamples.Lg(), _host).Count, "a monitor's HID and serial functions share no files");
    }

    [TestMethod]
    public async Task ALockedPhone_SaysHowToShare_AndItsStorageComesWhenUnlocked()
    {
        var phone = new FakePhone(unlocked: false);
        var tool = await ToolAsync(phone);

        StringAssert.Contains(tool.Browser!.Note, "unlock it");
        StringAssert.Contains(tool.Status, "unlock it");
        Assert.AreEqual(0, tool.Browser.Roots.Count);

        phone.Unlock();
        for (int i = 0; i < 200 && tool.Browser.Roots.Count == 0; i++) await Task.Delay(10);

        Assert.AreEqual("Internal storage", tool.Browser.Roots.Single().Name, "unlocking shows its storage without being asked");
        Assert.IsNull(tool.Browser.Note);
        StringAssert.Contains(tool.Status, "Internal storage");
    }

    [TestMethod]
    public async Task PathsOnAPhone_AreTheWayDown()
    {
        var tool = await ToolAsync(new FakePhone(unlocked: true));
        await tool.Browser!.OpenCommand.ExecuteAsync(tool.Browser.Entries.Single(e => e.Name == "DCIM"));

        Assert.AreEqual(@"Galaxy S21\Internal storage\DCIM", tool.Browser.FolderPath);
        Assert.AreEqual(@"Galaxy S21\Internal storage\DCIM\photo.jpg", tool.Browser.Entries.Single().Path);
        CollectionAssert.AreEqual(new[] { "Internal storage", "DCIM" }, tool.Browser.Crumbs.Select(c => c.Label).ToArray());

        var said = await tool.Operations.Single(o => o.Name == "list_folder")
            .RunAsync(new Dictionary<string, string> { ["path"] = @"Galaxy S21\Internal storage\DCIM" }, CancellationToken.None);
        StringAssert.Contains(said, "photo.jpg", "the agent reaches a folder by the way down");
    }

    [TestMethod]
    public async Task AFileOnThePhone_IsFetchedAndCheckedBeforeItOpens()
    {
        var tool = await ToolAsync(new FakePhone(unlocked: true));
        await tool.Browser!.OpenCommand.ExecuteAsync(tool.Browser.Entries.Single(e => e.Name == "DCIM"));
        await tool.Browser.OpenCommand.ExecuteAsync(tool.Browser.Entries.Single());

        var opened = _host.OpenedFiles.Single();
        StringAssert.StartsWith(opened, Path.GetTempPath());
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, File.ReadAllBytes(opened));
        StringAssert.Contains(tool.Browser.LastResult, "1 read back and matched");
        File.Delete(opened);
    }

    [TestMethod]
    public async Task NoPortableDevice_SaysSo_AndOpensNothing()
    {
        var provider = new PortableToolProvider(_ => [], _ => throw new InvalidOperationException("nothing to open"));
        var tool = (PortableDeviceTool)provider.Offer(Phone(), _host).Single();
        await tool.ActivateAsync(CancellationToken.None);

        Assert.IsNull(tool.Browser);
        StringAssert.Contains(tool.Note, "portable device");
    }

    [TestMethod]
    public async Task ClosingTheTool_LetsGoOfThePhone()
    {
        var phone = new FakePhone(unlocked: true);
        var tool = await ToolAsync(phone);
        tool.Dispose();
        Assert.IsTrue(phone.Disposed);
    }
}
