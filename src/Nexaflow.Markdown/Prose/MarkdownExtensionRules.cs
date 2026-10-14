namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The YAML a document opens with between two lines of <c>---</c> — or a <c>---</c> and a <c>...</c> — space after either
/// allowed. Only on a document's first line, and only where something closes it: until then it is a rule with
/// paragraphs under it.
/// </summary>
internal sealed class FrontMatterRule : MarkdownBlockRule
{
    public override bool TakesLines => true;

    public override bool MayStart(char first) => first == '-';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (!scan.IsDocument || scan.LineIndex != 0 || scan.Line.Content != 0 || !Fenced(scan.Text, scan.Line, "---")) return Started.No;

        int? closing = null;

        // Something has to stand between the two fences: two rules in a row are two rules.
        for (var line = 2; closing is null && scan.Lines.TryLine(line, out var next); line++)
            if (Fenced(scan.Text, next, "---") || Fenced(scan.Text, next, "..."))
                closing = line;

        if (closing is not { } last) return Started.No;

        var front = scan.Open(new ScannedFrontMatter(this, 0, last) { Line = 0 });
        front.Reach(scan.Trimmed(0));
        scan.AdvanceToEnd();

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var front = (ScannedFrontMatter)block;
        if (scan.LineIndex != front.Closing) return Continued.Matched;

        front.Reach(scan.Trimmed(scan.Line.Content));
        scan.Close(front);
        return Continued.Consumed;
    }

    /// <summary>Whether a line is <paramref name="fence"/>, with nothing after it but space.</summary>
    private static bool Fenced(string text, MarkdownLine line, string fence)
    {
        if (line.End - line.Start < fence.Length || string.CompareOrdinal(text, line.Start, fence, 0, fence.Length) != 0) return false;

        for (var at = line.Start + fence.Length; at < line.End; at++)
            if (text[at] is not (' ' or '\t')) return false;

        return true;
    }

    private sealed class ScannedFrontMatter(MarkdownBlockRule rule, int start, int closing) : ScannedBlock(rule, ScannedKind.FrontMatter, start)
    {
        public int Closing { get; } = closing;
    }
}

/// <summary>
/// A quote whose first line is <c>[!NAME]</c> and nothing else, ended by a line ending: an alert, naming what kind of note
/// it is. The marker is the alert's, not the first paragraph's — which is no paragraph at all where the marker was all of it.
/// </summary>
internal sealed class AlertRule : QuoteRule
{
    public override void Close(MarkdownScanner scan, ScannedBlock block)
    {
        if (block.Children.Count == 0 || block.Children[0] is not { Kind: ScannedKind.Paragraph } first) return;

        var text = scan.Text;
        var (from, to) = first.Lines[0];

        if (to - from < 4 || to >= text.Length || text[from] != '[' || text[from + 1] != '!') return;

        var name = from + 2;
        while (name < to && char.IsAsciiLetter(text[name])) name++;

        if (name == from + 2 || name >= to || text[name] != ']') return;

        for (var after = name + 1; after < to; after++)
            if (text[after] is not (' ' or '\t')) return;

        var alert = (ScannedQuote)block;
        alert.Kind = ScannedKind.Alert;
        alert.Named = (from + 2, name - from - 2);

        first.Lines.RemoveAt(0);

        if (first.Lines.Count == 0)
        {
            block.RemoveAt(0);
            return;
        }

        var start = first.Lines[0].From;
        while (start < first.Lines[0].To && text[start] is ' ' or '\t') start++;
        first.Start = start;
    }
}

/// <summary>
/// A figure: a fence of three or more <c>^</c> holding blocks, closed by a fence as long, and captioned by whatever
/// is written after either fence.
/// </summary>
internal sealed class FigureRule : MarkdownBlockRule
{
    private readonly CaptionRule _caption = new();

    public override bool MayStart(char first) => first == '^';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        var at = scan.NextNonspace;
        if (scan.Indented || scan.Peek(at) != '^') return Started.No;

        var length = 0;
        while (scan.Peek(at + length) == '^') length++;
        if (length < 3) return Started.No;

        var figure = scan.Open(new ScannedFence(this, ScannedKind.Figure, at, '^', length, scan.Indent) { Line = scan.LineIndex });
        figure.Reach(at + length);
        this.Captioned(scan, figure, at + length);
        scan.AdvanceToEnd();

        return Started.Container;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var figure = (ScannedFence)block;
        var at = scan.NextNonspace;

        if (scan.Indented || scan.Peek(at) != '^') return Continued.Matched;

        var run = at;
        while (scan.Peek(run) == '^') run++;
        if (run - at < figure.Length) return Continued.Matched;

        figure.Closed = at;
        figure.Reach(run);
        this.Captioned(scan, figure, run);
        scan.Close(figure);

        return Continued.Consumed;
    }

    public override bool Holds(ScannedKind kind) => Flows(kind) || kind == ScannedKind.Caption;

    /// <summary>What is written after a fence on its line, as the figure's caption.</summary>
    private void Captioned(MarkdownScanner scan, ScannedBlock figure, int after)
    {
        var from = after;
        while (from < scan.Line.End && scan.Text[from] is ' ' or '\t') from++;

        var to = scan.Trimmed(from);
        if (to <= from) return;

        var caption = new ScannedBlock(_caption, ScannedKind.Caption, from) { Line = scan.LineIndex, IsOpen = false };

        caption.Reach(to);
        figure.Add(caption);
        figure.Reach(to);
    }

    private sealed class CaptionRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}

