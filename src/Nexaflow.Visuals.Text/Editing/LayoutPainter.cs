using System.Windows.Media;

using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Paints a layout tree — every mark of every piece, in the frame that piece was measured in.
///
/// <para>
/// It never asks what it is walking, which is the point of it. A canvas holding a bar of music beside a
/// fraction beside a paragraph is one tree of pieces that drew marks, so it paints in one descent and no
/// content type has a painter of its own.
/// </para>
/// <para>
/// Descended rather than flattened, because a mark is recorded relative to its own piece's anchor and an
/// anchor is relative to its parent's. That is what makes a subtree drawable wherever it is put down. Where each
/// piece stands is carried down the descent and handed to its marks rather than pushed as a transform, so a picture
/// being kept is marks rather than a group for every piece; only a piece drawn in a frame of its own — turned, or
/// snapped to a pixel grid — pushes one.
/// </para>
/// <para>
/// The same relationship that answers a hit test, so the picture and the answers cannot disagree about
/// where anything is.
/// </para>
/// </summary>
public static class LayoutPainter
{
    /// <summary>
    /// Paints <paramref name="piece"/> and everything inside it, in its parent's frame.
    ///
    /// <para>
    /// In two descents, always: every <see cref="WashMark"/> in the tree goes down, and then everything
    /// else over it. A wash is a background — an opaque colour box a formula asked for behind one of its
    /// own terms — so it has to be under the glyphs of every term, not merely under its own, and boxes
    /// overlap. That is the only ordering question a drawing asks, and it is answered by what a mark
    /// <em>is</em>.
    /// </para>
    /// <para>
    /// <strong>Settled here, and not asked of the content.</strong> This took a predicate saying which marks
    /// to paint, so a caller could paint itself in layers of its own choosing — and three callers chose three
    /// different orders, which is three painters wearing one signature.
    /// </para>
    /// <para>
    /// A <em>selection</em> is not one of the layers. It is translucent and it goes on last, over everything,
    /// where it tints what it marks instead of sitting behind it — which is why the barcode no longer needs
    /// its digits painted in a pass of their own.
    /// </para>
    /// </summary>
    /// <param name="showing">
    /// The part of the page on screen, where only that is being looked at: a piece keeping a picture is painted only near
    /// it (<see cref="Around"/>), and one far from it lets its picture go — so a long document costs a screen of pictures
    /// however long it is. Null paints everything.
    /// </param>
    public static void Paint(DrawingContext dc, Piece piece, Brush foreground, Rect? showing = null)
    {
        if (!piece.Exists) return;

        var near = showing is { } shown ? Around(shown) : (Rect?)null;
        var far = showing is { } seen ? Beyond(seen) : (Rect?)null;

        Descend(dc, piece, foreground, washes: true, default, near, far);
        Descend(dc, piece, foreground, washes: false, default, near, far);
    }

    /// <summary>
    /// What is painted round the part of the page on screen: a screen's worth above and below it, and the whole width, so a
    /// little scrolling shows what is already painted rather than asking for another paint.
    /// </summary>
    public static Rect Around(Rect showing) =>
        new(-Wide, showing.Y - showing.Height, Wide * 2, showing.Height * 3);

    /// <summary>How far from what is shown a piece has to be before the picture kept of it is let go.</summary>
    private static Rect Beyond(Rect showing) =>
        new(-Wide, showing.Y - (showing.Height * 4), Wide * 2, showing.Height * 9);

    /// <summary>Wider than any page, and finite: a rectangle running to infinity has no right-hand edge to be inside.</summary>
    private const double Wide = 1e9;

