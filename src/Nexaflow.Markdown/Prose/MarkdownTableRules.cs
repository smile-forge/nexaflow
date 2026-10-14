using System.Text.RegularExpressions;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A pipe table, found in a paragraph once it is whole: a line holding a column delimiter, under it a line of a column
/// separator — <c>:?-+:?</c> — for each column, and every line after that a row, to the end of the paragraph. Lines above
/// the head stay the paragraph they were.
///
/// <para>
/// A pipe is a column delimiter only where nothing else holds it: escaped, inside a code span, a tag or a link it is that
/// construct's. A pipe at either end of a line, with only space beyond it, opens or closes the row rather than dividing
/// it. A cell is what stands between two delimiters, space at either end left out.
/// </para>
/// </summary>
internal sealed partial class PipeTables : IParagraphReading
{
    private readonly WrittenRule _written = new();

    public void Read(MarkdownScanner scan, ScannedBlock paragraph)
    {
        var text = scan.Text;
        var lines = paragraph.Lines;

        for (var head = 0; head + 1 < lines.Count; head++)
        {
            if (!Shaped(text, lines[head + 1]) || Delimiters(text, lines[head]).Count == 0 || Separator(text, lines[head + 1]) is not { } aligns)
                continue;

            var table = new ScannedTable(_written, Trim(text, lines[head]).From) { Line = paragraph.Line + head, IsOpen = false };
            table.Aligns.AddRange(aligns);

            // The separator is the table's, though it is no row of it.
            table.Reach(Trim(text, lines[head + 1]).To);

            for (var line = head; line < lines.Count; line++)
            {
                if (line == head + 1) continue;

                var row = this.Row(text, lines[line], line == head);
                table.Add(row);
                table.Reach(row.End);

                while (table.Aligns.Count < row.Children.Count) table.Aligns.Add(null);
            }

            var parent = paragraph.Parent!;
            parent.Insert(parent.IndexOf(paragraph) + 1, table);
            parent.Reach(table.End);

            lines.RemoveRange(head, lines.Count - head);
            if (lines.Count > 0) paragraph.End = Trim(text, lines[^1]).To;

            return;
        }
    }

    /// <summary>A line as a row: the cells between its delimiters.</summary>
    private ScannedRow Row(string text, (int From, int To) line, bool head)
    {
        var (from, to) = Trim(text, line);
        var row = new ScannedRow(_written, from, head) { IsOpen = false };
        row.Reach(to);

        var delimiters = Delimiters(text, line);

        // A pipe at the very start opens the row, and one at the very end closes it; the rest divide it.
        var opens = delimiters.Count > 0 && delimiters[0] == from;
        var closes = delimiters.Count > (opens ? 1 : 0) && delimiters[^1] == to - 1;
        var start = opens ? from + 1 : from;
        var column = 0;

        foreach (var delimiter in delimiters.Skip(opens ? 1 : 0).Take(delimiters.Count - (opens ? 1 : 0) - (closes ? 1 : 0)))
        {
            row.Add(Cell(text, start, delimiter, column++));
            start = delimiter + 1;
        }

        row.Add(Cell(text, start, closes ? to - 1 : to, column));

        return row;
    }

    private ScannedCell Cell(string text, int from, int to, int column)
    {
        while (from < to && text[from] is ' ' or '\t') from++;
        while (to > from && text[to - 1] is ' ' or '\t') to--;

        var cell = new ScannedCell(_written, from, column) { IsOpen = false };
        cell.Reach(to);

        return cell;
    }

    /// <summary>
    /// Whether a line is made of nothing but what a separator is made of, a dash among it — asked first, because it costs no
    /// more than a look at each character, and most lines of most paragraphs are not.
    /// </summary>
    private static bool Shaped(string text, (int From, int To) line)
    {
        var dashed = false;

        for (var at = line.From; at < line.To; at++)
        {
            switch (text[at])
            {
                case '-': dashed = true; break;
                case '|' or ':' or ' ' or '\t': break;
                default: return false;
            }
        }

        return dashed;
    }

