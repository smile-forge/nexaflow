using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// The "Markdown" file action — the explicit route into the live-edit Markdown tab, distinct from whatever
/// the file-type default mapping happens to be. It must open the <c>Markdown</c> page kind with the file as
/// the <c>path</c> parameter (the tab-parameter convention every registration reads), and it must never be
/// destructive or claim multi-file support, since a Markdown tab edits exactly one document.
/// </summary>
[TestClass]
[CoversNode("markdown-open-action")]
public class MarkdownOpenActionTests
{
    private static (ShowMarkdownAction Action, IShellServices Shell) Make()
    {
        var shell = Substitute.For<IShellServices>();
        return (new ShowMarkdownAction(shell), shell);
    }

    [TestMethod]
    public void PerformAction_OpensTheMarkdownTab_WithThePath()
    {
        var (action, shell) = Make();

        Assert.IsTrue(action.PerformAction(@"C:\notes\readme.md"));

        shell.Received(1).OpenTab("Markdown",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\notes\readme.md"),
            Arg.Any<IPageView?>());
    }

    [TestMethod]
    public void PerformAction_OnAnEmptyMultiSelection_OpensNothing()
    {
        var (action, shell) = Make();

        Assert.IsFalse(action.PerformAction([]));

        shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default, default, default);
    }

    [TestMethod]
    public void PerformAction_OnAMultiSelection_OpensTheFirstFileOnly()
    {
        var (action, shell) = Make();

        Assert.IsTrue(action.PerformAction([@"C:\a.md", @"C:\b.md"]));

        shell.Received(1).OpenTab("Markdown", Arg.Any<Dictionary<string, string>>(), Arg.Any<IPageView?>());
        shell.DidNotReceive().OpenTab("Markdown",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\b.md"),
            Arg.Any<IPageView?>());
    }

    [TestMethod]
    public void Action_IsANonDestructiveSingleFileViewer()
    {
        var (action, _) = Make();

        Assert.IsFalse(action.IsDestructive);
        Assert.IsFalse(action.SupportsMultipleFiles);
        Assert.IsTrue(action.OpensViewer);
        Assert.AreEqual(ShowMarkdownAction.StaticExperienceId, action.ExperienceId);
    }
}
