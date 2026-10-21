using System.Collections.Specialized;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;
using NSubstitute;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The Pages tab: one thumbnail per page, each the same object however its page moves — so a picture drawn and a
/// selection made go where the page goes — and the page edits it offers, driven as its commands.
/// </summary>
[TestClass]
[CoversNode("pdf-panel-pages")]
public class PdfPagesPanelTests
{
    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-reorder")]
    public async Task APageDragged_MovesItsThumbnail_TheSameObject_WithoutRebuildingTheList()
    {
        var (session, panel) = await Opened();
        using var _ = session;
        var third = panel.Pages[2];
        var changes = new List<NotifyCollectionChangedAction>();
        panel.Pages.CollectionChanged += (_, e) => changes.Add(e.Action);

        panel.ReorderCommand.Execute(new ReorderRequest(third, 0));

        Assert.AreSame(third, panel.Pages[0]);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, panel.Pages.Select(page => page.Number).ToArray(), "numbers are places");
        CollectionAssert.DoesNotContain(changes, NotifyCollectionChangedAction.Reset);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-delete")]
    public async Task DeletingIsOffered_ForSomePages_NeverForEveryOne()
    {
        var (session, panel) = await Opened();
        using var _ = session;

        Assert.IsFalse(panel.DeleteSelectedCommand.CanExecute(null), "nothing selected");
        panel.Pages[0].IsSelected = true;
        Assert.IsTrue(panel.DeleteSelectedCommand.CanExecute(null));
        foreach (var page in panel.Pages) page.IsSelected = true;
        Assert.IsFalse(panel.DeleteSelectedCommand.CanExecute(null), "a document keeps a page");

        panel.Pages[0].IsSelected = false;
        panel.DeleteSelectedCommand.Execute(null);
        Assert.AreEqual(1, panel.Pages.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-reorder")]
    public async Task TheSelectedPages_MoveAPlaceTogether()
    {
        var (session, panel) = await Opened();
        using var _ = session;
        var second = panel.Pages[1];
        var third = panel.Pages[2];
        second.IsSelected = third.IsSelected = true;

        panel.MoveSelectionCommand.Execute(-1);

        Assert.AreSame(second, panel.Pages[0]);
        Assert.AreSame(third, panel.Pages[1]);
        Assert.IsTrue(second.IsSelected && third.IsSelected, "the pages moved stay selected");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-pdf")]
    public async Task APdfPicked_IsInsertedWhereAsked()
    {
        var shell = Substitute.For<IShellServices>();
        shell.PickOpenFilesAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>())
             .Returns(Task.FromResult<IReadOnlyList<string>>([Copy("text.pdf")]));
        var (session, panel) = await Opened(shell);
        using var _ = session;

        await panel.InsertPdfsAtCommand.ExecuteAsync(1);

        Assert.AreEqual(4, panel.Pages.Count);
        Assert.AreNotEqual(session.Arrangement[0].Source, session.Arrangement[1].Source);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-image")]
    public void ARefusal_NamesTheFile()
    {
        var message = PdfPagesPanel.Refusal(new PdfEditRefusedException(@"C:\pictures\notes.txt", PdfEditRefusal.NotAPicture));

        StringAssert.Contains(message, "notes.txt");
    }

    /// <summary>A copy of the outlined sample opened, and its Pages tab following its edits as the reader does.</summary>
    private static async Task<(PdfDocumentSession Session, PdfPagesPanel Panel)> Opened(IShellServices? shell = null)
    {
        var session = new PdfDocumentSession(Copy("outline.pdf"));
        Assert.IsTrue(await session.OpenAsync(CancellationToken.None));
        var panel = new PdfPagesPanel(session, shell ?? Substitute.For<IShellServices>(), _ => true, () => null) { CanEdit = true };
        session.Changed += (_, next) => panel.Show(next);
        panel.Show(session.Arrangement);
        return (session, panel);
    }

    private static string Copy(string sample)
    {
        var path = Path.Combine(TestTemp.CreateSubdirectory().FullName, sample);
        File.Copy(TestSampleData.Path("pdf", sample), path);
        return path;
    }
}
