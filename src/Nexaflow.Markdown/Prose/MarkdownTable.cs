using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A table, as the rows and cells it is written as.
///
/// <para>
/// A row is a row and a cell is a cell, named with the roles every language that has a grid already uses —
/// which is what lets a table be laid on the same tree a matrix is laid on, and selected down a column the
/// same way. The pipes and the rule under the head are not part of any cell: they fall between the cells and
/// are kept as trivia, so the table prints back as the characters somebody lined up by hand. A cell of a pipe table is what
/// stands between two pipes, with the space at either end of it left among the trivia — and an empty cell is still a cell,
/// holding nothing where it stands.
/// </para>
/// <para>
/// <strong>A cell is read where it stands.</strong> A pipe table's cell is a run of words on its line. A grid table's cell is
/// its slice of every line it covers — over several lines, and down through the rule under it where it covers the next row too
/// — and what is written there is a small document of its own: one paragraph is a run of words, anything else the blocks it
/// is. Its slices are interleaved with the other cells' on the same lines, so it is no one stretch of the source: each of its
/// pieces says where it stands, and everything between them that no cell owns — the pipes, the rules, the line endings — is
/// the table's trivia.
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

    /// <summary>A table the scan found, read where it stands up to <paramref name="to"/> — a table written in a grid table's cell.</summary>
    internal static ContentNode Read(ScannedTable table, Cut read, int to)
    {
        var stands = read.Here;
        int? at = read.Stretches(read.At, to).Skip(1).Any() ? null : stands;
        var parts = new List<ContentNode>();

        Rows(table, parts, read);
        read.Gap(parts, to);

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Body, at);
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
        if (table.Rule is GridTableRule) Grid(table, parts, read);
        else Piped(table, parts, read);
    }

    // ── Pipe tables ──────────────────────────────────────────────────────────

    /// <summary>A pipe table's rows, each a line, and each cell the words on it between two pipes.</summary>
    private static void Piped(ScannedTable table, List<ContentNode> parts, Cut read)
    {
        foreach (var block in table.Children)
        {
            if (block is not ScannedRow row) continue;

            var from = Math.Clamp(row.Start, read.At, read.Length);
            var to = read.Closing(row.End, from);
            if (to == from) continue;

            read.Gap(parts, from);

            var inside = new List<ContentNode>();

            foreach (var held in row.Children)
            {
                if (held is not ScannedCell cell) continue;

                var start = Math.Clamp(cell.Start, read.At, to);
                var end = Math.Clamp(cell.End, start, to);

                read.Gap(inside, start);

                var stands = read.Here;
                inside.Add(Cell(table, cell, MarkdownInline.Words(read, [(start, end)], end), stands));
            }

            // The last pipe, and the line ending after it.
            read.Gap(inside, to);

            parts.Add(ContentNode.Branch(MarkdownKinds.Row, inside, row.Head ? MarkdownRoles.Head : Roles.Row));
        }
    }

    // ── Grid tables ──────────────────────────────────────────────────────────

    /// <summary>
    /// A grid table's rows and cells, and between and round them — wherever no cell's slice stands — the table's trivia, each in
    /// the order it was written.
    /// </summary>
    private static void Grid(ScannedTable table, List<ContentNode> parts, Cut read)
    {
        var placed = new List<(int At, ContentNode Piece)>();
        var claimed = new List<(int From, int To)>();

        foreach (var block in table.Children)
        {
            if (block is not ScannedRow row) continue;

            var cells = new List<ContentNode>();

            foreach (var held in row.Children)
            {
                if (held is not ScannedCell cell) continue;

                var owned = cell.Lines.Where(slice => slice.To > slice.From).ToList();
                var reading = new Cut(read.Source, read.Stands, owned);

                cells.Add(Cell(table, cell, Written(cell, reading), owned.Count == 1 ? read.Stands + owned[0].From : null));
                claimed.AddRange(owned);
            }

            if (cells.Count > 0) placed.Add((row.Start, ContentNode.Branch(MarkdownKinds.Row, cells, row.Head ? MarkdownRoles.Head : Roles.Row)));
        }

        claimed.Sort();

        var at = read.At;
        var trivia = new List<ContentNode>();

        foreach (var (from, to) in claimed)
        {
            if (from > at) Trivia(read, at, from, trivia);
            at = Math.Max(at, to);
        }

        foreach (var piece in trivia) placed.Add((piece.Offset!.Value - read.Stands, piece));

        foreach (var (_, piece) in placed.OrderBy(place => place.At)) parts.Add(piece);

        read.Accounted(at);
    }

    /// <summary>What no cell owns between <paramref name="from"/> and <paramref name="to"/>, of what the table's reading does, as trivia.</summary>
    private static void Trivia(Cut read, int from, int to, List<ContentNode> parts)
    {
        foreach (var (start, end) in read.Stretches(from, to))
        {
            var text = read.Source[start..end];
            parts.Add(ContentNode.Leaf(text.AsSpan().IsWhiteSpace() ? Kinds.Space : Kinds.Token, text, Roles.Trivia, offset: read.Stands + start));
        }
    }

    /// <summary>
    /// What is written in a grid table's cell, read over its slices as the small document it is: a run of words where it is one
    /// paragraph, or nothing at all, and otherwise the blocks it is.
    /// </summary>
    private static ContentNode Written(ScannedCell cell, Cut reading)
    {
        var scanned = MarkdownScanner.Scan(MarkdownLines.Over(reading.Source, cell.Lines), document: false);
        var written = scanned.Children.Where(block => !block.Defines).ToList();

        return written switch
        {
            [] => MarkdownInline.Words(reading, [], reading.Length),
            [{ Kind: ScannedKind.Paragraph } paragraph] when scanned.Children.Count == 1 => MarkdownInline.Words(reading, paragraph.Lines, reading.Length),
            _ => MarkdownParser.Contents(scanned, reading, reading.Length),
        };
    }

    /// <summary>A cell: what is written in it, and what is known of it that nobody wrote on it — its column's alignment, what it covers.</summary>
    private static ContentNode Cell(ScannedTable table, ScannedCell cell, ContentNode written, int? stands)
    {
        var inside = new List<ContentNode> { written };

        if (Aligned(table, cell.Column) is { } how) inside.Add(how);
        if (Covers(cell) is { } spans) inside.Add(spans);

        return ContentNode.Branch(MarkdownKinds.Cell, inside, Roles.Cell, stands);
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
