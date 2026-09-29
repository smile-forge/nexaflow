using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Hdf5.Vfs;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Hdf5;

/// <summary>
/// An HDF5 file as a VFS container, through the process-wide VFS the explorer uses: groups are folders, datasets
/// are .npy or .txt files of their exact length, what has nothing to read is left out, and a file inside a zip
/// browses the same.
/// </summary>
[TestClass]
[CoversNode("hdf5-vfs")]
public sealed class Hdf5ArchiveHandlerTests
{
    private string _dir = null!;
    private string _h5 = null!;

    private static VirtualFileSystem Vfs => VirtualFileSystem.Instance;

    [TestInitialize]
    public void Setup()
    {
        Vfs.RegisterHandler(new Hdf5ArchiveHandler());
        Vfs.RegisterHandler(new ZipArchiveHandler());
        _dir = Path.Combine(Path.GetTempPath(), "nexa-hdf5-vfs-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _h5 = Path.Combine(_dir, "experiment.h5");
        File.Copy(TestSampleData.Path("hdf5", "experiment.h5"), _h5);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void TheFile_IsAContainer_WhoseRootListsGroupsAsFolders()
    {
        Assert.IsTrue(Vfs.IsContainer(_h5));
        var root = Vfs.EnumerateEntries(_h5).ToDictionary(e => e.Name);

        Assert.IsTrue(root["calibration"].IsDirectory);
        Assert.IsTrue(root["measurements"].IsDirectory);
        Assert.IsTrue(root["metadata"].IsDirectory);
        Assert.IsFalse(root["latest.npy"].IsDirectory, "a soft link to a dataset lists as that dataset");
        Assert.IsFalse(root.ContainsKey("dangling"), "a link to nothing has nothing to read");
        Assert.IsFalse(root.ContainsKey("elsewhere"));
        Assert.IsFalse(root.ContainsKey("typedef"), "a named datatype holds no elements");
    }

    [TestMethod]
    public void Datasets_ListAsNpyOrTxt_OfTheirExactLength()
    {
        var metadata = Vfs.EnumerateEntries(Path.Combine(_h5, "metadata")).ToDictionary(e => e.Name);
        CollectionAssert.AreEquivalent(
            new[] { "blob.npy", "config.txt", "empty.npy", "flags.npy", "labels.npy", "notes.txt", "scalar.npy", "state.npy" },
            metadata.Keys.ToArray());

        var notes = Path.Combine(_h5, "metadata", "notes.txt");
        Assert.AreEqual("first\nsecond line\\nwrapped\nthird\n", Encoding.UTF8.GetString(Vfs.ReadAllBytes(notes)));
        Assert.AreEqual(Vfs.ReadAllBytes(notes).Length, metadata["notes.txt"].Size);

        var temperature = Path.Combine(_h5, "measurements", "temperature.npy");
        var bytes = Vfs.ReadAllBytes(temperature);
        Assert.AreEqual(0x93, bytes[0]);
        Assert.AreEqual("NUMPY", Encoding.ASCII.GetString(bytes, 1, 5));
        Assert.AreEqual(bytes.Length, Vfs.GetLength(temperature));
    }

    [TestMethod]
    public void EntryInfo_TellsGroupsFromDatasets_AndNothingFromSomething()
    {
        Assert.IsTrue(Vfs.IsDirectory(Path.Combine(_h5, "calibration")));
        Assert.IsFalse(Vfs.IsDirectory(Path.Combine(_h5, "calibration", "gain.npy")));
        Assert.IsTrue(Vfs.Exists(Path.Combine(_h5, "calibration", "gain.npy")));
        Assert.IsFalse(Vfs.Exists(Path.Combine(_h5, "calibration", "gain")), "a dataset is named by its projection");
        Assert.IsFalse(Vfs.Exists(Path.Combine(_h5, "calibration", "gain.txt")), "and only by the one it has");
    }

    [TestMethod]
    public void ExtractAll_WalksEveryGroup()
    {
        var summary = Vfs.DescribeArchive(_h5)!;
        var names = summary.Entries.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "measurements/frames.npy");
        CollectionAssert.Contains(names, "metadata/notes.txt");
        CollectionAssert.Contains(names, "calibration");
    }

    [TestMethod]
    public void AFileInsideAZip_BrowsesTheSame()
    {
        var zip = Path.Combine(_dir, "bundle.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(_h5, "run/experiment.h5");

        var inside = Path.Combine(zip, "run", "experiment.h5");
        var names = Vfs.EnumerateEntries(Path.Combine(inside, "measurements")).Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "events.npy");
        Assert.AreEqual("{\"bins\": 4096}\n", Encoding.UTF8.GetString(Vfs.ReadAllBytes(Path.Combine(inside, "metadata", "config.txt"))));
    }
}
