using System.Collections.Specialized;
using System.IO;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;
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
        Assert.AreNotEqual(session.Arrangement[0].Sheet.Pieces[0].Source, session.Arrangement[1].Sheet.Pieces[0].Source);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-insert-image")]
    public void ARefusal_NamesTheFile()
    {
        var message = PdfPagesPanel.Refusal(new PdfEditRefusedException(@"C:\pictures\notes.txt", PdfEditRefusal.NotAPicture));

        StringAssert.Contains(message, "notes.txt");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-split")]
    public async Task ASplit_DrawsItsCutsOnTheSelectedPages_MarksOneAcrossTheArtwork_AndSplitsAsSetUp()
    {
        var (session, panel) = await Opened(sample: "two-up.pdf");
        using var _ = session;
        Assert.IsFalse(panel.BeginSplitCommand.CanExecute(null), "nothing selected");
        panel.Pages[0].IsSelected = panel.Pages[1].IsSelected = true;

        panel.BeginSplitCommand.Execute(null);
        await panel.CutsInspected;

        var first = panel.Pages[0].Cuts.Single();
        Assert.AreEqual(panel.Pages[0].SheetWidth / 2, first.X1, 0.01, "a landscape sheet is cut down the middle");
        Assert.AreEqual(first.X1, first.X2);
        Assert.IsFalse(first.CrossesInk, "the first sheet's fold is clear");
        Assert.IsTrue(panel.Pages[1].Cuts.Single().CrossesInk, "the second sheet's artwork runs over the fold");
    Assert.AreNotEqual(string.Empty, panel.SplitWarning);
        Assert.AreEqual(Str.Format("Pdf.Pages.PageFormat", 1), panel.Pages[0].Name, "a row is heard as its page");
        Assert.AreEqual(Str.Format("Pdf.Pages.PageCrossedFormat", 2), panel.Pages[1].Name, "and a cut across its artwork is heard, not only seen");
        Assert.IsNull(panel.Pages[0].CrossingCutLines);
        Assert.IsNotNull(panel.Pages[1].CrossingCutLines);
        Assert.IsFalse(panel.IsCheckingCuts);

        panel.SplitCount = panel.SplitCounts[1];
        Assert.AreEqual(2, panel.Pages[0].Cuts.Count, "three parts, two cuts, read from the picture already taken");
        Assert.IsFalse(panel.CanUnfoldBooklet);

        panel.SplitCount = panel.SplitCounts[0];
        panel.SplitBooklet = true;
        panel.ApplySheetEditCommand.Execute(null);

        Assert.IsFalse(panel.IsEditingSheets);
        Assert.AreEqual("1 2 2 1", string.Join(" ", session.Arrangement.Pages.Select(page => page.Sheet.Pieces[0].Number)), "unfolded: one, two, three, four");
        Assert.IsTrue(panel.Pages.All(page => page.Cuts.Count == 0));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("pdf-page-merge")]
    [CoversNode("pdf-page-size")]
    public async Task AMerge_IsForTwoPagesOrMore_AndTheBarFromTheToolbar_IsForEveryPage()
    {
        var (session, panel) = await Opened(sample: "two-up.pdf");
        using var _ = session;
        panel.Pages[0].IsSelected = true;
        Assert.IsFalse(panel.BeginMergeCommand.CanExecute(null), "one page is no merge");
        Assert.IsTrue(panel.BeginResizeCommand.CanExecute(null));
        panel.Pages[1].IsSelected = true;

        panel.BeginMergeCommand.Execute(null);
        Assert.IsTrue(panel.IsMergeShown);
        Assert.IsFalse(panel.AppliesToAll);
        Assert.AreEqual(Str.Format("Pdf.Merge.SummaryFormat", 2, 1), panel.MergeSummary);
        Assert.IsTrue(panel.Pages.All(page => page.Cuts.Count == 0), "a merge cuts nothing");
        panel.ApplySheetEditCommand.Execute(null);

        Assert.AreEqual(1, panel.Pages.Count);
        Assert.AreEqual(new System.Windows.Size(792, 1224), session.PageSize(0), "two wide sheets, one above the other");

        panel.BeginSheetEditCommand.Execute(null);
        Assert.IsTrue(panel.AppliesToAll);
        panel.SheetEdit = panel.SheetEdits.Single(choice => choice.Value == PdfSheetEdit.Size);
        Assert.IsTrue(panel.IsSizeShown);
        panel.SizePaper = panel.SizePapers.Single(choice => choice.Value is null);
        panel.CustomWidth = "wide";
        Assert.IsFalse(panel.ApplySheetEditCommand.CanExecute(null), "a size of its own that is no size");
        panel.CustomWidth = 100.ToString(System.Globalization.CultureInfo.CurrentCulture);
        panel.CustomHeight = 200.ToString(System.Globalization.CultureInfo.CurrentCulture);
        panel.ApplySheetEditCommand.Execute(null);

        Assert.IsFalse(panel.IsEditingSheets);
        Assert.AreEqual(100 * 72 / 25.4, session.PageSize(0).Width, 0.01, "set on paper 100 by 200 millimetres, upright as the sheet is");
    }

    /// <summary>
    /// A copy of a sample opened, its pages laid as the reader lays them, and its Pages tab following its edits as the
    /// reader does.
    /// </summary>
    private static async Task<(PdfDocumentSession Session, PdfPagesPanel Panel)> Opened(IShellServices? shell = null, string sample = "outline.pdf")
    {
        var session = new PdfDocumentSession(Copy(sample));
        Assert.IsTrue(await session.OpenAsync(CancellationToken.None));
        var pages = new PdfPageSource(session.Arrangement, session.Opened(), Brushes.White);
        var panel = new PdfPagesPanel(session, shell ?? Substitute.For<IShellServices>(), _ => true, () => pages) { CanEdit = true };
        session.Changed += (_, next) =>
        {
            pages.Rearrange(next, session.Opened());
            panel.Show(next);
        };
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
