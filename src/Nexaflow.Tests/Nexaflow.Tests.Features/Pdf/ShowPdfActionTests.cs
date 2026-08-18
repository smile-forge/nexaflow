using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The "As Pdf" action that opens a document in the reader. No renderer is involved — its whole job is to ask
/// the shell for a <c>Pdf</c> page with the right path, so it is asserted against a stub shell.
/// </summary>
[TestClass]
[CoversNode("pdf-open-action")]
public class ShowPdfActionTests
{
    private static (ShowPdfAction Action, IShellServices Shell) Subject()
    {
        var shell = Substitute.For<IShellServices>();
        return (new ShowPdfAction(shell), shell);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OpensTheDocumentInAPdfTab()
    {
        var (action, shell) = Subject();

        Assert.IsTrue(action.PerformAction(@"C:\docs\report.pdf"));
        shell.Received(1).OpenTab("Pdf",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\docs\report.pdf"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OnASelection_OpensTheFirstOnly()
    {
        var (action, shell) = Subject();

        Assert.IsTrue(action.PerformAction([@"C:\a.pdf", @"C:\b.pdf"]));
        shell.Received(1).OpenTab("Pdf", Arg.Any<Dictionary<string, string>>());
        shell.Received(1).OpenTab("Pdf", Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\a.pdf"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PerformAction_OnAnEmptySelection_DoesNothing()
    {
        var (action, shell) = Subject();

        Assert.IsFalse(action.PerformAction([]));
        shell.DidNotReceive().OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ItsExperienceSitsBelowExtractImages_SoDoubleClickHasOneAnswer()
    {
        var (action, _) = Subject();

        // Both actions match a .pdf. DefaultFileOpener breaks a specificity tie by experience depth, so the
        // reader's extra level is what stops a double-click landing on "Extract images" — which would pop a
        // folder picker instead of opening the document.
        Assert.AreEqual("/document/pdf/read", action.ExperienceId);
        Assert.AreEqual(action.ExperienceId, ShowPdfAction.StaticExperienceId,
            "FeatureManager reads the static form by reflection without instantiating");
        Assert.IsTrue(action.ExperienceId.StartsWith("/document/pdf/"),
            "a descendant of /document/pdf, so 'Extract images' keeps its place on the strip");
        Assert.IsTrue(action.OpensViewer, "it opens an internal viewer tab, so the Define New wizard lists it");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ItIsNamedLikeTheOtherViewerActions()
    {
        var (action, _) = Subject();

        Assert.AreEqual("As Pdf", action.DisplayName);
    }
}
