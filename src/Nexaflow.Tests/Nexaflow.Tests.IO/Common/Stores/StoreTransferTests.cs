using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Stores;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Stores;

/// <summary>
/// Copies between stores: between two disks it is the file engine's verified copy; to and from a store that is not a
/// disk (a phone, here a store in memory) each file is streamed, hashed as it is read and read back from where it landed
/// — and a copy that reads back differently is removed, not kept.
/// </summary>
[TestClass]
[CoversNode("io-file-stores")]
public class StoreTransferTests
{
    /// <summary>A store that is not a disk: ids are opaque, there is no path, and reading back can be made to lie.</summary>
    sealed class InMemoryStore : IFileStore
    {
        sealed record Item(string Id, string Parent, string Name, bool Folder, byte[] Data, DateTime Modified);

        readonly Dictionary<string, Item> _items = [];
        int _next;

        public InMemoryStore() => _items["root"] = new Item("root", "", "Internal storage", true, [], DateTime.Now);

        /// <summary>What reading a file back gives instead of what was written — media that keeps different bytes.</summary>
        public Func<byte[], byte[]>? ReadsBack { get; set; }

        public string Name => "Phone";

        public IReadOnlyDictionary<string, string> Facts { get; } = new Dictionary<string, string>();
        public event EventHandler? Changed { add { } remove { } }

        public Task<IReadOnlyList<StoreRoot>> RootsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoreRoot>>([new StoreRoot("root", "Internal storage", null, 1_000_000, 900_000, false)]);

        public Task<IReadOnlyList<StoreEntry>> ListAsync(string folderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoreEntry>>([.. _items.Values.Where(i => i.Parent == folderId).Select(Entry)]);

        public Task<Stream> OpenReadAsync(string fileId, CancellationToken ct)
        {
            var data = _items[fileId].Data;
            return Task.FromResult<Stream>(new MemoryStream(ReadsBack?.Invoke(data) ?? data));
        }

        public async Task<StoreEntry> WriteAsync(string folderId, string name, long length, DateTime? modified, Stream content, CancellationToken ct)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            var item = new Item($"o{++_next}", folderId, name, false, buffer.ToArray(), modified ?? DateTime.Now);
            _items[item.Id] = item;
            return Entry(item);
        }

        public Task<StoreEntry> CreateFolderAsync(string folderId, string name, CancellationToken ct)
        {
            var item = new Item($"o{++_next}", folderId, name, true, [], DateTime.Now);
            _items[item.Id] = item;
            return Task.FromResult(Entry(item));
        }

        public Task<StoreEntry> RenameAsync(StoreEntry entry, string name, CancellationToken ct)
        {
            _items[entry.Id] = _items[entry.Id] with { Name = name };
            return Task.FromResult(entry with { Name = name });
        }

        public Task DeleteAsync(StoreEntry entry, CancellationToken ct)
        {
            foreach (var child in _items.Values.Where(i => i.Parent == entry.Id).ToList()) DeleteAsync(Entry(child), ct);
            _items.Remove(entry.Id);
            return Task.CompletedTask;
        }

        public string? LocalPathOf(string id) => null;
        public void Dispose() { }

        public byte[]? FileNamed(string name) => _items.Values.FirstOrDefault(i => !i.Folder && i.Name == name)?.Data;
        public bool Holds(string name) => _items.Values.Any(i => i.Name == name);

        static StoreEntry Entry(Item i) => new(i.Id, i.Name, i.Folder, i.Data.Length, i.Modified);
    }

