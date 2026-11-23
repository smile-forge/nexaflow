using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A file copied from one location to another is made there with its own permission bits, not the destination's
/// defaults — a private key stays private, a script stays runnable — while a file it is copied over keeps its own, as a
/// save over it does. A file from this disk, which keeps no POSIX bits, takes the location's defaults.
/// </summary>
[TestClass]
[CoversNode("io-locations-transfer-permissions")]
public sealed class LocationPermissionTransferTests
{
    private const string Nas = "::memory-nas";
    private const string Box = "::memory-box";

    private static readonly UnixFileMode Private = Octal("600");
    private static readonly UnixFileMode Script  = Octal("755");
    private static readonly UnixFileMode Shared  = Octal("644");

    private LocationFileSystem _vfs = null!;
    private string _local = string.Empty;

    private static UnixFileMode Octal(string bits) => (UnixFileMode)Convert.ToInt32(bits, 8);

    [TestInitialize]
    public void Setup()
    {
        _local = TestTemp.CreateSubdirectory("nexa-permissions-").FullName;
        _vfs   = new LocationFileSystem(new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName));
        Mount("memory-nas", "nas");
        Mount("memory-box", "box");
    }

    private void Mount(string id, string name)
    {
        var provider = new MemoryLocationProvider(new MemoryLocationHost(name, TimeSpan.Zero) { KeepsPermissions = true });
        var profile  = new ConnectionProfile(id, "memory", name, name);
        _vfs.RegisterLocation(new LocationMount(profile, provider.Capabilities,
                                                new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null))));
    }

    private async Task<string> FileWith(string path, string content, UnixFileMode? bits)
    {
        await _vfs.CreateDirectoryAsync(Path.GetDirectoryName(path)!);
        await using var write = await _vfs.BeginWriteAsync(path, WriteMode.CreateNew, permissions: bits);
        await write.Content.WriteAsync(Encoding.UTF8.GetBytes(content));
        await write.CommitAsync(CancellationToken.None);
        return path;
    }

    private async Task<UnixFileMode?> BitsOf(string path) => (await _vfs.GetEntryAsync(path))!.Mode;

    private async Task Copy(string source, string destination, ConflictPolicy conflicts = ConflictPolicy.AutoRename)
    {
        var result = await FileTransferEngine.RunAsync(
            new FileTransferRequest(TransferKind.Copy, [new TransferItem(source, destination)], conflicts) { FileSystem = _vfs });
        Assert.IsTrue(result.Completed && result.Failures.Count == 0, string.Join("; ", result.Failures.Select(f => f.Message)));
    }

    [TestMethod]
    public async Task AFileCopiedToAnotherLocation_IsMadeWithItsOwnBits()
    {
        var key    = await FileWith($@"{Nas}\home\id_ed25519", "secret", Private);
        var script = await FileWith($@"{Nas}\home\run.sh", "echo hi", Script);
        await _vfs.CreateDirectoryAsync($@"{Box}\keys");
        await _vfs.CreateDirectoryAsync($@"{Box}\bin");

        await Copy(key, $@"{Box}\keys\id_ed25519");
        await Copy(script, $@"{Box}\bin\run.sh");

        Assert.AreEqual(Private, await BitsOf($@"{Box}\keys\id_ed25519"), "a private key stays private");
        Assert.AreEqual(Script,  await BitsOf($@"{Box}\bin\run.sh"), "a script stays runnable");
    }

    [TestMethod]
    public async Task AFileCopiedOverAnother_KeepsTheReplacedFilesBits()
    {
        var source = await FileWith($@"{Nas}\notes.txt", "new", Script);
        var target = await FileWith($@"{Box}\notes.txt", "old", Shared);

        await Copy(source, target, ConflictPolicy.Overwrite);

        Assert.AreEqual("new", await _vfs.ReadAllTextAsync(target));
        Assert.AreEqual(Shared, await BitsOf(target), "a copy over a file keeps that file's bits, as a save over it does");
    }

    [TestMethod]
    public async Task AFolderCopiedToAnotherLocation_CarriesEachFilesBits()
    {
        await FileWith($@"{Nas}\project\deploy.sh", "deploy", Script);
        await FileWith($@"{Nas}\project\config\token", "t", Private);

        await Copy($@"{Nas}\project", $@"{Box}\project");

        Assert.AreEqual(Script,  await BitsOf($@"{Box}\project\deploy.sh"));
        Assert.AreEqual(Private, await BitsOf($@"{Box}\project\config\token"));
    }

    [TestMethod]
    public async Task AFileFromThisDisk_TakesTheLocationsDefaults()
    {
        var local = Path.Combine(_local, "readme.txt");
        File.WriteAllText(local, "hello");

        await Copy(local, $@"{Box}\readme.txt");

        Assert.IsNull(await BitsOf($@"{Box}\readme.txt"), "this disk keeps no POSIX bits, so none are carried");
    }
}
