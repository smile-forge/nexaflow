using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Common;

/// <summary>
/// The shared viewer-breadcrumb helper: every file/media viewer shows "parent dir › leaf" with a single
/// clickable parent crumb that opens a file-explorer tab at that folder.
/// </summary>
[TestClass]
[CoversNode("chrome-breadcrumb-crosstab")]
public class FileBreadcrumbsTests
{
    [TestMethod]
    public void SetFileBreadcrumbs_SingleFile_ClickableParentThenFileName()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(@"D:\temp\photo.jpg");

        Assert.AreEqual(2, page.Breadcrumbs.Count);

        var parent = page.Breadcrumbs[0];
        Assert.AreEqual(@"D:\temp", parent.Label);
        Assert.AreEqual(FileBreadcrumbs.FileSystemPageKind, parent.TargetPageKind);
        Assert.IsNotNull(parent.TargetPageParams);
        Assert.AreEqual("path", parent.TargetPageParams!["mode"]);
        Assert.AreEqual(@"D:\temp", parent.TargetPageParams["path"]);
        Assert.AreEqual("temp", parent.TargetPageParams["label"]);

        var leaf = page.Breadcrumbs[1];
        Assert.AreEqual("photo.jpg", leaf.Label);
        Assert.IsNull(leaf.TargetPageKind, "the file-name leaf is not clickable");
    }

    [TestMethod]
    public void SetFileBreadcrumbs_CustomLeaf_OverridesFileName()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(@"D:\temp\data.csv", "data.csv (preview)");
        Assert.AreEqual("data.csv (preview)", page.Breadcrumbs[^1].Label);
    }

    [TestMethod]
    public void SetFileBreadcrumbs_NoDirectory_LeafOnly()
    {
        var page = new Page();
        page.SetFileBreadcrumbs("loose.txt");
        Assert.AreEqual(1, page.Breadcrumbs.Count);
        Assert.AreEqual("loose.txt", page.Breadcrumbs[0].Label);
    }

    [TestMethod]
    public void SetFileBreadcrumbs_EmptyPath_ShowsFallbackLeaf()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(string.Empty, "Text");
        Assert.AreEqual(1, page.Breadcrumbs.Count);
        Assert.AreEqual("Text", page.Breadcrumbs[0].Label);
    }

    [TestMethod]
    public void SetFileBreadcrumbs_CalledAgain_RebuildsRatherThanAppends()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(@"D:\a\one.txt");
        page.SetFileBreadcrumbs(@"D:\b\two.txt");

        Assert.AreEqual(2, page.Breadcrumbs.Count);
        Assert.AreEqual(@"D:\b", page.Breadcrumbs[0].Label);
        Assert.AreEqual("two.txt", page.Breadcrumbs[1].Label);
    }

    [TestMethod]
    public void CommonDirectory_AllSameFolder_ReturnsIt()
        => Assert.AreEqual(@"D:\pics", FileBreadcrumbs.CommonDirectory([@"D:\pics\a.jpg", @"D:\pics\b.jpg"]));

    [TestMethod]
    public void CommonDirectory_IsCaseInsensitive_FirstWins()
        => Assert.AreEqual(@"D:\Pics", FileBreadcrumbs.CommonDirectory([@"D:\Pics\a.jpg", @"D:\pics\b.jpg"]));

    [TestMethod]
    public void CommonDirectory_DifferentFolders_ReturnsNull()
        => Assert.IsNull(FileBreadcrumbs.CommonDirectory([@"D:\x\a.jpg", @"D:\y\b.jpg"]));

    [TestMethod]
    public void CommonDirectory_Empty_ReturnsNull()
        => Assert.IsNull(FileBreadcrumbs.CommonDirectory([]));

    [TestMethod]
    public void SetMultiFileBreadcrumbs_SameFolder_ClickableParentThenSummary()
    {
        var page = new Page();
        page.SetMultiFileBreadcrumbs([@"D:\pics\a.jpg", @"D:\pics\b.jpg"], "2 images");

        Assert.AreEqual(2, page.Breadcrumbs.Count);
        Assert.AreEqual(@"D:\pics", page.Breadcrumbs[0].Label);
        Assert.AreEqual(FileBreadcrumbs.FileSystemPageKind, page.Breadcrumbs[0].TargetPageKind);
        Assert.AreEqual("2 images", page.Breadcrumbs[1].Label);
    }

    [TestMethod]
    public void SetMultiFileBreadcrumbs_MixedFolders_SummaryOnly()
    {
        var page = new Page();
        page.SetMultiFileBreadcrumbs([@"D:\x\a.jpg", @"D:\y\b.jpg"], "2 images");

        Assert.AreEqual(1, page.Breadcrumbs.Count);
        Assert.AreEqual("2 images", page.Breadcrumbs[0].Label);
    }

    // ── Copy path ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("chrome-breadcrumb-copy-path")]
    public void SetFileBreadcrumbs_EachCrumbCarriesTheLocationItNames()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(@"D:\temp\photo.jpg");

        Assert.AreEqual(@"D:\temp", page.Breadcrumbs[0].Path, "the parent crumb copies the folder");
        Assert.AreEqual(@"D:\temp\photo.jpg", page.Breadcrumbs[1].Path, "the leaf copies the file itself");
    }

    [TestMethod]
    [CoversNode("chrome-breadcrumb-copy-path")]
    public void SetFileBreadcrumbs_EmptyPath_LeavesNothingToCopy()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(string.Empty, "Text");

        Assert.IsNull(page.Breadcrumbs[0].Path, "a fallback title names no location");
    }

    [TestMethod]
    [CoversNode("chrome-breadcrumb-copy-path")]
    public void SetMultiFileBreadcrumbs_SummaryLeafNamesNoLocation()
    {
        var page = new Page();
        page.SetMultiFileBreadcrumbs([@"D:\pics\a.jpg", @"D:\pics\b.jpg"], "2 images");

        Assert.AreEqual(@"D:\pics", page.Breadcrumbs[0].Path);
        Assert.IsNull(page.Breadcrumbs[1].Path, "'2 images' is a count, not a path — the bar falls back to the folder");
    }
}