    string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-stores-").FullName;
        Directory.CreateDirectory(Path.Combine(_dir, "pc", "Album"));
        File.WriteAllText(Path.Combine(_dir, "pc", "notes.txt"), "notes");
        File.WriteAllBytes(Path.Combine(_dir, "pc", "Album", "a.jpg"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(_dir, "out"));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    string Pc(string name) => Path.Combine(_dir, "pc", name);

    [TestMethod]
    public async Task OntoAPhone_EachFileIsStreamed_AndReadBack()
    {
        using var phone = new InMemoryStore();
        var pc = DiskStore.Over(Path.Combine(_dir, "pc"));

        var result = await StoreTransfer.CopyAsync(pc, [DiskStore.EntryOf(Pc("notes.txt")), DiskStore.EntryOf(Pc("Album"))], phone, "root", null, CancellationToken.None);

        Assert.AreEqual(2, result.Files);
        Assert.AreEqual(2, result.Verified.Count, "every file read back and matched");
        Assert.AreEqual(0, result.Failures.Count);
        CollectionAssert.AreEqual("notes"u8.ToArray(), phone.FileNamed("notes.txt"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, phone.FileNamed("a.jpg"));
        Assert.IsTrue(phone.Holds("Album"), "a folder comes across as a folder");
    }

    [TestMethod]
    public async Task OffAPhone_ToThisPc_IsReadBackToo()
    {
        using var phone = new InMemoryStore();
        await StoreTransfer.CopyAsync(DiskStore.Over(Path.Combine(_dir, "pc")), [DiskStore.EntryOf(Pc("notes.txt"))], phone, "root", null, CancellationToken.None);
        var onPhone = (await phone.ListAsync("root", CancellationToken.None)).Single();

        string destination = Path.Combine(_dir, "out");
        var result = await StoreTransfer.CopyAsync(phone, [onPhone], DiskStore.Over(destination), destination, null, CancellationToken.None);

        Assert.AreEqual(1, result.Verified.Count);
        Assert.AreEqual("notes", File.ReadAllText(Path.Combine(destination, "notes.txt")));
    }

    [TestMethod]
    public async Task ACopyThatReadsBackDifferently_IsRemoved_AndReported()
    {
        using var phone = new InMemoryStore { ReadsBack = data => [.. data, 0xFF] };

        var result = await StoreTransfer.CopyAsync(DiskStore.Over(Path.Combine(_dir, "pc")), [DiskStore.EntryOf(Pc("notes.txt"))], phone, "root", null, CancellationToken.None);

        Assert.AreEqual(0, result.Verified.Count);
        Assert.AreEqual("verify", result.Failures.Single().Verb);
        Assert.IsFalse(phone.Holds("notes.txt"), "a copy that is not the original does not stay");
    }

    [TestMethod]
    public async Task ANameAlreadyThere_IsKept_AndTheCopyNamedAlongsideIt()
    {
        using var phone = new InMemoryStore();
        var pc = DiskStore.Over(Path.Combine(_dir, "pc"));
        await StoreTransfer.CopyAsync(pc, [DiskStore.EntryOf(Pc("notes.txt"))], phone, "root", null, CancellationToken.None);
        await StoreTransfer.CopyAsync(pc, [DiskStore.EntryOf(Pc("notes.txt"))], phone, "root", null, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "notes.txt", "notes (2).txt" },
            (await phone.ListAsync("root", CancellationToken.None)).Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public async Task BetweenTwoDisks_ItIsTheFileEnginesVerifiedCopy()
    {
        string destination = Path.Combine(_dir, "out");
        var result = await StoreTransfer.CopyAsync(DiskStore.Over(Path.Combine(_dir, "pc")), [DiskStore.EntryOf(Pc("Album"))],
            DiskStore.Over(destination), destination, null, CancellationToken.None);

        Assert.AreEqual(1, result.Verified.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "Album", "a.jpg")));
    }

    [TestMethod]
    public async Task ADisk_ListsWhatAPersonWouldSee_AndChangesAsAsked()
    {
        string hidden = Pc("System Volume Information");
        Directory.CreateDirectory(hidden);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory);
        var disk = DiskStore.Over(Path.Combine(_dir, "pc"));
        string root = Path.Combine(_dir, "pc");

        CollectionAssert.AreEquivalent(new[] { "Album", "notes.txt" }, (await disk.ListAsync(root, CancellationToken.None)).Select(e => e.Name).ToArray());

        var made = await disk.CreateFolderAsync(root, "Scans", CancellationToken.None);
        var renamed = await disk.RenameAsync(made, "Receipts", CancellationToken.None);
        Assert.AreEqual(Path.Combine(root, "Receipts"), renamed.Id);
        Assert.AreEqual(renamed.Id, disk.LocalPathOf(renamed.Id), "a disk's ids are its paths");
        await disk.DeleteAsync(renamed, CancellationToken.None);
        Assert.IsFalse(Directory.Exists(renamed.Id));
    }
}
