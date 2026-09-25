using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// End-to-end dispatch: an <c>abc</c> or <c>lilypond</c> fence in a document is engraved rather than shown as the
/// characters it was written as.
///
/// <para>
/// Both dialects, because they are two languages reaching one engraver and a registration that named only one of them
/// looks right until somebody writes the other.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
public class MusicRendererTests
{
    private static Laid Lay(string md) => Laying.Lay(null, md, 700, StyleFormat.Dark);

    private static bool Engraved(Laid laid) =>
        laid.Root.SelfAndDescendants().Any(piece => piece.Kind == MusicPiece.Page)
        && !laid.Root.SelfAndDescendants().Any(piece => piece.Kind == MarkdownPieces.Verbatim);

    [TestMethod]
    public void ValidAbc_Engraves() => UiThread.Run(() =>
        Assert.IsTrue(Engraved(Lay("```abc\nX:1\nM:4/4\nK:G\n|:GABc dedB|c2A2 A2BA:|\n```\n")),
                      "valid notation should engrave, not fall back to its source"));

    [TestMethod]
    [CoversNode("ly-core")]
    public void ValidLilyPond_Engraves() => UiThread.Run(() =>
        Assert.IsTrue(Engraved(Lay("```lilypond\n\\relative c' { \\time 4/4 c4 d e f | g1 }\n```\n"))));
}
