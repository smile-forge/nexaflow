using System.Collections.Generic;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// The "Browse" file action that routes an .html / .htm / .url file into a Web tab. No browser is involved —
/// the action's whole job is to ask the shell for an <c>Html</c> page with the right path, so it is asserted
/// against a stub shell.
/// </summary>
[TestClass]
[CoversNode("web-browse-action")]
public class ShowHtmlActionTests
{
    private static (ShowHtmlAction Action, IShellServices Shell) Subject()
    {
        var shell = Substitute.For<IShellServices>();
        return (new ShowHtmlAction(shell), shell);
    }

    private static Dictionary<string, string> Params(string path) => new() { ["path"] = path };

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OpensTheFileInAWebTab()
    {
        var (action, shell) = Subject();

        Assert.IsTrue(action.PerformAction(@"C:\pages\index.html"));
        shell.Received(1).OpenTab("Html", Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\pages\index.html"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OnASelection_OpensTheFirstWebFile_AndIgnoresTheRest()
    {
        var (action, shell) = Subject();

        // Single-file action (SupportsMultipleFiles is false): one tab, for the first file it recognises.
        Assert.IsTrue(action.PerformAction([@"C:\notes.txt", @"C:\site\page.htm", @"C:\other.html"]));
        shell.Received(1).OpenTab("Html", Arg.Any<Dictionary<string, string>>());
        shell.Received(1).OpenTab("Html", Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\site\page.htm"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OnASelectionWithNothingWebby_OpensNothing_AndReportsFailure()
    {
        var (action, shell) = Subject();

        Assert.IsFalse(action.PerformAction([@"C:\notes.txt", @"C:\image.png"]));
        shell.DidNotReceive().OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RecognisedExtensions_AreCaseInsensitive_AndIncludeInternetShortcuts()
    {
        var (action, shell) = Subject();

        Assert.IsTrue(action.PerformAction([@"C:\LINK.URL"]));
        shell.Received(1).OpenTab("Html", Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\LINK.URL"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ActionMetadata_MarksItANonDestructiveViewerForHtml()
    {
        var (action, _) = Subject();

        Assert.AreEqual("Browse", action.DisplayName);
        Assert.AreEqual("/text/html", action.ExperienceId);
        Assert.AreEqual(ShowHtmlAction.StaticExperienceId, action.ExperienceId, "the static and instance ids must agree");
        Assert.IsTrue(action.OpensViewer);
        Assert.IsFalse(action.IsDestructive);
        Assert.IsFalse(action.SupportsMultipleFiles);
    }
}
