using System.IO;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

[TestClass]
public class LlmAttachmentTests
{
    [TestMethod]
    public void IsImage_TrueForImageMime()
    {
        Assert.IsTrue(new LlmAttachment("shot", "image/png").IsImage);
        Assert.IsTrue(new LlmAttachment("shot", "IMAGE/JPEG").IsImage);   // case-insensitive
    }

    [TestMethod]
    public void IsImage_FallsBackToExtension_WhenNoMime()
    {
        Assert.IsTrue(new LlmAttachment("c:/x/pic.PNG").IsImage);
        Assert.IsTrue(new LlmAttachment("c:/x/pic.jpeg").IsImage);
        Assert.IsFalse(new LlmAttachment("c:/x/notes.txt").IsImage);
        Assert.IsFalse(new LlmAttachment("c:/x/data.json").IsImage);
    }

    [TestMethod]
    public void ResolvedMimeType_PrefersExplicitMime_ThenInfersFromExtension()
    {
        Assert.AreEqual("image/png",  new LlmAttachment("x", "image/png").ResolvedMimeType);
        Assert.AreEqual("image/jpeg", new LlmAttachment("pic.jpg").ResolvedMimeType);
        Assert.AreEqual("image/gif",  new LlmAttachment("pic.gif").ResolvedMimeType);
        Assert.AreEqual("image/png",  new LlmAttachment("pic.unknown").ResolvedMimeType);   // default
    }

    [TestMethod]
    public void ReadBytes_PrefersInMemoryBytes_OverFile()
    {
        var bytes = new byte[] { 10, 20, 30 };
        var att   = new LlmAttachment("does-not-exist.png", "image/png", bytes);
        CollectionAssert.AreEqual(bytes, att.ReadBytes());
    }

    [TestMethod]
    public void ReadBytes_ReadsFromDisk_WhenNoBytes()
    {
        var path  = Path.Combine(Path.GetTempPath(), $"attach_{System.Guid.NewGuid():N}.bin");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, bytes);
        try
        {
            CollectionAssert.AreEqual(bytes, new LlmAttachment(path).ReadBytes());
        }
        finally { File.Delete(path); }
    }
}
