using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// End-to-end dispatch: a <c>#% … #%</c> block parsed by the pipeline renders through <see cref="BlockRenderer"/>
/// onto the same engraved page a fenced <c>abc</c> or <c>lilypond</c> block makes.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("music-block")]
public class MusicRendererTests
{
    private static MusicBlock Block(string md) =>
        MdMarkdown.Parse(md, MarkdownPipelineFactory.Default).OfType<MusicBlock>().Single();

    [TestMethod]
    public void ValidAbc_EngravesToNonFallbackElement() => UiThread.Run(() =>
    {
        var mb = Block("#%abc\nX:1\nM:4/4\nK:G\n|:GABc dedB|c2A2 A2BA:|\n#%\n");
        var fe = BlockRenderer.Render(mb);
        Assert.IsNotNull(fe);
        Assert.IsFalse(fe is Border, "valid notation should engrave, not fall back to the source box");
    });

    [TestMethod]
    public void ValidLilyPond_EngravesToNonFallbackElement() => UiThread.Run(() =>
    {
        var mb = Block("#%lilypond\n\\relative c' { \\time 4/4 c4 d e f | g1 }\n#%\n");
        var fe = BlockRenderer.Render(mb);
        Assert.IsNotNull(fe);
        Assert.IsFalse(fe is Border);
    });
}
