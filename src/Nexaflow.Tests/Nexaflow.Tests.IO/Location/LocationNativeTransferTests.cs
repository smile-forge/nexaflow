using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A native path is for readers. A copy, move or delete named by a location's path goes through the location, which
/// knows its exact names, its links and its permissions — never through the Windows folder that shows it. The native
/// root here is a decoy folder: anything the engine does there is a change made behind the location's back.
/// </summary>
[TestClass]
[CoversNode("io-locations-transfer")]
public sealed class LocationNativeTransferTests
{
    private const string Root = "::native-probe";

    private LocationFileSystem _vfs = null!;
    private string _decoy = string.Empty;

    private sealed class SameNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _decoy = TestTemp.CreateSubdirectory("nexa-native-decoy-").FullName;
        File.WriteAllText(Path.Combine(_decoy, "file.txt"), "decoy");

        var provider = new MemoryLocationProvider(new MemoryLocationHost("native", TimeSpan.Zero));
        var profile  = new ConnectionProfile("native-probe", "memory", "Probe", "native");
        _vfs = new LocationFileSystem(new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName));
        _vfs.RegisterLocation(new LocationMount(profile, provider.Capabilities,
                                                new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null)))
        {
            NativeRoots = [_decoy],
            NativePaths = new SameNames(),
        });
        await _vfs.WriteAllTextAsync($@"{Root}\file.txt", "real");
    }

    private Task<TransferResult> RunAsync(TransferKind kind, string destination)
        => FileTransferEngine.RunAsync(new FileTransferRequest(kind, [new TransferItem($@"{Root}\file.txt", destination)],
                                                               ConflictPolicy.Fail, false) { FileSystem = _vfs });

    [TestMethod]
    public async Task ADeleteGoesThroughTheLocation()
    {
        await RunAsync(TransferKind.Delete, $@"{Root}\file.txt");

        Assert.IsTrue(File.Exists(Path.Combine(_decoy, "file.txt")), "nothing is deleted through the Windows folder");
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\file.txt"));
    }

    [TestMethod]
    public async Task ACopyWithinGoesThroughTheLocation()
    {
        await RunAsync(TransferKind.Copy, $@"{Root}\copy.txt");

        Assert.IsFalse(File.Exists(Path.Combine(_decoy, "copy.txt")), "nothing is copied through the Windows folder");
        Assert.AreEqual("real", await _vfs.ReadAllTextAsync($@"{Root}\copy.txt"));
    }

    [TestMethod]
    public async Task AMoveGoesThroughTheLocation()
    {
        await RunAsync(TransferKind.Move, $@"{Root}\moved.txt");

        Assert.IsTrue(File.Exists(Path.Combine(_decoy, "file.txt")), "nothing is moved through the Windows folder");
        Assert.AreEqual("real", await _vfs.ReadAllTextAsync($@"{Root}\moved.txt"));
    }

    // ── Changes named by the Windows folder ─────────────────────────────────

    private string Decoy(string name) => Path.Combine(_decoy, name);

    [TestMethod]
    public async Task AWriteNamedByTheWindowsFolder_GoesThroughTheLocation()
    {
        await _vfs.WriteAllTextAsync(Decoy("file.txt"), "saved");

        Assert.AreEqual("saved", await _vfs.ReadAllTextAsync($@"{Root}\file.txt"));
        Assert.AreEqual("decoy", File.ReadAllText(Decoy("file.txt")), "nothing is written through the Windows folder");
    }

    [TestMethod]
    public async Task AStreamedSaveNamedByTheWindowsFolder_GoesThroughTheLocation()
    {
        await using (var write = await _vfs.BeginWriteAsync(Decoy("file.txt"), WriteMode.Replace))
        {
            await write.Content.WriteAsync("streamed"u8.ToArray());
            await write.CommitAsync(CancellationToken.None);
        }

        Assert.AreEqual("streamed", await _vfs.ReadAllTextAsync($@"{Root}\file.txt"));
        Assert.AreEqual("decoy", File.ReadAllText(Decoy("file.txt")));
    }

    [TestMethod]
    public async Task AMoveADeleteAndANewFolderNamedByTheWindowsFolder_GoThroughTheLocation()
    {
        await _vfs.MoveAsync(Decoy("file.txt"), Decoy("moved.txt"), replace: false);
        await _vfs.CreateDirectoryAsync(Decoy("made"));

        Assert.AreEqual("real", await _vfs.ReadAllTextAsync($@"{Root}\moved.txt"));
        Assert.IsTrue(await _vfs.IsDirectoryAsync($@"{Root}\made"));
        Assert.IsTrue(File.Exists(Decoy("file.txt")) && !File.Exists(Decoy("moved.txt")) && !Directory.Exists(Decoy("made")));

        await _vfs.DeleteAsync(Decoy("moved.txt"), recursive: false);
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\moved.txt"));
    }

    [TestMethod]
    public async Task ATransferNamedByTheWindowsFolder_GoesThroughTheLocation()
    {
        await FileTransferEngine.RunAsync(new FileTransferRequest(TransferKind.Copy, [new TransferItem(Decoy("file.txt"), Decoy("copy.txt"))],
                                                                  ConflictPolicy.Fail, false) { FileSystem = _vfs });

        Assert.AreEqual("real", await _vfs.ReadAllTextAsync($@"{Root}\copy.txt"), "the location's own bytes, copied there");
        Assert.IsFalse(File.Exists(Decoy("copy.txt")), "nothing is copied through the Windows folder");
    }
}
