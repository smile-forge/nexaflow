using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// A document's headings make its sections (<see cref="WithSections"/>): each heading stands over the blocks after it up to
/// the next heading as deep or deeper, and a section says which blocks those are by their places among the document's parts.
/// </summary>
[TestClass]
[CoversNode("markdown-sections")]
public class MarkdownSectionsTests
{
    [TestMethod]
    public void AHeading_StandsOverTheBlocksAfterIt_UpToTheNextHeadingAsDeep()
    {
        const string Source = "intro\n\n# One\n\nfirst\n\n## One a\n\nnested\n\n# Two\n\nsecond\n";
        var root = MarkdownParser.Worked(Source);
        var sections = MarkdownSections.Of(root);

        Assert.AreEqual(2, sections.Sections.Count);
        var (one, two) = (sections.Sections[0], sections.Sections[1]);

        Assert.AreEqual("# One\n", Printed(root, one.Heading).TrimEnd('\n') + "\n");
        Assert.AreEqual(1, one.Sections.Count, "the second-rank heading stands under the first");
        Assert.AreEqual(one.Sections[0].End, two.Heading, "the inner section ends where the next first-rank heading starts");
        Assert.AreEqual(one.End, two.Heading);

        CollectionAssert.AreEqual(new[] { "first" }, Blocks(root, one.Heading + 1, one.Body), "a section's own blocks stop where the one under it starts");
        CollectionAssert.AreEqual(new[] { "second" }, Blocks(root, two.Heading + 1, two.End));
        CollectionAssert.AreEqual(new[] { "intro" }, Blocks(root, 0, sections.Preamble), "what comes before the first heading is the preamble");
    }

    [TestMethod]
    public void AHeadingSkippingARank_StillStandsUnderTheNearestOneAboveIt()
    {
        var sections = MarkdownSections.Of(MarkdownParser.Worked("# Top\n\n### Deep\n\ntext\n\n## Middle\n"));

        var top = sections.Sections.Single();
        CollectionAssert.AreEqual(new[] { 3, 2 }, top.Sections.Select(section => section.Rank).ToArray());
    }

    [TestMethod]
    public void ASection_CarriesTheNameItsHeadingAnswersTo_RepeatsToldApart()
    {
        var sections = MarkdownSections.Of(MarkdownParser.Worked("# Notes\n\na\n\n# Notes\n\nb\n"));

        CollectionAssert.AreEqual(new[] { "notes", "notes-1" }, sections.All().Select(section => section.Anchor).ToArray());
    }

    [TestMethod]
    public void AHeadingInAQuoteOrAList_HeadsNothing()
    {
        var sections = MarkdownSections.Of(MarkdownParser.Worked("> # Quoted\n\n- # Listed\n\ntext\n"));

        Assert.AreEqual(0, sections.Sections.Count);
    }

    [TestMethod]
    public void ASetextHeading_MakesASectionAsAnAtxOneDoes()
    {
        var sections = MarkdownSections.Of(MarkdownParser.Worked("Title\n=====\n\ntext\n\nPart\n----\n\nmore\n"));

        var title = sections.Sections.Single();
        Assert.AreEqual(1, title.Rank);
        Assert.AreEqual(2, title.Sections.Single().Rank);
    }

    [TestMethod]
    public void ADocumentWithNoHeading_HasNoSections_AndPrintsAsWritten()
    {
        const string Source = "just words\n\nand more\n";
        var root = MarkdownParser.Worked(Source);

        Assert.AreSame(MarkdownSections.None, MarkdownSections.Of(root));
        Assert.AreEqual(Source, root.Print());
    }

    [TestMethod]
    public void HangingTheSections_LeavesTheSourceAsWritten()
    {
        const string Source = "# One\n\n```mermaid\npie\n\"a\": 1\n```\n\n## Two\n\n| a |\n|---|\n| b |\n";

        Assert.AreEqual(Source, MarkdownParser.Worked(Source).Print());
    }

    [TestMethod]
    public void AHeadingWithNothingUnderIt_HoldsNothing()
    {
        var sections = MarkdownSections.Of(MarkdownParser.Worked("# Empty\n\n# Full\n\ntext\n"));

        Assert.IsFalse(sections.Sections[0].Holds);
        Assert.IsTrue(sections.Sections[1].Holds);
    }

    /// <summary>What the part at <paramref name="at"/> prints as.</summary>
    private static string Printed(ContentNode root, int at) => root.Children[at].Print();

    /// <summary>The words of every block of its own from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    private static string[] Blocks(ContentNode root, int from, int to) =>
        [.. root.Children.Skip(from).Take(to - from)
                .Where(part => !part.IsDerived && part.Role != Roles.Trivia)
                .Select(part => part.Print().Trim())];
}
