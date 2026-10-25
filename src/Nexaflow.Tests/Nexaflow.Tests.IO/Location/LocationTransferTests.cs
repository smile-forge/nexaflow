using System.IO.Compression;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.IO.Common;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// Copy, move and delete where an end is somewhere only the file system reaches — a connected location, an archive.
/// The engine keeps its promises there too: nothing throws, a move removes each source only once its copy landed, a
/// verified run reads back what arrived, and a name already taken is resolved the way the disk resolves it.
/// </summary>
[TestClass]
[CoversNode("io-locations-transfer")]
public sealed class LocationTransferTests
{
    private const string Root = "::memory-nas";

    private MemoryLocationHost _host = null!;
    private VirtualFileSystem _shared = null!;
    private LocationFileSystem _vfs = null!;
    private string _local = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _shared = new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName);
        _shared.RegisterHandler(new ZipTestHandler());
        _local  = TestTemp.CreateSubdirectory("nexa-transfer-").FullName;

        _host = new MemoryLocationHost("nas", TimeSpan.Zero);
        var provider = new MemoryLocationProvider(_host);
        var profile  = new ConnectionProfile("memory-nas", "memory", "NAS", "nas");
        _vfs = new LocationFileSystem(_shared);
        _vfs.RegisterLocation(new LocationMount(profile, provider.Capabilities,
                                                new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null))));
    }

    private string Local(params string[] parts) => Path.Combine([_local, .. parts]);

    private string LocalFile(string relative, string content)
    {
        var path = Local(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<string> RemoteFile(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        await _vfs.CreateDirectoryAsync(Path.GetDirectoryName(path)!);
        await _vfs.WriteAllTextAsync(path, content);
        return path;
    }

    private bool OnHost(string providerPath) => _host.With(nodes => nodes.ContainsKey(providerPath));

    private Task<TransferResult> Run(TransferKind kind, string source, string destination,
                                     ConflictPolicy conflicts = ConflictPolicy.AutoRename, bool verify = false,
                                     IReadOnlyList<string>? companions = null)
        => FileTransferEngine.RunAsync(
            new FileTransferRequest(kind, [new TransferItem(source, destination) { CompanionSuffixes = companions ?? [] }],
                                    conflicts, verify) { FileSystem = _vfs });

    [TestMethod]
    public async Task ATreeCopiedOntoALocationArrivesWhole_AndTheSourceIsUntouched()
    {
        LocalFile(@"src\top.txt", "top");
        LocalFile(@"src\sub\leaf.txt", "leaf");

        var result = await Run(TransferKind.Copy, Local("src"), $@"{Root}\dest\src");

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.AreEqual(2, result.ItemsTransferred);
        Assert.AreEqual("leaf", await _vfs.ReadAllTextAsync($@"{Root}\dest\src\sub\leaf.txt"));
        Assert.IsTrue(File.Exists(Local(@"src\top.txt")));
    }

    [TestMethod]
    public async Task AMoveOffALocationRemovesEachSourceOnlyOnceItsCopyLanded()
    {
        await RemoteFile(@"away\one.txt", "one");
        await RemoteFile(@"away\deep\two.txt", "two");

        var result = await Run(TransferKind.Move, $@"{Root}\away", Local("here"));

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.AreEqual("two", File.ReadAllText(Local(@"here\deep\two.txt")));
        Assert.IsFalse(OnHost("/away"), "the source folder goes once everything in it has");
    }

    [TestMethod]
    public async Task AMoveWithinOneLocationIsARename()
    {
        await RemoteFile(@"a\note.txt", "note");

        var result = await Run(TransferKind.Move, $@"{Root}\a\note.txt", $@"{Root}\a\renamed.txt");

        Assert.AreEqual(1, result.ItemsTransferred);
        Assert.AreEqual(0, result.BytesTransferred, "a rename moves no bytes");
        Assert.IsTrue(OnHost("/a/renamed.txt"));
        Assert.IsFalse(OnHost("/a/note.txt"));
    }

    [TestMethod]
    public async Task ACopyWithinALocationThatCopiesOnItsServerSendsNoBytesThrough()
    {
        var host     = new MemoryLocationHost("files", TimeSpan.Zero) { CopiesOnServer = true };
        var provider = new MemoryLocationProvider(host);
        var profile  = new ConnectionProfile("memory-files", "memory", "Files", "files");
        _vfs.RegisterLocation(new LocationMount(profile, provider.Capabilities,
                                                new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null))));
        await _vfs.CreateDirectoryAsync(@"::memory-files\a");
        await _vfs.WriteAllTextAsync(@"::memory-files\a\big.txt", "server side");

        var result = await Run(TransferKind.Copy, @"::memory-files\a", @"::memory-files\b");

        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.AreEqual(1, host.ServerCopies);
        Assert.AreEqual("server side", await _vfs.ReadAllTextAsync(@"::memory-files\b\big.txt"));
    }

    [TestMethod]
    public async Task ANameTakenOnTheLocationIsResolvedAsOnDisk()
    {
        await RemoteFile("a.txt", "theirs");
        var mine = LocalFile("a.txt", "mine");

        var result = await Run(TransferKind.Copy, mine, $@"{Root}\a.txt");

        Assert.AreEqual("theirs", await _vfs.ReadAllTextAsync($@"{Root}\a.txt"));
        Assert.AreEqual("mine", await _vfs.ReadAllTextAsync($@"{Root}\a (2).txt"));
        CollectionAssert.AreEqual(new[] { $@"{Root}\a (2).txt" }, result.RenamedDestinations.ToArray());

        var refused = await Run(TransferKind.Copy, mine, $@"{Root}\a.txt", ConflictPolicy.Fail);
        Assert.AreEqual(183, refused.Failures.Single().Win32Code);
    }

    [TestMethod]
    public async Task AVerifiedCopyReadsBackWhatArrivedOnTheLocation()
    {
        var bytes = new byte[600_000];
        Random.Shared.NextBytes(bytes);
        var big = Local("big.bin");
        File.WriteAllBytes(big, bytes);

        var result = await Run(TransferKind.Copy, big, $@"{Root}\big.bin", verify: true);

        Assert.AreEqual(1, result.Verified.Count);
        Assert.AreEqual(bytes.Length, result.BytesTransferred);
        CollectionAssert.AreEqual(bytes, await _vfs.ReadAllBytesAsync($@"{Root}\big.bin"));
    }

    [TestMethod]
    public async Task DeletingOnALocationTakesTheTreeAndCountsEachFile()
    {
        await RemoteFile(@"gone\a.txt", "a");
        await RemoteFile(@"gone\deep\b.txt", "b");

        var result = await Run(TransferKind.Delete, $@"{Root}\gone", $@"{Root}\gone");

        Assert.AreEqual(2, result.ItemsTransferred);
        Assert.IsFalse(OnHost("/gone"));
    }

    [TestMethod]
    public async Task ACompanionFollowsItsFileOntoTheLocation()
    {
        var photo = LocalFile("photo.jpg", "jpeg");
        LocalFile("photo.jpg.nfedit", "edits");

        var result = await Run(TransferKind.Copy, photo, $@"{Root}\photo.jpg", companions: [".nfedit"]);

        Assert.AreEqual(0, result.Failures.Count);
        Assert.AreEqual("edits", await _vfs.ReadAllTextAsync($@"{Root}\photo.jpg.nfedit"));
    }

    [TestMethod]
    public async Task AnArchiveEntryCopiesOutOfItsArchive()
    {
        var zip = Local("bundle.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry("inner.txt").Open())
            entry.Write(Encoding.UTF8.GetBytes("inside"));

        var result = await Run(TransferKind.Copy, Path.Combine(zip, "inner.txt"), Local("out.txt"));

        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.AreEqual("inside", File.ReadAllText(Local("out.txt")));
    }

    [TestMethod]
    public async Task AScanMeasuresATreeOnTheLocation()
    {
        await RemoteFile(@"t\a.txt", "12345");
        await RemoteFile(@"t\deep\b.txt", "123");
        LocalFile("c.txt", "12");

        var scan = await FileTransferEngine.ScanAsync([$@"{Root}\t", Local("c.txt")], _vfs);

        Assert.AreEqual(3, scan.TotalFiles);
        Assert.AreEqual(10, scan.TotalBytes);
        Assert.AreEqual(2, scan.TotalFolders);
    }
}
