using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Names the columns, and says under each cell which one it stands in.
///
/// <para>
/// A cell knows nothing about its column from its own characters — the name is on a different line, and
/// the position is a matter of counting the cells before it. So it is worked out here and hung
/// underneath, which is what lets everything after this ask a cell what it is rather than work out where
/// it is all over again.
/// </para>
/// <para>
/// A table with no header still has columns: they are numbered, and a cell carries its number with no
/// name. That is what makes the smallest plot — two columns of numbers and nothing else — a plot.
/// </para>
/// <para>
/// Down a matrix the leading cell names its row rather than standing in a column, so the columns start
/// at the cell after it and the header names them all.
/// </para>
/// </summary>
public sealed class ResolveColumns : IAstStage
{
    public string Name => "plot:columns";

    public ContentNode Run(ContentNode tree)
    {
        var rows = tree.Rows();
        if (rows.Count == 0) return tree;

        var matrix = tree.Said(PlotRoles.Form) == ResolveShape.Matrix;
        var header = rows.FirstOrDefault(row => row.IsHeader());
        List<string> names = header is null ? [] : [.. header.Cells().Select(cell => cell.Says())];

        return AstRewrite.Each(tree, node =>
            node.Kind == PlotKinds.Row ? Said(node, names, matrix) : node);
    }

    private static ContentNode Said(ContentNode row, IReadOnlyList<string> names, bool matrix)
    {
        // The header names the columns rather than standing in them, so nothing is hung on its cells:
        // a name is not a value, and a cell saying it stood in the column it names would be a lie the
        // next stage would draw.
        if (row.IsHeader()) return row;

        // Down a matrix the first cell names the row. It carries that already and stands in no column.
        var shift = matrix ? 1 : 0;

        return row.WithCells((cell, at) =>
        {
            if (at < shift) return cell;

            var which = at - shift;

            return which < names.Count
                ? cell.Telling((PlotKinds.Fact, PlotRoles.Index, PlotNumber.Written(which)),
                               (PlotKinds.Fact, PlotRoles.Column, names[which]))
                : cell.Telling((PlotKinds.Fact, PlotRoles.Index, PlotNumber.Written(which)));
        });
    }
}
