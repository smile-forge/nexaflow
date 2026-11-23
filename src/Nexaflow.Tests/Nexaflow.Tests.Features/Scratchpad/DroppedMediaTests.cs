using System;
using System.IO;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Interaction;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
[CoversNode("universal-drop-target")]
public class DroppedMediaTests
{
    private static DragPayload Text(string s) => new((DragKinds.Text, s));

    [TestMethod]
    public void TryGetSingleUrl_BareHttpUrl_Succeeds()
    {
        Assert.IsTrue(DroppedMedia.TryGetSingleUrl(Text("https://example.com/a/b?x=1"), out var url));
        Assert.AreEqual("https://example.com/a/b?x=1", url);
    }

    [TestMethod]
    public void TryGetSingleUrl_TrimsSurroundingWhitespace()
    {
        Assert.IsTrue(DroppedMedia.TryGetSingleUrl(Text("  https://example.com  "), out var url));
        Assert.AreEqual("https://example.com", url);
    }

    [TestMethod]
    public void TryGetSingleUrl_MultipleTokens_Fails()
        => Assert.IsFalse(DroppedMedia.TryGetSingleUrl(Text("see https://example.com now"), out _));

    [TestMethod]
    public void TryGetSingleUrl_NonHttpScheme_Fails()
        => Assert.IsFalse(DroppedMedia.TryGetSingleUrl(Text("file:///C:/x.txt"), out _));

    [TestMethod]
    public void TryGetSingleUrl_NoText_Fails()
        => Assert.IsFalse(DroppedMedia.TryGetSingleUrl(new DragPayload(), out _));

    [TestMethod]
    public void KeepIn_AFileThatCameAsContent_IsTheNotesOwn_UnderItsOwnName()
    {
        var root = TestTemp.CreateSubdirectory("dm-keep-").FullName;
        var staged = Path.Combine(root, "report.pdf");
        File.WriteAllText(staged, "content");

        var kept = DroppedMedia.KeepIn(staged, Path.Combine(root, "attachments"));
        File.Delete(staged);

        Assert.IsNotNull(kept);
        Assert.AreEqual("report.pdf", Path.GetFileName(kept));
        Assert.AreEqual("content", File.ReadAllText(kept), "it outlives the staged copy");
    }

    [TestMethod]
    public void KeepIn_AFolder_IsKeptWholeWithWhatIsInIt()
    {
        var root = TestTemp.CreateSubdirectory("dm-keep-").FullName;
        var staged = Path.Combine(root, "album");
        Directory.CreateDirectory(Path.Combine(staged, "inner", "empty"));
        File.WriteAllText(Path.Combine(staged, "inner", "a.txt"), "a");

        var kept = DroppedMedia.KeepIn(staged, Path.Combine(root, "attachments"));

        Assert.IsNotNull(kept);
        Assert.AreEqual("a", File.ReadAllText(Path.Combine(kept, "inner", "a.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(kept, "inner", "empty")));
    }

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
        var file = Path.Combine(TestTemp.Root, $"dm_{Guid.NewGuid():N}.txt");
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
        var dir = Path.Combine(TestTemp.Root, $"dmdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            StringAssert.StartsWith(DroppedMedia.FileLinkMarkdown(dir), "[📁 ");
        }
        finally { Directory.Delete(dir); }
    }

    [TestMethod]
    public void FileLinkMarkdown_ANameShapedLikeALink_CannotPointAnywhereElse()
    {
        var path = Path.Combine(TestTemp.Root, "invoice](calc.exe) [x.pdf");

        var md = DroppedMedia.FileLinkMarkdown(path);

        StringAssert.Contains(md, @"invoice\](calc.exe) \[x.pdf", "the label is not ended by the name");
        var address = md[(md.LastIndexOf("](file:", StringComparison.Ordinal) + 2)..^1];
        Assert.IsFalse(address.Contains('(') || address.Contains(')'), "the address is not ended by the name");
        Assert.AreEqual(path, new Uri(address).LocalPath);
    }

    [TestMethod]
    public void EmbedMarkdown_AnyName_IsFoundAgainByWhatShowsTheNote()
    {
        var dir = TestTemp.CreateSubdirectory("dm-embed-").FullName;
        const string name = "my photo (1)](x) #2 100%.png";
        File.WriteAllText(Path.Combine(dir, name), "picture");

        var address = DroppedMedia.EmbedMarkdown(name)[4..^1];

        Assert.IsFalse(address.Any(c => c is ' ' or '(' or ')'), address);
        Assert.AreEqual(Path.Combine(dir, name), MarkdownPictures.Under(address, dir));
    }

    [TestMethod]
    public void CopyImageInto_CopiesUnderUniqueNamePreservingExtension()
    {
        var src = Path.Combine(TestTemp.Root, $"src_{Guid.NewGuid():N}.png");
        File.WriteAllText(src, "fake-image-bytes");
        var dir = Path.Combine(TestTemp.Root, $"att_{Guid.NewGuid():N}");
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
            Path.Combine(TestTemp.Root, $"nope_{Guid.NewGuid():N}.png"),
            Path.Combine(TestTemp.Root, $"att_{Guid.NewGuid():N}")));
}
