using System.IO.Compression;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// An archive on a location this machine also reaches as a Windows folder — a WSL distro — runs over local bytes, as all
/// archive work does: it is read through that folder, a native path being for readers, and every change to it is made on
/// a local copy and put in place through the location, as is anything extracted into the location. The Windows folder here
/// holds the archive's bytes for reading; nothing may be written there.
/// </summary>
[TestClass]
[CoversNode("io-locations-routing")]
public sealed class LocationArchiveTests
{
    private const string Root = "::archive-probe";

    private LocationFileSystem _vfs = null!;
    private string _native = string.Empty;

    /// <summary>Names as 9P shows a distro's to Windows: a colon, which a Windows name cannot hold, as a private-use character.</summary>
    private sealed class ColonMapped : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name.Replace(':', '');
        public string FromNativeName(string nativeName) => nativeName.Replace('', ':');
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        return buffer.ToArray();
    }

    private static Dictionary<string, string> Entries(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        return zip.Entries.Where(e => e.Name.Length > 0)
                  .ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    /// <summary>A file the location holds and its Windows folder shows: the same bytes in both.</summary>
    private async Task BothAsync(string relative, byte[] bytes)
    {
        if (Path.GetDirectoryName(relative) is { Length: > 0 } folder) await _vfs.CreateDirectoryAsync($@"{Root}\{folder}");
        await _vfs.WriteAllBytesAsync($@"{Root}\{relative}", bytes);
        var native = Path.Combine(_native, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        await File.WriteAllBytesAsync(native, bytes);
    }

    [TestInitialize]
    public async Task Setup()
    {
        _native = TestTemp.CreateSubdirectory("nexa-archive-native-").FullName;
        var provider = new MemoryLocationProvider(new MemoryLocationHost("archive", TimeSpan.Zero));
        var profile  = new ConnectionProfile("archive-probe", "memory", "Probe", "archive");
        var shared   = new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName);
        shared.RegisterHandler(new Nexaflow.Tests.IO.Common.ZipTestHandler(modifiable: true));
        _vfs = new LocationFileSystem(shared);
        _vfs.RegisterLocation(new LocationMount(profile, provider.Capabilities,
                                                new MemoryLocationSessionSource(provider, profile, new StoredCredentials(null)))
        {
            NativeRoots = [_native],
            NativePaths = new ColonMapped(),
        });
        await BothAsync("book.zip", Zip(("a.txt", "alpha"), ("dir/b.txt", "beta")));
    }

    [TestMethod]
    public async Task AnArchiveOnTheLocation_IsBrowsedAndRead()
    {
        Assert.IsTrue(await _vfs.IsContainerAsync($@"{Root}\book.zip"));
        var names = new List<string>();
        await foreach (var entry in _vfs.EnumerateAsync($@"{Root}\book.zip")) names.Add(entry.Name);

        CollectionAssert.AreEquivalent(new[] { "a.txt", "dir" }, names);
        Assert.AreEqual("beta", await _vfs.ReadAllTextAsync($@"{Root}\book.zip\dir\b.txt"));
        Assert.IsNotNull(await _vfs.DescribeArchiveAsync($@"{Root}\book.zip"));
        Assert.AreEqual(VirtualBacking.Materialized, _vfs.GetBacking($@"{Root}\book.zip\a.txt"), "one entry, as from any archive");
    }

    [TestMethod]
    public async Task ExtractingIntoTheLocation_WritesThroughIt()
    {
        await _vfs.ExtractAllAsync($@"{Root}\book.zip", $@"{Root}\out");

        Assert.AreEqual("alpha", await _vfs.ReadAllTextAsync($@"{Root}\out\a.txt"));
        Assert.AreEqual("beta", await _vfs.ReadAllTextAsync($@"{Root}\out\dir\b.txt"));
        Assert.IsFalse(Directory.Exists(Path.Combine(_native, "out")), "nothing is written through the Windows folder");
    }

    [TestMethod]
    public async Task AnArchiveMadeOnTheLocation_IsWrittenThroughIt()
    {
        await BothAsync(@"src\one.txt", "one"u8.ToArray());
        await BothAsync(@"src\two.txt", "two"u8.ToArray());

        await _vfs.CreateArchiveAsync($@"{Root}\made.zip", $@"{Root}\src");

        CollectionAssert.AreEquivalent(new[] { "one.txt", "two.txt" },
                                       Entries(await _vfs.ReadAllBytesAsync($@"{Root}\made.zip")).Keys.ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(_native, "made.zip")));
    }

    [TestMethod]
    public async Task AddingToAnArchiveOnTheLocation_RewritesItThroughTheLocation()
    {
        var added = Path.Combine(TestTemp.CreateSubdirectory("nexa-archive-add-").FullName, "c.txt");
        await File.WriteAllTextAsync(added, "gamma");
        var before = await File.ReadAllBytesAsync(Path.Combine(_native, "book.zip"));

        await _vfs.AddFilesAsync($@"{Root}\book.zip", [(added, "c.txt")]);

        var entries = Entries(await _vfs.ReadAllBytesAsync($@"{Root}\book.zip"));
        Assert.AreEqual("gamma", entries["c.txt"]);
        Assert.AreEqual("alpha", entries["a.txt"]);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(Path.Combine(_native, "book.zip")), "the Windows folder's copy is untouched");
    }

    [TestMethod]
    public async Task AnEntryWrittenInAnArchiveOnTheLocation_RewritesItThroughTheLocation()
    {
        await _vfs.WriteAllTextAsync($@"{Root}\book.zip\a.txt", "changed");

        Assert.AreEqual("changed", Entries(await _vfs.ReadAllBytesAsync($@"{Root}\book.zip"))["a.txt"]);
        Assert.AreEqual("alpha", Entries(await File.ReadAllBytesAsync(Path.Combine(_native, "book.zip")))["a.txt"]);
    }

    [TestMethod]
    public async Task ARecompressedArchiveOnTheLocation_IsWrittenThroughIt()
    {
        await _vfs.RepackageAsync($@"{Root}\book.zip", $@"{Root}\again.zip");

        CollectionAssert.AreEquivalent(new[] { "a.txt", "dir/b.txt" },
                                       Entries(await _vfs.ReadAllBytesAsync($@"{Root}\again.zip")).Keys.ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(_native, "again.zip")));
    }

    [TestMethod]
    public async Task ExtractingIntoTheLocation_KeepsANameAWindowsFolderCannotHold()
    {
        await BothAsync("clock.zip", Zip(("times/at 12:30.txt", "half past"), ("../out.txt", "climbs out")));

        await _vfs.ExtractAllAsync($@"{Root}\clock.zip", $@"{Root}\out");

        Assert.AreEqual("half past", await _vfs.ReadAllTextAsync(_vfs.ChildPath($@"{Root}\out\times", "at 12:30.txt")));
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\out.txt"), "an entry climbing out of the folder is left out");
    }

    [TestMethod]
    public async Task AnArchiveMadeOfTheLocationsFiles_NamesThemAsTheLocationDoes()
    {
        // The Windows folder shows the colon mapped, as 9P shows a distro's.
        await _vfs.CreateDirectoryAsync($@"{Root}\src");
        Directory.CreateDirectory(Path.Combine(_native, "src", "times"));
        await File.WriteAllTextAsync(Path.Combine(_native, "src", "times", "at 1230.txt"), "half past");

        await _vfs.CreateArchiveAsync($@"{Root}\made.zip", $@"{Root}\src");
        await _vfs.CreateArchiveAsync($@"{Root}\picked.zip", [$@"{Root}\src\times"]);

        CollectionAssert.AreEquivalent(new[] { "times/at 12:30.txt" }, Entries(await _vfs.ReadAllBytesAsync($@"{Root}\made.zip")).Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { "times/at 12:30.txt" }, Entries(await _vfs.ReadAllBytesAsync($@"{Root}\picked.zip")).Keys.ToArray(),
                                       "a picked folder goes in under its own name");
    }

    [TestMethod]
    public async Task AnEntryIsNotRemovedOrMovedInsideAnArchive_HereAsOnThisPc()
    {
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => _vfs.DeleteAsync($@"{Root}\book.zip\a.txt", recursive: false).AsTask());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => _vfs.MoveAsync($@"{Root}\book.zip\a.txt", $@"{Root}\book.zip\z.txt", replace: false).AsTask());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => _vfs.CreateDirectoryAsync($@"{Root}\book.zip\new").AsTask());
    }
}
