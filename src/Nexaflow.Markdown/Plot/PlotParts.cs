using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// Reading a plot block's tree back — the few walks every stage and the builder both want.
/// </summary>
public static class PlotParts
{
    /// <summary>Every row of a block, the header among them, in the order they were written.</summary>
    public static IReadOnlyList<ContentNode> Rows(this ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == PlotKinds.Row)];

    /// <summary>
    /// The cells of a row, in the order they were written — the ones the row holds directly, so the
    /// characters inside a quoted cell are not counted as a cell of their own.
    /// </summary>
    public static IReadOnlyList<ContentNode> Cells(this ContentNode row) =>
        [.. row.Children.Where(child => child.Kind == PlotKinds.Cell)];

    /// <summary>
    /// What a cell says, without the quotes it may have been written in — which is what is drawn,
    /// pressed and typed into.
    /// </summary>
    public static string Says(this ContentNode cell) =>
        cell.IsLeaf
            ? cell.Text
            : cell.Children.FirstOrDefault(child => child.Kind == PlotKinds.Cell && child.IsLeaf)?.Text
              ?? string.Empty;

    /// <summary>Whether a row was worked out to be the one that names the columns.</summary>
    public static bool IsHeader(this ContentNode row) => row is PlotRowNode { Header: true };

    /// <summary>The same row with each of its cells put through <paramref name="of"/>, counted from nought.</summary>
    public static ContentNode WithCells(this ContentNode row, Func<ContentNode, int, ContentNode> of)
    {
        var children = new List<ContentNode>(row.Children);
        var at = 0;

        for (var which = 0; which < children.Count; which++)
            if (children[which].Kind == PlotKinds.Cell)
                children[which] = of(children[which], at++);

        return row.With(children);
    }
}
