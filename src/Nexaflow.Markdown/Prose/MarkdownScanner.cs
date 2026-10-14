namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Where each block of some markdown starts and ends, and which it is — read the way CommonMark reads it: one pass over
/// the lines, with the blocks still open on a stack.
///
/// <para>
/// Each line first goes on into every open container that will take it, then may start blocks of its own, and what is
/// left is text for the innermost open leaf — or, where some container did not take it but a paragraph is open, that
/// paragraph's lazy continuation. <strong>A block, once closed, is never opened again</strong>, which is what lets a
/// document read a stretch at a time stop exactly where a block starts with nothing else open.
/// </para>
/// <para>
/// What each kind of block is — how it starts, how it goes on, what closing it does — is its rule's to say
/// (<see cref="MarkdownBlockRule"/>), and which rules are read with is the scan's options (<see cref="MarkdownBlockRules"/>).
/// The scan knows only the order things happen in, and where on the line it stands.
/// </para>
/// <para>
/// Nothing is copied: every block is places in the source (<see cref="ScannedBlock"/>), and the characters are cut out at
/// them by whoever reads it.
/// </para>
/// </summary>
internal sealed class MarkdownScanner
{
    private readonly ScannedBlock _document;
    private ScannedBlock _oldTip;
    private ScannedBlock _lastMatched;
    private bool _allClosed = true;

    private MarkdownScanner(MarkdownLines lines, MarkdownBlockRules rules, bool document)
    {
        this.Lines = lines;
        this.Rules = rules;
        this.IsDocument = document;

        _document = new ScannedBlock(rules.Document, ScannedKind.Document, 0);
        this.Tip = _oldTip = _lastMatched = _document;
    }

    /// <summary>
    /// The blocks <paramref name="lines"/> are written in, under a block standing for all of them.
    /// </summary>
    /// <param name="document">
    /// Whether the lines are a document's own from its first, rather than what is written inside a block — which is the
    /// one thing front matter needs to know.
    /// </param>
    public static ScannedBlock Scan(MarkdownLines lines, bool document, MarkdownBlockRules? rules = null) =>
        Scan(lines, document, null, out _, rules);

    /// <summary>
    /// The blocks <paramref name="lines"/> are written in — stopping, past <paramref name="soft"/>, at the first block that
    /// starts with nothing but the document open: every block before it is whole, because a block once closed stays
    /// closed. That block is left for whoever reads on from <paramref name="stopped"/>, where its first character stands.
    /// </summary>
    /// <param name="soft">Where to start looking for a place to stop; null to read to the end.</param>
    /// <param name="stopped">Where the block it stopped at starts, or null where it read to the end.</param>
    public static ScannedBlock Scan(MarkdownLines lines, bool document, int? soft, out int? stopped, MarkdownBlockRules? rules = null)
    {
        var scan = new MarkdownScanner(lines, rules ?? MarkdownBlockRules.Reading, document);
        stopped = null;

        for (var index = 0; lines.TryLine(index, out var line); index++)
        {
            var before = scan._document.Children.Count;
            scan.Incorporate(index);

            var blocks = scan._document.Children;
            if (soft is not { } from || line.Start < from || index == 0 || blocks.Count <= before) continue;

            // Only a block this very line started, behind which nothing is still open — and nothing that can be opened
            // again, which a definition list can be by the next term. A definition is no block anybody reads, so it is
            // no place to stop either: what is between two blocks stays one stretch of trivia. Nor is a paragraph opening
            // with a bracket, which is not known to be a paragraph rather than a link's definition until it closes.
            var started = -1;
            for (var at = Math.Max(before - 1, 0); at < blocks.Count && started < 0; at++)
                if (blocks[at].Line == index && !blocks[at].Defines && !(blocks[at].Kind == ScannedKind.Paragraph && scan.Text[blocks[at].Start] == '['))
                    started = at;

            if (started <= 0 || blocks.Take(started).Any(block => block.IsOpen) || blocks[started - 1].Kind == ScannedKind.DefinitionList) continue;

            stopped = blocks[started].Start;
            scan._document.RemoveFrom(started);
            break;
        }

        scan.Tip = scan._document;
        scan.Close(scan._document);

        return scan._document;
    }

    /// <summary>The line counted from nought as <paramref name="index"/>.</summary>
    public MarkdownLine LineAt(int index) => this.Lines.TryLine(index, out var line) ? line : default;

