using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A table, as the rows and cells it is written as.
///
/// <para>
/// A row is a row and a cell is a cell, named with the roles every language that has a grid already uses —
/// which is what lets a table be laid on the same tree a matrix is laid on, and selected down a column the
/// same way. The pipes and the rule under the head are not part of any cell: they fall between the cells and
/// are kept as trivia, so the table prints back as the characters somebody lined up by hand. A cell is what
/// stands between two pipes, with the space at either end of it left among the trivia — and an empty cell is
/// still a cell, holding nothing where it stands.
/// </para>
/// <para>
/// Which way a column is set is the one thing here that nobody wrote on the cell — the colons that say it
/// are in a row of their own, three lines up. So it is hung on each cell as a derived part, which draws and
/// is not source.
/// </para>
/// </summary>
public static class MarkdownTable
{
    /// <summary><paramref name="source"/> as the rows and cells written in it.</summary>
    /// <param name="at">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    /// <param name="lead">What stands in front of <paramref name="source"/> on the line it starts on — see <see cref="MarkdownLines"/>.</param>
    public static ContentNode Read(string? source, int at = 0, string? lead = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body, at);

        var read = new Cut(text, at);
        var parts = new List<ContentNode>();

        Tables(MarkdownScanner.Scan(MarkdownLines.Of(text, lead), document: false), parts, read);
        read.Gap(parts, read.Length);

        return MarkdownParser.Checked(Kinds.Sequence, parts, text, Roles.Body, at);
    }

    /// <summary>Every table in the reading, wherever it turns up.</summary>
    private static void Tables(ScannedBlock blocks, List<ContentNode> parts, Cut read)
    {
        foreach (var block in blocks.Children)
            if (block is ScannedTable table) Rows(table, parts, read);
            else if (block.Children.Count > 0) Tables(block, parts, read);
    }

    private static void Rows(ScannedTable table, List<ContentNode> parts, Cut read)
    {
        foreach (var block in table.Children)
        {
            if (block is not ScannedRow row) continue;

            var from = Math.Clamp(row.Start, read.At, read.Length);
            var to = read.Closing(row.End, from);
            if (to == from) continue;

            read.Gap(parts, from);

            var inside = new List<ContentNode>();
            Cells(table, row, inside, read, to);

            // The last pipe, and the line ending after it.
            read.Gap(inside, to);

            parts.Add(ContentNode.Branch(MarkdownKinds.Row, inside, row.Head ? MarkdownRoles.Head : Roles.Row));
        }
    }

    private static void Cells(ScannedTable table, ScannedRow row, List<ContentNode> parts, Cut read, int end)
    {
        foreach (var block in row.Children)
        {
            if (block is not ScannedCell cell) continue;

            var from = Math.Clamp(cell.Start, read.At, end);
            var to = Math.Clamp(cell.End, from, end);

            read.Gap(parts, from);

            var stands = read.Here;

            var inside = new List<ContentNode>
            {
                // Held as written: what is in a cell is the same language a paragraph is written in, and
                // reading it is that reader's business.
                ContentNode.Leaf(Kinds.Verbatim, read.Text(to), Roles.Body, offset: stands),
            };

            if (Aligned(table, cell.Column) is { } how) inside.Add(how);
            if (Covers(cell) is { } spans) inside.Add(spans);
            if (cell.Blocks) inside.Add(ContentNode.Holding(MarkdownKinds.Blocks, Roles.Derived, true));

            parts.Add(ContentNode.Branch(MarkdownKinds.Cell, inside, Roles.Cell, stands));
        }
    }

    /// <summary>Which way this cell's column is set, where the rule under the head says anything about it.</summary>
    private static ContentNode? Aligned(ScannedTable table, int column) =>
        column >= 0 && column < table.Aligns.Count && table.Aligns[column] is { } how
            ? ContentNode.Holding(MarkdownKinds.Aligned, Roles.Derived, how)
            : null;

    /// <summary>
    /// How many columns and rows a cell was written to cover, where it covers more than one.
    ///
    /// <para>
    /// Derived, like the alignment and for the same reason: the characters of a cell say nothing about it.
    /// What says it is the shape of the grid drawn round it, which only the scan that took the whole table in has seen.
    /// </para>
    /// </summary>
    private static ContentNode? Covers(ScannedCell cell) =>
        cell.Across <= 1 && cell.Down <= 1
            ? null
            : ContentNode.Holding(MarkdownKinds.Spans, Roles.Derived, new MarkdownSpans(Math.Max(cell.Across, 1), Math.Max(cell.Down, 1)));
}

/// <summary>How much of a grid one cell was written to cover.</summary>
/// <param name="Across">How many columns, counting its own.</param>
/// <param name="Down">How many rows, counting its own.</param>
public sealed record MarkdownSpans(int Across, int Down);
