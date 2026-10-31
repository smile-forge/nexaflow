using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;

namespace Nexaflow.Markdown.Layout.TreeView;

/// <summary>
/// Which place in a tree view a point on its drawing stands for — read back off the layout <see cref="TreeViewBuilder"/> laid, beside
/// it, so what it knows of how a row is drawn stays in one place.
/// </summary>
internal static class TreeViewPlaces
{
    /// <summary>How far into a row from its top or bottom edge a point still stands for in front of it or after it, rather than under it.</summary>
    private const double Edge = 0.25;

    /// <summary>
    /// Where something let go at <paramref name="at"/>, on <paramref name="piece"/> or what holds it, goes beside the node whose row
    /// that is: the top quarter of the row in front of it, the bottom quarter after it, and between them under it — or null where
    /// the point is on no row.
    /// </summary>
    public static OutlineSide? SideAt(LayoutPiece piece, LayoutPoint at)
    {
        var row = piece.Kind == TreeViewPiece.Row ? piece : piece.Ancestors().FirstOrDefault(up => up.Kind == TreeViewPiece.Row);
        if (!row.Exists || row.Bounds.Height <= 0) return null;

        var down = (at.Y - row.Bounds.Top) / row.Bounds.Height;
        return down < Edge ? OutlineSide.Before : down > 1 - Edge ? OutlineSide.After : OutlineSide.Inside;
    }
}
