using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// Markdown a writer gave as one paragraph, spelled as a paragraph and nothing more (<see cref="MarkdownParser.SpellParagraph"/>):
/// a line that would open a block has its opening made a character, and everything else — inline code at the start of a line
/// included — is written as it was.
/// </summary>
[TestClass]
[CoversNode("documents-summary")]
public class MarkdownSpellParagraphTests
{
    [TestMethod]
    public void ALineOpeningNothing_IsWrittenAsItWas_CodeAtItsStartIncluded()
    {
        const string Said = "`ContentEngine` lays it out, *quickly*, with $x^2$ and [a link](https://example.org).";

        Assert.AreEqual(Said, MarkdownParser.SpellParagraph(Said));
    }

    [TestMethod]
    public void ALineThatWouldOpenABlock_IsSpelledAsWords()
    {
        foreach (var opening in new[] { "# heading", "- item", "+ item", "> quote", "1. item", "2) item", "```fence", "---", "<div>", "[a]: https://a.example" })
        {
            var spelled = MarkdownParser.SpellParagraph("Words.\n" + opening);

            Assert.IsTrue(OneParagraph(spelled), $"'{opening}' opened something: {spelled}");
        }
    }

    [TestMethod]
    public void AnUnderline_DoesNotTurnTheLineAboveIntoAHeading()
    {
        Assert.IsTrue(OneParagraph(MarkdownParser.SpellParagraph("Title\n=====")));
        Assert.IsTrue(OneParagraph(MarkdownParser.SpellParagraph("Title\n-----")));
    }

    [TestMethod]
    public void LinesThatTogetherMakeATable_AreSpelledToo() =>
        Assert.IsTrue(OneParagraph(MarkdownParser.SpellParagraph("a | b\n--|--")));

    [TestMethod]
    public void AnIndentedLine_IsNoCodeBlock() =>
        Assert.IsTrue(OneParagraph(MarkdownParser.SpellParagraph("    four spaces in")));

    private static bool OneParagraph(string text) =>
        MarkdownParser.Worked(text).Children.Where(part => !part.IsDerived && part.Role != Roles.Trivia).Select(part => part.Kind).ToList() is [MarkdownKinds.Paragraph];
}
