using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// A block picked out by column rather than by line: the same columns of every line from one to another, as a text editor picks out with
/// Alt and a drag — one stretch a line, carried as what is picked out (<see cref="EditState.Selected"/>) so every edit knows to write on each
/// of them. A line too short to reach the columns has a stretch of nothing at its end.
///
/// <para>
/// Columns are counted as the line is set, a tab reaching to its next stop (<see cref="TabWidth"/> characters apart), so a block picks out
/// what stands in a column on the page however each line's indent was written.
/// </para>
/// </summary>
public sealed class ColumnRanges : ReadOnlyCollection<EditRange>
{
    internal ColumnRanges(IList<EditRange> ranges, (int Line, int Column) anchor, (int Line, int Column) caret, int tabWidth) : base(ranges)
    {
        this.Anchor = anchor;
        this.Caret = caret;
        this.TabWidth = tabWidth;
    }

    /// <summary>Where the block was begun: the line, and the column on it.</summary>
    public (int Line, int Column) Anchor { get; }

    /// <summary>Where the block was stretched to: the line, and the column on it.</summary>
    public (int Line, int Column) Caret { get; }

    /// <summary>How many characters apart tab stops stand, which columns are counted by.</summary>
    public int TabWidth { get; }

    /// <summary>The first line of the block.</summary>
    public int FirstLine => Math.Min(this.Anchor.Line, this.Caret.Line);

    /// <summary>The column the block starts at.</summary>
    public int Low => Math.Min(this.Anchor.Column, this.Caret.Column);

    /// <summary>The column the block ends at.</summary>
    public int High => Math.Max(this.Anchor.Column, this.Caret.Column);
}

/// <summary>
/// Picking out, writing over and taking out a block of columns (<see cref="ColumnRanges"/>): what Alt with a drag or with Shift and the arrows
/// makes, and what typing, Backspace, Delete and pasting do to one — every line of it at once, as one change to take back.
/// </summary>
public static class ColumnSelection
{
    /// <summary>
    /// <paramref name="state"/> with the block from <paramref name="anchor"/> to <paramref name="caret"/> picked out — lines, and columns on
    /// them counted with tab stops <paramref name="tabWidth"/> characters apart — and the caret at <paramref name="caret"/>.
    /// </summary>
    public static EditState Between(EditState state, (int Line, int Column) anchor, (int Line, int Column) caret, int tabWidth)
    {
        var document = state.Document;
        var lines = document.LineCount;

        anchor = (Math.Clamp(anchor.Line, 0, lines - 1), Math.Max(anchor.Column, 0));
        caret = (Math.Clamp(caret.Line, 0, lines - 1), Math.Max(caret.Column, 0));

        var (first, last) = (Math.Min(anchor.Line, caret.Line), Math.Max(anchor.Line, caret.Line));
        var (low, high) = (Math.Min(anchor.Column, caret.Column), Math.Max(anchor.Column, caret.Column));
        var ranges = new List<EditRange>(last - first + 1);
        var at = 0;

        for (var line = first; line <= last; line++)
        {
            var (start, end) = document.LineAt(document.OffsetOf(line));
            var (from, to) = (OffsetAt(document, start, end, low, tabWidth), OffsetAt(document, start, end, high, tabWidth));

            ranges.Add(new EditRange(from, to - from));
            if (line == caret.Line) at = caret.Column == low ? from : to;
        }

        return state with { Selected = new ColumnRanges(ranges, anchor, caret, tabWidth), Caret = at, Raw = null };
    }

    /// <summary>Where <paramref name="offset"/> stands as a block counts it: its line, and its column on that line.</summary>
    public static (int Line, int Column) Place(ContentDocument document, int offset, int tabWidth)
    {
        var line = document.LineOf(offset);
        return (line, Column(document, document.OffsetOf(line), offset, tabWidth));
    }

