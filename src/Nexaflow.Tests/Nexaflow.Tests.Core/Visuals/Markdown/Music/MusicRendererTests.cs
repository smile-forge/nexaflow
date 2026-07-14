using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// End-to-end dispatch: a <c>#% … #%</c> block parsed by the pipeline renders through
/// <see cref="BlockRenderer"/> → <see cref="MusicRenderer"/> to an engraved element for valid notation,
/// and degrades to a themed source-text <see cref="Border"/> (never throws) for notation that yields no music.
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

    [TestMethod]
    public void UnparseableNotation_FallsBackToSourceBox() => UiThread.Run(() =>
    {
        // Header only, no music → an empty score → the themed source-text fallback (a Border).
        var mb = Block("#%abc\nX:1\nT:Empty\n#%\n");
        var fe = BlockRenderer.Render(mb);
        Assert.IsInstanceOfType(fe, typeof(Border), "no music should degrade to the source-text box");
    });
}
