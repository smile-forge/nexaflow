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
/// under the head. A <c>|</c> missing where a column ends makes the cell cover the next column too, a cell can be written
/// over several lines, and one written into the separator under it covers the next row too (<see cref="Grid"/>).
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

        var grid = new Grid(text, from, to);

        for (var line = 1; line < lines.Count; line++)
        {
            var (start, end) = lines[line];

            if (text[start] == '+') grid.Ruled(start, end);
            else grid.Written(start, end, ruled: false);
        }

        grid.Ended();

        // A grid whose cells do not add up to its columns is no table: it is the paragraph its lines are, as Markdig reads it.
        if (!grid.Holds())
        {
            table.Kind = ScannedKind.Paragraph;
            return;
        }

        table.Aligns.AddRange(grid.Aligns);

        foreach (var (cells, head) in grid.Rows)
        {
            var row = new ScannedRow(_written, cells[0].Slices[0].From, head) { IsOpen = false };

            foreach (var written in cells)
            {
                var cell = new ScannedCell(_written, written.Slices[0].From, written.Column)
                {
                    IsOpen = false, Across = written.Across, Down = written.Down,
                };

                foreach (var slice in written.Slices) cell.Lines.Add(slice);

                cell.Reach(written.Slices[^1].To);
                row.Add(cell);
                row.Reach(cell.End);
            }

            table.Add(row);
        }

        table.Lines.Clear();
    }

    [GeneratedRegex(@"^\+(?:[ \t]*:?[ \t]*-+[ \t]*:?[ \t]*\+)+[ \t]*$")]
    private static partial Regex RowSeparator();

    /// <summary>
    /// A grid read the way Markdig reads one, line by line. Its columns are where the first rule's <c>+</c> stand. A line of
    /// <c>|</c> writes into the cells it divides — one covering as many columns as no <c>|</c> stands between — and goes on the
    /// row above it while it divides them the same way; a line dividing them otherwise starts a row. A rule ends the row, but
    /// where something other than the rule's dashes is written in a column, the cell above goes on down through it into the next
    /// row, and what is written there is the cell's. A rule of <c>=</c> makes every row above it the head.
    ///
    /// <para>
    /// A cell is the slice of each line it covers, from past the bar opening it to the bar closing it, the space at its end left
    /// out — its own small document, written over those slices. The last cell of a line written without its closing bar takes
    /// the rest of the line.
    /// </para>
    /// </summary>
    private sealed class Grid
    {
        private readonly string _text;
        private readonly List<Column> _columns = [];

        public Grid(string text, int from, int to)
        {
            _text = text;

            var bounds = new List<int>();
            for (var at = from; at < to; at++)
                if (text[at] == '+') bounds.Add(at - from);

            for (var column = 0; column + 1 < bounds.Count; column++)
            {
                var spec = text.AsSpan(from + bounds[column] + 1, bounds[column + 1] - bounds[column] - 1).Trim(" \t");
                var left = spec.StartsWith(":");
                var right = spec.EndsWith(":") && spec.Length > 1;

                _columns.Add(new Column(bounds[column], bounds[column + 1]));
                this.Aligns.Add(left && right ? MarkdownAligns.Center : right ? MarkdownAligns.Right : left ? MarkdownAligns.Left : null);
            }
        }

        /// <summary>How each column is set, where the first rule says.</summary>
        public List<string?> Aligns { get; } = [];

        /// <summary>The rows, in order, each with the cells starting in it and whether it is the head.</summary>
        public List<(List<Cell> Cells, bool Head)> Rows { get; } = [];

        /// <summary>A rule from <paramref name="from"/> to <paramref name="to"/>: the row above ends, but for any cell going on down through it.</summary>
        public void Ruled(int from, int to)
        {
            var head = this.At(from, to, 1) == '=' || this.At(from, to, 2) == '=';
            var spanning = false;

            foreach (var column in _columns)
            {
                if (column.Cell is not { } cell) continue;

                var start = Math.Min(from + column.Start + 1, to);
                var said = _text.AsSpan(start, Math.Max(Math.Min(from + column.End, to) - start, 0)).Trim(" \t");

                if (said.IsEmpty || !Rule(said))
                {
                    spanning = true;
                    cell.Down++;
                    cell.Closes = false;
                }
                else
                {
                    cell.Closes = true;
                }
            }

            this.Divided(from, to);
            this.Ended(last: false);

            if (head)
                for (var row = 0; row < this.Rows.Count; row++) this.Rows[row] = (this.Rows[row].Cells, true);

            if (spanning) this.Written(from, to, ruled: true);
        }

        /// <summary>
        /// A line from <paramref name="from"/> to <paramref name="to"/> writing into the cells it divides — a rule, where a cell goes on
        /// down through it.
        /// </summary>
        public void Written(int from, int to, bool ruled)
        {
            this.Divided(from, to);

            if (!ruled && !this.Continues()) this.Ended(last: false);

            for (var index = 0; index < _columns.Count;)
            {
                var column = _columns[index];
                var next = index + column.Span;
                if (next == index) break;

                var start = Math.Min(from + column.Start + 1, to);
                int end;

                if (next < _columns.Count)
                {
                    end = from + _columns[next].Start;
                }
                else
                {
                    var closing = _columns[^1].End;
                    var mark = this.At(from, to, closing);

                    end = mark == '|' || (ruled && mark == '+') ? from + closing
                        : to > from && _text[to - 1] == '|' ? to - 1
                        : to;
                }

                end = Math.Clamp(end, start, to);
                while (end > start && _text[end - 1] is ' ' or '\t') end--;

                if (!ruled || !Rule(_text.AsSpan(start, end - start)))
                {
                    column.Cell ??= new Cell(index, column.Span);
                    column.Cell.Slices.Add((start, end));
                }

                index = next;
            }
        }

        /// <summary>The grid is done: whatever rows are open end.</summary>
        public void Ended() => this.Ended(last: true);

        /// <summary>
        /// Whether the cells add up: the grid has a row, no row holds more columns than the grid has — counting the cells coming
        /// down into it from the rows above — and no cell goes down past the last row.
        /// </summary>
        public bool Holds()
        {
            if (this.Rows.Count == 0) return false;

            var widths = new int[this.Rows.Count];

            for (var row = 0; row < this.Rows.Count; row++)
            {
                foreach (var cell in this.Rows[row].Cells)
                {
                    widths[row] += cell.Across;

                    for (var down = 1; down < cell.Down; down++)
                    {
                        if (row + down >= this.Rows.Count) return false;
                        widths[row + down] += cell.Across;
                    }
                }

                if (widths[row] > _columns.Count) return false;
            }

            return true;
        }

        /// <summary>How many columns the cell starting at each column covers on this line: up to the next <c>|</c> or <c>+</c> standing where a column does.</summary>
        private void Divided(int from, int to)
        {
            foreach (var column in _columns)
            {
                column.Before = column.Span;
                column.Span = 0;
            }

            var opening = -1;

            for (var index = 0; index < _columns.Count; index++)
            {
                if (this.At(from, to, _columns[index].Start) is '|' or '+') opening = index;
                if (opening >= 0) _columns[opening].Span++;
            }
        }

        /// <summary>Whether this line divides the columns as the line above it did, and so goes on its row.</summary>
        private bool Continues()
        {
            foreach (var column in _columns)
                if (column.Before != column.Span) return false;

            return true;
        }

        /// <summary>The row being written ends: its cells are gathered, and each that does not go on down is done with.</summary>
        private void Ended(bool last)
        {
            List<Cell>? row = null;

            foreach (var column in _columns)
            {
                if (column.Cell is { } cell)
                {
                    if (!cell.Placed)
                    {
                        (row ??= []).Add(cell);
                        cell.Placed = true;
                    }
                }

                if (last || column.Span == 0 || column.Cell is { Closes: true }) column.Cell = null;
            }

            if (row is { Count: > 0 }) this.Rows.Add((row, false));
        }

        /// <summary>The character <paramref name="offset"/> along the line from <paramref name="from"/>, or nothing past its end.</summary>
        private char At(int from, int to, int offset) => from + offset < to ? _text[from + offset] : '\0';

        /// <summary>Whether a column's stretch of a rule is nothing but the rule: dashes, equals signs and colons.</summary>
        private static bool Rule(ReadOnlySpan<char> said)
        {
            foreach (var character in said)
                if (character is not ('-' or '=' or ':')) return false;

            return true;
        }

        /// <summary>A column: where the <c>+</c> before and after it stand along the line, and the cell being written in it.</summary>
        private sealed class Column(int start, int end)
        {
            public int Start { get; } = start;

            public int End { get; } = end;

            /// <summary>How many columns the cell starting here covers on this line — nought where none starts here.</summary>
            public int Span { get; set; } = -1;

            /// <summary>The same, on the line before.</summary>
            public int Before { get; set; }

            public Cell? Cell { get; set; }
        }
    }

    /// <summary>A cell of a grid: the column it starts in, how many columns and rows it covers, and its slice of each line it covers.</summary>
    private sealed class Cell(int column, int across)
    {
        public int Column { get; } = column;

        public int Across { get; } = across;

        public int Down { get; set; } = 1;

        /// <summary>Whether it ends with the row — false while something is written in it on the rule under it.</summary>
        public bool Closes { get; set; } = true;

        /// <summary>Whether it is in a row already: a cell going on down belongs to the row it started in.</summary>
        public bool Placed { get; set; }

        public List<(int From, int To)> Slices { get; } = [];
    }

    private sealed class WrittenRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}
