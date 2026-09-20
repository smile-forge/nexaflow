using System.Windows;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Another language drawn inside a diagram's words: a tune on a node, a formula on a class. The sibling of
/// <see cref="DiagramWords"/> — something measured that can draw itself — for content that is a whole layout rather
/// than a run of glyphs.
///
/// <para>
/// It is grafted rather than painted, so every piece of it is still a piece of the tree it lands in: the tune's own
/// notes are selectable, each still standing for the bar it was written as.
/// </para>
/// </summary>
/// <param name="Laid">The nested content, laid out against the room it was given.</param>
/// <param name="At">
/// Where its source begins inside the source that holds it. It was laid out from a slice, so its parts count from
/// the start of that slice, and everything that selects or carets has to be told the difference.
/// </param>
internal sealed record DiagramInset(Laid Laid, int At)
{
    /// <summary>How wide it turned out.</summary>
    public double Width => Laid.Size.Width;

    /// <summary>How tall it turned out.</summary>
    public double Height => Laid.Size.Height;

    /// <summary>The room it takes.</summary>
    public Size Taken => Laid.Size;

    /// <summary>Sets it down at <paramref name="at"/>, as a piece of <paramref name="kind"/> holding the whole of it.</summary>
    public void Set(LayoutBuilder build, Point at, string kind)
    {
        build.Open(kind, part: null, stops: Stops.None);
        build.Graft(Laid.Tree, at, shift: At);
        build.Close();
    }
}
