namespace Nexaflow.Markdown.Prose;

/// <summary>What everything is written in: it goes on into every line, and holds whatever can stand among blocks.</summary>
internal sealed class DocumentRule : MarkdownBlockRule
{
    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Matched;

    public override bool Holds(ScannedKind kind) => Flows(kind);
}

/// <summary>
/// A run of text with a blank line either side of it: what every line no other rule claims becomes. Closing it is where
/// it can turn out to have been something else — link definitions at its head, a table — which its readings say
/// (<see cref="IParagraphReading"/>).
/// </summary>
internal sealed class ParagraphRule(IReadOnlyList<IParagraphReading> readings) : MarkdownBlockRule
{
    public override bool TakesLines => true;

    public override bool KeepsLines => true;

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => scan.Blank ? Continued.Unmatched : Continued.Matched;

    public override void Close(MarkdownScanner scan, ScannedBlock block)
    {
        foreach (var reading in readings)
        {
            reading.Read(scan, block);
            if (block.Lines.Count == 0) break;
        }

        // A paragraph that was nothing but what its readings took is no paragraph.
        if (block.Lines.Count == 0 && block.Parent is not null) scan.Remove(block);
    }
}

/// <summary>What closing a paragraph can find it was — taken off its head, or made of the whole of it.</summary>
internal interface IParagraphReading
{
    /// <summary>
    /// Reads <paramref name="paragraph"/> again now it is whole: what this finds is put in its place or in front of it, and
    /// what is left of the paragraph stays — which may be none of it, when it is taken out of what held it.
    /// </summary>
    void Read(MarkdownScanner scan, ScannedBlock paragraph);
}

/// <summary>
/// A quote, or anything else every line of which carries a mark in front of what it holds — a footer's <c>^^</c>. A line
/// without the mark ends it, but for a paragraph's lazy continuation.
/// </summary>
internal class QuoteRule(string mark, ScannedKind kind) : MarkdownBlockRule
{
    public QuoteRule() : this(">", ScannedKind.Quote)
    {
    }

    public override bool MayStart(char first) => first == mark[0];

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented || !this.Marked(scan)) return Started.No;

        var at = scan.NextNonspace;
        this.Pass(scan);

        scan.Open(new ScannedQuote(this, kind, at) { Line = scan.LineIndex }).Reach(at + mark.Length);
        return Started.Container;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        if (scan.Indented || !this.Marked(scan)) return Continued.Unmatched;

        var at = scan.NextNonspace;
        this.Pass(scan);
        block.Reach(at + mark.Length);

        return Continued.Matched;
    }

    public override bool Holds(ScannedKind child) => Flows(child);

    /// <summary>Whether the mark stands next on the line — and, for a mark that is a run, not the start of a longer one.</summary>
    private bool Marked(MarkdownScanner scan)
    {
        var at = scan.NextNonspace;

        for (var index = 0; index < mark.Length; index++)
            if (scan.Peek(at + index) != mark[index]) return false;

        return mark.Length == 1 || scan.Peek(at + mark.Length) != mark[^1];
    }

    /// <summary>Moves past the mark and the one space after it that belongs to it.</summary>
    private void Pass(MarkdownScanner scan)
    {
        scan.AdvanceNextNonspace();
        scan.AdvanceOffset(mark.Length, columns: false);

        if (scan.Peek(scan.Offset) is ' ' or '\t') scan.AdvanceOffset(1, columns: true);
    }
}

/// <summary><c># Title</c>: one line, as deep as the hashes that open it.</summary>
internal sealed class HeadingRule : MarkdownBlockRule
{
    public override bool MayStart(char first) => first == '#';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented || scan.Peek(scan.NextNonspace) != '#') return Started.No;

        var at = scan.NextNonspace;
        var hashes = 0;
        while (scan.Peek(at + hashes) == '#') hashes++;

        if (hashes > 6 || scan.Peek(at + hashes) is not (' ' or '\t' or '\0')) return Started.No;

        scan.AdvanceNextNonspace();
        var heading = scan.Open(new ScannedHeading(this, at, hashes) { Line = scan.LineIndex });
        heading.Reach(scan.Trimmed(at));

        if (Worded(scan, at + hashes) is { } words) heading.Lines.Add(words);

        scan.AdvanceToEnd();

        return Started.Leaf;
    }

    /// <summary>
    /// Where a heading's words stand on its line: past the hashes and the space after them, and short of a closing run of
    /// hashes with space in front of it and the space round that — or null where it says nothing at all.
    /// </summary>
    private static (int From, int To)? Worded(MarkdownScanner scan, int after)
    {
        var from = after;
        while (from < scan.Line.End && scan.Text[from] is ' ' or '\t') from++;

        var to = scan.Trimmed(from);
        var closing = to;
        while (closing > from && scan.Text[closing - 1] == '#') closing--;

        if (closing == from)
        {
            to = from;
        }
        else if (closing < to && scan.Text[closing - 1] is ' ' or '\t')
        {
            to = closing;
            while (to > from && scan.Text[to - 1] is ' ' or '\t') to--;
        }

        return to > from ? (from, to) : null;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
}

