using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A <c>mermaid</c> block read back from its tree: the word its header starts with, and the front matter's title,
/// config and where the diagram starts — the answers every diagram's renderer is handed.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class MermaidBlockTests
{
    [TestMethod]
    public void TheHeaderIsFoundPastFrontMatterCommentsAndDirectives()
    {
        const string source = "---\ntitle: T\n---\n\n%% a note\n%%{\n  init: {}\n}%%\ngitGraph\n  commit";

        var block = MermaidBlock.Read(source);

        Assert.AreEqual("gitGraph", block.Keyword?.Text);
        Assert.AreEqual(source.IndexOf("gitGraph", StringComparison.Ordinal), block.Keyword!.Start);
    }

    [TestMethod]
    public void NoHeaderHasNoKeyword_AndAnyWordInItsPlaceIsTheKeywordAsWritten()
    {
        Assert.IsNull(MermaidBlock.Read("").Keyword);
        Assert.IsNull(MermaidBlock.Read("%% only a comment\n\n").Keyword);
        Assert.AreEqual("wibble", MermaidBlock.Read("wibble\n  a").Keyword?.Text, "whether it names a diagram is the fence's to say");
        Assert.IsNull(MermaidBlock.Read("123\n  a").Keyword);
        Assert.IsNull(MermaidBlock.Read("---\nconfig:\npie\n").Keyword, "a fence never closed is in the header's place");
    }

    [TestMethod]
    public void TheBodyIsWhatFollowsTheFrontMatter()
    {
        var block = MermaidBlock.Read("---\nconfig:\n  theme: forest\n---\npie\n  \"A\" : 1\n");

        Assert.AreEqual("pie\n  \"A\" : 1\n", block.Body);
        Assert.AreEqual(block.Source.Length - block.Body.Length, block.BodyStart);
    }

    [TestMethod]
    public void WithNoFrontMatterTheBodyIsTheBlock()
    {
        foreach (var source in new[] { "pie\n  \"A\" : 1\n", "---\nconfig:\npie\n", "" })
        {
            var block = MermaidBlock.Read(source);
            Assert.AreEqual(source, block.Body, "a fence never closed is not front matter");
            Assert.AreEqual(0, block.BodyStart);
            Assert.IsNull(block.Config);
        }
    }

    [TestMethod]
    public void TheTitleIsTheTopLevelOne_WithoutItsQuotes()
    {
        Assert.AreEqual("My Chart", MermaidBlock.Read("---\ntitle: My Chart\nconfig:\n  theme: dark\n---\npie\n").FrontMatterTitleText);
        Assert.AreEqual("Quoted", MermaidBlock.Read("\n---\ntitle: \"Quoted\"\n---\npie").FrontMatterTitleText);
        Assert.AreEqual("Single", MermaidBlock.Read("---\nTitle: 'Single'\n---\npie").FrontMatterTitleText);
        Assert.IsNull(MermaidBlock.Read("---\nconfig:\n  title: nested\n---\npie\n").FrontMatterTitleText);
        Assert.IsNull(MermaidBlock.Read("---\ntitle: \"\"\n---\npie").FrontMatterTitleText);
        Assert.IsNull(MermaidBlock.Read("---\ntitle:\n---\npie").FrontMatterTitleText);
        Assert.IsNull(MermaidBlock.Read("pie title Inline").FrontMatterTitleText, "a title on the header is the diagram's own");
    }

    [TestMethod]
    public void TheTitleIsAPartOfTheSource()
    {
        const string source = "---\ntitle: \"My Chart\"\n---\npie";
        var title = MermaidBlock.Read(source).FrontMatterTitle!;

        Assert.AreEqual("\"My Chart\"", source.Substring(title.Start, title.Length));
    }

    [TestMethod]
    public void TheConfigIsEverythingBetweenTheFences()
    {
        Assert.AreEqual("title: T\nconfig:\n  theme: dark",
                        MermaidBlock.Read("---\ntitle: T\nconfig:\n  theme: dark\n---\npie").Config);
        Assert.AreEqual("", MermaidBlock.Read("---\n---\npie").Config);
        Assert.AreEqual("a: 1\r", MermaidBlock.Read("---\r\na: 1\r\n---\r\npie").Config,
                        "only the last line's newline goes; every other character of what was written stays");
    }

    [TestMethod]
    public void ABlockWrittenInsideADocumentReadsTheSameAsOneOnItsOwn()
    {
        // A fenced diagram is read where it sits, so its parts are counted in the document. What is sliced out of its own
        // source has to be counted from where that source starts, or a diagram in a document loses its configuration.
        const string source = "---\nconfig:\n  theme: dark\n---\npie\n";

        var alone = MermaidBlock.Read(source);
        var held = MermaidBlock.Of(MermaidParser.Parse(source), at: 40);

        Assert.AreEqual(alone.Config, held.Config);
        Assert.AreEqual(alone.BodyStart, held.BodyStart);
        Assert.AreEqual(alone.Body, held.Body);
    }

    [TestMethod]
    public void TheAccessibleTitleAndDescriptionAreRead()
    {
        // A diagram nothing reads, so its own line stays whole and the accessibility lines are all that is read out of it.
        var block = MermaidBlock.Read("wibble\n  accTitle: Decisions\n  accDescr {\n    Bob's stand\n  }\n  a --> b");

        Assert.AreEqual("Decisions", block.AccessibleTitle?.Text);
        Assert.AreEqual("Bob's stand", block.AccessibleDescription?.Text);
        CollectionAssert.AreEqual(new[] { "a --> b" }, block.Statements.Select(part => part.Text).ToArray());
    }
}
