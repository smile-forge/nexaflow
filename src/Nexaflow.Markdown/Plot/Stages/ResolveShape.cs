using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// Works out what shape the table is: which row names the columns, and whether the rows are a long list
/// of points or a matrix.
///
/// <para>
/// Neither is in the characters of any one line, which is why neither is the parser's. A row of words is
/// a header only because the rows under it are not, and a matrix is only a matrix because every row is
/// one cell wider than the head of it — both are facts about the table as a whole, and both change as
/// the next line is typed.
/// </para>
/// <para>
/// <strong>A header is the first row no cell of which reads as a number.</strong> That is the rule a
/// reader already has in their head, and it needs no keyword. <c>header:</c> settles the two cases it
/// cannot reach: a table of categories whose first row is words like the rest, and a table of numbers
/// whose columns a reader wants named by position anyway.
/// </para>
/// <para>
/// <strong>A matrix is a header with every row one cell wider than it</strong>, the extra leading cell
/// naming the row. It is how anybody writes a correlation matrix, and reading it needs no setting at all
/// — the shape says it.
/// </para>
/// </summary>
public sealed class ResolveShape(PlotSettings settings) : IAstStage
{
    public string Name => "plot:shape";

    public ContentNode Run(ContentNode tree)
    {
        var rows = tree.Rows();
        if (rows.Count == 0) return tree;

        var header = Heads(rows, settings.Header);
        var matrix = header && LooksLikeMatrix(rows);

        var at = 0;

        return AstRewrite.Each(tree, node => node.Kind switch
        {
            PlotKinds.Row => Said(node, at++, header, matrix),
            PlotKinds.Block => node.Saying(PlotKinds.Fact, PlotRoles.Form, matrix ? Matrix : Long),
            _ => node,
        });
    }

    /// <summary>The table read down its side as well as across it.</summary>
    public const string Matrix = "matrix";

    /// <summary>The table read as one point per row, which is what a table usually is.</summary>
    public const string Long = "long";

    private static ContentNode Said(ContentNode row, int at, bool header, bool matrix)
    {
        if (header && at == 0)
            return row.Saying(PlotKinds.Fact, PlotRoles.Header, PlotNumber.Written(row.Cells().Count));

        if (!matrix) return row;

        // Down the side of a matrix, the leading cell names the row rather than holding a value, and
        // every cell of that row shares it. Said on both, so neither the row nor the cell has to look at
        // the other to know.
        var names = row.Cells() is [var first, ..] ? first.Says() : string.Empty;

        return row.WithCells((cell, which) => which == 0
                                 ? cell.Telling((PlotKinds.Fact, PlotRoles.Names, names))
                                 : cell)
                  .Saying(PlotKinds.Fact, PlotRoles.Names, names);
    }

    /// <summary>
    /// Whether the first row names the columns. Read rather than told, unless it was told.
    /// </summary>
    private static bool Heads(IReadOnlyList<ContentNode> rows, bool? told)
    {
        if (told is { } said) return said;

        var cells = rows[0].Cells();

        return cells.Count > 0 && cells.All(cell => SettingValues.Read(cell.Says()) is null);
    }

    /// <summary>
    /// Whether the rows under the header are a matrix: each one cell wider than the header, with a
    /// leading cell that names it rather than counting as a value.
    /// </summary>
    private static bool LooksLikeMatrix(IReadOnlyList<ContentNode> rows)
    {
        if (rows.Count < 2) return false;

        var wide = rows[0].Cells().Count;
        if (wide == 0) return false;

        foreach (var row in rows.Skip(1))
        {
            var cells = row.Cells();

            if (cells.Count != wide + 1) return false;
            if (SettingValues.Read(cells[0].Says()) is not null) return false;
        }

        return true;
    }
}
