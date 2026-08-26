using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a drag from one piece of content to another selected: a set of whole nodes, and the source ranges
/// they stand for.
/// <para>
/// A set rather than a range, and more than one range, because a selection is not always a run of text. A
/// column of a matrix is three cells that are nowhere near each other in the source, and it is a perfectly
/// ordinary thing to want. Whole nodes rather than offsets, because a node's range is what the parser
/// built it from — so what you copy, replace or drag away is well formed by construction rather than by
/// counting braces afterwards.
/// </para>
/// </summary>
public sealed class ContentSelection
{
    private ContentSelection(IReadOnlyList<ILayoutNode> nodes, IReadOnlyList<(int Start, int Length)>? ranges = null)
    {
        Nodes = nodes;
        Ranges = ranges ?? LayoutQuery.Ranges(nodes);
    }

    /// <summary>The selected pieces, outermost, in reading order.</summary>
    public IReadOnlyList<ILayoutNode> Nodes { get; }

    /// <summary>The stretches of source they stand for, in order and never overlapping.</summary>
    public IReadOnlyList<(int Start, int Length)> Ranges { get; }

    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Nothing selected.</summary>
    public static ContentSelection None { get; } = new([]);

    /// <summary>
    /// What was selected by dragging from <paramref name="anchor"/> to <paramref name="focus"/>.
    /// <para>
    /// Inside a grid the answer is a block of cells — down a column gives the column, across gives the
    /// row, corner to corner gives everything between, exactly as it would if the cells were a sheet.
    /// Anywhere else it is the run from one to the other, grown out to whole constructs.
    /// </para>
    /// </summary>
    public static ContentSelection Between(ILayoutNode root, ILayoutNode? anchor, ILayoutNode? focus)
    {
        if (anchor is null || focus is null) return None;

        if (SharedGrid(anchor, focus) is { } grid)
            return Block(grid, anchor, focus);

        var from = System.Math.Min(anchor.SourceStart, focus.SourceStart);
        var to = System.Math.Max(anchor.SourceEnd(), focus.SourceEnd());

        var touched = root.Ink().Where(n => n.SourceStart >= from && n.SourceEnd() <= to).ToList();
        return touched.Count == 0 ? None : new ContentSelection(LayoutQuery.Promote(touched));
    }

    /// <summary>One whole node, as a selection.</summary>
    public static ContentSelection Of(ILayoutNode? node) => node is null ? None : new([node]);

    /// <summary>
    /// The innermost grid holding both ends. Innermost so a matrix inside a matrix behaves like the one
    /// you are actually pointing into.
    /// </summary>
    private static ILayoutNode? SharedGrid(ILayoutNode anchor, ILayoutNode focus) =>
        anchor.Ancestors().FirstOrDefault(a =>
            a.SelfAndDescendants().Contains(focus) && a.Grid().Count > 0);

    private static ContentSelection Block(ILayoutNode grid, ILayoutNode anchor, ILayoutNode focus)
    {
        var cells = grid.Grid();
        var (fromRow, fromColumn) = Locate(cells, anchor);
        var (toRow, toColumn) = Locate(cells, focus);
        if (fromRow < 0 || toRow < 0) return None;

        var (top, bottom) = fromRow <= toRow ? (fromRow, toRow) : (toRow, fromRow);
        var (left, right) = fromColumn <= toColumn ? (fromColumn, toColumn) : (toColumn, fromColumn);

        var block = new List<ILayoutNode>();
        var ranges = new List<(int Start, int Length)>();

        for (var row = top; row <= bottom; row++)
        {
            // A cell is a container the typesetter wrapped the content in, and may name nothing itself;
            // what stands for it in the source is whatever it holds.
            var ink = Enumerable.Range(left, right - left + 1)
                .Where(column => column < cells[row].Count)
                .SelectMany(column => cells[row][column].Ink())
                .ToList();
            if (ink.Count == 0) continue;

            block.AddRange(ink);

            // One range per row, from its first selected cell to its last — separators included. Cells
            // chosen as a block are adjacent by construction, so everything between two of them is the
            // grid's own punctuation and belongs to the selection. Taking each cell separately instead
            // would leave a selected row reading as three selected digits with the `&` between them
            // conspicuously unselected.
            var start = ink.Min(n => n.SourceStart);
            ranges.Add((start, ink.Max(n => n.SourceEnd()) - start));
        }

        return new ContentSelection([.. LayoutQuery.Promote(block)], LayoutQuery.Merge(ranges));
    }

    private static (int Row, int Column) Locate(
        IReadOnlyList<IReadOnlyList<ILayoutNode>> cells, ILayoutNode node)
    {
        for (var row = 0; row < cells.Count; row++)
            for (var column = 0; column < cells[row].Count; column++)
                if (cells[row][column] == node || node.Ancestors().Contains(cells[row][column]))
                    return (row, column);

        return (-1, -1);
    }
}
