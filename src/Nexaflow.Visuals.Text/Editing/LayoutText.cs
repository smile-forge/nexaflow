using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Words as pieces of a layout tree, and a tree of any kind painted onto any surface.
///
/// <para>
/// Neither of these is about music, maths or diagrams, which is the whole point of them being here. A
/// layout tree is a canvas of things that were drawn, each naming the source it was drawn from, and what
/// kind of thing it is has never mattered to anything that selects, hit-tests or measures. So a title
/// over a tune, a caption under a diagram and the prose between two formulae are one job done once —
/// and a canvas holding a bar of music beside a fraction beside a paragraph paints in a single walk,
/// because the walk was never asked what it was walking.
/// </para>
/// <para>
/// It arrived the other way round, which is worth remembering. The first version of this lived inside
/// the score builder, as a music engraver that had learnt to draw a title. Everything in it that was
/// really about music — where a title goes relative to a staff, that verses set in two columns — stayed
/// there; everything else was general and had no business being in one content type.
/// </para>
/// </summary>
public static class LayoutText
{
    /// <summary>
    /// Places one run of text and hands back the piece it became.
    ///
    /// <para>
    /// The text is aligned within <paramref name="room"/> by the type engine, which is also what breaks a
    /// long run into lines — a title too wide for its page is a paragraph, and how much room it takes is
    /// not known until it has been broken. But the <em>piece</em> is the letters rather than the column
    /// they were aligned in, so its bounds are where the words actually landed. That is what a reader
    /// drags across and what a wash covers, and a centred title whose bounds were the whole page would
    /// highlight the margins either side of itself.
    /// </para>
    /// </summary>
    /// <param name="part">
    /// The source this text was written in. Without one the piece is drawn and cannot be selected, which
    /// is the right answer for a label the content invented and the wrong one for anything a reader typed.
    /// </param>
    public static LayoutNode Place(LayoutNode into, FormattedText text, Point at, double room,
                                   TextAlignment align, ISourcePart? part, string kind)
    {
        text.MaxTextWidth = System.Math.Max(1, room);
        text.TextAlignment = align;

        var x = align switch
        {
            TextAlignment.Center => at.X + ((room - text.Width) / 2),
            TextAlignment.Right => at.X + room - text.Width,
            _ => at.X,
        };

        var node = new LayoutNode(new Rect(x, at.Y, text.Width, text.Height), part, kind,
                                  isInk: part is { Length: > 0 });

        into.Add(node);
        node.Drew(new TextMark(text, at, null));
        return node;
    }
}