    /// <summary>The column separators a line is, as each column's alignment — or null where it is not one.</summary>
    private static List<string?>? Separator(string text, (int From, int To) line)
    {
        var (from, to) = Trim(text, line);
        if (from >= to) return null;

        var delimiters = Delimiters(text, line);
        var aligns = new List<string?>();
        var start = from;

        if (delimiters.Count > 0 && delimiters[0] == from)
        {
            start++;
            delimiters.RemoveAt(0);
        }

        if (delimiters.Count > 0 && delimiters[^1] == to - 1)
        {
            to--;
            delimiters.RemoveAt(delimiters.Count - 1);
        }

        foreach (var end in delimiters.Append(to))
        {
            var cell = text.AsSpan(start, end - start).Trim(" \t");
            start = end + 1;

            var left = cell.StartsWith(":");
            var right = cell.EndsWith(":") && cell.Length > 1;
            var dashes = cell[(left ? 1 : 0)..(cell.Length - (right ? 1 : 0))];

            if (dashes.Length == 0 || dashes.ContainsAnyExcept('-')) return null;

            aligns.Add(left && right ? MarkdownAligns.Center : right ? MarkdownAligns.Right : left ? MarkdownAligns.Left : null);
        }

        return aligns;
    }

    /// <summary>
    /// Where on a line the pipes stand that divide it: not escaped, and not inside a code span, a tag, an address in angle
    /// brackets or a link — whose own characters they are.
    /// </summary>
    internal static List<int> Delimiters(string text, (int From, int To) line)
    {
        var found = new List<int>();
        var (at, to) = line;

        while (at < to)
        {
            switch (text[at])
            {
                case '\\':
                    at += 2;
                    continue;

                case '`':
                {
                    var run = at;
                    while (run < to && text[run] == '`') run++;

                    var closing = Closing(text, run, to, run - at);
                    at = closing ?? run;
                    continue;
                }

                case '$' when Formula(text, at, to) is { } past:
                    at = past;
                    continue;

                case '<' when Tag().Match(text, at, to - at) is { Success: true } tag:
                    at += tag.Length;
                    continue;

                case '[' when Linked(text, at, to) is { } past:
                    at = past;
                    continue;

                case '|':
                    found.Add(at);
                    break;
            }

            at++;
        }

        return found;
    }

    /// <summary>Past the run of exactly <paramref name="length"/> backticks closing a code span, where one does on the line.</summary>
    private static int? Closing(string text, int from, int to, int length)
    {
        for (var at = from; at < to;)
        {
            if (text[at] != '`')
            {
                at++;
                continue;
            }

            var run = at;
            while (run < to && text[run] == '`') run++;
            if (run - at == length) return run;

            at = run;
        }

        return null;
    }

    /// <summary>
    /// Past a formula written in a sentence — one or two dollars, something that does not start or end with space, and as many
    /// dollars again, not followed by a digit — where one starts at <paramref name="at"/> on the line.
    /// </summary>
    private static int? Formula(string text, int at, int to)
    {
        var opens = at;
        while (opens < to && text[opens] == '$') opens++;

        var length = opens - at;
        if (length > 2 || opens >= to || char.IsWhiteSpace(text[opens])) return null;

        for (var next = opens; next < to; next++)
        {
            if (text[next] == '\\') { next++; continue; }
            if (text[next] != '$') continue;

            var run = next;
            while (run < to && text[run] == '$') run++;

            if (run - next == length && !char.IsWhiteSpace(text[next - 1]) && (run >= to || !char.IsAsciiDigit(text[run]))) return run;

            next = run - 1;
        }

        return null;
    }

