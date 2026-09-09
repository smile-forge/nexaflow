using System.Windows.Media;
using System;
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
    /// <summary>Paints <paramref name="piece"/> and everything inside it, in its parent's frame.</summary>
    /// <param name="which">
    /// Which marks to paint, for content that wants something of its own between two layers of itself — a
    /// barcode's selection wash goes over the bars and under the digits, and a formula's colour boxes go
    /// under every glyph so one term's wash cannot cover another's letters. Said as a question about marks
    /// rather than as a second tree, because the order things are painted in is a fact about the drawing and
    /// not about the structure. Null paints all of it.
    /// </param>
    public static void Paint(DrawingContext dc, Piece piece, Brush foreground,
                             Predicate<LayoutMark>? which = null)
    {
        if (!piece.Exists) return;

        var pushed = Enter(dc, piece);

        foreach (var mark in piece.Marks)
            if (which is null || which(mark)) mark.PaintOn(dc, foreground);

        foreach (var child in piece.Children) Paint(dc, child, foreground, which);

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
    public static void PaintOne(DrawingContext dc, Piece piece, Brush foreground,
                                Predicate<LayoutMark>? which = null)
    {
        if (!piece.Exists) return;

        var above = new List<Piece>();
        foreach (var up in piece.Ancestors()) above.Add(up);

        var pushed = 0;
        for (var at = above.Count - 1; at >= 0; at--) pushed += Enter(dc, above[at]);

        Paint(dc, piece, foreground, which);

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