    /// <summary>
    /// What writing <paramref name="text"/> over <paramref name="block"/> comes to: the text on every line of it in place of what was picked
    /// out there — a line of a pasted text on each line, where it has as many lines as the block — a line too short to reach the block
    /// filled out with spaces only where it is written to; and after it, a block of no width standing after what was written, so typing goes
    /// on writing on every line.
    /// </summary>
    public static EditState Written(EditState state, ColumnRanges block, string text)
    {
        var document = state.Document;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var each = lines.Length > 1 && lines.Length == block.Count ? lines : null;
        var writes = new List<ContentWrite>(block.Count);

        for (var row = 0; row < block.Count; row++)
        {
            var range = block[row];
            var written = each?[row] ?? text;
            var (start, end) = document.LineAt(range.Start);
            var reached = Column(document, start, range.Start, block.TabWidth);

            // A line ending before the block is filled out to it, and only because something is written there.
            var pad = range.Length == 0 && range.Start == end && reached < block.Low && written.Length > 0 ? new string(' ', block.Low - reached) : string.Empty;

            writes.Add(new ContentWrite(range.Start, range.Length, pad + written));
        }

        var made = state.Made(new ContentChange(writes, state.Caret));
        var across = block.Low + Widest(each ?? [text], block.TabWidth);

        return Between(made, (block.Anchor.Line, across), (block.Caret.Line, across), block.TabWidth);
    }

    /// <summary>
    /// What Backspace, or Delete where <paramref name="forward"/> says, does to <paramref name="block"/>: takes out what is picked out on every
    /// line of it — or, from a block of no width, the character before it, or after it, on every line that has one there.
    /// </summary>
    public static EditState Erased(EditState state, ColumnRanges block, bool forward)
    {
        if (block.High > block.Low) return Written(state, block, string.Empty);

        var document = state.Document;
        var writes = new List<ContentWrite>(block.Count);

        foreach (var range in block)
        {
            var (start, end) = document.LineAt(range.Start);

            // Only a line reaching the block has a character at its edge to take.
            if (Column(document, start, range.Start, block.TabWidth) != block.Low) continue;

            if (!forward && range.Start > start) writes.Add(new ContentWrite(range.Start - 1, 1, string.Empty));
            else if (forward && range.Start < end) writes.Add(new ContentWrite(range.Start, 1, string.Empty));
        }

        if (writes.Count == 0) return state;

        var made = state.Made(new ContentChange(writes, state.Caret));
        var column = forward ? block.Low : Math.Max(0, block.Low - 1);

        return Between(made, (block.Anchor.Line, column), (block.Caret.Line, column), block.TabWidth);
    }

    /// <summary>What <paramref name="block"/> picks out, a line of it a line, as the document ends its lines.</summary>
    public static string Text(ContentDocument document, ColumnRanges block) =>
        string.Join(document.LineEnding, block.Select(range => document.Slice(range.Start, range.Length)));

    /// <summary>The column <paramref name="offset"/> stands at on a line starting at <paramref name="start"/>, a tab reaching to its next stop.</summary>
    public static int Column(ContentDocument document, int start, int offset, int tabWidth)
    {
        var column = 0;
        for (var at = start; at < offset; at++) column += document[at] == '\t' ? Stop(column, tabWidth) : 1;

        return column;
    }

    /// <summary>Where on a line from <paramref name="start"/> to <paramref name="end"/> the column <paramref name="column"/> stands — the line's end, where it does not reach it.</summary>
    private static int OffsetAt(ContentDocument document, int start, int end, int column, int tabWidth)
    {
        var (at, reached) = (start, 0);

        while (at < end)
        {
            var wide = document[at] == '\t' ? Stop(reached, tabWidth) : 1;
            if (reached + wide > column) break;

            reached += wide;
            at++;
        }

        return at;
    }

    /// <summary>How far a tab at <paramref name="column"/> reaches: to the next stop.</summary>
    private static int Stop(int column, int tabWidth) => Math.Max(1, tabWidth) - (column % Math.Max(1, tabWidth));

    /// <summary>The widest of <paramref name="texts"/> as a block counts columns.</summary>
    private static int Widest(IEnumerable<string> texts, int tabWidth)
    {
        var widest = 0;

        foreach (var text in texts)
        {
            var column = 0;
            foreach (var character in text) column += character == '\t' ? Stop(column, tabWidth) : 1;
            widest = Math.Max(widest, column);
        }

        return widest;
    }
}
