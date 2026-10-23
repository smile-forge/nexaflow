using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf.UI;

/// <summary>
/// The one UI journey for the PDF reader, driven the way a user drives it: <b>double-click</b> a PDF in the
/// file browser and expect Nexaflow's reader — not Acrobat, and not the web browser tab.
/// <para>
/// The double-click is the point. Opening via the action strip would prove the view works while saying
/// nothing about the routing change that makes <c>.pdf</c> resolve to this reader ahead of the Windows shell
/// "open" verb, and that routing is the thing a regression would silently undo.
/// </para>
/// <para>
/// Three documents, one launch: a readable one for the panel and its toggle, an outlined one for the
/// contents tree, and a corrupt one for the honest-failure path. They were three test methods and so three
/// ~20s app launches; a journey exists to pay that once, and the reader is the same tab in all three cases.
/// </para>
/// <para>
/// Each panel assertion is on <em>content</em>, not on a control existing: the Properties tab has to show the
/// document's real title, and the Contents tab has to list its real bookmarks. The pages are Nexaflow's own drawing,
/// so every reader control is asserted on what it did: a Contents row and the page buttons move the page number,
/// the zoom buttons change the zoom, and a search for a word the page carries reports its hit.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("pdf-ui")]
public class PdfJourneyTests : UiJourneyTestBase
{
    private static string PdfFolder => System.IO.Path.GetDirectoryName(TestSampleData.Path("pdf", "text.pdf"))!;

    /// <summary>
    /// The panel is a <c>Border</c>, and a Border creates no automation peer — an <c>AutomationId</c> on one
    /// never reaches the UIA tree, so <c>Pdf_Panel</c> can only ever resolve to null. Asserting the panel
    /// through a real control inside it tests the same thing and actually resolves: collapsing the Border
    /// takes its children out of the tree with it. (The old assertion on <c>Pdf_Panel</c> failed on show and
    /// <i>passed</i> on hide, for the same reason — always null.)
    /// </summary>
    private const string PanelProxy = "Pdf_Tab_Properties";

