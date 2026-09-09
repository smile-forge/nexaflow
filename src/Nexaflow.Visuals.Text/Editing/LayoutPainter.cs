using System.Windows.Media;

using System.Collections.Generic;

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
/// anchor is relative to its parent's. That is what makes a subtree drawable wherever it is put down —
/// and what a transform stack says in one line. A piece anchored at its parent's own origin pushes
/// nothing, so the depth of the stack is the depth of the content rather than the count of the pieces.
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
    public static void Paint(DrawingContext dc, Piece piece, Brush foreground)
    {
        if (!piece.Exists) return;

        Descend(dc, piece, foreground, washes: true);
        Descend(dc, piece, foreground, washes: false);
    }

    /// <summary>One layer of one subtree — see <see cref="Paint"/>, which is both of them.</summary>
    private static void Descend(DrawingContext dc, Piece piece, Brush foreground, bool washes)
    {
        var pushed = Enter(dc, piece);

        foreach (var mark in piece.Marks)
            if (mark is WashMark == washes) mark.PaintOn(dc, foreground);

        foreach (var child in piece.Children) Descend(dc, child, foreground, washes);

        for (var at = 0; at < pushed; at++) dc.Pop();
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

        var pushed = 0;
        for (var at = above.Count - 1; at >= 0; at--) pushed += Enter(dc, above[at]);

        Paint(dc, piece, foreground);

        for (var at = 0; at < pushed; at++) dc.Pop();
    }

    /// <summary>
    /// Puts the drawing context into a piece's own frame, and says how many pushes to undo.
    ///
    /// <para>
    /// Where it sits first, then how it is drawn: a piece turned inside whatever holds it is turned about
    /// its own anchor, which is what makes the turn part of the piece rather than of the page.
    /// </para>
    /// </summary>
    private static int Enter(DrawingContext dc, Piece piece)
    {
        var pushed = 0;

        var offset = piece.Offset;
        if (offset.X != 0 || offset.Y != 0)
        {
            dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
            pushed++;
        }

        if (piece.Painting is not { } painting) return pushed;

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
