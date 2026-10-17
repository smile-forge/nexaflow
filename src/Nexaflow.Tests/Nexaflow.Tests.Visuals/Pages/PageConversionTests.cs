using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Pages;
using NSubstitute;

namespace Nexaflow.Tests.Visuals.Pages;

/// <summary>
/// Converting pages to Markdown: read once and kept, their pictures let go; a draft opened in the Markdown editor in the
/// right-hand pane, holding only the figures its text shows and leading back to its source; page reading's download
/// offered — and what was asked done once it is here — or why it cannot run.
/// </summary>
[TestClass]
[CoversNode("page-reading")]
public class PageConversionTests
{
    private string _dir = "";

    [TestInitialize]
    public void Make() => _dir = TestTemp.CreateSubdirectory("nexaflow-conversion-").FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => { call.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static IPageReading Reading(ToolModelsState state, params PageBlock[] blocks)
    {
        var reading = Substitute.For<IPageReading>();
        reading.State.Returns(state);
        reading.ModelsVersion.Returns("test-1");
        reading.DownloadBytes.Returns(3L << 30);
        reading.DownloadFailure.Returns((string?)null);
        if (blocks.Length == 0)
            blocks = [new PageBlock(PageBlockKind.Title, 0.1, 0.1, 0.9, 0.15, "1. Introduction"),
                      new PageBlock(PageBlockKind.Text, 0.1, 0.2, 0.9, 0.4, "We read pages.")];
        reading.ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            foreach (var block in blocks) call.Arg<IProgress<PageBlock>?>()?.Report(block);
            return Task.FromResult<IReadOnlyList<PageBlock>>(blocks);
        });
        return reading;
    }

    /// <summary>Pages from <c>scan.png</c> in the test folder: one white picture each time they are asked for, noted in
    /// <paramref name="made"/>.</summary>
    private PageSource Scan(List<string> made, string? key = null) => new(
        key ?? Guid.NewGuid().ToString(), Path.Combine(_dir, "scan.png"), _ =>
        {
            string page = Path.Combine(_dir, $"page-{Guid.NewGuid():N}.png");
            var pixels = new byte[200 * 200 * 4];
            Array.Fill(pixels, (byte)255);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 200 * 4)));
            using (var file = File.Create(page)) encoder.Save(file);
            made.Add(page);
            return Task.FromResult<IReadOnlyList<string>>([page]);
        },
        "Images", new Dictionary<string, string> { ["paths"] = Path.Combine(_dir, "scan.png") });

    [TestMethod]
    public async Task Pages_AreReadOnce_TheirTextKept_AndTheirPicturesLetGo()
    {
        var reading = Reading(ToolModelsState.Ready);
        var conversion = new PageConversion(reading, null, Shell());
        var made = new List<string>();
        string key = Guid.NewGuid().ToString();

        var first = await conversion.ReadAsync(Scan(made, key));
        var again = await conversion.ReadAsync(Scan(made, key));

        Assert.AreEqual("## 1. Introduction\n\nWe read pages.\n", first!.Markdown);
        Assert.AreEqual(first.Markdown, again!.Markdown, "the kept reading, found again");
        Assert.AreEqual(1, conversion.PageTexts!.Count);
        Assert.IsNull(conversion.Status);
        await reading.Received(1).ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, made.Count, "not read again");
        Assert.IsFalse(File.Exists(made.Single()), "the page's picture goes once it is read");
    }

    [TestMethod]
    public async Task AConversion_OpensADraftInTheRightPane_HoldingOnlyTheFiguresItsTextShows_LeadingBackToItsSource()
    {
        var reading = Reading(ToolModelsState.Ready,
            new PageBlock(PageBlockKind.Figure, 0.1, 0.1, 0.5, 0.3, ""),
            new PageBlock(PageBlockKind.Caption, 0.1, 0.31, 0.5, 0.35, "Figure 1: A flow."),
            new PageBlock(PageBlockKind.Figure, 0.1, 0.5, 0.5, 0.7, ""),
            new PageBlock(PageBlockKind.Caption, 0.1, 0.71, 0.5, 0.75, "Figure 2: A photograph."));
        var ai = Substitute.For<IAIService>();
        ai.AskAboutPictureAsync(Arg.Any<string>(), Arg.Any<ContextImage>(), Arg.Any<CancellationToken>())
          .Returns("```mermaid\nflowchart TD\n  A --> B\n```", "NONE");
        var shell = Shell();
        var caller = Substitute.For<IPageView>();
        Dictionary<string, string>? opened = null;
        shell.When(s => s.OpenTab("Markdown", Arg.Any<Dictionary<string, string>?>(), caller, true))
             .Do(call => opened = call.Arg<Dictionary<string, string>?>());

        await new PageConversion(reading, ai, shell).ConvertAsync(Scan([]), caller);

        Assert.IsNotNull(opened, "the Markdown opens in the right-hand pane beside the source");
        string draft = opened!["path"];
        Assert.AreEqual("scan.md", Path.GetFileName(draft), "named after its source");
        Assert.AreEqual(_dir, opened["draft"], "a draft, offered to be saved beside its source");
        string text = File.ReadAllText(draft);
        StringAssert.Contains(text, "```mermaid", "the diagram drawn again stands in its figure's place");
        StringAssert.Contains(text, "](scan.figures/figure-2.png)");
        CollectionAssert.AreEqual(new[] { "figure-2.png" },
                                  Directory.GetFiles(Path.Combine(Path.GetDirectoryName(draft)!, "scan.figures")).Select(Path.GetFileName).ToArray(),
                                  "only the figure its text shows");
        Assert.AreEqual("Images", OriginBreadcrumbs.ParentCrumbFor(draft)?.TargetPageKind, "its crumbs lead back to the picture");
        OriginBreadcrumbs.Clear(draft);
    }

    [TestMethod]
    public async Task UntilPageReadingIsHere_ItsDownloadIsOffered_AndWhatWasAskedIsDoneWhenItArrives()
    {
        var reading = Reading(ToolModelsState.NotDownloaded);
        var shell = Shell();
        var conversion = new PageConversion(reading, null, shell);

        await conversion.ConvertAsync(Scan([]), null);

        Assert.IsTrue(conversion.CanDownload);
        Assert.AreEqual(Str.Format("Text.PageReading.NeedsTool", SizeFormatter.FormatBytes(3L << 30)), conversion.Status, "it says what the download is");
        conversion.DownloadCommand.Execute(null);
        reading.Received(1).Download();

        reading.State.Returns(ToolModelsState.Ready);
        reading.Changed += Raise.Event();
        await Task.Delay(200);

        shell.Received(1).OpenTab("Markdown", Arg.Any<Dictionary<string, string>?>(), null, true);
        Assert.IsNull(conversion.Status);
    }

    [TestMethod]
    public async Task ADownloadThatStopped_IsOfferedAgain_SayingWhy()
    {
        var reading = Reading(ToolModelsState.NotDownloaded);
        reading.DownloadFailure.Returns("the network went");
        var conversion = new PageConversion(reading, null, Shell());

        await conversion.ConvertAsync(Scan([]), null);

        Assert.IsTrue(conversion.CanDownload);
        Assert.AreEqual(Str.Format("Text.PageReading.DownloadStopped", "the network went"), conversion.Status);
    }

    [TestMethod]
    public async Task WherePageReadingCannotRun_ItSaysSo()
    {
        var conversion = new PageConversion(Reading(ToolModelsState.Unavailable), null, Shell());

        Assert.IsNull(await conversion.ReadAsync(Scan([])));

        Assert.IsFalse(conversion.Available);
        Assert.IsFalse(conversion.CanDownload);
        Assert.AreEqual(Str.Get("Text.PageReading.Unavailable"), conversion.Status);
    }
}