    public MarkdownLines Lines { get; }

    public MarkdownBlockRules Rules { get; }

    public bool IsDocument { get; }

    public string Text => this.Lines.Text;

    /// <summary>The innermost block still open.</summary>
    public ScannedBlock Tip { get; private set; }

    // ── Where on the line the scan stands ───────────────────────────────────

    /// <summary>The line being read, and which it is.</summary>
    public MarkdownLine Line { get; private set; }

    public int LineIndex { get; private set; }

    /// <summary>Where the scan stands on the line.</summary>
    public int Offset { get; private set; }

    /// <summary>The column it stands at.</summary>
    public int Column { get; private set; }

    /// <summary>Whether the tab at <see cref="Offset"/> has had some of its columns taken already.</summary>
    public bool PartialTab { get; private set; }

    /// <summary>Where the next character that is not space stands, and at which column.</summary>
    public int NextNonspace { get; private set; }

    public int NextNonspaceColumn { get; private set; }

    /// <summary>How many columns of space stand between <see cref="Column"/> and <see cref="NextNonspace"/>.</summary>
    public int Indent { get; private set; }

    /// <summary>Whether nothing but space is left on the line.</summary>
    public bool Blank { get; private set; }

    /// <summary>Whether so much space stands in front of what is next that it is indented code.</summary>
    public bool Indented => this.Indent >= 4;

    /// <summary>The character at <paramref name="at"/> on the line, or nought past its end.</summary>
    public char Peek(int at) => at >= 0 && at < this.Line.End ? this.Text[at] : '\0';

    /// <summary>Whether this line went on into every block that was open — false where it is a lazy continuation, if anything.</summary>
    public bool AllMatched => _allClosed;

    public void FindNextNonspace()
    {
        var at = this.Offset;
        var column = this.Column;

        while (at < this.Line.End)
        {
            var character = this.Text[at];

            if (character == ' ') column++;
            else if (character == '\t') column += MarkdownLines.TabStop - column % MarkdownLines.TabStop;
            else break;

            at++;
        }

        this.Blank = at >= this.Line.End;
        this.NextNonspace = at;
        this.NextNonspaceColumn = column;
        this.Indent = column - this.Column;
    }

    public void AdvanceNextNonspace()
    {
        this.Offset = this.NextNonspace;
        this.Column = this.NextNonspaceColumn;
        this.PartialTab = false;
    }

    /// <summary>
    /// Moves on by <paramref name="count"/> characters, or — where <paramref name="columns"/> — by that many columns, which
    /// can stop part-way through a tab.
    /// </summary>
    public void AdvanceOffset(int count, bool columns)
    {
        while (count > 0 && this.Offset < this.Line.End)
        {
            if (this.Text[this.Offset] == '\t')
            {
                var toTab = MarkdownLines.TabStop - this.Column % MarkdownLines.TabStop;

                if (columns)
                {
                    this.PartialTab = toTab > count;
                    var taken = Math.Min(count, toTab);
                    this.Column += taken;
                    if (!this.PartialTab) this.Offset++;
                    count -= taken;
                }
                else
                {
                    this.PartialTab = false;
                    this.Column += toTab;
                    this.Offset++;
                    count--;
                }
            }
            else
            {
                this.PartialTab = false;
                this.Offset++;
                this.Column++;
                count--;
            }
        }
    }

    /// <summary>Moves on to the end of the line.</summary>
    public void AdvanceToEnd()
    {
        this.Offset = this.Line.End;
        this.PartialTab = false;
    }

    /// <summary>Where the last character that is not space stands on the line, from <paramref name="from"/> on, plus one.</summary>
    public int Trimmed(int from)
    {
        var end = this.Line.End;
        while (end > from && this.Text[end - 1] is ' ' or '\t') end--;
        return end;
    }

    // ── What a rule does to the blocks ──────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="block"/> inside whatever open block can hold it, closing every one that cannot — and every
    /// one this line did not go on into.
    /// </summary>
    public T Open<T>(T block) where T : ScannedBlock
    {
        this.CloseUnmatched();

        while (!this.Tip.Rule.Holds(block.Kind)) this.Close(this.Tip);

        this.Tip.Add(block);
        block.LastLine = this.LineIndex;
        block.LastWritten = this.LineIndex;
        this.Tip = block;

        return block;
    }

