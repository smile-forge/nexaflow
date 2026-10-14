namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A block's source as the lines it is written on, each knowing where what its containers put in front of it ends.
///
/// <para>
/// <strong>A block is read as it reads where it was written.</strong> A list written inside a list item starts part-way
/// along its first line, and every line after that carries the indentation — or the <c>&gt;</c> — of the containers it
/// sits in. Read as it stands, its second item would be indented past its first and read as a list inside it. So each
/// line says where its own content begins: past the marks its containers put there, which the first line's
/// <paramref name="lead"/> names. Those marks are <em>recorded, not stripped</em> — nothing is copied into a second string,
/// so nothing read from these lines has to be mapped back; what falls between one line's content and the last is the
/// trivia it already is, where it already stands.
/// </para>
/// </summary>
internal sealed class MarkdownLines
{
    /// <summary>How many columns a tab advances to the next multiple of.</summary>
    public const int TabStop = 4;

    private readonly List<MarkdownLine> _lines = [];
    private readonly List<int>? _marks;
    private readonly int _width;

    /// <summary>Where the first line not yet read starts.</summary>
    private int _next;

    private bool _read;

    private MarkdownLines(string text, int from, List<int>? marks, int width)
    {
        this.Text = text;
        _next = from;
        _marks = marks;
        _width = width;
    }

    /// <summary>The characters the lines are cut from.</summary>
    public string Text { get; }

    /// <summary>
    /// <paramref name="text"/> from <paramref name="from"/> on, as lines each with its containers' marks passed over —
    /// read as they are asked for, so a scan that stops early never reads the rest.
    /// </summary>
    /// <param name="lead">
    /// What stands in front of the first line's content — the marks and indentation of the containers holding it — or
    /// nothing for text that starts its line. The first line's content starts where the text does, at the column the lead
    /// reaches; every line after it carries the same marks again, and passes over as many of them as it does carry.
    /// </param>
    /// <param name="from">
    /// Where the first line's content starts — part-way along its line where what is read starts there, at the column the
    /// characters in front of it reach.
    /// </param>
    public static MarkdownLines Of(string text, string? lead = null, int from = 0)
    {
        var width = 0;
        var marks = lead is { Length: > 0 } ? Marks(lead, out width) : null;

        if (marks is null && from > 0)
            for (var at = text.LastIndexOf('\n', from - 1) + 1; at < from; at++)
                width = text[at] == '\t' ? (width / TabStop + 1) * TabStop : width + 1;

        return new MarkdownLines(text, from, marks, width);
    }

    /// <summary>The line counted from nought as <paramref name="index"/>, where there is one — the last is there even where nothing ends it.</summary>
    public bool TryLine(int index, out MarkdownLine line)
    {
        while (_lines.Count <= index && !_read) this.ReadLine();

        line = index < _lines.Count ? _lines[index] : default;
        return index < _lines.Count;
    }

    private void ReadLine()
    {
        var at = _next;
        var feed = this.Text.IndexOf('\n', at);
        var next = feed < 0 ? this.Text.Length : feed + 1;
        var end = feed < 0 ? this.Text.Length : feed > at && this.Text[feed - 1] == '\r' ? feed - 1 : feed;

        _lines.Add(_lines.Count == 0 ? new MarkdownLine(at, at, _width, end, next) : Past(this.Text, at, end, next, _marks));

        if (feed < 0) _read = true;
        else _next = next;
    }

    /// <summary>
    /// What a block's containers put at the head of each of its lines, read off the line it starts on: a quote's
    /// <c>&gt;</c> where one stands, which every line the quote holds carries again, and the width of everything else —
    /// indentation, and a list item's marker, which its later lines carry as that many columns of space.
    /// </summary>
    /// <returns>A quote's marker as -1; anything else as how many columns of space stand for it.</returns>
    private static List<int> Marks(string lead, out int width)
    {
        var marks = new List<int>();
        var column = 0;
        var run = 0;

        foreach (var character in lead)
        {
            if (character == '>')
            {
                if (run > 0) marks.Add(run);
                marks.Add(-1);

                run = 0;
                column++;
                continue;
            }

            var next = character == '\t' ? (column / TabStop + 1) * TabStop : column + 1;
            run += next - column;
            column = next;
        }

        if (run > 0) marks.Add(run);

        width = column;
        return marks;
    }

    /// <summary>
    /// The line from <paramref name="at"/>, its content starting past each quote marker it carries and as much of each run
    /// of indentation as the first line's marks take up. A line that stops carrying them — a lazy continuation — keeps
    /// whatever is left.
    /// </summary>
    private static MarkdownLine Past(string text, int at, int end, int next, List<int>? marks)
    {
        var start = at;
        var column = 0;

        if (marks is null) return new MarkdownLine(start, at, 0, end, next);

        for (var index = 0; index < marks.Count; index++)
        {
            var mark = marks[index];

            if (mark < 0)
            {
                // Up to three spaces may stand in front of a quote's marker.
                var probe = at;
                var spaces = 0;

                while (probe < end && text[probe] == ' ' && spaces < 3)
                {
                    probe++;
                    spaces++;
                }

                if (probe >= end || text[probe] != '>') break;

                column += spaces + 1;
                at = probe + 1;
                continue;
            }

            var goal = column + mark;
            var stopped = false;

            while (at < end && column < goal)
            {
                var step = text[at] switch
                {
                    ' ' => column + 1,
                    '\t' => (column / TabStop + 1) * TabStop,
                    _ => -1,
                };

                // Not indentation, or a tab reaching past what the containers take: the rest is the block's own.
                if (step < 0 || step > goal)
                {
                    stopped = true;
                    break;
                }

                column = step;
                at++;
            }

            // Indentation in front of a quote's marker need not all be there: the marker stands up to three spaces in
            // from wherever the line goes on, and a line that carries less still carries the quote.
            if ((stopped || column < goal) && !(index + 1 < marks.Count && marks[index + 1] < 0)) break;
        }

        return new MarkdownLine(start, at, column, end, next);
    }
}

/// <summary>One line of a block.</summary>
/// <param name="Start">Where the line starts.</param>
/// <param name="Content">Where what the line holds starts, past its containers' marks.</param>
/// <param name="Column">The column <paramref name="Content"/> stands at, which is where a tab after it stops.</param>
/// <param name="End">Where the line's characters end, before its line ending.</param>
/// <param name="Next">Where the next line starts, past the line ending — or the end of the text.</param>
internal readonly record struct MarkdownLine(int Start, int Content, int Column, int End, int Next);
