using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;
using NSubstitute;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Visuals.Pages;

/// <summary>The page text panel: pages read once and kept, their text filling in; the download offered until page
/// reading is here.</summary>
[TestClass]
[CoversNode("page-reading")]
public class PageReadingPanelTests
{
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => { call.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static IPageReading Reading(ToolModelsState state)
    {
        var reading = Substitute.For<IPageReading>();
        reading.State.Returns(state);
        reading.ModelsVersion.Returns("test-1");
        reading.DownloadBytes.Returns(3L << 30);
        reading.ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var blocks = new[] { new PageBlock(PageBlockKind.Title, 0.1, 0.1, 0.9, 0.15, "1. Introduction"),
                                 new PageBlock(PageBlockKind.Text, 0.1, 0.2, 0.9, 0.4, "We read pages.") };
            foreach (var block in blocks) call.Arg<IProgress<PageBlock>?>()?.Report(block);
            return Task.FromResult<IReadOnlyList<PageBlock>>(blocks);
        });
        return reading;
    }

    private static Func<CancellationToken, Task<IReadOnlyList<string>>> OnePage(List<string> made) => _ =>
    {
        string page = Path.Combine(Path.GetTempPath(), $"panel-test-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(page, [1]);
        made.Add(page);
        return Task.FromResult<IReadOnlyList<string>>([page]);
    };

    [TestMethod]
    public async Task Pages_AreReadOnce_TheirTextKept_AndTheirPicturesLetGo()
    {
        var reading = Reading(ToolModelsState.Ready);
        var panel = new PageReadingPanel(reading, null, Shell());
        string key = Guid.NewGuid().ToString();
        var made = new List<string>();

        panel.Open(key, "scan", OnePage(made));
        await panel.Done;

        Assert.AreEqual("## 1. Introduction\n\nWe read pages.\n", panel.Markdown);
        Assert.AreEqual(1, panel.PageTexts!.Count);
        Assert.IsNull(panel.Status);
        Assert.IsFalse(File.Exists(made.Single()), "the page's picture goes once it is read");

        panel.Open(key, "scan", OnePage(made));
        await panel.Done;

        Assert.AreEqual("## 1. Introduction\n\nWe read pages.\n", panel.Markdown, "the kept reading, shown again");
        await reading.Received(1).ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, made.Count, "not read again");
    }

    [TestMethod]
    public async Task UntilPageReadingIsHere_ItsDownloadIsOffered()
    {
        var reading = Reading(ToolModelsState.NotDownloaded);
        var panel = new PageReadingPanel(reading, null, Shell());

        panel.Open(Guid.NewGuid().ToString(), "scan", OnePage([]));
        await panel.Done;

        Assert.IsTrue(panel.IsOpen);
        Assert.IsTrue(panel.CanDownload);
        Assert.IsNotNull(panel.Status, "it says what the download is");
        Assert.IsNull(panel.PageTexts);
        panel.DownloadCommand.Execute(null);
        reading.Received(1).Download();
    }

    [TestMethod]
    public async Task WherePageReadingCannotRun_ThePanelSaysSo()
    {
        var panel = new PageReadingPanel(Reading(ToolModelsState.Unavailable), null, Shell());

        panel.Open(Guid.NewGuid().ToString(), "scan", OnePage([]));
        await panel.Done;

        Assert.IsFalse(panel.Available);
        Assert.IsFalse(panel.CanDownload);
        Assert.IsNotNull(panel.Status);
    }

    [TestMethod]
    public async Task ADownloadThatStopped_IsOfferedAgain_SayingWhy()
    {
        var reading = Reading(ToolModelsState.NotDownloaded);
        reading.DownloadFailure.Returns("the network went");
        var panel = new PageReadingPanel(reading, null, Shell());

        panel.Open(Guid.NewGuid().ToString(), "scan", OnePage([]));
        await panel.Done;

        Assert.IsTrue(panel.CanDownload);
        Assert.AreEqual(Str.Format("Text.PageReading.DownloadStopped", "the network went"), panel.Status);
    }

    /// <summary>A page picture of the given size, white, written where the page's pictures are made.</summary>
    private static Func<CancellationToken, Task<IReadOnlyList<string>>> BlankPage(string folder) => _ =>
    {
        string page = Path.Combine(folder, $"page-{Guid.NewGuid():N}.png");
        var pixels = new byte[200 * 200 * 4];
        Array.Fill(pixels, (byte)255);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 200 * 4)));
        using (var file = File.Create(page)) encoder.Save(file);
        return Task.FromResult<IReadOnlyList<string>>([page]);
    };

    [TestMethod]
    public async Task ASave_IsOfferedBesideWhatWasRead_AndTakesOnlyTheFiguresItsTextShows()
    {
        string folder = Directory.CreateTempSubdirectory("nexaflow-panel-save-").FullName;
        try
        {
            var reading = Reading(ToolModelsState.Ready);
            reading.ReadPageAsync(Arg.Any<string>(), Arg.Any<IProgress<PageBlock>?>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyList<PageBlock>>([
                    new PageBlock(PageBlockKind.Figure, 0.1, 0.1, 0.5, 0.3, ""),
                    new PageBlock(PageBlockKind.Caption, 0.1, 0.31, 0.5, 0.35, "Figure 1: A flow."),
                    new PageBlock(PageBlockKind.Figure, 0.1, 0.5, 0.5, 0.7, ""),
                    new PageBlock(PageBlockKind.Caption, 0.1, 0.71, 0.5, 0.75, "Figure 2: A photograph."),
                ]));
            var ai = Substitute.For<IAIService>();
            ai.AskAboutPictureAsync(Arg.Any<string>(), Arg.Any<ContextImage>(), Arg.Any<CancellationToken>())
              .Returns("```mermaid\nflowchart TD\n  A --> B\n```", "NONE");
            var shell = Shell();
            string target = Path.Combine(folder, "saved", "notes.md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>())
                 .Returns(Task.FromResult<string?>(target));
            var panel = new PageReadingPanel(reading, ai, shell);

            panel.Open(Guid.NewGuid().ToString(), Path.Combine(folder, "scan.png"), BlankPage(folder));
            await panel.Done;
            await panel.SaveCommand.ExecuteAsync(null);

            await shell.Received(1).PickSaveFileAsync("scan.md", Arg.Any<IReadOnlyList<string>?>(), folder, Arg.Any<long>());
            string saved = File.ReadAllText(target);
            StringAssert.Contains(saved, "```mermaid", "the diagram drawn again stands in its figure's place");
            StringAssert.Contains(saved, "](notes.figures/figure-2.png)", "the picture kept is linked beside the save");
            CollectionAssert.AreEqual(new[] { "figure-2.png" },
                                      Directory.GetFiles(Path.Combine(folder, "saved", "notes.figures")).Select(Path.GetFileName).ToArray(),
                                      "only the figure the text shows is saved with it");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
