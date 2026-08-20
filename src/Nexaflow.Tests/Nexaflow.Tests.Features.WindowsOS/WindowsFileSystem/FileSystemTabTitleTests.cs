using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// What a file-explorer tab is called. The <c>label</c> page param is optional, so the interesting cases are
/// the ones that omit it — which is most of them:
/// <list type="bullet">
/// <item>a ribbon button stores only <c>mode</c> + <c>path</c>;</item>
/// <item><c>ApplyBreadcrumbs</c> rewrites the params to <c>mode</c> + <c>path</c> on every navigation, so a
/// tab rebuilt afterwards (an options save calls <c>RefreshTabs</c>, which reopens from those params) has
/// lost any label it started with;</item>
/// <item>a restored session reopens from the same stored params.</item>
/// </list>
/// All three used to land on a literal "Files".
/// </summary>
[TestClass]
[CoversNode("winfs-breadcrumb")]
public class FileSystemTabTitleTests
{
    private static FileSystemPageRegistration Registration()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        return new FileSystemPageRegistration(
            shell, ai, new Dictionary<Type, IFeatureConfig>(),
            new FileMapConfig(), new ExternalAppsConfig(), new TemplatedCreateConfig());
    }

    /// <summary>The params a ribbon button holds: how the folder was reached, and nothing about naming.</summary>
    private static Dictionary<string, string> PathParams(string path, string? label = null)
    {
        var p = new Dictionary<string, string> { ["mode"] = "path", ["path"] = path };
        if (label is not null) p["label"] = label;
        return p;
    }

    // ── The reported bug ──────────────────────────────────────────────────────

    [TestMethod]
    public void PathTab_WithNoLabel_IsTitledWithTheFolderName()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Documents"));

        Assert.AreEqual("Documents", page.Title);
    }

    [TestMethod]
    public void PathTab_WithNoLabel_IsNotTitledFiles()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Downloads"));

        Assert.AreNotEqual("Files", page.Title,
            "a ribbon button stores only mode+path, so this is the path every ribbon-opened tab takes");
    }

    [TestMethod]
    public void PathTab_BreadcrumbMatchesTheTitle()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Pictures"));

        Assert.AreEqual(1, page.Breadcrumbs.Count);
        Assert.AreEqual("Pictures", page.Breadcrumbs[0].Label);
    }

    // ── Reopening from params a navigated tab left behind ─────────────────────

    [TestMethod]
    public void ReopeningFromNavigatedParams_KeepsTheFolderName()
    {
        // ApplyBreadcrumbs replaces the tab's params with exactly this shape after any navigation. RefreshTabs
        // (options save) and session restore both reopen from it, which is the "reverts to Files" case.
        var afterNavigation = new Dictionary<string, string> { ["mode"] = "path", ["path"] = @"D:\Projects\nexaflow" };

        var page = Registration().CreatePageDefinition(afterNavigation);

        Assert.AreEqual("nexaflow", page.Title);
    }

    // ── An explicit label still wins ──────────────────────────────────────────

    [TestMethod]
    public void ExplicitLabel_IsUsedAsGiven()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Documents", "My Stuff"));

        Assert.AreEqual("My Stuff", page.Title, "a caller that named the tab still gets its name");
    }

    [TestMethod]
    public void EmptyLabel_FallsBackRatherThanTitlingTheTabNothing()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Music", ""));

        Assert.AreEqual("Music", page.Title);
    }

    // ── Paths with no folder name of their own ────────────────────────────────

    [TestMethod]
    [DataRow(@"C:\", @"C:\")]
    [DataRow(@"C:", @"C:")]
    public void DriveRoot_FallsBackToThePath(string path, string expected)
    {
        var page = Registration().CreatePageDefinition(PathParams(path));

        Assert.AreEqual(expected, page.Title, "a drive root has no leaf name to use");
    }

    [TestMethod]
    public void TrailingSeparator_DoesNotProduceAnEmptyTitle()
    {
        var page = Registration().CreatePageDefinition(PathParams(@"C:\Users\sam\Videos\"));

        Assert.AreEqual("Videos", page.Title);
    }

    // ── This PC is unaffected ─────────────────────────────────────────────────

    [TestMethod]
    public void ThisPcTab_IsStillCalledThisPc()
    {
        var page = Registration().CreatePageDefinition(new Dictionary<string, string> { ["mode"] = "thispc" });

        Assert.AreEqual("This PC", page.Title);
    }

    // ── The one rule, shared ──────────────────────────────────────────────────

    [TestMethod]
    public void ViewerBreadcrumbAndTabTitle_AgreeOnTheFolderName()
    {
        // FileBreadcrumbs.ForDirectory supplies the label explicitly; CreatePageDefinition derives it when
        // absent. Both go through DirectoryLabel, so a folder is named the same however the tab was opened.
        const string dir = @"C:\Users\sam\Documents";

        var fromViewer = FileBreadcrumbs.ForDirectory(dir).TargetPageParams!["label"];
        var fromRibbon = Registration().CreatePageDefinition(PathParams(dir)).Title;

        Assert.AreEqual(fromViewer, fromRibbon);
    }

    [TestMethod]
    [DataRow(@"C:\Users\sam\Documents", "Documents")]
    [DataRow(@"\\server\share\Docs", "Docs")]
    [DataRow(@"C:\", @"C:\")]
    [DataRow("", "")]
    public void DirectoryLabel_NamesAFolderByItsOwnName(string path, string expected)
        => Assert.AreEqual(expected, FileBreadcrumbs.DirectoryLabel(path));

    [TestMethod]
    public void DirectoryLabel_TreatsAUncShareRootLikeADriveRoot()
    {
        // \\server\share is a root, not a folder inside one — Path.GetFileName finds no leaf, and showing the
        // whole path beats showing nothing.
        Assert.AreEqual(@"\\server\share", FileBreadcrumbs.DirectoryLabel(@"\\server\share"));
    }
}