    /// <summary>
    /// Closes every block this line did not go on into. Asked once a line is known not to be a lazy continuation —
    /// before it opens anything, or adds itself to anything.
    /// </summary>
    public void CloseUnmatched()
    {
        if (_allClosed) return;

        while (_oldTip != _lastMatched)
        {
            var parent = _oldTip.Parent!;
            this.Close(_oldTip);
            _oldTip = parent;
        }

        _allClosed = true;
    }

    /// <summary>Closes a block: its rule says what that makes of it, and what holds it reaches as far as it does.</summary>
    public void Close(ScannedBlock block)
    {
        if (!block.IsOpen) return;

        // Closing a block closes everything still open inside it.
        while (block.LastChild is { IsOpen: true } inner) this.Close(inner);

        var parent = block.Parent;

        block.IsOpen = false;
        block.Rule.Close(this, block);

        if (parent is null) return;

        if (block.Parent == parent) parent.Reach(block.End);
        if (this.Tip == block) this.Tip = parent;
    }

    /// <summary>Takes a block out of what holds it, as though it had never been opened — a paragraph that turned out to be terms.</summary>
    public void Remove(ScannedBlock block)
    {
        var parent = block.Parent!;
        parent.Remove(block);
        block.Parent = null;
        block.IsOpen = false;

        if (this.Tip == block) this.Tip = parent;
        if (_oldTip == block) _oldTip = parent;
        if (_lastMatched == block) _lastMatched = parent;
    }

    /// <summary>
    /// Opens a block again — the one exception to a closed block staying closed: a definition list, which the next term's
    /// paragraph goes on.
    /// </summary>
    public void Resume(ScannedBlock block)
    {
        block.IsOpen = true;
        this.Tip = block;
    }

    /// <summary>Goes back to where the scan stood on the line before.</summary>
    public void Rewind(int offset, int column)
    {
        this.Offset = offset;
        this.Column = column;
        this.PartialTab = false;
    }

    /// <summary>Puts a block a rule made in the place of <paramref name="was"/>, the tip of the scan where that was.</summary>
    public void Replace(ScannedBlock was, ScannedBlock now)
    {
        var parent = was.Parent!;
        parent.Put(parent.IndexOf(was), now);
        now.LastLine = was.LastLine;
        now.LastWritten = was.LastWritten;
        now.Reach(was.End);

        if (this.Tip == was) this.Tip = now;
        if (_oldTip == was) _oldTip = now;
        if (_lastMatched == was) _lastMatched = now;
    }

    /// <summary>Takes what is left of the line into the tip, a leaf that takes lines.</summary>
    public void AddLine()
    {
        var from = this.Offset;

        // A paragraph whose head was taken off by definitions starts where its next line does.
        if (this.Tip.Kind == ScannedKind.Paragraph && this.Tip.Lines.Count == 0) this.Tip.Start = from;

        var leaf = this.Tip;
        leaf.Taken = (from, this.Line.End);
        leaf.LastLine = this.LineIndex;
        if (leaf.Rule.KeepsLines) leaf.Lines.Add((from, this.Line.End));

        var end = this.Trimmed(from);
        if (end > from)
        {
            leaf.Reach(end);
            leaf.LastWritten = this.LineIndex;
        }

        // Only once the line is the block's can its rule say the line closed it.
        leaf.Rule.Added(this, leaf);
    }

    // ── A line ──────────────────────────────────────────────────────────────

