using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using System.Collections.Generic;

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
    /// <param name="letters">
    /// Where each character of the text was written, so that words select the way words select — a letter,
    /// a word, a phrase — rather than all or nothing. There is nothing special about prose here: a typeset
    /// formula has a piece per glyph and always has, which is why its letters could be picked out one by
    /// one while a title could not.
    /// <para>
    /// Handed in rather than worked out. Whether the nth character of what is drawn is the nth character of
    /// what was written is a fact about the content — a label with a prefix, or two fields set as one line,
    /// is neither — and nothing here is in a position to know it. Null draws the run as one piece.
    /// </para>
    /// </param>
    public static LayoutNode Place(LayoutNode into, FormattedText text, Point at, double room,
                                   TextAlignment align, ISourcePart? part, string kind,
                                   IReadOnlyList<ISourcePart>? letters = null)
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

        Letters(node, text, at, letters, kind);
        return node;
    }

    /// <summary>
    /// One piece per character, so the run can be selected through rather than only as a whole.
    ///
    /// <para>
    /// They draw nothing — the run drew all of it in one go, which is what keeps the type engine's kerning
    /// and line breaking intact. These are geometry and a place in the source, which is all a selection
    /// ever asks of a piece.
    /// </para>
    /// </summary>
    private static void Letters(LayoutNode into, FormattedText text, Point at,
                                IReadOnlyList<ISourcePart>? letters, string kind)
    {
        if (letters is null || letters.Count != text.Text.Length) return;

        for (var i = 0; i < letters.Count; i++)
        {
            if (text.BuildHighlightGeometry(at, i, 1) is not { } box) continue;

            var bounds = box.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

            into.Add(new LayoutNode(bounds, kind + "-letter", letters[i]));
        }
    }
}
