using System.Windows.Media;
using System;

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
    /// <summary>Paints <paramref name="piece"/> and everything inside it.</summary>
    /// <param name="which">
    /// Which marks to paint, for content that wants something of its own between two layers of itself — a
    /// barcode's selection wash goes over the bars and under the digits, which is where it has always been
    /// drawn. Said as a question about marks rather than as a second tree, because the order things are
    /// painted in is a fact about the drawing and not about the structure. Null paints all of it.
    /// </param>
    public static void Paint(DrawingContext dc, Piece piece, Brush foreground,
                             Predicate<LayoutMark>? which = null)
    {
        if (!piece.Exists) return;

        var offset = piece.Offset;
        var moved = offset.X != 0 || offset.Y != 0;
        if (moved) dc.PushTransform(new TranslateTransform(offset.X, offset.Y));

        foreach (var mark in piece.Marks)
            if (which is null || which(mark)) mark.PaintOn(dc, foreground);

        foreach (var child in piece.Children) Paint(dc, child, foreground, which);

        if (moved) dc.Pop();
    }
}
