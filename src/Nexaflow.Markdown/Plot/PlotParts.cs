using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// Reading a plot block's tree back — the few walks every stage and the reader both want.
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
    public static bool IsHeader(this ContentNode row) => row.Said(PlotRoles.Header) is not null;

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

    /// <summary>
    /// The same cell with facts hung underneath it.
    ///
    /// <para>
    /// A leaf holds its characters in itself and a piece with children holds them in its children, so a
    /// fact hung straight onto a bare cell would take the cell's text away with it. The cell is therefore
    /// opened into a piece holding its own characters first — which is the shape a quoted cell already
    /// has, so nothing downstream has two cases to read where it had one.
    /// </para>
    /// </summary>
    public static ContentNode Telling(this ContentNode cell,
                                      params (string Kind, string Role, string Text)[] facts)
    {
        if (facts.Length == 0) return cell;

        var opened = cell.IsLeaf
            ? ContentNode.Branch(cell.Kind, [ContentNode.Leaf(cell.Kind, cell.Text, cell.Role)], cell.Role)
            : cell;

        return opened.Saying(facts);
    }

    /// <summary>
    /// Every fact of <paramref name="role"/> hung under this piece, in the order they were hung.
    ///
    /// <para>
    /// <c>Said</c> answers with the first, which is all a piece with one answer needs. A cell whose column
    /// feeds both colour and shape has two, and dropping the second would silently pick one.
    /// </para>
    /// </summary>
    public static IEnumerable<ContentNode> Facts(this ContentNode node, string role)
    {
        foreach (var child in node.Children)
        {
            if (child.Role != Roles.Derived) continue;

            foreach (var inner in child.Children)
                if (inner.Role == role) yield return inner;
        }
    }
}