    /// <summary>One layer of one subtree — see <see cref="Paint"/>, which is both of them.</summary>
    /// <param name="offset">Where the frame <paramref name="piece"/> is measured in stands, against what was last pushed.</param>
    /// <param name="near">What is painted of the pieces keeping pictures, on the page — null for all of them.</param>
    /// <param name="far">Past this, a piece's kept picture is let go.</param>
    private static void Descend(DrawingContext dc, Piece piece, Brush foreground, bool washes, Vector offset,
                                Rect? near = null, Rect? far = null)
    {
        offset += piece.Offset;
        var pushed = Enter(dc, piece, ref offset);

        // Inside a frame of its own the offset is no longer where on the page anything is, so nothing inside is left out.
        if (pushed > 0) near = far = null;

        // A piece that keeps its picture is painted whole, both layers, when the washes go down: what keeps a picture is a
        // block, and nothing of any other block is between its washes and its ink.
        if (piece.Painting?.Kept is { } kept)
        {
            if (washes)
            {
                var box = piece.Box.IsEmpty ? Rect.Empty : Rect.Offset(piece.Box, offset);

                if (near is not { } painted || painted.IntersectsWith(box))
                    Recorded(dc, kept.For(foreground, into => Inside(into, piece, foreground)), offset);
                else if (far is { } beyond && !beyond.IntersectsWith(box))
                    kept.Forget();
            }
        }
        else
        {
            foreach (var mark in piece.Marks)
                if (mark is WashMark == washes) mark.PaintOn(dc, foreground, offset);

            foreach (var child in piece.Children) Descend(dc, child, foreground, washes, offset, near, far);
        }

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    /// <summary>Everything a piece holds, both layers, in the piece's own frame — what a kept picture is recorded from.</summary>
    private static void Inside(DrawingContext dc, Piece piece, Brush foreground)
    {
        foreach (var mark in piece.Marks)
            if (mark is WashMark) mark.PaintOn(dc, foreground, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: true, default);

        foreach (var mark in piece.Marks)
            if (mark is not WashMark) mark.PaintOn(dc, foreground, default);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes: false, default);
    }

    /// <summary>A kept picture, set down where its piece stands.</summary>
    private static void Recorded(DrawingContext dc, Drawing picture, Vector offset)
    {
        if (offset is { X: 0, Y: 0 })
        {
            dc.DrawDrawing(picture);

            return;
        }

        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        dc.DrawDrawing(picture);
        dc.Pop();
    }

    /// <summary>
    /// Paints one piece where it sits on the page, rather than where it sits in its parent.
    ///
    /// <para>
    /// For a caller redrawing part of a tree on its own — a caret blink, a term-by-term reveal — which is
    /// what makes repainting a formula affordable without caching the whole of it as one drawing. Everything
    /// holding it is entered first and draws nothing, because a piece's place is relative and its frame is
    /// therefore the thing above it.
    /// </para>
    /// </summary>
    public static void PaintOne(DrawingContext dc, Piece piece, Brush foreground)
    {
        if (!piece.Exists) return;

        var above = new List<Piece>();
        foreach (var up in piece.Ancestors()) above.Add(up);

        var offset = default(Vector);
        var pushed = 0;

        for (var at = above.Count - 1; at >= 0; at--)
        {
            offset += above[at].Offset;
            pushed += Enter(dc, above[at], ref offset);
        }

        Descend(dc, piece, foreground, washes: true, offset);
        Descend(dc, piece, foreground, washes: false, offset);

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    /// <summary>
    /// Puts the drawing context into a piece's own frame where it is drawn in one — turned, or snapped to a pixel grid — and
    /// says how many pushes to undo.
    ///
    /// <para>
    /// Where it sits first, then how it is drawn: a piece turned inside whatever holds it is turned about its own anchor,
    /// which is what makes the turn part of the piece rather than of the page. So what has built up of where it stands is
    /// pushed before the turn, and inside it everything is measured from nothing again. Any other piece pushes nothing:
    /// where it stands is carried down to its marks.
    /// </para>
    /// </summary>
    private static int Enter(DrawingContext dc, Piece piece, ref Vector offset)
    {
        if (piece.Painting is not { } painting || (painting.Turn is not { Count: > 0 } && painting.Snap is null)) return 0;

        var pushed = 0;

        if (offset is not { X: 0, Y: 0 })
        {
            dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
            pushed++;
            offset = default;
        }

        if (painting.Turn is { } turns)
            foreach (var turn in turns)
            {
                dc.PushTransform(turn);
                pushed++;
            }

        if (painting.Snap is { } snap)
        {
            dc.PushGuidelineSet(snap);
            pushed++;
        }

        return pushed;
    }
}