/// <summary><c>*[HTML]: HyperText Markup Language</c> — one line saying what an abbreviation stands for.</summary>
internal sealed class AbbreviationRule : MarkdownBlockRule
{
    public override bool MayStart(char first) => first == '*';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        var at = scan.NextNonspace;
        if (scan.Indented || scan.Peek(at) != '*' || scan.Peek(at + 1) != '[') return Started.No;

        var shut = at + 2;
        while (shut < scan.Line.End && scan.Text[shut] is not (']' or '[')) shut++;

        if (scan.Peek(shut) != ']' || scan.Peek(shut + 1) != ':' || scan.Text.AsSpan(at + 2, shut - at - 2).IsWhiteSpace()) return Started.No;

        var abbreviation = scan.Open(new ScannedBlock(this, ScannedKind.Abbreviation, at) { Line = scan.LineIndex });
        abbreviation.Reach(scan.Trimmed(at));
        scan.AdvanceToEnd();
        scan.Close(abbreviation);

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
}

/// <summary>
/// A definition list: a paragraph's lines, each a term, and under them a line opening <c>:</c> with the column four past
/// the line's start, holding what the terms mean — the paragraph at most one blank line above it. The next term's
/// paragraph, under a list already there, goes on the same list.
/// </summary>
internal sealed class DefinitionRule : MarkdownBlockRule
{
    private readonly ListRule _list = new();
    private readonly TermRule _term = new();

    public override bool MayStart(char first) => first == ':';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        var at = scan.NextNonspace;
        if (scan.Indented || scan.Peek(at) != ':' || !Indented(scan, at, out var through)) return Started.No;

        var paragraph = container.Kind == ScannedKind.Paragraph ? container
            : container.LastChild is { Kind: ScannedKind.Paragraph } before && scan.LineIndex - before.LastLine <= 2 ? before
            : null;

        var list = container.Kind == ScannedKind.DefinitionList ? container : null;

        if (paragraph is null && list is null) return Started.No;

        var item = new ScannedDefinition(this, at, scan.Column) { Line = scan.LineIndex };

        if (paragraph is not null)
        {
            var parent = paragraph.Parent!;
            var index = parent.IndexOf(paragraph);
            list = index > 0 && parent.Children[index - 1] is { Kind: ScannedKind.DefinitionList } earlier ? earlier : null;

            scan.Remove(paragraph);

            foreach (var (from, to) in paragraph.Lines)
            {
                var start = from;
                while (start < to && scan.Text[start] is ' ' or '\t') start++;

                var term = new ScannedBlock(_term, ScannedKind.Term, start) { Line = paragraph.Line, IsOpen = false };

                term.Reach(scan.Text.AsSpan(start, to - start).TrimEnd().Length + start);
                item.Add(term);
                item.Reach(term.End);
            }

            scan.CloseUnmatched();

            if (list is null) list = scan.Open(new ScannedBlock(_list, ScannedKind.DefinitionList, paragraph.Start) { Line = paragraph.Line });
            else scan.Resume(list);
        }
        else
        {
            scan.CloseUnmatched();
        }

        // The item holds its terms, so it starts where the first of them does.
        if (item.Children.Count > 0) item.Start = item.Children[0].Start;

        scan.Open(item).Reach(at + 1);
        list!.Reach(at + 1);

        scan.AdvanceNextNonspace();
        scan.AdvanceOffset(through - scan.Column, columns: true);

        return Started.Container;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block)
    {
        var item = (ScannedDefinition)block;

        if (scan.Indented)
        {
            scan.AdvanceOffset(4, columns: true);
            item.AfterBlank = false;
            return Continued.Matched;
        }

        // Another definition: this one ends, and the line opens the next.
        if (scan.Peek(scan.NextNonspace) == ':' && Indented(scan, scan.NextNonspace, out _)) return Continued.Unmatched;

        if (scan.Blank)
        {
            item.AfterBlank = true;
            scan.AdvanceNextNonspace();
            return Continued.Matched;
        }

        // Unindented, the line only goes on a paragraph still running — lazily.
        if (!item.AfterBlank && item.LastChild is { Kind: ScannedKind.Paragraph, IsOpen: true }) return Continued.Matched;

        return Continued.Unmatched;
    }

    public override bool Holds(ScannedKind kind) => Flows(kind) || kind == ScannedKind.Term;

    /// <summary>
    /// Whether a <c>:</c> at <paramref name="at"/> opens a definition: what follows it reaches at least four columns past
    /// where the line's content starts — and where it does, the column the definition's content starts at.
    /// </summary>
    private static bool Indented(MarkdownScanner scan, int at, out int column)
    {
        var start = scan.Column;
        column = scan.NextNonspaceColumn + 1;

        for (var next = at + 1; next < scan.Line.End; next++)
        {
            var character = scan.Text[next];
            if (character == ' ') column++;
            else if (character == '\t') column += MarkdownLines.TabStop - column % MarkdownLines.TabStop;
            else break;
        }

        if (column - start < 4) return false;

        column = start + 4;
        return true;
    }

    /// <summary>The list a definition's items are gathered in: open while its last item is.</summary>
    private sealed class ListRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) =>
            block.LastChild is { Kind: ScannedKind.DefinitionItem, IsOpen: true } ? Continued.Matched : Continued.Unmatched;

        public override bool Holds(ScannedKind kind) => kind == ScannedKind.DefinitionItem;
    }

    private sealed class TermRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}