    /// <summary>Past a link — its bracketed words and the destination after them — where one starts at <paramref name="at"/>.</summary>
    private static int? Linked(string text, int at, int to)
    {
        var depth = 0;
        var shut = -1;

        for (var next = at; next < to; next++)
        {
            if (text[next] == '\\') { next++; continue; }
            if (text[next] == '[') depth++;
            else if (text[next] == ']' && --depth == 0) { shut = next; break; }
        }

        if (shut < 0 || shut + 1 >= to || text[shut + 1] != '(') return null;

        var parens = 0;

        for (var next = shut + 1; next < to; next++)
        {
            if (text[next] == '\\') { next++; continue; }
            if (text[next] == '(') parens++;
            else if (text[next] == ')' && --parens == 0) return next + 1;
        }

        return null;
    }

    private static (int From, int To) Trim(string text, (int From, int To) line)
    {
        var (from, to) = line;
        while (from < to && text[from] is ' ' or '\t') from++;
        while (to > from && text[to - 1] is ' ' or '\t') to--;
        return (from, to);
    }

    /// <summary>An opening or closing tag, or an address in angle brackets.</summary>
    [GeneratedRegex("""\G(?:<[A-Za-z][A-Za-z0-9-]*(?:\s+[A-Za-z_:][A-Za-z0-9_.:-]*(?:\s*=\s*(?:[^"'=<>`\s]+|'[^']*'|"[^"]*"))?)*\s*/?>|</[A-Za-z][A-Za-z0-9-]*\s*>|<[A-Za-z][A-Za-z0-9+.-]{1,31}:[^<>\s]*>|<[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*>)""")]
    private static partial Regex Tag();

    private sealed class WrittenRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}

/// <summary>
/// A line under a paragraph that a pipe table's separator could be — dashes, colons, space and at least one pipe — is the
/// paragraph's text, whatever it would start on its own: <c>- | -</c> is a column separator, not a list item.
/// </summary>
internal sealed class PipeSeparatorRule : MarkdownBlockRule
{
    public override bool MayStart(char first) => first is '-' or ':' or '|';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (container.Kind != ScannedKind.Paragraph || scan.Indented) return Started.No;

        var piped = false;
        var dashed = false;

        for (var at = scan.NextNonspace; at < scan.Line.End; at++)
        {
            switch (scan.Text[at])
            {
                case '|': piped = true; break;
                case '-': dashed = true; break;
                case ':' or ' ' or '\t': break;
                default: return Started.No;
            }
        }

        return piped && dashed ? Started.Text : Started.No;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
}

/// <summary>
/// A grid table: a row separator — <c>+</c>, then for each column dashes between optional colons and a <c>+</c> — then rows
/// whose <c>|</c> stand where the separator's <c>+</c> do, separated by more separators; <c>=</c> in place of the dashes
/// under the head. A <c>|</c> missing where a column ends makes the cell cover the next column too.
/// </summary>
internal sealed partial class GridTableRule : MarkdownBlockRule
{
    private readonly WrittenRule _written = new();

    public override bool TakesLines => true;

    public override bool KeepsLines => true;

    public override bool MayStart(char first) => first == '+';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        var at = scan.NextNonspace;
        if (scan.Indented || scan.Peek(at) != '+' || !RowSeparator().IsMatch(scan.Text.AsSpan(at, scan.Line.End - at))) return Started.No;

        scan.Open(new ScannedTable(this, at) { Line = scan.LineIndex });
        scan.AdvanceNextNonspace();

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var first = block.Lines[0].From;
        var column = first - scan.LineAt(block.Line).Start;
        var at = scan.Line.Start + column;

        if (scan.Blank || at >= scan.Line.End || scan.Text[at] is not ('|' or '+')) return Continued.Unmatched;

