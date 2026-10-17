using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Pdf;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;
using NSubstitute;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The reader's AI surface. The point of it is that a PDF's content is not in the model's context: the tools
/// are the only way in, so what they refuse, clamp or admit to matters as much as what they return.
/// </summary>
[TestClass]
[CoversNode("pdf-ai")]
public class PdfViewModelAiTests
{
    private static string Sample(string name) => TestSampleData.Path("pdf", name);

    private static PdfViewModel LoadedVm(string sample = "outline.pdf")
    {
        var vm = new PdfViewModel(Sample(sample), Substitute.For<IShellServices>(), new PdfConfig());
        vm.LoadAsync(System.Windows.Media.Brushes.White).GetAwaiter().GetResult();
        return vm;
    }

    private static IClientTool Tool(PdfViewModel vm, string name)
    {
        var tool = vm.GetClientTools().FirstOrDefault(t => t.Name == name);
        Assert.IsNotNull(tool, $"{name} should be offered");
        return tool;
    }

    private static Task<ToolResult> Invoke(PdfViewModel vm, string name, JsonObject? args = null)
        => Tool(vm, name).InvokeAsync(args ?? [], CancellationToken.None);

    // ── The surface ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-ai-act")]
    public void EveryToolIsOffered_AndNoneOfThemWrites()
    {
        var vm = LoadedVm();
        try
        {
            var names = vm.GetClientTools().Select(t => t.Name).ToArray();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "pdf_get_info", "pdf_outline", "pdf_read_text", "pdf_find_text", "pdf_read_scans",
                    "pdf_list_images", "pdf_get_image", "pdf_page_image", "pdf_view_page",
                },
                names);

