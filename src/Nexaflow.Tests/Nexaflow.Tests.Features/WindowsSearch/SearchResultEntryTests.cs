using Nexaflow.Features.WindowsSearch;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("search-results-list")]
public class SearchResultEntryTests
{
    private static SearchResultEntry Make(
        string kind      = "document",
        long?  sizeBytes = null,
        DateTime? modified = null) =>
        new()
        {
            FilePath  = @"C:\foo\bar.txt",
            FileName  = "bar.txt",
            Directory = @"C:\foo",
            Kind      = kind,
            SizeBytes = sizeBytes,
            Modified  = modified,
        };

    // ── IsFolder ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsFolder_KindFolder_True()
        => Assert.IsTrue(Make(kind: "folder").IsFolder);

    [TestMethod]
    public void IsFolder_KindFolderCaseInsensitive_True()
        => Assert.IsTrue(Make(kind: "Folder").IsFolder);

    [TestMethod]
    public void IsFolder_KindContainsFolder_True()
        => Assert.IsTrue(Make(kind: "file folder").IsFolder);

    [TestMethod]
    public void IsFolder_KindDocument_False()
        => Assert.IsFalse(Make(kind: "document").IsFolder);

    // ── SizeDisplay ──────────────────────────────────────────────────────────

    [TestMethod]
    public void SizeDisplay_Null_Empty()
        => Assert.AreEqual(string.Empty, Make(sizeBytes: null).SizeDisplay);

    [TestMethod]
    public void SizeDisplay_Zero_Empty()
        => Assert.AreEqual(string.Empty, Make(sizeBytes: 0).SizeDisplay);

    [TestMethod]
    public void SizeDisplay_Bytes_FormattedAsB()
        => Assert.AreEqual("512 B", Make(sizeBytes: 512).SizeDisplay);

    [TestMethod]
    public void SizeDisplay_Kilobytes_FormattedAsKb()
    {
        var entry = Make(sizeBytes: 2048);
        Assert.AreEqual("2.0 KB", entry.SizeDisplay);
    }

    [TestMethod]
    public void SizeDisplay_Megabytes_FormattedAsMb()
    {
        var entry = Make(sizeBytes: 1024L * 1024 * 3);
        Assert.AreEqual("3.0 MB", entry.SizeDisplay);
    }

    [TestMethod]
    public void SizeDisplay_Gigabytes_FormattedAsGb()
    {
        var entry = Make(sizeBytes: 1024L * 1024 * 1024 * 2);
        Assert.AreEqual("2.0 GB", entry.SizeDisplay);
    }

    // ── ModifiedDisplay ───────────────────────────────────────────────────────

    [TestMethod]
    public void ModifiedDisplay_Null_Empty()
        => Assert.AreEqual(string.Empty, Make(modified: null).ModifiedDisplay);

    [TestMethod]
    public void ModifiedDisplay_KnownDate_FormattedCorrectly()
    {
        var dt    = new DateTime(2024, 3, 15, 10, 30, 0);
        var entry = Make(modified: dt);

        Assert.AreEqual("2024-03-15 10:30", entry.ModifiedDisplay);
    }
}