        scan.AdvanceOffset(at - scan.Offset, columns: false);
        return Continued.Matched;
    }

    public override void Close(MarkdownScanner scan, ScannedBlock block)
    {
        var table = (ScannedTable)block;
        var text = scan.Text;
        var lines = table.Lines;
        var (from, to) = lines[0];

        // Where the first separator's '+' stand is where every column ends, counted from the line's own start.
        var bounds = new List<int>();
        for (var at = from; at < to; at++)
            if (text[at] == '+') bounds.Add(at - from);

        for (var column = 0; column + 1 < bounds.Count; column++)
        {
            var spec = text.AsSpan(from + bounds[column] + 1, bounds[column + 1] - bounds[column] - 1).Trim(" \t");
            var left = spec.StartsWith(":");
            var right = spec.EndsWith(":") && spec.Length > 1;
            table.Aligns.Add(left && right ? MarkdownAligns.Center : right ? MarkdownAligns.Right : left ? MarkdownAligns.Left : null);
        }

        var group = new List<(int From, int To)>();
        var rows = new List<(List<(int From, int To)> Lines, bool Head)>();

        for (var line = 1; line < lines.Count; line++)
        {
            var (start, end) = lines[line];

            if (text[start] == '+')
            {
                if (group.Count > 0) rows.Add((group, false));
                group = [];

                if (text.AsSpan(start, end - start).Contains('=')) rows = [.. rows.Select(row => (row.Lines, true))];
                continue;
            }

            group.Add((start, end));
        }

        if (group.Count > 0) rows.Add((group, false));

        foreach (var (rowLines, head) in rows)
        {
            var (rowFrom, _) = rowLines[0];
            var (_, rowTo) = rowLines[^1];

            var row = new ScannedRow(_written, rowFrom, head) { IsOpen = false };
            row.Reach(Trimmed(text, rowTo, rowFrom));

            // The first of the row's lines says where its cells end: at each bound it has a '|' on.
            var (lineFrom, lineTo) = rowLines[0];
            var ends = bounds.Skip(1).Where(bound => lineFrom + bound < lineTo && text[lineFrom + bound] == '|').ToList();
            if (ends.Count == 0 || ends[^1] != bounds[^1]) ends.Add(bounds[^1]);

            var begins = 0;

            foreach (var ending in ends)
            {
                var columns = bounds.Count(bound => bound > begins && bound <= ending);
                var cell = new ScannedCell(_written, 0, bounds.IndexOf(begins)) { IsOpen = false, Across = Math.Max(columns, 1) };

                var segments = rowLines
                    .Select(segment => (From: Math.Min(segment.From + begins + 1, segment.To), To: Math.Min(segment.From + ending, segment.To)))
                    .ToList();

                cell.Start = FirstWritten(text, segments[0]);
                cell.Reach(Trimmed(text, segments[^1].To, segments[^1].From));


                cell.Blocks = HoldsBlocks(text, segments);
                row.Add(cell);
                begins = ending;
            }

            table.Add(row);
        }

        table.Lines.Clear();
    }

    /// <summary>Whether what is written in a cell's segments is blocks rather than one paragraph.</summary>
    private static bool HoldsBlocks(string text, List<(int From, int To)> segments)
    {
        var written = string.Join('\n', segments.Select(segment => text[segment.From..segment.To].TrimEnd()));
        var read = MarkdownScanner.Scan(MarkdownLines.Of(written), document: false);

        return read.Children.Count > 1 || (read.Children.Count == 1 && read.Children[0].Kind != ScannedKind.Paragraph);
    }

    /// <summary>Where the first character in a segment that is not space stands — or its end, where there is none.</summary>
    private static int FirstWritten(string text, (int From, int To) segment)
    {
        var at = segment.From;
        while (at < segment.To && text[at] is ' ' or '\t') at++;
        return at;
    }

    private static int Trimmed(string text, int to, int from)
    {
        while (to > from && text[to - 1] is ' ' or '\t' or '|') to--;
        return to;
    }

    [GeneratedRegex(@"^\+(?:[ \t]*:?[ \t]*-+[ \t]*:?[ \t]*\+)+[ \t]*$")]
    private static partial Regex RowSeparator();

    private sealed class WrittenRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}
