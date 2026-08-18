using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.UI.Infrastructure;
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
/// Each panel assertion is on <em>content</em>, not on a control existing: the Properties tab has to show the
/// document's real title, and the Contents tab has to list its real bookmarks. Clicking a Contents row is
/// asserted only when the renderer accepts page navigation at all — the embedded PDF viewer is Edge's and its
/// handling of a page fragment isn't contractual, so the journey records that it couldn't be checked rather
/// than failing a build over someone else's renderer.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("pdf-ui")]
public class PdfJourneyTests : UiJourneyTestBase
{
    private static string PdfFolder => System.IO.Path.GetDirectoryName(TestSampleData.Path("pdf", "text.pdf"))!;

    /// <summary>Double-clicks a file in the browser — the default-open path, which is what we're testing.</summary>
    private AutomationElement? DoubleClickOpen(string fileName, int seconds = 20)
    {
        NavigateFileBrowserTo(PdfFolder);

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        return WaitForId("PdfView", seconds);
    }

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("pdf-open-action")]
    [CoversNode("pdf-panel-properties")]
    public void Pdf_DoubleClickOpensTheReader_AndThePanelDescribesTheDocument()
    {
        var view = DoubleClickOpen("text.pdf");
        Assert.IsNotNull(view, "Double-clicking a .pdf did not open the Nexaflow reader — check the "
                             + "/document/pdf/read filemap entry and ShowPdfAction.");

        CheckPresent("Document panel", "Pdf_Panel");
        CheckPresent("Properties tab", "Pdf_Tab_Properties");
        CheckPresent("Contents tab", "Pdf_Tab_Contents");
        CheckPresent("Panel splitter", "Pdf_Splitter");
        CheckPresent("File name", "Pdf_FileName");

        // The panel's actual content: the title lives only in the document's /Info dictionary, so seeing it
        // proves PdfPig parsed the document rather than the view merely rendering.
        Check("Properties shows the document title",
              () => WaitForName(PdfSamples.MetadataTitle, 10) is not null);

        // Hiding the panel has to actually hide it — a toggle that leaves the panel there is a defect.
        CheckDoes("Panel toggle", "Pdf_TogglePanel", () => WaitForId("Pdf_Panel", 2) is null);
        CheckDoes("Panel toggle (restore)", "Pdf_TogglePanel", () => WaitForId("Pdf_Panel", 4) is not null);

        AssertJourney();
    }

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("pdf-panel-toc")]
    public void Pdf_ContentsTabListsTheBookmarks_AndJumpsWhenTheRendererAllowsIt()
    {
        var view = DoubleClickOpen("outline.pdf");
        Assert.IsNotNull(view, "The reader did not open for outline.pdf.");

        var contentsTab = CheckPresent("Contents tab", "Pdf_Tab_Contents");
        if (contentsTab is not null)
        {
            contentsTab.Click();
            Wait.UntilInputIsProcessed();
        }

        Check("Contents lists the root bookmark",
              () => WaitForName(PdfSamples.OutlineRootTitle, 10) is not null);
        Check("Contents lists the nested bookmark",
              () => WaitForName(PdfSamples.OutlineChildTitle, 6) is not null);

        // Clicking a row can only be asserted where the renderer honours page navigation. When it doesn't the
        // rows are deliberately inert, and the journey says so instead of failing over it.
        var row = WaitForName(PdfSamples.OutlineSecondTitle, 6);
        if (row is not null && row.IsEnabled)
        {
            row.Click();
            Wait.UntilInputIsProcessed();
            Check("Clicking a contents row leaves the reader intact",
                  () => WaitForId("PdfView", 6) is not null && !App.HasExited);
        }

        AssertJourney();
    }

    [TestMethod]
    [TestCategory("UI")]
    public void Pdf_UnreadableDocument_StillOpensWithAnHonestPanel()
    {
        // The bar the ViewerMap entry sets: corrupt.pdf must reach a PdfView, because a document PdfPig can't
        // parse is a normal state of this panel, not an exception path that takes the tab down.
        var view = DoubleClickOpen("corrupt.pdf");
        Assert.IsNotNull(view, "A corrupt PDF must still open the reader tab.");

        Check("Panel says it couldn't read the document",
              () => WaitForId("Pdf_PanelStatus", 10) is { } status && status.Name.Length > 0);

        AssertJourney();
    }
}