/// <summary>
/// A line of <c>=</c> or <c>-</c> under a paragraph, which makes the paragraph a heading — once any link definitions at
/// its head are taken off, and only if anything is left.
/// </summary>
internal sealed class UnderlineRule(IParagraphReading definitions) : MarkdownBlockRule
{
    public override bool MayStart(char first) => first is '=' or '-';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented || container.Kind != ScannedKind.Paragraph) return Started.No;

        var at = scan.NextNonspace;
        var mark = scan.Peek(at);
        if (mark is not ('=' or '-')) return Started.No;

        var run = at;
        while (scan.Peek(run) == mark) run++;
        if (scan.Trimmed(run) != run) return Started.No;

        scan.CloseUnmatched();
        definitions.Read(scan, container);
        if (container.Lines.Count == 0) return Started.No;

        var heading = new ScannedHeading(this, container.Start, mark == '=' ? 1 : 2) { Line = container.Line };
        scan.Replace(container, heading);
        heading.Reach(run);

        // Its words are the paragraph's, on the lines the paragraph was written on.
        heading.Lines.AddRange(container.Lines);
        scan.AdvanceToEnd();

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
}

/// <summary><c>***</c>, <c>---</c>, <c>___</c>: three or more of one, with only space among them.</summary>
internal sealed class RuleRule : MarkdownBlockRule
{
    public override bool MayStart(char first) => first is '*' or '-' or '_';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented || !IsRule(scan, scan.NextNonspace)) return Started.No;

        var at = scan.NextNonspace;
        scan.Open(new ScannedBlock(this, ScannedKind.Rule, at) { Line = scan.LineIndex }).Reach(scan.Trimmed(at));
        scan.AdvanceToEnd();

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;

    /// <summary>Whether the line from <paramref name="at"/> is a rule.</summary>
    public static bool IsRule(MarkdownScanner scan, int at)
    {
        var mark = scan.Peek(at);
        if (mark is not ('*' or '-' or '_')) return false;

        var count = 0;

        for (; at < scan.Line.End; at++)
        {
            var character = scan.Text[at];
            if (character == mark) count++;
            else if (character is not (' ' or '\t')) return false;
        }

        return count >= 3;
    }
}

/// <summary>
/// A fence: three or more backticks or tildes — or, for another kind, another mark and how many make one — the word
/// naming its language, and everything up to a fence at least as long as the one that opened it.
/// </summary>
internal sealed class FenceRule(ScannedKind kind, string marks, int least, int most = int.MaxValue) : MarkdownBlockRule
{
    public FenceRule() : this(ScannedKind.Fence, "`~", 3)
    {
    }

    public override bool TakesLines => true;

    /// <summary>Where each line written inside starts past its containers' marks and the fence's own indentation — what the body is.</summary>
    public override bool KeepsLines => true;

    public override bool MayStart(char first) => marks.Contains(first);

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        var at = scan.NextNonspace;
        var mark = scan.Peek(at);
        if (scan.Indented || !marks.Contains(mark)) return Started.No;

        var length = 0;
        while (scan.Peek(at + length) == mark) length++;
        if (length < least || length > most) return Started.No;

        // What follows a run of backticks cannot hold one, or the run would be code in a sentence — and the same of dollars,
        // or the line is a formula written in one.
        if (mark is '`' or '$' && scan.Text.AsSpan(at + length, scan.Line.End - at - length).Contains(mark)) return Started.No;

        var indent = scan.Indent;
        scan.AdvanceNextNonspace();

        var fence = scan.Open(new ScannedFence(this, kind, at, mark, length, indent) { Line = scan.LineIndex });
        fence.Language = Named(scan.Text, at + length, scan.Line.End);
        fence.Reach(scan.Trimmed(at));
        scan.AdvanceToEnd();

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var fence = (ScannedFence)block;
        var line = scan.Offset;
        var at = scan.NextNonspace;

        if (!scan.Indented && scan.Peek(at) == fence.Mark)
        {
            var run = at;
            while (scan.Peek(run) == fence.Mark) run++;

            if (run - at >= fence.Length && scan.Trimmed(run) == run)
            {
                fence.Closed = at;
                fence.Shut = line;
                fence.Reach(run);
                fence.LastLine = scan.LineIndex;
                fence.LastWritten = scan.LineIndex;
                scan.Close(fence);
                return Continued.Consumed;
            }
        }

