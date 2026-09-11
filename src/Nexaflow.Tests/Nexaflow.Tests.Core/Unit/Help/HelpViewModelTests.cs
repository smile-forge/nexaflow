using Nexaflow.Core.Help;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The help pane's view model without its view: what the assistant is told (the help text, not its markup), what
/// search_page returns (the page being read first, ids naming the page and the match), and how a move within help
/// pins the pane while a re-point from the shell does not.
/// </summary>
[TestClass]
public class HelpViewModelTests
{
    private static HelpViewModel Open(HelpFixture help, string? topic, out Page page)
    {
        page = new Page { PageKind = "Help", PageParams = topic is null ? [] : new() { ["topic"] = topic } };
        return new HelpViewModel(help.Library, Substitute.For<IShellServices>(), page);
    }

    [TestMethod]
    [CoversNode("help-ai-context")]
    public void Context_IsTheHelpText_WithoutItsMarkup()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Text", out _);

        var context = vm.GetContext();

        StringAssert.Contains(context, "Text viewer");
        StringAssert.Contains(context, "Open any text file; the pane shows it with line numbers.");
        Assert.IsFalse(context.Contains("![", StringComparison.Ordinal), "no image syntax");
        Assert.IsFalse(context.Contains("images/shot.png", StringComparison.Ordinal));
        Assert.IsNotNull(vm.GetAiSystemPromptGuidance());
    }

    [TestMethod]
    [CoversNode("help-ai-context")]
    public void APageKindWithNoHelp_ShowsTheIndex_AndTheContextSaysSo()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Hex", out _);

        Assert.AreEqual(HelpLibrary.IndexTopic, vm.Topic);
        Assert.AreEqual("Hex", vm.RequestedTopic);
        StringAssert.Contains(vm.GetContext(), "'Hex'");
    }

    [TestMethod]
    [CoversNode("help-search")]
    public async Task SearchForTheAgent_ListsThePageBeingReadFirst()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Markdown", out _);

        var outcome = await vm.SearchAsync(SearchSyntax.ParseRequest("pane"), display: false, CancellationToken.None);

        Assert.IsFalse(outcome.Failed);
        Assert.AreEqual("Markdown#0", outcome.Hits[0].Id, "the page being read comes first");
        CollectionAssert.Contains(outcome.Hits.Select(h => h.Id).ToList(), "Text#0");
        Assert.AreEqual(outcome.Hits.Count, outcome.MatchCount);
    }

    [TestMethod]
    [CoversNode("help-search")]
    public async Task ASearchThatCannotRun_SaysWhy()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Text", out _);

        var outcome = await vm.SearchAsync(SearchSyntax.ParseRequest("/[unclosed/"), display: false, CancellationToken.None);

        Assert.IsTrue(outcome.Failed);
        Assert.IsNotNull(outcome.Message);
    }

    [TestMethod]
    [CoversNode("help-pane-controller")]
    public void AMoveWithinHelp_Pins_AndARepointFromTheShellDoesNot()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Text", out var page);
        Assert.IsFalse(vm.IsPinned);

        Assert.IsTrue(vm.FollowLink("help:Markdown"));
        Assert.IsTrue(vm.IsPinned, "following a link inside help pins it");
        Assert.AreEqual("Markdown", vm.Topic);
        Assert.AreEqual("Markdown", page.PageParams!["topic"], "the tab's params follow what it shows");

        vm.Navigate("Text", null, fromUser: false);
        Assert.IsFalse(vm.IsPinned, "the shell pointing it at a page unpins it");
        Assert.AreEqual("Text", vm.Topic);

        Assert.IsFalse(vm.FollowLink("https://example.com"), "a web link is the browser's");
    }

    [TestMethod]
    [CoversNode("help-topics")]
    public void ALinkToAHeadingOnAnotherPage_OpensThePage_ThenAsksForTheHeading()
    {
        using var help = new HelpFixture();
        var vm = Open(help, "Text", out _);
        string? scrolledNow = null;
        vm.AnchorRequested += anchor => scrolledNow = anchor;

        Assert.IsTrue(vm.FollowLink("help:Markdown#tables"));
        Assert.AreEqual("Markdown", vm.Topic);
        Assert.AreEqual("tables", vm.TakePendingAnchor(), "the view scrolls there once the page has laid out");
        Assert.IsNull(vm.TakePendingAnchor(), "and only once");

        Assert.IsTrue(vm.FollowLink("help:Markdown#diagrams"));
        Assert.AreEqual("diagrams", scrolledNow, "already on that page: it just moves to the heading");
    }
}
