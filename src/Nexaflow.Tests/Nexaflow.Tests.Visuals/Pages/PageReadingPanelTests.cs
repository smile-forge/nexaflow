using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Pages;
using NSubstitute;

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
}
