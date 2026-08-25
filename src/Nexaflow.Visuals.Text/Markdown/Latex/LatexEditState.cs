using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A stretch of source the reader is being shown literally rather than typeset — a command being typed,
/// or one that backspace has just un-rendered.
/// </summary>
/// <param name="Start">Offset of the first raw character.</param>
/// <param name="End">One past the last.</param>
public readonly record struct LatexRawZone(int Start, int End)
{
    /// <summary>How many characters are raw.</summary>
    public int Length => End - Start;

    /// <summary>Whether the caret at <paramref name="offset"/> is inside or at either edge of the zone.</summary>
    public bool Holds(int offset) => offset >= Start && offset <= End;
}

/// <summary>One selected stretch of source.</summary>
/// <param name="Start">Offset of the first selected character.</param>
/// <param name="Length">How many characters.</param>
public readonly record struct LatexRange(int Start, int Length)
{
    /// <summary>One past the last selected character.</summary>
    public int End => Start + Length;
}

/// <summary>
/// The editable state of one formula, as a value: its source, where the caret is, what is selected, and
/// which part (if any) is being shown raw.
/// <para>
/// Every editing rule the user meets lives here and nowhere else — what typing does, when a half-written
/// command stops being raw, what backspace behind a rendered symbol means. Keeping it a pure value with
/// no control, no layout and no WPF is what makes those rules testable as rules, rather than only
/// reachable by driving a control and looking at pixels.
/// </para>
/// <para>
/// Two things it deliberately does not know: whether a given source typesets, and what was drawn where.
/// Both are the typesetter's business, so the caller passes in the answers.
/// </para>
/// </summary>
/// <param name="Latex">The source. Always the truth — the raw zone is a presentation concern.</param>
/// <param name="Caret">Where the caret sits, as an offset into <paramref name="Latex"/>.</param>
/// <param name="Selected">
/// The selected stretches, or nothing. More than one is ordinary rather than exotic: a column of a matrix
/// is three cells that are nowhere near each other in the source.
/// </param>
/// <param name="Raw">The stretch being shown literally, if any.</param>
public sealed record LatexEditState(
    string Latex,
    int Caret,
    IReadOnlyList<LatexRange>? Selected = null,
    LatexRawZone? Raw = null)
{
    /// <summary>
    /// What is selected: in order, never overlapping, never empty-length.
    /// <para>
    /// A pass-through rather than something computed here, deliberately. A record's <c>with</c> copies
    /// backing fields and does not re-run property initialisers, so anything tidied on the way in would
    /// go stale the moment a copy set the ranges. <see cref="Select(IReadOnlyList{LatexRange})"/> is the
    /// one door in, and it tidies.
    /// </para>
    /// </summary>
    public IReadOnlyList<LatexRange> Selection => Selected ?? [];

    /// <summary>A fresh state with the caret at the end and nothing selected.</summary>
    public static LatexEditState For(string latex) =>
        new(latex ?? string.Empty, (latex ?? string.Empty).Length);

    /// <summary>Whether anything is selected.</summary>
    public bool HasSelection => Selection.Count > 0;

    /// <summary>Where the selection starts.</summary>
    public int SelectionStart => HasSelection ? Selection[0].Start : 0;

    /// <summary>
    /// How far the selection reaches, from its first character to its last. For one stretch that is its
    /// length; for several it spans the gaps between them too, which is why washing and editing go by
    /// <see cref="Selection"/> and not by this.
    /// </summary>
    public int SelectionLength => HasSelection ? Selection[^1].End - Selection[0].Start : 0;

    /// <summary>The selected source. Disjoint stretches come back joined, in order.</summary>
    public string SelectedText =>
        string.Concat(Selection.Select(r => Latex.Substring(r.Start, r.Length)));

    /// <summary>The source being shown literally, or empty.</summary>
    public string RawText =>
        Raw is { } zone && zone.End <= Latex.Length ? Latex[zone.Start..zone.End] : string.Empty;

    /// <summary>The source with the raw stretch taken out — what still typesets while a command is half-typed.</summary>
    public string Committed =>
        Raw is { } zone && zone.End <= Latex.Length ? Latex[..zone.Start] + Latex[zone.End..] : Latex;

    /// <summary>
    /// Maps an offset in <see cref="Latex"/> to the same place in <see cref="Committed"/>, so a caret
    /// and a selection can be drawn against a layout built from the part that still typesets.
    /// </summary>
    public int ToCommitted(int offset)
    {
        if (Raw is not { } zone) return offset;
        if (offset <= zone.Start) return offset;
        return offset >= zone.End ? offset - zone.Length : zone.Start;
    }

    /// <summary>
    /// Maps back the other way, turning an offset the layout reported into one in <see cref="Latex"/> —
    /// what a click on the typeset part means in the real source.
    /// </summary>
    public int FromCommitted(int offset)
    {
        if (Raw is not { } zone) return offset;
        return offset <= zone.Start ? offset : offset + zone.Length;
    }

    // ── Moving and selecting ────────────────────────────────────────────────

    /// <summary>Puts the caret somewhere and drops the selection.</summary>
    public LatexEditState MoveCaretTo(int offset) =>
        this with { Caret = Clamp(offset), Selected = [] };

    /// <summary>Selects one range and leaves the caret at its end.</summary>
    public LatexEditState Select(int start, int length) =>
        length <= 0 ? this with { Selected = [] } : Select([new LatexRange(start, length)]);

    /// <summary>Selects several stretches at once, and leaves the caret after the last of them.</summary>
    public LatexEditState Select(IReadOnlyList<LatexRange> ranges)
    {
        var tidied = Tidy(ranges, Latex);
        return tidied.Count == 0
            ? this with { Selected = [] }
            : this with { Selected = tidied, Caret = tidied[^1].End };
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Types one character.
    /// <para>
    /// A backslash opens a raw zone, and letters extend it: that is TeX's own rule for a control word,
    /// and it is why <c>\alpha</c> shows as itself while you write it instead of flickering through
    /// four different failed parses. Anything that is not a letter ends the word — so <c>\alpha+</c>
    /// settles the command first and then types the plus, exactly as TeX would read it.
    /// </para>
    /// </summary>
    public LatexEditState Type(char character)
    {
        var state = HasSelection ? DeleteSelection() : this;

        if (state.Raw is { } zone && zone.Holds(state.Caret))
        {
            if (char.IsLetter(character))
                return state.Splice(character.ToString(), zone with { End = zone.End + 1 });

            // The control word is finished. Settle it, then type this character against the result.
            return (state with { Raw = null }).Splice(character.ToString(), null);
        }

        return character == '\\'
            ? state.Splice("\\", new LatexRawZone(state.Caret, state.Caret + 1))
            : state.Splice(character.ToString(), null);
    }

    /// <summary>
    /// Ends a raw zone, as space or Enter does — a request to settle what has been written and set it.
    /// <para>
    /// The separator is kept only where LaTeX needs it: after a control word, to say where the name
    /// stopped, since dropping it would silently turn <c>\alpha x</c> into the unknown command
    /// <c>\alphax</c>. Everywhere else the space is not typeset, so putting one in the source leaves a
    /// character the reader cannot see and cannot find — it looks like nothing happened, and then
    /// backspace has to be pressed once for every invisible space before anything moves.
    /// </para>
    /// </summary>
    public LatexEditState Commit(string separator = " ")
    {
        if (Raw is null) return this;   // nothing half-written; the caller only wanted a fresh reading

        var settled = this with { Raw = null };
        return NeedsSeparator(RawText) ? settled.Splice(separator, null) : settled;
    }

    /// <summary>
    /// Whether a control word needs something after it to end its name. <c>\alpha</c> does; <c>\\</c>
    /// and <c>\,</c> do not, because a single non-letter after the backslash is the whole command.
    /// </summary>
    private static bool NeedsSeparator(string raw) =>
        raw.Length > 1 && raw[0] == '\\' && char.IsLetter(raw[^1]);

    /// <summary>
    /// Inserts text at the caret, replacing any selection — how a palette key types itself.
    /// <paramref name="caretBack"/> walks the caret back into the hole a template left, so
    /// <c>\frac{}{}</c> lands you in the numerator rather than past the whole thing.
    /// </summary>
    public LatexEditState Insert(string text, int caretBack = 0)
    {
        if (string.IsNullOrEmpty(text)) return this;

        var state = HasSelection ? DeleteSelection() : this;
        state = state with { Raw = null };   // a deliberate insertion settles whatever was half-typed
        var spliced = state.Splice(text, null);
        return spliced with { Caret = spliced.Clamp(spliced.Caret - Math.Max(0, caretBack)) };
    }

    /// <summary>
    /// Wraps the selection, or inserts the pair around the caret — <c>\sqrt{…}</c> over what you picked.
    /// </summary>
    public LatexEditState Wrap(string before, string after)
    {
        // Nothing picked out, so the construct arrives with its arguments empty and the caret in the
        // first of them. The braces are all it takes: the parser makes a hole out of an empty argument
        // and the hole draws itself, so the construct is visible and aimable the moment it arrives
        // without a character of it being written that the reader did not ask for.
        if (!HasSelection) return Insert(before + after, after.Length);


        // Each stretch keeps its place and gets a wrapper of its own, last one first so the offsets of
        // those still to come do not move. Gathering them into one wrapper instead would lift a matrix
        // column out of its matrix; wrapping each is what "put a root over what I picked" has to mean
        // when what you picked is three cells.
        var latex = Latex;
        foreach (var range in Selection.Reverse())
            latex = latex.Insert(range.End, after).Insert(range.Start, before);

        // The caret lands inside the last wrapper, as it does when there is only one: everything before it
        // has gained a pair of wrappers, and its own opener sits in front of it.
        var caret = Selection[^1].End
                    + (Selection.Count - 1) * (before.Length + after.Length)
                    + before.Length;

        return this with
        {
            Latex = latex,
            Caret = Math.Clamp(caret, 0, latex.Length),
            Selected = [],
            Raw = null,
        };
    }

    // ── Deleting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Backspace.
    /// <para>
    /// Behind a rendered command it un-renders rather than deletes: the caret sits after an <c>α</c>
    /// that six characters of source produced, and removing one of those six would leave <c>\alph</c> —
    /// a broken formula the reader never asked for. Showing <c>\alpha</c> raw puts them back in front of
    /// what they actually wrote, and a second backspace then deletes a character of it.
    /// </para>
    /// </summary>
    /// <param name="renderedBefore">
    /// The construct drawn immediately before the caret, when more than one character produced it. The
    /// caller reads this off the layout; null means there is nothing to un-render.
    /// </param>
    public LatexEditState Backspace((int Start, int Length)? renderedBefore = null)
    {
        if (HasSelection) return DeleteSelection();
        if (Caret <= 0) return this;

        // Inside a raw zone the source is already on show, so backspace just deletes.
        if (Raw is { } zone && zone.Holds(Caret) && Caret > zone.Start)
        {
            var shrunk = zone with { End = zone.End - 1 };
            return DeleteBack(1, shrunk.Length > 0 ? shrunk : null);
        }

        if (renderedBefore is { } atom && atom.Length > 1 && atom.Start + atom.Length == Caret)
            return this with { Raw = new LatexRawZone(atom.Start, atom.Start + atom.Length) };

        return DeleteBack(1, Raw);
    }

    /// <summary>
    /// Takes out one stretch of the source, leaving the caret where it began — how a whole symbol goes
    /// when backspace lands behind one. Six characters of <c>\alpha</c> are one α to look at and one
    /// thing to delete.
    /// </summary>
    public LatexEditState Remove(int start, int length)
    {
        var from = Math.Clamp(start, 0, Latex.Length);
        var count = Math.Clamp(length, 0, Latex.Length - from);
        if (count == 0) return this;

        return this with
        {
            Latex = Latex.Remove(from, count),
            Caret = from,
            Selected = [],
            Raw = null,
        };
    }

    /// <summary>Forward delete. Never un-renders — you cannot be looking at what is ahead of the caret.</summary>
    public LatexEditState Delete()
    {
        if (HasSelection) return DeleteSelection();
        if (Caret >= Latex.Length) return this;
        return this with { Latex = Latex.Remove(Caret, 1), Raw = Shift(Raw, Caret, -1) };
    }

    // ── Mechanics ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cuts out everything selected. Last stretch first, so that removing one never moves the offsets of
    /// the ones still to go — the reason a disjoint selection can be deleted at all.
    /// </summary>
    private LatexEditState DeleteSelection()
    {
        var latex = Latex;
        foreach (var range in Selection.Reverse())
            latex = latex.Remove(range.Start, range.Length);

        return this with
        {
            Latex = latex,
            Caret = Math.Clamp(SelectionStart, 0, latex.Length),
            Selected = [],
            Raw = null,
        };
    }

    /// <summary>
    /// Ranges in order, clipped to the source, with empties dropped and overlaps merged — so nothing
    /// downstream has to wonder whether a selection can contradict itself.
    /// </summary>
    private static IReadOnlyList<LatexRange> Tidy(IReadOnlyList<LatexRange>? ranges, string latex)
    {
        if (ranges is null || ranges.Count == 0) return [];

        var length = latex?.Length ?? 0;
        var clipped = ranges
            .Select(r => (Start: Math.Clamp(r.Start, 0, length), End: Math.Clamp(r.End, 0, length)))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start)
            .ToList();
        if (clipped.Count == 0) return [];

        var merged = new List<LatexRange>();
        var (start, end) = clipped[0];
        foreach (var (nextStart, nextEnd) in clipped.Skip(1))
        {
            if (nextStart <= end) { end = Math.Max(end, nextEnd); continue; }
            merged.Add(new LatexRange(start, end - start));
            (start, end) = (nextStart, nextEnd);
        }

        merged.Add(new LatexRange(start, end - start));
        return merged;
    }

    private LatexEditState DeleteBack(int count, LatexRawZone? raw) =>
        this with
        {
            Latex = Latex.Remove(Caret - count, count),
            Caret = Caret - count,
            Raw = raw,
        };

    private LatexEditState Splice(string text, LatexRawZone? raw) =>
        this with
        {
            Latex = Latex.Insert(Clamp(Caret), text),
            Caret = Clamp(Caret) + text.Length,
            Selected = [],
            Raw = raw ?? Shift(Raw, Caret, text.Length),
        };

    /// <summary>Keeps a raw zone over the same characters after an edit somewhere else moved them.</summary>
    private static LatexRawZone? Shift(LatexRawZone? zone, int at, int by)
    {
        if (zone is not { } z) return null;
        if (at <= z.Start) return new LatexRawZone(z.Start + by, z.End + by);
        return at < z.End ? new LatexRawZone(z.Start, z.End + by) : z;
    }

    private int Clamp(int offset) => Math.Clamp(offset, 0, Latex.Length);
}