        // Each line inside gives up as much of the fence's own indentation as it has.
        for (var spaces = fence.Indent; spaces > 0 && scan.Peek(scan.Offset) is ' ' or '\t'; spaces--) scan.AdvanceOffset(1, columns: true);

        return Continued.Matched;
    }

    /// <summary>A fence nobody closed runs on to the end of what holds it, blank lines and all: they are what was written in it.</summary>
    public override void Close(MarkdownScanner scan, ScannedBlock block)
    {
        if (((ScannedFence)block).Closed is null && block.Taken is { } last) block.Reach(last.To);
    }

    /// <summary>
    /// The language the word after a fence names: its first word, as it means rather than as it is spelled — a backslash
    /// taken off the character it escapes, an entity read as the character it stands for.
    /// </summary>
    internal static string Named(string text, int from, int to)
    {
        while (from < to && text[from] is ' ' or '\t') from++;

        var end = from;
        while (end < to && text[end] is not (' ' or '\t')) end++;

        if (end == from) return string.Empty;

        return MarkdownLinkSyntax.Unescaped(text[from..end]);
    }
}

/// <summary>Four columns of indentation, not under a paragraph: code, up to the last line that is not blank.</summary>
internal sealed class IndentedCodeRule : MarkdownBlockRule
{
    public override bool TakesLines => true;

    /// <summary>Where each line's code starts, past its containers' marks and the four columns that made it code.</summary>
    public override bool KeepsLines => true;

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (!scan.Indented || scan.Blank || scan.Tip.Kind == ScannedKind.Paragraph) return Started.No;

        var at = scan.Offset;
        scan.AdvanceOffset(4, columns: true);
        scan.Open(new ScannedBlock(this, ScannedKind.IndentedCode, at) { Line = scan.LineIndex });

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        if (scan.Indented)
        {
            scan.AdvanceOffset(4, columns: true);
            return Continued.Matched;
        }

        if (!scan.Blank) return Continued.Unmatched;

        scan.AdvanceNextNonspace();
        return Continued.Matched;
    }
}

/// <summary>
/// A list item, and the list it starts or goes on — a bullet, or an ordered item's number and the character after it —
/// and with <paramref name="lettered"/> an ordered item's letter or roman numeral too.
/// </summary>
internal sealed class ItemRule(bool lettered) : MarkdownBlockRule
{
    private readonly ListRule _list = new();

