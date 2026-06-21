using System;
using System.IO;
using Nexaflow.Providers.Local;
using Nexaflow.Providers.Local.Catalog;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class LocalModelDownloaderTests
{
    [TestMethod]
    public void IsPresent_TrueForGgufMagic_FalseForGarbage()
    {
        var root = Path.Combine(Path.GetTempPath(), "nf-local-" + Guid.NewGuid().ToString("N"));
        try
        {
            var v   = new LocalModelVariant { Id = "m", Files = ["a.gguf"] };
            var dir = Path.Combine(root, "m");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "a.gguf");

            File.WriteAllText(file, "<html>404 Not Found</html>");
            Assert.IsFalse(LocalModelDownloader.IsPresent(v, root), "an HTML error page must not count as a present model");

            File.WriteAllBytes(file, [(byte)'G', (byte)'G', (byte)'U', (byte)'F', 0, 0, 0, 0]);
            Assert.IsTrue(LocalModelDownloader.IsPresent(v, root), "a file with the GGUF magic should count as present");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best effort */ } }
    }

    [TestMethod]
    public void IsPresent_FalseWhenFileMissing()
    {
        var v = new LocalModelVariant { Id = "m", Files = ["a.gguf"] };
        var root = Path.Combine(Path.GetTempPath(), "nf-nope-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(LocalModelDownloader.IsPresent(v, root));
    }

    [TestMethod]
    public void IsPresent_FalseWhenAnyShardMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "nf-local-" + Guid.NewGuid().ToString("N"));
        try
        {
            var v   = new LocalModelVariant { Id = "m", Files = ["s1.gguf", "s2.gguf"] };
            var dir = Path.Combine(root, "m");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "s1.gguf"), [(byte)'G', (byte)'G', (byte)'U', (byte)'F']);
            // s2 absent
            Assert.IsFalse(LocalModelDownloader.IsPresent(v, root));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best effort */ } }
    }
}
