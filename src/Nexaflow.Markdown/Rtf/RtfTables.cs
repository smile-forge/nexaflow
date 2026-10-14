using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// What a row's definition says of one of its cells: where its right edge is, and whether it is merged with a neighbour.
/// </summary>
internal sealed record RtfCellDefinition
{
    /// <summary>Its right edge in twips (<c>\cellx</c>), or null where none was given.</summary>
    public int? Right { get; set; }

    /// <summary><c>\clmgf</c> — the first of cells merged across.</summary>
    public bool MergesAcross { get; set; }

    /// <summary><c>\clmrg</c> — merged into the cell before it.</summary>
    public bool MergedAcross { get; set; }

    /// <summary><c>\clvmgf</c> — the first of cells merged down.</summary>
    public bool MergesDown { get; set; }

    /// <summary><c>\clvmrg</c> — merged into the cell above it.</summary>
    public bool MergedDown { get; set; }
}

/// <summary>A table as it is read: its rows, and the one being written.</summary>
internal sealed class RtfTableBuild
{
    public List<RtfRowBuild> Rows { get; } = [];

    public RtfRowBuild? Row { get; set; }

    /// <summary>
    /// The table, as markdown's table is: its rows, each of its cells covering as many squares as its definition merged
    /// into it — and a cell merged into another written, but not a cell of its own.
    /// </summary>
    public ContentNode Built()
    {
        var rows = new List<ContentNode>(Rows.Count);

        for (var row = 0; row < Rows.Count; row++)
        {
            var built = Rows[row];
            var parts = new List<ContentNode>(built.Cells.Count + 1);

            for (var at = 0; at < built.Cells.Count; at++)
            {
                var cell = built.Cells[at];
                var defined = built.Defined.ElementAtOrDefault(at);
                var body = ContentNode.Branch(Kinds.Sequence, cell.Blocks, Roles.Body, RtfReading.Begins(cell.Blocks));

                if (defined is { MergedAcross: true } or { MergedDown: true })
                {
                    parts.Add(ContentNode.Branch(RtfKinds.Merged, [body], Roles.Trivia, body.Offset));

                    continue;
                }

                var inside = new List<ContentNode> { body };

                var (across, down) = (Across(built, at), Down(row, at, defined));
                if (across > 1 || down > 1)
                    inside.Add(ContentNode.Holding(MarkdownKinds.Spans, Roles.Derived, new MarkdownSpans(across, down)));

                inside.Add(ContentNode.Holding(MarkdownKinds.Blocks, Roles.Derived, true));

                parts.Add(ContentNode.Branch(MarkdownKinds.Cell, inside, Roles.Cell, body.Offset));
            }

            if (built.Trailing.Count > 0)
                parts.Add(ContentNode.Branch(RtfKinds.Between, built.Trailing, Roles.Trivia, RtfReading.Begins(built.Trailing)));

            rows.Add(ContentNode.Branch(MarkdownKinds.Row, parts, Roles.Row, RtfReading.Begins(parts)));
        }

        return ContentNode.Branch(MarkdownKinds.Table, rows, Roles.Element, RtfReading.Begins(rows));
    }

    /// <summary>How many columns the cell at <paramref name="at"/> covers: itself, and every cell after it merged into it.</summary>
    private static int Across(RtfRowBuild row, int at)
    {
        if (row.Defined.ElementAtOrDefault(at) is not { MergesAcross: true }) return 1;

        var across = 1;
        while (row.Defined.ElementAtOrDefault(at + across) is { MergedAcross: true }) across++;

        return across;
    }

    /// <summary>
    /// How many rows the cell at <paramref name="at"/> of <paramref name="row"/> covers: itself, and the cell under it in
    /// every row below merged into the one above — found by where its right edge is, since a row merged across has fewer
    /// cells than one that is not.
    /// </summary>
    private int Down(int row, int at, RtfCellDefinition? defined)
    {
        if (defined is not { MergesDown: true }) return 1;

        var down = 1;

        for (var below = row + 1; below < Rows.Count; below++)
        {
            var under = defined.Right is { } right
                ? Rows[below].Defined.FirstOrDefault(cell => cell.Right == right)
                : Rows[below].Defined.ElementAtOrDefault(at);

            if (under is not { MergedDown: true }) break;

            down++;
        }

        return down;
    }
}

/// <summary>A row as it is read: its cells, the one being written, and what follows the last of them.</summary>
internal sealed class RtfRowBuild
{
    public List<RtfCellBuild> Cells { get; } = [];

    public RtfCellBuild? Cell { get; set; }

    /// <summary>What follows the last cell — a repeat of the row's definition, and the <c>\row</c> ending it.</summary>
    public List<ContentNode> Trailing { get; } = [];

    /// <summary>Its cells as its definition gave them.</summary>
    public List<RtfCellDefinition> Defined { get; set; } = [];
}

/// <summary>A cell as it is read: the blocks written in it.</summary>
internal sealed class RtfCellBuild
{
    public List<ContentNode> Blocks { get; } = [];
}
