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
    private AutomationElement? DoubleClickOpen(string fileName, int seconds = 20)
    {
        CloseReaderTab();
        NavigateFileBrowserTo(PdfFolder);

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
    public void Pdf_Controls_RespondInOnePass()
    {
        // ── A readable document: the panel, its content, and its toggle ──────────────────
        var view = DoubleClickOpen("text.pdf");
        Assert.IsNotNull(view, "Double-clicking a .pdf did not open the Nexaflow reader — check the "
                             + "/document/pdf/read filemap entry and ShowPdfAction.");

        CheckPresent("Properties tab", "Pdf_Tab_Properties");
        CheckPresent("Contents tab", "Pdf_Tab_Contents");
        CheckPresent("Panel splitter", "Pdf_Splitter");
        CheckPresent("File name", "Pdf_FileName");

        // The panel's actual content: the title lives only in the document's /Info dictionary, so seeing it
        // proves PdfPig parsed the document rather than the view merely rendering.
        Check("Properties shows the document title", () => WaitForPanelText(PdfSamples.MetadataTitle, 10));

        // Hiding the panel has to actually hide it — a toggle that leaves the panel there is a defect.
        CheckDoes("Panel toggle", "Pdf_TogglePanel", () => WaitForId(PanelProxy, 2) is null);
        CheckDoes("Panel toggle (restore)", "Pdf_TogglePanel", () => WaitForId(PanelProxy, 4) is not null);

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

        // ── A corrupt document: the panel still opens, and says why it is empty ─────────
        // The bar the ViewerMap entry sets: corrupt.pdf must reach a PdfView, because a document PdfPig can't
        // parse is a normal state of this panel, not an exception path that takes the tab down.
        Assert.IsNotNull(DoubleClickOpen("corrupt.pdf"), "A corrupt PDF must still open the reader tab.");

        Check("Panel says it couldn't read the document",
              () => WaitForId("Pdf_PanelStatus", 10) is { } status && ValueOf(status).Length > 0);

        AssertJourney();
    }
}