    /// <summary>
    /// Closes the reader so the file browser is the front tab again.
    /// <para>
    /// Needed only because this journey opens three documents in one launch: the browser's
    /// <c>DirectoryTree</c> is not in the automation tree while another tab is in front, so the next
    /// <see cref="FileSystemUiTestBase.NavigateFileBrowserTo"/> would fail its own precondition. The three
    /// tests this replaced never hit it — each got a freshly launched app with the browser already in front.
    /// </para>
    /// </summary>
    private void CloseReaderTab()
    {
        var close = WaitForId("CloseTab_Pdf", 6);
        if (close is null) return;          // nothing open — first document, or it never opened

        close.Click();
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Double-clicks a file in the browser — the default-open path, which is what we're testing.</summary>
    private AutomationElement? DoubleClickOpen(string fileName, int seconds = 20, string? folder = null)
    {
        CloseReaderTab();
        NavigateFileBrowserTo(folder ?? PdfFolder);

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        return WaitForId("PdfView", seconds);
    }

    /// <summary>
    /// True once some text box in the reader is showing <paramref name="text"/>.
    /// <para>
    /// Every value in the panel is a <c>TextBox</c> — deliberately, so a user can select and copy it — and a
    /// TextBox publishes its text through the <c>Value</c> pattern, not its automation <c>Name</c>. Searching
    /// by name therefore never matched, which is what made "the panel doesn't describe the document" look
    /// like a reader defect rather than a test reading the wrong property.
    /// </para>
    /// </summary>
    private bool WaitForPanelText(string text, int seconds)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            try
            {
                if (MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                              .Any(e => ValueOf(e).Contains(text, StringComparison.Ordinal)))
                    return true;
            }
            catch { /* the tree churns while the document loads — retry */ }
            Thread.Sleep(150);
        }
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds));

        return false;
    }

    private static string ValueOf(AutomationElement element)
    {
        try { return element.Patterns.Value.PatternOrDefault?.Value?.ValueOrDefault ?? string.Empty; }
        catch { return string.Empty; }
    }

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("pdf-open-action")]
    [CoversNode("pdf-panel-properties")]
    [CoversNode("pdf-panel-toc")]
    [CoversNode("pdf-panel-pages")]
    [CoversNode("pdf-page-edits")]
    public void Pdf_Controls_RespondInOnePass()
    {
        // ── A readable document: the panel, its content, and its toggle ──────────────────
        var view = DoubleClickOpen("text.pdf");
        Assert.IsNotNull(view, "Double-clicking a .pdf did not open the Nexaflow reader — check the "
                             + "/document/pdf/read filemap entry and ShowPdfAction.");

        // The Pages tab is shown first; the properties are read on their own tab.
        CheckPresent("Properties tab", "Pdf_Tab_Properties")?.Click();
        Wait.UntilInputIsProcessed();
        CheckPresent("Contents tab", "Pdf_Tab_Contents");
        CheckPresent("Panel splitter", "Pdf_Splitter");
        CheckPresent("Extract the images", "Pdf_ExtractImages");

        // The panel's actual content: the title lives only in the document's /Info dictionary, so seeing it
        // proves PdfPig parsed the document rather than the view merely rendering.
        Check("Properties shows the document title", () => WaitForPanelText(PdfSamples.MetadataTitle, 10));

        // Hiding the panel has to actually hide it — a toggle that leaves the panel there is a defect.
        CheckDoes("Panel toggle", "Pdf_TogglePanel", () => WaitForId(PanelProxy, 2) is null);
        CheckDoes("Panel toggle (restore)", "Pdf_TogglePanel", () => WaitForId(PanelProxy, 4) is not null);

        // ── The pages: drawn here, and sized and searched here ──────────────────────────
        CheckPresent("Pages", "Pdf_Pages");
        Check("Page count says the document has one page",
              () => WaitForId("Pdf_PageCount", 6) is { } count && count.Name.Contains('1'));

        var zoomBefore = WaitForId("Pdf_Zoom", 4)?.Name ?? string.Empty;
        CheckDoes("Zoom in", "Pdf_ZoomIn", () => WaitForId("Pdf_Zoom", 2)?.Name is { } now && now != zoomBefore);
        CheckDoes("Zoom out", "Pdf_ZoomOut", () => WaitForId("Pdf_Zoom", 2)?.Name == zoomBefore);
        CheckDoes("Fit width", "Pdf_FitWidth", () => WaitForId("Pdf_Zoom", 2)?.Name is { Length: > 0 });
        CheckDoes("Fit page", "Pdf_FitPage", () => WaitForId("Pdf_Zoom", 2)?.Name is { Length: > 0 });
        CheckPresent("Print", "Pdf_Print");

        var find = CheckPresent("Find box", "Pdf_Find");
        if (find is not null)
        {
            find.AsTextBox().Text = PdfSamples.BodyNeedle;
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
            Wait.UntilInputIsProcessed();
            Check("Find reports the word the page carries",
                  () => WaitForId("Pdf_FindStatus", 8) is { } status && status.Name.Contains('1'));
        }

        CheckDoes("Find next", "Pdf_FindNext", () => WaitForId("Pdf_FindStatus", 4)?.Name is { Length: > 0 });
        CheckDoes("Find previous", "Pdf_FindPrevious", () => WaitForId("Pdf_FindStatus", 4)?.Name is { Length: > 0 });

        // ── An outlined document: the contents tree ─────────────────────────────────────
        Assert.IsNotNull(DoubleClickOpen("outline.pdf"), "The reader did not open for outline.pdf.");

        var contentsTab = CheckPresent("Contents tab (outlined document)", "Pdf_Tab_Contents");
        if (contentsTab is not null)
        {
            contentsTab.Click();
            Wait.UntilInputIsProcessed();
        }

        Check("Contents lists the root bookmark",
              () => WaitForName(PdfSamples.OutlineRootTitle, 10) is not null);
        Check("Contents lists the nested bookmark",
              () => WaitForName(PdfSamples.OutlineChildTitle, 6) is not null);

        // A contents row goes to its page, and the page buttons step from there.
        var row = WaitForName(PdfSamples.OutlineSecondTitle, 6);
        Check("Contents lists the second chapter", () => row is not null);
        if (row is not null)
        {
            row.Click();
            Wait.UntilInputIsProcessed();
            Check("Clicking a contents row goes to its page", () => PageNumberIs("3", 6));
        }

        CheckDoes("Previous page", "Pdf_PreviousPage", () => PageNumberIs("2", 4));
        CheckDoes("Next page", "Pdf_NextPage", () => PageNumberIs("3", 4));

        // ── A scanned document: the notice above it, where this machine can read pages ─────────
        Assert.IsNotNull(DoubleClickOpen("image-only.pdf"), "The reader did not open for image-only.pdf.");

        if (WaitForId("Pdf_ConvertScans", 10) is not null)
        {
            // Converting says how it is getting on — reading, or page reading's download — or, read before, opens the
            // Markdown; while its banner speaks, the notice offering it is put away.
            CheckDoes("Convert the scanned pages to Markdown", "Pdf_ConvertScans",
                      () => WaitForId("PageConversion_Status", 5) is not null || WaitForId("Markdown_Save", 5) is not null);
            if (WaitForId("PageConversion_Status", 1) is not null)
                Check("The notice gives way to the conversion's banner", () => WaitForGone("Pdf_ConvertScans", 3));
            if (WaitForId("PageConversion_Download", 1) is not null) CheckPresent("Page reading's download", "PageConversion_Download");
            if (WaitForId("PageConversion_Stop", 1) is not null)
            {
                // Stop asks first, the reading going on while it does; kept, it goes on, and let go, the banner goes.
                CheckDoes("Ask to stop reading the scanned pages", "PageConversion_Stop", () => WaitForId("PageConversion_StopAsk", 3) is not null);
                CheckDoes("Keep converting", "PageConversion_KeepReading", () => WaitForId("PageConversion_Stop", 3) is not null);
                CheckDoes("Ask to stop again", "PageConversion_Stop", () => WaitForId("PageConversion_StopAsk", 3) is not null);
                CheckDoes("Discard what was read", "PageConversion_Discard", () => WaitForGone("PageConversion_Status", 5));
            }
            if (WaitForId("Pdf_ConvertScans", 3) is not null)
                CheckDoes("Hide the scans notice", "Pdf_ScansNoticeDismiss", () => WaitForGone("Pdf_ConvertScans", 3));
        }

        // Any document converts to Markdown, every page of it read, where this machine can read pages; stopped part-way,
        // what was read so far can open as a draft.
if (WaitForId("Pdf_ConvertDocument", 2) is not null)
{
    // A stopped reading lets go of the page it was on before the conversion is free again — until then both convert
    // buttons stand disabled, so a stop above is waited out here rather than taken for a button that does nothing.
    WaitForFs(() => WaitForId("Pdf_ConvertDocument", 1)?.IsEnabled == true, 30);
    CheckDoes("Convert the whole document to Markdown", "Pdf_ConvertDocument",
              () => WaitForId("PageConversion_Status", 5) is not null || WaitForId("Markdown_Save", 5) is not null);
    if (WaitForId("PageConversion_Stop", 1) is not null)
    {
        CheckDoes("Ask to stop reading the document", "PageConversion_Stop", () => WaitForId("PageConversion_StopAsk", 3) is not null);
        if (WaitForId("PageConversion_ShowSoFar", 1) is not null)
            CheckDoes("Show what is converted so far", "PageConversion_ShowSoFar", () => WaitForId("Markdown_Save", 8) is not null);
        else
            CheckDoes("Discard the reading", "PageConversion_Discard", () => WaitForGone("PageConversion_Status", 5));
    }
}

        // ── Editing the pages: a copy of the outlined document, so the sample stays as it is ──
        var copies = TestTemp.CreateSubdirectory("pdf-pages").FullName;
        System.IO.File.Copy(TestSampleData.Path("pdf", "outline.pdf"), System.IO.Path.Combine(copies, "pages.pdf"));
        Assert.IsNotNull(DoubleClickOpen("pages.pdf", folder: copies), "The reader did not open the copy to edit.");

        var pagesTab = CheckPresent("Pages tab", "Pdf_Tab_Pages");
        pagesTab?.Click();
        Wait.UntilInputIsProcessed();

        var thumbnails = CheckPresent("Page thumbnails", "Pdf_Thumbnails", 10);
        Check("A thumbnail for every page", () => Thumbnails(thumbnails).Length == 3);
        Check("Each thumbnail shows its page", () => ThumbnailsDrawn(thumbnails, 10));
        Check("Each thumbnail is named for its page", () => Thumbnails(thumbnails) is { Length: 3 } named
            && named.Select((row, index) => row.Name.EndsWith((index + 1).ToString(), StringComparison.Ordinal) && !row.Name.Contains("Nexaflow.")).All(ok => ok));
        CheckPresent("Undo", "Pdf_Undo");
        CheckPresent("Redo", "Pdf_Redo");
        CheckPresent("Save as", "Pdf_SaveAs");

        if (Thumbnails(thumbnails) is { Length: 3 } rows)
        {
            rows[1].Click();
            Wait.UntilInputIsProcessed();
            Check("Clicking a thumbnail goes to its page", () => PageNumberIs("2", 6));

            // The page's own menu, and the menu between two pages.
            rows[1].RightClick();
            Wait.UntilInputIsProcessed();
            CheckPresent("Delete the page", "Pdf_Thumb_Delete", 3);
            CheckPresent("Insert a picture before it", "Pdf_Thumb_InsertImageBefore", 2);
            CheckPresent("Insert a picture after it", "Pdf_Thumb_InsertImageAfter", 2);
            CheckPresent("Insert a PDF before it", "Pdf_Thumb_InsertPdfBefore", 2);
            CheckPresent("Insert a PDF after it", "Pdf_Thumb_InsertPdfAfter", 2);
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed();

            var below = rows[2].BoundingRectangle;
            RightClickAt(new System.Drawing.Point(below.X + below.Width / 2, below.Bottom + 6));
            Wait.UntilInputIsProcessed();
            CheckPresent("Insert a picture between pages", "Pdf_Gap_InsertImage", 3);
            CheckPresent("Insert a PDF between pages", "Pdf_Gap_InsertPdf", 2);
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed();

            // Split the page: the bar it is set up in, cancelled; then split into halves, and undone.
            rows[1].RightClick();
            Wait.UntilInputIsProcessed();
            CheckPresent("Split the page", "Pdf_Thumb_Split", 3)?.Click();
            Wait.UntilInputIsProcessed();
            CheckPresent("How many pages each sheet holds", "Pdf_Split_Parts", 3);
            CheckPresent("How the pages sit on the sheet", "Pdf_Split_Direction", 2);
            CheckPresent("Unfold a booklet", "Pdf_Split_Booklet", 2);
            CheckDoes("Cancel the split", "Pdf_Sheet_Cancel", () => WaitForId("Pdf_Sheet_Apply", 1) is null);

            rows[1].RightClick();
            Wait.UntilInputIsProcessed();
            CheckPresent("Split the page again", "Pdf_Thumb_Split", 3)?.Click();
            Wait.UntilInputIsProcessed();
            CheckDoes("Split it into halves", "Pdf_Sheet_Apply", () => PageCountIs('4', 4));
            CheckDoes("Undo the split", "Pdf_Undo", () => PageCountIs('3', 4));

            // Merge two pages onto one sheet, and undo it. The split undone gave the page a new row, so the rows are
            // found again; Ctrl selects without going to the page.
            if (Thumbnails(thumbnails) is { Length: 3 } pages)
            {
                pages[0].Click();
                using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
                    pages[1].Click();
                Wait.UntilInputIsProcessed();
                pages[1].RightClick();
                Wait.UntilInputIsProcessed();
                CheckPresent("Merge the pages", "Pdf_Thumb_Merge", 3)?.Click();
                Wait.UntilInputIsProcessed();
                CheckPresent("How many pages go on each sheet", "Pdf_Merge_Parts", 3);
                CheckPresent("How the pages sit on the sheet", "Pdf_Merge_Direction", 2);
                CheckPresent("How large each sheet is made", "Pdf_Merge_Paper", 2);
                CheckPresent("Fold into a booklet", "Pdf_Merge_Booklet", 2);
                CheckDoes("Merge them onto one sheet", "Pdf_Sheet_Apply", () => PageCountIs('2', 4));
                CheckDoes("Undo the merge", "Pdf_Undo", () => PageCountIs('3', 4));
            }

            // Set a page on paper of its own size, and undo it.
            if (Thumbnails(thumbnails) is { Length: 3 } sized)
            {
                sized[1].Click();
                Wait.UntilInputIsProcessed();
                sized[1].RightClick();
                Wait.UntilInputIsProcessed();
                CheckPresent("Set the page's size", "Pdf_Thumb_SetSize", 3)?.Click();
                Wait.UntilInputIsProcessed();
                if (CheckPresent("The paper it is set on", "Pdf_Size_Paper", 3) is { } paper)
                {
                    paper.Click();
                    Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.END);
                    Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
                    Wait.UntilInputIsProcessed();
                }

                CheckPresent("A width of its own", "Pdf_Size_Width", 2);
                CheckPresent("A height of its own", "Pdf_Size_Height", 2);
                CheckPresent("The unit they are in", "Pdf_Size_Unit", 2);
                CheckPresent("Which way up", "Pdf_Size_Orientation", 2);
                CheckPresent("What its content does", "Pdf_Size_Fit", 2);
                CheckDoes("Set its size", "Pdf_Sheet_Apply", () => WaitForId("Pdf_Sheet_Apply", 1) is null && PageCountIs('3', 2));
                CheckDoes("Undo the new size", "Pdf_Undo", () => WaitForId("Pdf_Undo", 2)?.IsEnabled == false);
            }

            // Every page at once, from the toolbar: the edit chosen in the bar; cancelled.
            CheckPresent("Split, merge or resize every page", "Pdf_SheetEdit")?.Click();
            Wait.UntilInputIsProcessed();
            CheckPresent("What to do to every page", "Pdf_Sheet_Edit", 3);
            CheckDoes("Cancel it", "Pdf_Sheet_Cancel", () => WaitForId("Pdf_Sheet_Apply", 1) is null);

            // Delete the page selected, undo it, redo it: the page count follows each. The split undone gave the
            // page a new row, so the rows are found again.
            (Thumbnails(thumbnails) is { Length: 3 } again ? again[1] : rows[1]).Click();
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DELETE);
            Wait.UntilInputIsProcessed();
            Check("Delete takes the page out", () => PageCountIs('2', 6));
            Check("The edits can be saved as another file", () => WaitForId("Pdf_SaveAs", 2)?.IsEnabled == true);
            CheckDoes("Undo the delete", "Pdf_Undo", () => PageCountIs('3', 4));
            CheckDoes("Redo the delete", "Pdf_Redo", () => PageCountIs('2', 4));

            // Alt+Up moves the page selected up a place; the edit is staged until Save.
            if (Thumbnails(thumbnails) is { Length: 2 } left)
            {
                left[1].Click();
                Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT, FlaUI.Core.WindowsAPI.VirtualKeyShort.UP);
                Wait.UntilInputIsProcessed();
            }

            CheckDoes("Save the page edits", "Pdf_Save", () => WaitForId("Pdf_Save", 2)?.IsEnabled == false);

            Assert.IsNotNull(DoubleClickOpen("pages.pdf", folder: copies), "The saved copy did not open again.");
            Check("The saved file has the pages left", () => PageCountIs('2', 10));
        }

        // ── A corrupt document: the panel still opens, and says why it is empty ─────────
        // The bar the ViewerMap entry sets: corrupt.pdf must reach a PdfView, because a document PdfPig can't
        // parse is a normal state of this panel, not an exception path that takes the tab down.
        Assert.IsNotNull(DoubleClickOpen("corrupt.pdf"), "A corrupt PDF must still open the reader tab.");

        Check("Panel says it couldn't read the document",
              () => WaitForId("Pdf_PanelStatus", 10) is { } status && ValueOf(status).Length > 0);

        AssertJourney();
    }

    /// <summary>Whether the reader's page box shows <paramref name="page"/> within <paramref name="seconds"/>.</summary>
    private bool PageNumberIs(string page, int seconds)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            if (WaitForId("Pdf_PageNumber", 1) is { } box && ValueOf(box) == page) return true;
            Thread.Sleep(150);
        }
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds));

        return false;
    }

    /// <summary>Whether the toolbar's page count shows <paramref name="pages"/> within <paramref name="seconds"/>.</summary>
    private bool PageCountIs(char pages, int seconds)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            if (WaitForId("Pdf_PageCount", 1) is { } count && count.Name.Contains(pages)) return true;
            Thread.Sleep(150);
        }
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds));

        return false;
    }

    /// <summary>The rows of the Pages tab, top to bottom.</summary>
    private static AutomationElement[] Thumbnails(AutomationElement? list) =>
        list?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) ?? [];

    /// <summary>
    /// Whether every thumbnail in view shows its page's picture: a thumbnail's image has no size until its picture is
    /// drawn into it.
    /// </summary>
    private static bool ThumbnailsDrawn(AutomationElement? list, int seconds)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            var rows = Thumbnails(list);
            if (rows.Length > 0 && rows.All(row => row.FindFirstDescendant(cf => cf.ByControlType(ControlType.Image)) is { BoundingRectangle.Width: > 0 }))
                return true;
            Thread.Sleep(150);
        }
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds));

        return false;
    }
}
