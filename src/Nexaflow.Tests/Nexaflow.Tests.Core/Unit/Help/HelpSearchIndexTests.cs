using Nexaflow.Core.Help;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The "then every help page" half of a help search: pages held as the text a reader sees
/// (<see cref="HelpPlainText"/>), matched with the same matcher the open page highlights with, ranked by matches, and
/// listed with the page being read first.
/// </summary>
[TestClass]
[CoversNode("help-search")]
public class HelpSearchIndexTests
{
    private static TextSearchMatcher Matcher(string query)
    {
        Assert.IsTrue(TextSearchMatcher.TryCreate(SearchSyntax.ParseRequest(query), out var matcher, out var error), error);
        return matcher;
    }

    private static HelpTopic Topic(string topic, string title) => new(topic, title, $"P.{topic}/help/{topic}.md");

    private static HelpSearchIndex Index(params (string Topic, string Title, string Markdown)[] pages)
        => new(() => pages.Select(p => (Topic(p.Topic, p.Title), p.Markdown)).ToList());

    [TestMethod]
    public void PlainText_KeepsWhatRenders_AndDropsWhatDoesNot()
    {
        var text = HelpPlainText.Extract(
            "# Title\n\nSee [the docs](https://example.com/secret) now.\n\n![alt words](x.png)\n\n" +
            "```mermaid\ngraph TD; hiddenNode\n```\n\n```csharp\nvar visible = 1;\n```\n\n" +
            "| head | er |\n|---|---|\n| cellone | celltwo |\n\n<div>markup</div>\n");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        CollectionAssert.Contains(lines, "Title");
        CollectionAssert.Contains(lines, "See the docs now.", "a link is its text, never its target");
        CollectionAssert.Contains(lines, "var visible = 1;", "code renders as text");
        CollectionAssert.Contains(lines, "cellone", "one line per table cell");
        CollectionAssert.Contains(lines, "celltwo");
        foreach (var hidden in new[] { "example.com", "secret", "alt words", "hiddenNode", "markup" })
            Assert.IsFalse(text.Contains(hidden, StringComparison.Ordinal), $"'{hidden}' never shows as text");
    }

    [TestMethod]
    public void Search_RanksOtherPagesByMatches_LeavingOutThePageBeingRead()
    {
        var index = Index(
            ("Text", "Text viewer", "# Text viewer\n\nThe pane. Another pane. A third pane.\n"),
            ("Markdown", "Markdown", "# Markdown\n\nOne pane here.\n"),
            ("Hex", "Hex", "# Hex\n\nNo match at all.\n"),
            ("Help", "Help", "# Help\n\nPane and pane.\n"));

        var results = index.Search(Matcher("pane"), excludeTopic: "Help");

        CollectionAssert.AreEqual(new[] { "Text", "Markdown" }, results.Select(r => r.Topic.Topic).ToList());
        Assert.AreEqual(3, results[0].MatchCount);
        Assert.AreEqual("The pane. Another pane. A third pane.", results[0].Preview);
    }

    [TestMethod]
    public void Hits_PutThePageBeingReadFirst_InReadingOrder()
    {
        var index = Index(
            ("Text", "Text viewer", "# Text viewer\n\nPane one.\n\nPane two. Pane three.\n"),
            ("Markdown", "Markdown", "# Markdown\n\nA pane.\n"));

        var hits = index.Hits(Matcher("pane"), currentTopic: "Markdown", cap: 10, out var total);

        Assert.AreEqual(4, total);
        CollectionAssert.AreEqual(
            new[] { "Markdown#0", "Text#0", "Text#1", "Text#2" },
            hits.Select(h => $"{h.Topic.Topic}#{h.Ordinal}").ToList());
        Assert.AreEqual(2, index.Hits(Matcher("pane"), "Markdown", cap: 2, out var capped).Count);
        Assert.AreEqual(4, capped, "the cap trims the list, never the count");
    }

    [TestMethod]
    public void Search_MatchesWithThePagesOwnRules_WholeWordsWildcardsAndRegex()
    {
        var index = Index(("Text", "Text viewer", "# Text viewer\n\nPanel and panes, not a pan.\n\nPress F12.\n"));

        Assert.AreEqual(0, index.Search(Matcher("pane"), null).Count, "a plain term is a whole word");
        Assert.AreEqual(1, index.Search(Matcher("pan*"), null).Single().MatchCount > 0 ? 1 : 0);
        Assert.AreEqual(1, index.Search(Matcher("/F[0-9]+/"), null).Count);
    }
}
