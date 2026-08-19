using System.IO;
using System.Linq;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>Drives the shared <c>archive/</c> sample fixtures through the VFS, covering a plain zip and a
/// nested zip (zip-in-zip) end to end.</summary>
[TestClass]
[CoversNode("compressed-vfs")]
public class ArchiveSampleTests
{
    private static VirtualFileSystem Vfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        return vfs;
    }

    [TestMethod]
    public void SampleZip_BrowsesAndReads()
    {
        var zip = TestSampleData.Path("archive", "sample.zip");
        var vfs = Vfs();

        Assert.IsTrue(vfs.IsContainer(zip));
        var top = vfs.EnumerateEntries(zip).Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "docs", "readme.txt" }, top.ToList());

        StringAssert.Contains(vfs.ReadAllText(Path.Combine(zip, "readme.txt")), "Archive sample");
        StringAssert.Contains(vfs.ReadAllText(Path.Combine(zip, "docs", "data.json")), "\"name\"");
    }

    [TestMethod]
    public void NestedZip_ResolvesThroughTwoLayers()
    {
        var nested = TestSampleData.Path("archive", "nested.zip");
        var vfs = Vfs();

        var top = vfs.EnumerateEntries(nested).Select(e => e.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "top.txt", "inner.zip" }, top.ToList());

        // inner.zip is itself a container the VFS descends into — both listing and reading it.
        var innerZip = Path.Combine(nested, "inner.zip");
        Assert.IsTrue(vfs.IsDirectory(innerZip));
        CollectionAssert.AreEqual(new[] { "hello.txt" },
            vfs.EnumerateEntries(innerZip).Select(e => e.Name).ToList());
        Assert.AreEqual("hello from inside a nested archive",
            vfs.ReadAllText(Path.Combine(innerZip, "hello.txt")));
    }
}
