using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music;
using MdMarkdown = Markdig.Markdown;
using Markdig.Syntax;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// End-to-end dispatch: an <c>abc</c> or <c>lilypond</c> fence parsed by the pipeline renders through
/// <see cref="BlockRenderer"/> onto an engraved page rather than falling back to a box of its source.
///
/// <para>
/// Both dialects, because they are two languages reaching one engraver and a registration that named only
/// one of them looks right until somebody writes the other.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
public class MusicRendererTests
{
    private static FrameworkElement Render(string md)
    {
        var fence = MdMarkdown.Parse(md, MarkdownParser.Pipeline).OfType<FencedCodeBlock>().Single();

        return BlockRenderer.Render(fence, md);
    }

    [TestMethod]
    public void ValidAbc_EngravesToNonFallbackElement() => UiThread.Run(() =>
    {
        var fe = Render("```abc\nX:1\nM:4/4\nK:G\n|:GABc dedB|c2A2 A2BA:|\n```\n");

        Assert.IsNotNull(fe);
        Assert.IsFalse(fe is Border, "valid notation should engrave, not fall back to the source box");
    });

    [TestMethod]
    [CoversNode("ly-core")]
    public void ValidLilyPond_EngravesToNonFallbackElement() => UiThread.Run(() =>
    {
        var fe = Render("```lilypond\n\\relative c' { \\time 4/4 c4 d e f | g1 }\n```\n");

        Assert.IsNotNull(fe);
        Assert.IsFalse(fe is Border);
    });
}
