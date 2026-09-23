using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A table, as the rows and cells it is written as.
///
/// <para>
/// A row is a row and a cell is a cell, named with the roles every language that has a grid already uses —
/// which is what lets a table be laid on the same tree a matrix is laid on, and selected down a column the
/// same way. The pipes and the rule under the head are not part of any cell: they fall between the spans and
/// are kept as trivia, so the table prints back as the characters somebody lined up by hand.
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
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        try
        {
            Tables(Markdig.Markdown.Parse(Unquoted(text), pipeline ?? MarkdownParser.Pipeline), parts, read);
        }
        catch
        {
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        return MarkdownParser.Checked(Kinds.Sequence, parts, text, Roles.Body);
    }

    /// <summary>
    /// A table written inside a quote carries the quote's marks at the head of every line after its first — the quote's,
    /// not the table's. They are read as the spaces they stand in for, a character for a character, so every cell is still
    /// found where it was written and the marks stay in the source as the trivia they are.
    /// </summary>
    private static string Unquoted(string text) =>
        QuoteMarks.Replace(text, marks => marks.Value.Replace('>', ' '));

    private static readonly Regex QuoteMarks = new(@"^(?:[ \t]*>)+", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Every table in the reading, for the same reason a list's items are gathered wherever they are found: a
    /// table written inside something else does not always read back as a table on its own.
    /// </summary>
    private static void Tables(ContainerBlock blocks, List<ContentNode> parts, Cut read)
    {
        foreach (var block in blocks)
            if (block is Table table) Rows(table, parts, read);
            else if (block is ContainerBlock inner) Tables(inner, parts, read);
    }

    private static void Rows(Table table, List<ContentNode> parts, Cut read)
    {
        foreach (var block in table)
        {
            if (block is not TableRow row) continue;

            var from = read.Starts(row);
            var to = read.Closes(row, from);
            if (to == from) continue;

            read.Gap(parts, from);

            var inside = new List<ContentNode>();
            Cells(table, row, inside, read, to);

            // The last pipe, and the line ending after it.
            read.Gap(inside, to);

            parts.Add(ContentNode.Branch(MarkdownKinds.Row, inside, row.IsHeader ? MarkdownRoles.Head : Roles.Row));
        }
    }

    private static void Cells(Table table, TableRow row, List<ContentNode> parts, Cut read, int end)
    {
        var column = 0;

        foreach (var block in row)
        {
            if (block is not TableCell cell) continue;

            var at = column++;
            if (Where(cell, read, end) is not { } span) continue;

            read.Gap(parts, span.From);

            var inside = new List<ContentNode>
            {
                // Held as written: what is in a cell is the same language a paragraph is written in, and
                // reading it is that reader's business.
                ContentNode.Leaf(Kinds.Verbatim, read.Text(span.To), Roles.Body),
            };

            if (Aligned(table, at) is { } how) inside.Add(how);
            if (Covers(cell) is { } spans) inside.Add(spans);
            if (Blocked(cell)) inside.Add(ContentNode.Holding(MarkdownKinds.Blocks, Roles.Derived, true));

            parts.Add(ContentNode.Branch(MarkdownKinds.Cell, inside, Roles.Cell));

            column += Math.Max(cell.ColumnSpan, 1) - 1;
        }
    }

    /// <summary>
    /// Where a cell's own characters are. A cell is a container, and a container that was handed one
    /// paragraph does not always carry a span of its own — so where it has none, the paragraph inside it
    /// says where the cell is.
    /// </summary>
    private static (int From, int To)? Where(TableCell cell, Cut read, int end)
    {
        var from = read.Starts(cell);
        var to = read.Ends(cell);

        if (to <= from && cell.Count > 0 && cell[0] is { } only)
        {
            from = read.Starts(only);
            to = read.Ends(only);
        }

        to = Math.Min(to, end);

        return to > from ? (from, to) : null;
    }

    /// <summary>Which way this cell's column is set, where the rule under the head says anything about it.</summary>
    private static ContentNode? Aligned(Table table, int column)
    {
        if (column < 0 || column >= table.ColumnDefinitions.Count) return null;

        var how = table.ColumnDefinitions[column].Alignment switch
        {
            TableColumnAlign.Left => MarkdownAligns.Left,
            TableColumnAlign.Center => MarkdownAligns.Center,
            TableColumnAlign.Right => MarkdownAligns.Right,
            _ => null,
        };

        return how is null ? null : ContentNode.Holding(MarkdownKinds.Aligned, Roles.Derived, how);
    }

    /// <summary>
    /// How many columns and rows a cell was written to cover, where it covers more than one.
    ///
    /// <para>
    /// Derived, like the alignment and for the same reason: the characters of a cell say nothing about it.
    /// What says it is the shape of the grid drawn round it, three lines up and two lines down, which only
    /// the reader that took the whole table in has seen.
    /// </para>
    /// </summary>
    private static ContentNode? Covers(TableCell cell)
    {
        var across = Math.Max(cell.ColumnSpan, 1);
        var down = Math.Max(cell.RowSpan, 1);

        return across == 1 && down == 1
            ? null
            : ContentNode.Holding(MarkdownKinds.Spans, Roles.Derived, new MarkdownSpans(across, down));
    }

    /// <summary>
    /// Whether what is written in a cell is blocks rather than a run of words — a list, a quotation, two
    /// paragraphs. A pipe table's cell is always one line of prose; a grid table's can hold a document.
    ///
    /// <para>
    /// Settled here because only the reader that took the table in can tell: the characters of a cell look
    /// the same either way, and which they are depends on the shape of the grid drawn round them.
    /// </para>
    /// </summary>
    private static bool Blocked(TableCell cell) =>
        cell.Count > 1 || (cell.Count == 1 && cell[0] is not ParagraphBlock);
}

/// <summary>How much of a grid one cell was written to cover.</summary>
/// <param name="Across">How many columns, counting its own.</param>
/// <param name="Down">How many rows, counting its own.</param>
public sealed record MarkdownSpans(int Across, int Down);
