using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// The <c>#% … #%</c> block extension: the fence parses to a <see cref="MusicBlock"/>, the dialect tag
/// (or auto-detection) is honoured, and the raw notation source survives verbatim — including blank
/// lines, which must be treated as content (the LilyPond sample has them), not block terminators.
/// </summary>
[TestClass]
[CoversNode("music-block")]
public class MusicBlockParserTests
{
    private static MusicBlock? FirstMusicBlock(string src) =>
        MdMarkdown.Parse(src, MarkdownPipelineFactory.Default).OfType<MusicBlock>().FirstOrDefault();

    [TestMethod]
    public void TaggedAbc_ParsesToMusicBlock_WithAbcDialect()
    {
        var mb = FirstMusicBlock("#%abc\nX:1\nK:G\nGABc\n#%\n");
        Assert.IsNotNull(mb, "no MusicBlock parsed");
        Assert.AreEqual(MusicDialect.Abc, mb.Dialect);
        StringAssert.Contains(mb.Source, "X:1");
        StringAssert.Contains(mb.Source, "GABc");
    }

    [TestMethod]
    public void TaggedLilypond_ParsesToMusicBlock_WithLilyDialect()
    {
        var mb = FirstMusicBlock("#%lilypond\n\\relative c' { c4 d e f }\n#%\n");
        Assert.IsNotNull(mb);
        Assert.AreEqual(MusicDialect.LilyPond, mb.Dialect);
    }

    [TestMethod]
    public void Untagged_AbcHeaderDetected()
    {
        var mb = FirstMusicBlock("#%\nX:1\nK:C\nCDEF\n#%\n");
        Assert.IsNotNull(mb);
        Assert.AreEqual(MusicDialect.Abc, mb.Dialect);
    }

    [TestMethod]
    public void Untagged_LilypondBackslashDetected()
    {
        var mb = FirstMusicBlock("#%\n\\clef bass\nc4 d e f\n#%\n");
        Assert.IsNotNull(mb);
        Assert.AreEqual(MusicDialect.LilyPond, mb.Dialect);
    }

    [TestMethod]
    public void BlankLinesInsideBlock_ArePreserved()
    {
        // The block must not terminate on a blank line (fenced-code semantics).
        var mb = FirstMusicBlock("#%lilypond\nglobal = { }\n\nupper = { c4 }\n#%\n");
        Assert.IsNotNull(mb);
        StringAssert.Contains(mb.Source, "global");
        StringAssert.Contains(mb.Source, "upper");
    }

    [TestMethod]
    public void ClosingFence_EndsBlock_SurroundingMarkdownUnaffected()
    {
        var doc = MdMarkdown.Parse("# Title\n\n#%abc\nX:1\n#%\n\nAfter text\n", MarkdownPipelineFactory.Default);
        Assert.AreEqual(1, doc.OfType<MusicBlock>().Count(), "exactly one music block expected");
        // A heading before and a paragraph after the block still parse as normal markdown.
        Assert.IsTrue(doc.OfType<Markdig.Syntax.HeadingBlock>().Any(), "heading lost");
        Assert.IsTrue(doc.OfType<Markdig.Syntax.ParagraphBlock>().Any(), "trailing paragraph lost");
    }

    [TestMethod]
    public void HashNotFollowedByPercent_IsStillAHeading()
    {
        // Guard: the parser must not swallow ordinary ATX headings.
        var doc = MdMarkdown.Parse("# Heading\n", MarkdownPipelineFactory.Default);
        Assert.IsFalse(doc.OfType<MusicBlock>().Any());
        Assert.IsTrue(doc.OfType<Markdig.Syntax.HeadingBlock>().Any());
    }
}
