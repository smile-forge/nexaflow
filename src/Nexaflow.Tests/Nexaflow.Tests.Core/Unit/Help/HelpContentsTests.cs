using System.Text.RegularExpressions;
using Markdig.Syntax;
using Nexaflow.Core.Help;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A help page's generated menu (<see cref="HelpContents"/>): a Topics list of its <c>##</c> sections where the
/// introduction ends, a link back to it at the end of every section, ids exactly as the renderer gives them — and a page
/// with a single section left alone.
/// </summary>
[TestClass]
[CoversNode("help-topics")]
public class HelpContentsTests
{
    private const string Page =
        "# Help\n\nIntro text.\n\n---\n\n" +
        "## Opening\n\nOpen it.\n\n```markdown\n## not a heading\n```\n\n---\n\n" +
        "## Searching\n\n### Syntax\n\nFind things.\n\n" +
        "## Searching\n\nAgain.\n";

    [TestMethod]
    public void TheTopicsList_SitsBetweenTheIntroductionAndTheFirstSection()
    {
        var result = HelpContents.AddTopics(Page);

        CollectionAssert.AreEqual(
            new[] { "- [Opening](#opening)", "- [Searching](#searching)", "- [Searching](#searching-1)" },
            result.Split('\n').Where(l => l.StartsWith("- [", StringComparison.Ordinal)).ToList(),
            "every ## section in order, a repeat under its own id; ### headings and fenced lines are not topics");
        Assert.IsTrue(result.IndexOf("Intro text.", StringComparison.Ordinal) < result.IndexOf("- [Opening]", StringComparison.Ordinal));
        Assert.IsTrue(result.IndexOf("- [Searching](#searching-1)", StringComparison.Ordinal) < result.IndexOf("---", StringComparison.Ordinal),
            "before the rule that opens the first section");
    }

    [TestMethod]
    public void EverySection_EndsWithALinkBackToTheList()
    {
        var result = HelpContents.AddTopics(Page);
        var listId = MarkdownAnchors.IdOf(Markdig.Markdown.Parse(result, MarkdownPipelineFactory.Default)
                                                 .OfType<HeadingBlock>().First(h => h.Level == 2));

        var backs = Regex.Matches(result, @"\]\(#([^)]+)\)\s*$", RegexOptions.Multiline)
                         .Select(m => m.Groups[1].Value).Where(id => id == listId).Count();
        Assert.AreEqual(3, backs, "one per section");

        var sections = new[] { "## Opening", "## Searching\n\n### Syntax", "## Searching\n\nAgain." }
            .Select(s => result.IndexOf(s, StringComparison.Ordinal)).ToList();
        var backLinks = Regex.Matches(result, $@"\]\(#{Regex.Escape(listId!)}\)").Select(m => m.Index).ToList();
        for (var i = 0; i < sections.Count; i++)
        {
            Assert.IsTrue(backLinks[i] > sections[i], $"section {i}'s link follows its heading");
            if (i + 1 < sections.Count) Assert.IsTrue(backLinks[i] < sections[i + 1], $"…and comes before the next section");
        }
    }

    [TestMethod]
    public void APageWithOneSection_IsLeftAsItIs()
    {
        const string single = "# Text viewer\n\nIntro.\n\n## Searching\n\nType.\n";

        Assert.AreEqual(single, HelpContents.AddTopics(single));
    }

    [TestMethod]
    [TestCategory("UI")]
    public void EveryLinkItAdds_LandsOnARenderedHeading() => UiThread.Run(() =>
    {
        var result = HelpContents.AddTopics(Page);
        var doc = MarkdownFlowDocument.Build(result, MarkdownPalette.Dark);

        foreach (Match link in Regex.Matches(result, @"\]\(#([^)]+)\)"))
            Assert.IsNotNull(MarkdownAnchors.Find(doc, link.Groups[1].Value), $"#{link.Groups[1].Value} has a heading to land on");
    });
}