    public override bool MayStart(char first) =>
        first is '*' or '+' or '-' || char.IsAsciiDigit(first) || (lettered && char.IsAsciiLetter(first));

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented && container.Kind != ScannedKind.List) return Started.No;
        if (scan.Indent >= 4) return Started.No;

        var at = scan.NextNonspace;
        var pending = (container as ScannedList)?.Marker;

        if (Marked(scan, at, pending, out var marker, out var length) is false) return Started.No;

        var after = scan.Peek(at + length);
        if (after is not (' ' or '\t' or '\0')) return Started.No;

        // An item interrupting a paragraph has to hold something, and an ordered one has to start at the start.
        if (container.Kind == ScannedKind.Paragraph)
        {
            if (scan.Trimmed(at + length) == at + length) return Started.No;
            if (marker.Ordered && !First(marker)) return Started.No;
        }

        var offset = scan.Indent;
        scan.AdvanceNextNonspace();
        scan.AdvanceOffset(length, columns: true);

        var spacesColumn = scan.Column;
        var spacesOffset = scan.Offset;

        do
        {
            scan.AdvanceOffset(1, columns: true);
        }
        while (scan.Column - spacesColumn < 5 && scan.Peek(scan.Offset) is ' ' or '\t');

        var empty = scan.Peek(scan.Offset) == '\0';
        var spaces = scan.Column - spacesColumn;
        int padding;

        if (spaces >= 5 || spaces < 1 || empty)
        {
            padding = length + 1;
            scan.Rewind(spacesOffset, spacesColumn);
            if (scan.Peek(scan.Offset) is ' ' or '\t') scan.AdvanceOffset(1, columns: true);
        }
        else
        {
            padding = length + spaces;
        }

        scan.CloseUnmatched();

        if (scan.Tip is not ScannedList { } list || !list.Marker.Continues(marker))
            scan.Open(new ScannedList(_list, at, marker) { Line = scan.LineIndex });

        var item = scan.Open(new ScannedItem(this, at, marker, padding) { Line = scan.LineIndex, Offset = offset });
        item.Reach(at + length);

        return Started.Container;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var item = (ScannedItem)block;

        if (scan.Blank)
        {
            if (item.Children.Count == 0) return Continued.Unmatched;

            scan.AdvanceNextNonspace();
            return Continued.Matched;
        }

        if (scan.Indent < item.Offset + item.Padding) return Continued.Unmatched;

        scan.AdvanceOffset(item.Offset + item.Padding, columns: true);
        return Continued.Matched;
    }

    public override bool Holds(ScannedKind kind) => Flows(kind);

    public override void Close(MarkdownScanner scan, ScannedBlock block)
    {
        // The box an item can be ticked by leads its first paragraph, followed by space: it is the item's, not the words'.
        var item = (ScannedItem)block;
        if (item.Children.Count == 0 || item.Children[0] is not { Kind: ScannedKind.Paragraph } first) return;

        var at = first.Start;
        var text = scan.Text;

        if (at + 3 < first.End && text[at] == '[' && text[at + 1] is ' ' or 'x' or 'X' && text[at + 2] == ']' && text[at + 3] is ' ' or '\t')
        {
            item.Box = at;
            item.Done = text[at + 1] != ' ';
            first.Start = at + 3;
        }
    }

    /// <summary>What a marker reads as from <paramref name="at"/>, and how many characters it is.</summary>
    private bool Marked(MarkdownScanner scan, int at, ScannedMarker? pending, out ScannedMarker marker, out int length)
    {
        var first = scan.Peek(at);
        marker = default;
        length = 0;

        if (first is '*' or '+' or '-')
        {
            // A rule wins: three of them with space between are a line across the page, not an item.
            if (first is '*' or '-' && RuleRule.IsRule(scan, at)) return false;

            marker = new ScannedMarker(false, first, string.Empty, '\0');
            length = 1;
            return true;
        }

        var end = at;
        char bullet;

        if (char.IsAsciiDigit(first))
        {
            while (end - at < 9 && char.IsAsciiDigit(scan.Peek(end))) end++;
            bullet = '1';
        }
        else if (lettered && char.IsAsciiLetter(first))
        {
            // A roman numeral only starts a list, or goes on one that is already roman; anything else is one letter.
            var lower = char.IsAsciiLetterLower(first);
            var roman = IsRoman(first) && (pending is null || pending.Value.Bullet is 'i' or 'I');

            if (roman)
            {
                while (IsRoman(scan.Peek(end)) && char.IsAsciiLetterLower(scan.Peek(end)) == lower) end++;
                bullet = lower ? 'i' : 'I';
            }
            else
            {
                end++;
                bullet = lower ? 'a' : 'A';
            }
        }
        else
        {
            return false;
        }

        var delimiter = scan.Peek(end);
        if (delimiter is not ('.' or ')')) return false;

        marker = new ScannedMarker(true, bullet, scan.Text[at..end], delimiter);
        length = end + 1 - at;
        return true;
    }

    private static bool IsRoman(char character) => character is 'i' or 'v' or 'x' or 'I' or 'V' or 'X';

    /// <summary>
    /// Whether <paramref name="written"/> is what an item's marker counts by in front of its <c>.</c> or <c>)</c>: up to nine
    /// digits, one letter, or roman numerals all of one case — as <see cref="Marked"/> reads a marker.
    /// </summary>
    public static bool Counts(ReadOnlySpan<char> written)
    {
        if (written.IsEmpty) return false;
        if (char.IsAsciiDigit(written[0])) return written.Length <= 9 && !written.ContainsAnyExcept("0123456789");
        if (written.Length == 1) return char.IsAsciiLetter(written[0]);

        var lower = char.IsAsciiLetterLower(written[0]);

        foreach (var character in written)
            if (!IsRoman(character) || char.IsAsciiLetterLower(character) != lower) return false;

        return true;
    }

    /// <summary>Whether an ordered marker numbers the first item of its list: one, or the first letter or numeral.</summary>
    private static bool First(ScannedMarker marker) =>
        marker.Bullet == '1' ? int.TryParse(marker.Number, out var number) && number == 1 : marker.Number == marker.Bullet.ToString();

    /// <summary>The list items are gathered in, which is open as long as its last item is, and holds nothing else.</summary>
    private sealed class ListRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Matched;

    public override bool Holds(ScannedKind kind) => kind == ScannedKind.Item;

        public override bool Marks => false;

        /// <summary>
        /// Whether the list is loose: two of its items with a blank line between them, or an item holding two blocks with one
        /// between them. A line between two blocks that neither of them was written on is a blank one.
        /// </summary>
        public override void Close(MarkdownScanner scan, ScannedBlock block)
        {
            var list = (ScannedList)block;

            list.Loose = Apart(list.Children) || list.Children.Any(item => Apart(item.Children));
        }

        /// <summary>Whether a blank line stands between any two of <paramref name="blocks"/>, one after another.</summary>
        private static bool Apart(IReadOnlyList<ScannedBlock> blocks)
        {
            for (var at = 0; at + 1 < blocks.Count; at++)
                if (blocks[at + 1].Line > blocks[at].WrittenThrough + 1) return true;

            return false;
        }
    }
}
