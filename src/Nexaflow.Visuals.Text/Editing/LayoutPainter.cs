using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Paints a layout tree — every mark on every piece, through whatever each piece has been moved by.
///
/// <para>
/// It never asks what it is walking, which is the point of it. A canvas holding a bar of music beside a
/// fraction beside a paragraph is one tree of pieces that drew marks, so it paints in one descent and no
/// content type has a painter of its own.
/// </para>
/// <para>
/// Descended rather than flattened, because <see cref="ILayoutNode.Offset"/> nests: a piece moved inside
/// something that has itself been moved ends up displaced by both, and a transform stack is what says
/// that once. A tree nobody has moved pushes nothing and paints exactly as a flat walk would.
/// </para>
/// <para>
/// The same walk that answers a hit test, so the picture and the answers cannot disagree about where
/// anything is.
/// </para>
/// </summary>
public static class LayoutPainter
{
    /// <summary>Paints <paramref name="node"/> and everything inside it.</summary>
    public static void Paint(DrawingContext dc, ILayoutNode node, Brush foreground)
    {
        var moved = node.Offset.X != 0 || node.Offset.Y != 0;
        if (moved) dc.PushTransform(new TranslateTransform(node.Offset.X, node.Offset.Y));

        if (node is LayoutNode piece)
            foreach (var mark in piece.Marks) mark.PaintOn(dc, foreground);

        foreach (var child in node.Children) Paint(dc, child, foreground);

        if (moved) dc.Pop();
    }
}
