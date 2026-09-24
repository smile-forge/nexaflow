using System.Windows;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Another language's content, laid out and ready to be set down inside this one's — a tune on a flowchart node, a
/// molecule in a song's lyrics, a barcode in a formula.
///
/// <para>
/// Something measured that can draw itself, as a run of words is, for content that is a whole layout rather than a
/// row of glyphs. It is grafted rather than painted, so every piece of it is still a piece of the tree it lands in:
/// the tune's own notes are selectable, each standing for the bar it was written as, in the document that holds
/// them both.
/// </para>
/// </summary>
/// <param name="Laid">The content, laid out against the room it was given and positioned where it was written.</param>
internal sealed record ContentInset(Laid Laid)
{
    /// <summary>How wide it turned out.</summary>
    public double Width => Laid.Size.Width;

    /// <summary>How tall it turned out.</summary>
    public double Height => Laid.Size.Height;

    /// <summary>The room it takes.</summary>
    public Size Taken => Laid.Size;

    /// <summary>
    /// Whether there is anything in it to set down. One that drew nothing came back only to say why — its trouble — and the
    /// characters somebody typed go there instead.
    /// </summary>
    public bool Draws => Laid.Draws;

    /// <summary>Sets it down at <paramref name="at"/>, as a piece of <paramref name="kind"/> holding the whole of it.</summary>
    public void Set(LayoutBuilder build, Point at, string kind)
    {
        build.Open(kind, part: null, stops: Stops.None);
        build.Graft(Laid.Tree, at);
        build.Close();
    }
}