    private void Incorporate(int index)
    {
        this.LineIndex = index;
        this.Line = this.LineAt(index);
        this.Offset = this.Line.Content;
        this.Column = this.Line.Column;
        this.PartialTab = false;
        this.Blank = false;

        var container = _document;
        _oldTip = this.Tip;

        // Every open block, from the outside in, is asked whether this line goes on into it.
        while (container.LastChild is { IsOpen: true } last)
        {
            container = last;
            this.FindNextNonspace();

            var said = last.Rule.Continues(this, last);

            if (said == Continued.Consumed) return;

            if (said == Continued.Unmatched)
            {
                container = last.Parent!;
                break;
            }

            last.LastLine = index;
            if (!this.Blank && last.Rule.Marks) last.LastWritten = index;
        }

        _allClosed = container == _oldTip;
        _lastMatched = container;

        var leaf = container.Kind != ScannedKind.Paragraph && container.Rule.TakesLines;

        // Then what it starts, until a leaf is open or nothing starts.
        while (!leaf)
        {
            this.FindNextNonspace();

            if (this.Blank || (!this.Indented && !this.Rules.MayStart(this.Text[this.NextNonspace])))
            {
                this.AdvanceNextNonspace();
                break;
            }

            var started = Started.No;

            foreach (var rule in this.Rules.Starts)
            {
                started = rule.TryStart(this, container);
                if (started != Started.No) break;
            }

            if (started is Started.No or Started.Text)
            {
                this.AdvanceNextNonspace();
                break;
            }

            container = this.Tip;
            leaf = started == Started.Leaf;
        }

        // What is left is text: a lazy continuation of the paragraph open, or the tip's own line, or a new paragraph.
        if (!_allClosed && !this.Blank && this.Tip.Kind == ScannedKind.Paragraph)
        {
            this.AddLine();
            return;
        }

        this.CloseUnmatched();

        if (container.Rule.TakesLines && this.Tip == container)
        {
            this.AddLine();
        }
        else if (this.Offset < this.Line.End && !this.Blank)
        {
            this.Open(new ScannedBlock(this.Rules.Paragraph, ScannedKind.Paragraph, this.NextNonspace) { Line = index });
            this.AdvanceNextNonspace();
            this.AddLine();
        }
    }
}

/// <summary>What a rule makes of a line that might start a block of its kind.</summary>
internal enum Started
{
    /// <summary>Nothing of this kind starts here.</summary>
    No,

    /// <summary>A block that holds blocks started, and the rest of the line is asked again.</summary>
    Container,

    /// <summary>A block that takes lines started, and the rest of the line is its.</summary>
    Leaf,

    /// <summary>The line is the open paragraph's text, whatever another rule would have made of it.</summary>
    Text,
}

/// <summary>What a rule makes of a line an open block of its kind might go on into.</summary>
internal enum Continued
{
    /// <summary>It goes on into the block, past whatever marks the block puts on its lines.</summary>
    Matched,

    /// <summary>It does not — though as a paragraph's lazy continuation it might yet.</summary>
    Unmatched,

    /// <summary>It goes into the block and that is all of it: the block's closing line.</summary>
    Consumed,
}

/// <summary>
/// One kind of block: how it starts, how it goes on, what closing it makes of it, and what it holds.
/// </summary>
internal abstract class MarkdownBlockRule
{
    /// <summary>Whether a block of this kind can start with <paramref name="first"/> — what lets most lines skip asking.</summary>
    public virtual bool MayStart(char first) => false;

    /// <summary>Whether a line opens a block of this kind where the scan stands, opening it where it does.</summary>
    public virtual Started TryStart(MarkdownScanner scan, ScannedBlock container) => Started.No;

    /// <summary>Whether a line goes on into an open block of this kind, moving past the marks it puts on the line where it does.</summary>
    public abstract Continued Continues(MarkdownScanner scan, ScannedBlock block);

    /// <summary>What closing the block makes of it.</summary>
    public virtual void Close(MarkdownScanner scan, ScannedBlock block)
    {
    }

    /// <summary>Whether a block of this kind takes lines rather than blocks.</summary>
    public virtual bool TakesLines => false;

    /// <summary>
    /// Whether a block of this kind keeps every line it takes, for what closing it reads them for — a paragraph's tables
    /// and definitions. Most need only how far they reach, and keep nothing.
    /// </summary>
    public virtual bool KeepsLines => false;

    /// <summary>
    /// Whether a line going on into it is written in it. Not for a list, which has no marks of its own and goes on into every
    /// line: what is written in it is its items'.
    /// </summary>
    public virtual bool Marks => true;

    /// <summary>Whether it can hold a block of <paramref name="kind"/>.</summary>
    public virtual bool Holds(ScannedKind kind) => false;

    /// <summary>What a line just taken in makes of the block — the end of raw HTML, say.</summary>
    public virtual void Added(MarkdownScanner scan, ScannedBlock block)
    {
    }

    /// <summary>Whether <paramref name="kind"/> is a block that can stand among blocks, rather than only inside one kind of block.</summary>
    protected static bool Flows(ScannedKind kind) => kind is not (ScannedKind.Document or ScannedKind.Item or ScannedKind.Row
        or ScannedKind.Cell or ScannedKind.Term or ScannedKind.DefinitionItem or ScannedKind.Caption);
}
