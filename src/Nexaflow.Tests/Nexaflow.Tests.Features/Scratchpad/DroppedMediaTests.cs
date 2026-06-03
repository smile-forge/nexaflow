using System;
using System.IO;
using Nexaflow.Features.Scratchpad.Services;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
public class DroppedMediaTests
{
    [TestMethod]
    public void IsImageFile_RecognisesImageExtensionsCaseInsensitively()
    {
        Assert.IsTrue(DroppedMedia.IsImageFile("a.png"));
        Assert.IsTrue(DroppedMedia.IsImageFile("a.JPG"));
        Assert.IsTrue(DroppedMedia.IsImageFile(@"C:\x\b.webp"));
        Assert.IsFalse(DroppedMedia.IsImageFile("a.txt"));
        Assert.IsFalse(DroppedMedia.IsImageFile("noextension"));
    }

    [TestMethod]
    public void FileLinkMarkdown_File_UsesDocumentIconAndFileUri()
    {
        var file = Path.Combine(Path.GetTempPath(), $"dm_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");
        try
        {
            var md = DroppedMedia.FileLinkMarkdown(file);
            StringAssert.StartsWith(md, "[📄 ");
            StringAssert.Contains(md, Path.GetFileName(file));
            StringAssert.Contains(md, new Uri(file).AbsoluteUri);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void FileLinkMarkdown_Directory_UsesFolderIcon()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dmdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            StringAssert.StartsWith(DroppedMedia.FileLinkMarkdown(dir), "[📁 ");
        }
        finally { Directory.Delete(dir); }
    }

    [TestMethod]
    public void CopyImageInto_CopiesUnderUniqueNamePreservingExtension()
    {
        var src = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid():N}.png");
        File.WriteAllText(src, "fake-image-bytes");
        var dir = Path.Combine(Path.GetTempPath(), $"att_{Guid.NewGuid():N}");
        try
        {
            var name = DroppedMedia.CopyImageInto(src, dir);

            Assert.IsNotNull(name);
            Assert.IsTrue(File.Exists(Path.Combine(dir, name!)), "copied file exists in the attachment folder");
            StringAssert.EndsWith(name!, ".png");
            Assert.AreNotEqual(Path.GetFileName(src), name, "a unique suffix is added to avoid collisions");
        }
        finally
        {
            File.Delete(src);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CopyImageInto_MissingSource_ReturnsNull()
        => Assert.IsNull(DroppedMedia.CopyImageInto(
            Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.png"),
            Path.Combine(Path.GetTempPath(), $"att_{Guid.NewGuid():N}")));
}