            // Reading a document the user already opened changes nothing, so nothing here should stop to ask
            // permission — a confirmation prompt per page would make reading a report unusable.
            Assert.IsTrue(vm.GetClientTools().All(t => t.Safety == ToolSafety.SafeOperation));
        }
        finally { vm.Dispose(); }
    }

    private static IPageReading Reading(ToolModelsState state)
    {
        var reading = Substitute.For<IPageReading>();
        reading.State.Returns(state);
        reading.ModelsVersion.Returns("test-" + Guid.NewGuid().ToString("N"));
        reading.ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<PageBlock>>([new PageBlock(PageBlockKind.Text, 0, 0, 1, 1, "The peregrine nests on cliffs.")]));
        return reading;
    }

    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => { call.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<PagesRead?>>>()).Returns(call => call.Arg<Func<Task<PagesRead?>>>()());
        return shell;
    }

    [TestMethod]
    [CoversNode("pdf-scans-notice")]
    public void AScannedDocument_OffersItsScansForReading_WherePageReadingRuns()
    {
        var offered = new PdfViewModel(Sample("image-only.pdf"), RunningShell(), new PdfConfig(), Reading(ToolModelsState.NotDownloaded));
        var none = new PdfViewModel(Sample("image-only.pdf"), RunningShell(), new PdfConfig());
        try
        {
            offered.LoadAsync(System.Windows.Media.Brushes.White).GetAwaiter().GetResult();
            none.LoadAsync(System.Windows.Media.Brushes.White).GetAwaiter().GetResult();

            Assert.IsTrue(offered.HasScans);
            Assert.IsFalse(string.IsNullOrEmpty(offered.ScansSummary));
            Assert.IsFalse(none.HasScans, "no page reading here: nothing to offer");
        }
        finally
        {
            offered.Dispose();
            none.Dispose();
        }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-read-scans")]
    public async Task ReadScans_ReadsThemWithPageReading_AndReadTextThenIncludesTheirText()
    {
        var vm = new PdfViewModel(Sample("image-only.pdf"), RunningShell(), new PdfConfig(), Reading(ToolModelsState.Ready));
        try
        {
            await vm.LoadAsync(System.Windows.Media.Brushes.White);

            var scans = await Invoke(vm, "pdf_read_scans");
            var text = await Invoke(vm, "pdf_read_text", new JsonObject { ["page_from"] = 1 });
            var found = await Invoke(vm, "pdf_find_text", new JsonObject { ["query"] = "peregrine" });

            Assert.IsFalse(scans.IsError, scans.ModelText);
            StringAssert.Contains(scans.ModelText, "--- page 1 ---");
            StringAssert.Contains(scans.ModelText, "The peregrine nests on cliffs.");
            StringAssert.Contains(text.ModelText, "The peregrine nests on cliffs.");
            StringAssert.Contains(found.ModelText, "p.1:");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-scan-reading")]
    public async Task AScannedDocument_SaysSoAboveIt_UntilTheReaderPutsTheNoticeAway()
    {
        var vm = new PdfViewModel(Sample("image-only.pdf"), RunningShell(), new PdfConfig(), Reading(ToolModelsState.Ready));
        try
        {
            await vm.LoadAsync(System.Windows.Media.Brushes.White);

            Assert.IsTrue(vm.ShowsScansNotice, "a document of scans says so above it");
            StringAssert.Contains(vm.ScansSummary, "1");
            vm.DismissScansNoticeCommand.Execute(null);
            Assert.IsFalse(vm.ShowsScansNotice);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-scan-reading")]
    public async Task ADocumentWithText_HasNoScansNotice()
    {
        var vm = new PdfViewModel(Sample("outline.pdf"), RunningShell(), new PdfConfig(), Reading(ToolModelsState.Ready));
        try
        {
            await vm.LoadAsync(System.Windows.Media.Brushes.White);
            Assert.IsFalse(vm.ShowsScansNotice);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-read-scans")]
    public async Task ReadScans_OnADocumentWithNone_SaysSo()
    {
        var vm = new PdfViewModel(Sample("outline.pdf"), RunningShell(), new PdfConfig(), Reading(ToolModelsState.Ready));
        try
        {
            await vm.LoadAsync(System.Windows.Media.Brushes.White);

            var result = await Invoke(vm, "pdf_read_scans");

            Assert.IsFalse(result.IsError);
            StringAssert.Contains(result.Summary, "no scanned pages");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-context")]
    public void Context_NamesTheDocumentAndSaysTheTextIsNotInIt()
    {
        var vm = LoadedVm();
        try
        {
            var ctx = vm.GetContext();

            StringAssert.Contains(ctx, "outline.pdf");
            StringAssert.Contains(ctx, "3");                    // page count
            StringAssert.Contains(ctx, "table of contents");
            StringAssert.Contains(ctx, "pdf_");                  // points at the tools
            Assert.IsTrue(vm.IsContextReady);
            Assert.IsFalse(ctx.Contains("first page"),
                "the document's body must NOT be dumped into context — that's what pdf_read_text is for");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    public void SecurityContext_IsPerDocument()
    {
        var a = LoadedVm("outline.pdf");
        var b = LoadedVm("text.pdf");
        try
        {
            // Two PDF tabs pinned into one conversation have to stay distinguishable rather than collapsing
            // onto whichever was pinned first.
            StringAssert.Contains(a.GetSecurityContext(), "outline.pdf");
            Assert.AreNotEqual(a.GetSecurityContext(), b.GetSecurityContext());
            Assert.AreEqual(ContextSecurityRisk.Low, a.GetContextSecurityRisk());
        }
        finally { a.Dispose(); b.Dispose(); }
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-ai-act-read-text")]
    public async Task ReadText_MarksEachPage()
    {
        var vm = LoadedVm();
        try
        {
            var result = await Invoke(vm, "pdf_read_text", new JsonObject { ["page_from"] = 1, ["page_to"] = 3 });

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "--- page 1 ---");
            StringAssert.Contains(result.ModelText, "--- page 3 ---");
            StringAssert.Contains(result.ModelText, "second page");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-read-text")]
    public async Task ReadText_ErrorsOnAPageThatDoesNotExist()
    {
        var vm = LoadedVm();
        try
        {
            // Silently reading page 1 when the model asked for page 500 would have it draw confident
            // conclusions about the wrong part of the document.
            var result = await Invoke(vm, "pdf_read_text", new JsonObject { ["page_from"] = 500 });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.IsError);
            StringAssert.Contains(result.ModelText, "3");   // says how many pages there actually are
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-read-text")]
    public async Task ReadText_SaysAPageIsProbablyAScan_RatherThanReturningNothing()
    {
        var vm = LoadedVm("image-only.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_read_text", new JsonObject { ["page_from"] = 1 });

            Assert.IsTrue(result.Success, "no text is a real answer, not a failure");
            StringAssert.Contains(result.ModelText, "pdf_page_image");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-find-text")]
    public async Task FindText_ReportsThePageAPhraseIsOn()
    {
        var vm = LoadedVm();
        try
        {
            var result = await Invoke(vm, "pdf_find_text", new JsonObject { ["query"] = "third" });

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "p.3");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-find-text")]
    public async Task FindText_SaysWhereToLookNext_WhenThereIsNoTextToSearch()
    {
        var vm = LoadedVm("image-only.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_find_text", new JsonObject { ["query"] = "anything" });

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "pdf_page_image");
        }
        finally { vm.Dispose(); }
    }

    // ── Outline and info ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-ai-act-outline")]
    public async Task Outline_ListsEntriesWithTheirPages()
    {
        var vm = LoadedVm();
        try
        {
            var result = await Invoke(vm, "pdf_outline");

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, PdfSamples.OutlineRootTitle);
            StringAssert.Contains(result.ModelText, PdfSamples.OutlineChildTitle);
            StringAssert.Contains(result.ModelText, "p.3");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-outline")]
    public async Task Outline_SaysSoWhenThereIsNone()
    {
        var vm = LoadedVm("text.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_outline");

            Assert.IsTrue(result.Success, "no table of contents is the common case, not an error");
            StringAssert.Contains(result.ModelText, "no table of contents");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-get-info")]
    public async Task GetInfo_ReportsTheDocumentsOwnDescription()
    {
        var vm = LoadedVm("text.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_get_info");

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, PdfSamples.MetadataTitle);
            StringAssert.Contains(result.ModelText, "1 page");
        }
        finally { vm.Dispose(); }
    }

    // ── Images ────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-ai-act-page-image")]
    public async Task PageImage_ReadsAScannedPage_WithNoRendererWiredAtAll()
    {
        // The branch that makes a scanned PDF readable without a browser: a scanned page IS one full-page
        // image, so it comes straight out of the file at source resolution. No view is attached here, which
        // is exactly the point — this must not depend on one.
        var vm = LoadedVm("image-only.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_page_image", new JsonObject { ["page"] = 1 });

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Images);
            Assert.AreEqual(1, result.Images.Count);
            Assert.IsTrue(result.Images[0].Bytes.Length > 0);

            // The text has to stand alone too: when a separate vision model is configured the shell replaces
            // the bytes with its description, and when none can see images they're dropped entirely.
            StringAssert.Contains(result.ModelText, "Page 1");
            StringAssert.Contains(result.ModelText, "scan");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-page-image")]
    public async Task PageImage_DrawsATextPage_AsTheReaderShowsIt()
    {
        var vm = LoadedVm("text.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_page_image", new JsonObject { ["page"] = 1 });

            Assert.IsTrue(result.Success, "a page with text and no picture is drawn, as the reader draws it");
            Assert.IsNotNull(result.Images);
            Assert.AreEqual(1, result.Images.Count);
            Assert.AreEqual("image/png", result.Images[0].MimeType);
            Assert.IsTrue(result.Images[0].Bytes.Length > 0);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-page-image")]
    public async Task PageImage_SaysWhatToTryInstead_ForAPageTheDocumentLacks()
    {
        var vm = LoadedVm("text.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_page_image", new JsonObject { ["page"] = 99 });

            Assert.IsFalse(result.Success, "a page past the end can't be pictured");
            StringAssert.Contains(result.ModelText, "pdf_read_text");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-get-image")]
    public async Task GetImage_ReturnsTheEmbeddedImage()
    {
        var vm = LoadedVm("image-only.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_get_image", new JsonObject { ["page"] = 1 });

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Images);
            Assert.AreEqual("image/png", result.Images[0].MimeType);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-get-image")]
    public async Task GetImage_ErrorsOnAPageWithNoImages()
    {
        var vm = LoadedVm("text.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_get_image", new JsonObject { ["page"] = 1 });

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.ModelText, "pdf_page_image");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-list-images")]
    public async Task ListImages_ReportsRepeatsWithoutReturningAnyBytes()
    {
        var vm = LoadedVm("repeated-image.pdf");
        try
        {
            var result = await Invoke(vm, "pdf_list_images");

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Images, "the inventory is how the model decides what's worth fetching");
            StringAssert.Contains(result.ModelText, "distinct");
        }
        finally { vm.Dispose(); }
    }

    // ── Driving the view ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-ai-act-view-page")]
    public async Task ViewPage_FailsHonestly_WithNoViewAttached()
    {
        var vm = LoadedVm();
        try
        {
            var result = await Invoke(vm, "pdf_view_page", new JsonObject { ["page"] = 2 });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.IsError, "claiming to have moved a view that isn't there would be a lie");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("pdf-ai-act-view-page")]
    public async Task ViewPage_MovesTheReader_AndRefusesAPageTheDocumentLacks()
    {
        var vm = LoadedVm();
        try
        {
            int? shown = null;
            vm.NavigateToPage = (page, _) => shown = page;

            var moved = await Invoke(vm, "pdf_view_page", new JsonObject { ["page"] = 2 });
            Assert.IsTrue(moved.Success);
            Assert.AreEqual(2, shown);

            shown = null;
            var past = await Invoke(vm, "pdf_view_page", new JsonObject { ["page"] = vm.PageCount + 1 });
            Assert.IsFalse(past.Success);
            Assert.IsNull(shown, "a page that isn't there is not scrolled to");
            StringAssert.Contains(past.ModelText, vm.PageCount.ToString());
        }
        finally { vm.Dispose(); }
    }

    // ── Unreadable documents ──────────────────────────────────────────────────

    [TestMethod]
    public async Task UnreadableDocument_IsAdmitted_NotReportedAsEmpty()
    {
        var vm = LoadedVm("corrupt.pdf");
        try
        {
            Assert.IsTrue(vm.IsContextReady, "a document that will never parse must not block the send forever");

            var result = await Invoke(vm, "pdf_read_text");

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.IsError,
                "'couldn't read it' and 'it says nothing' must not collapse into each other");
        }
        finally { vm.Dispose(); }
    }
}
