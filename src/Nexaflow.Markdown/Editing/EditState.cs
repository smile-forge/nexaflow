using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// A stretch of source the reader is being shown literally rather than typeset — a command being typed,
/// or one that backspace has just un-rendered.
/// </summary>
/// <param name="Start">Offset of the first raw character.</param>
/// <param name="End">One past the last.</param>
public readonly record struct RawZone(int Start, int End)
{
    /// <summary>How many characters are raw.</summary>
    public int Length => End - Start;


    /// <summary>Whether the caret at <paramref name="offset"/> is inside or at either edge of the zone.</summary>
    public bool Holds(int offset) => offset >= Start && offset <= End;
}

/// <summary>One selected stretch of source.</summary>
/// <param name="Start">Offset of the first selected character.</param>
/// <param name="Length">How many characters.</param>
public readonly record struct EditRange(int Start, int Length)
{
    /// <summary>One past the last selected character.</summary>
    public int End => Start + Length;
}

/// <summary>
/// The editable state of some content, as a value: what is written, where the caret is, what is selected, and which part (if
/// any) is being shown raw. Every editing rule the user meets lives here and nowhere else — a pure value with no control, no
/// layout and no WPF, so those rules are testable directly rather than only by driving a control and looking at pixels.
/// Deliberately does not know whether a source typesets or what was drawn where; both are the typesetter's business, and the
/// caller passes in the answers.
///
/// <para>
/// <strong>Every edit here is one change made to the document</strong> (<see cref="ContentDocument.Apply"/>): typing over a
/// selection, wrapping three cells, taking out a symbol. So the version an edit leaves knows the change that made it, and what
/// keeps edits so they can be taken back, or tells whoever shows the content what changed, reads that rather than comparing the
/// document before with the document after.
/// </para>
/// </summary>
/// <param name="Document">What is written. Always the truth — the raw zone is a presentation concern.</param>
/// <param name="Caret">Where the caret sits, as an offset into <paramref name="Document"/>.</param>
/// <param name="Selected">
/// The selected stretches, or nothing. More than one is ordinary rather than exotic: a column of a matrix
/// is three cells that are nowhere near each other in the source.
/// </param>
/// <param name="Raw">The stretch being shown literally, if any.</param>
public sealed record EditState(
    ContentDocument Document,
    int Caret,
    IReadOnlyList<EditRange>? Selected = null,
    RawZone? Raw = null)
{
    /// <summary><paramref name="source"/>, made a document of its own.</summary>
    public EditState(string source, int caret, IReadOnlyList<EditRange>? selected = null, RawZone? raw = null)
        : this(ContentDocument.Of(source), caret, selected, raw)
    {
    }

    /// <summary>
    /// What is selected: in order, never overlapping, never empty-length. A pass-through, not computed here:
    /// a record's <c>with</c> copies backing fields without re-running property initialisers, so anything
    /// tidied here would go stale the moment a copy set the ranges. <see cref="Select(IReadOnlyList{EditRange})"/>
    /// is the one door in, and it tidies.
    /// </summary>
    public IReadOnlyList<EditRange> Selection => Selected ?? [];

    /// <summary>A fresh state with the caret at the end and nothing selected.</summary>
    public static EditState For(string latex) =>
        new(ContentDocument.Of(latex), (latex ?? string.Empty).Length);

    /// <summary><paramref name="document"/>, with the caret at its end and nothing picked out.</summary>
    public static EditState For(ContentDocument document) => new(document, document.Length);

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
        string.Concat(Selection.Select(r => Document.Slice(r.Start, r.Length)));

    /// <summary>The source being shown literally, or empty.</summary>
    public string RawText =>
        Raw is { } zone && zone.End <= Document.Length ? Document.Slice(zone.Start, zone.Length) : string.Empty;

    // There is deliberately no "committed source" here, and no mapping to and from it. The raw stretch
    // used to be cut out, the rest typeset, and the characters painted back over the gap — which meant
    // every offset existed twice and had to be translated at each of two dozen call sites, and the
    // painting covered whatever followed it. The typesetter is told which stretch to set as written
    // instead, so there is one formula, laid out around it, in one set of offsets: the source's.

    // ── Moving and selecting ────────────────────────────────────────────────

    /// <summary>Puts the caret somewhere and drops the selection.</summary>
    public EditState MoveCaretTo(int offset) =>
        this with { Caret = Clamp(offset), Selected = [] };

    /// <summary>Selects one range and leaves the caret at its end.</summary>
    public EditState Select(int start, int length) =>
        length <= 0 ? this with { Selected = [] } : Select([new EditRange(start, length)]);

    /// <summary>Selects several stretches at once, and leaves the caret after the last of them.</summary>
    public EditState Select(IReadOnlyList<EditRange> ranges)
    {
        var tidied = Tidy(ranges, Document.Length);
        return tidied.Count == 0
            ? this with { Selected = [] }
            : this with { Selected = tidied, Caret = tidied[^1].End };
    }

    // ── Writing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// What <paramref name="change"/> makes of this: every stretch it writes over written, the caret where it says and whatever it
    /// says is shown as written — or, for a change that writes nothing, only the caret moved, and what is picked out kept while the
    /// caret stays where it was. What the change picks out whole (<see cref="ContentChange.Chooses"/>) is picked out either way.
    /// </summary>
    public EditState Made(ContentChange change)
    {
        if (change.Writes.Count == 0)
        {
            var caret = Clamp(change.Caret);
            return this with { Caret = caret, Raw = change.Raw, Selected = change.Chooses ?? (caret == Caret ? Selected : []) };
        }

        var document = Document.Apply(change);
        return new EditState(document, Math.Clamp(change.Caret, 0, document.Length), change.Chooses ?? (IReadOnlyList<EditRange>)[], change.Raw);
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Types one character: it replaces whatever is selected and lands at the caret. Content with its own
    /// typing rule gets first refusal before this — its edit handler, asked by the engine (<see cref="IOnEdit"/>). LaTeX's own rule (a
    /// backslash opens a raw stretch, letters extend it) lives with LaTeX, not here, which is what makes
    /// <c>\alpha</c> show as itself while being typed instead of flickering through four failed parses.
    /// </summary>
    public EditState Type(char character) => Write(character.ToString());

    /// <summary>
    /// Writes text at the caret, replacing whatever is selected, and optionally shows a stretch of the result
    /// as itself rather than as what it means. The one door a content's own typing rule needs, since everything
    /// else about typing — what a selection is, what replacing it means, where the caret lands — is the same
    /// question whatever is being written.
    /// </summary>
    public EditState Write(string text, RawZone? shown = null)
    {
        if (HasSelection) return Written(OverSelection(text), SelectionStart + text.Length, shown);

        var at = Clamp(Caret);
        return Written([new ContentWrite(at, 0, text)], at + text.Length, shown ?? Shift(Raw, Caret, text.Length));
    }

    /// <summary>Stops showing anything as itself, leaving the source exactly as it stands.</summary>
    public EditState Settled() => Raw is null ? this : this with { Raw = null };

    /// <summary>
    /// Inserts text at the caret, replacing any selection — how a palette key types itself.
    /// <paramref name="caretBack"/> walks the caret back into the hole a template left, so
    /// <c>\frac{}{}</c> lands you in the numerator rather than past the whole thing. A deliberate insertion settles whatever was
    /// half-typed.
    /// </summary>
    public EditState Insert(string text, int caretBack = 0)
    {
        if (string.IsNullOrEmpty(text)) return this;

        var at = HasSelection ? SelectionStart : Clamp(Caret);
        var writes = HasSelection ? OverSelection(text) : [new ContentWrite(at, 0, text)];

        return Written(writes, at + text.Length - Math.Max(0, caretBack), null);
    }

    /// <summary>
    /// Wraps the selection, or inserts the pair around the caret — <c>\sqrt{…}</c> over what you picked.
    /// </summary>
    public EditState Wrap(string before, string after)
    {
        // Nothing picked out, so the construct arrives with its arguments empty and the caret in the
        // first of them. The braces are all it takes: the parser makes a hole out of an empty argument
        // and the hole draws itself, so the construct is visible and aimable the moment it arrives
        // without a character of it being written that the reader did not ask for.
        if (!HasSelection) return Insert(before + after, after.Length);

        // Each stretch keeps its place and gets a wrapper of its own. Gathering them into one wrapper instead would lift a matrix
        // column out of its matrix; wrapping each is what "put a root over what I picked" has to mean when what you picked is
        // three cells.
        var writes = Selection.SelectMany(range => (ContentWrite[])[new(range.Start, 0, before), new(range.End, 0, after)]).ToList();

        // The caret lands inside the last wrapper, as it does when there is only one: everything before it
        // has gained a pair of wrappers, and its own opener sits in front of it.
        var caret = Selection[^1].End
                    + (Selection.Count - 1) * (before.Length + after.Length)
                    + before.Length;

        return Written(writes, caret, null);
    }

    // ── Deleting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Backspace. Behind a rendered command it un-renders rather than deletes: the caret sits after an
    /// <c>α</c> that six characters of source produced, and removing one of those six would leave a broken
    /// <c>\alph</c> the reader never asked for. Showing <c>\alpha</c> raw puts them back in front of what
    /// they actually wrote, and a second backspace then deletes a character of it.
    /// </summary>
    /// <param name="renderedBefore">
    /// The construct drawn immediately before the caret, when more than one character produced it. The
    /// caller reads this off the layout; null means there is nothing to un-render.
    /// </param>
    public EditState Backspace((int Start, int Length)? renderedBefore = null)
    {
        if (HasSelection) return DeleteSelection();
        if (Caret <= 0) return this;

        // Inside a raw zone the source is already on show, so backspace just deletes.
        if (Raw is { } zone && zone.Holds(Caret) && Caret > zone.Start)
        {
            var shrunk = zone with { End = zone.End - 1 };
            return DeleteBack(shrunk.Length > 0 ? shrunk : null);
        }

        if (renderedBefore is { } atom && atom.Length > 1 && atom.Start + atom.Length == Caret)
            return this with { Raw = new RawZone(atom.Start, atom.Start + atom.Length) };

        return DeleteBack(Raw);
    }

    /// <summary>
    /// Takes out one stretch of the source, leaving the caret where it began — how a whole symbol goes
    /// when backspace lands behind one. Six characters of <c>\alpha</c> are one α to look at and one
    /// thing to delete.
    /// </summary>
    public EditState Remove(int start, int length)
    {
        var from = Clamp(start);
        var count = Math.Clamp(length, 0, Document.Length - from);
        if (count == 0) return this;

        return Written([new ContentWrite(from, count, string.Empty)], from, null);
    }

    /// <summary>Forward delete. Never un-renders — you cannot be looking at what is ahead of the caret.</summary>
    public EditState Delete()
    {
        if (HasSelection) return DeleteSelection();
        if (Caret >= Document.Length) return this;

        return Written([new ContentWrite(Caret, 1, string.Empty)], Caret, Shift(Raw, Caret, -1));
    }

    // ── Mechanics ───────────────────────────────────────────────────────────

    /// <summary>Cuts out everything selected, as one change.</summary>
    private EditState DeleteSelection() => Written(OverSelection(string.Empty), SelectionStart, null);

    /// <summary>
    /// The writes that put <paramref name="text"/> where what is selected stands: the first stretch written over with it, and every
    /// other stretch taken out — one change, however far apart the stretches are.
    /// </summary>
    private ContentWrite[] OverSelection(string text) =>
        [.. Selection.Select((range, at) => new ContentWrite(range.Start, range.Length, at == 0 ? text : string.Empty))];

    /// <summary>
    /// <paramref name="writes"/> made to the document as one change, with the caret at <paramref name="caret"/> in what they come to,
    /// nothing picked out, and <paramref name="raw"/> shown as written.
    /// </summary>
    private EditState Written(IReadOnlyList<ContentWrite> writes, int caret, RawZone? raw)
    {
        var document = Document.Apply(new ContentChange(writes, caret, raw));
        return this with { Document = document, Caret = Math.Clamp(caret, 0, document.Length), Selected = [], Raw = raw };
    }

    /// <summary>
    /// Ranges in order, clipped to the source, with empties dropped and overlaps merged — so nothing
    /// downstream has to wonder whether a selection can contradict itself.
    /// </summary>
    private static IReadOnlyList<EditRange> Tidy(IReadOnlyList<EditRange>? ranges, int length)
    {
        if (ranges is null || ranges.Count == 0) return [];

        var clipped = ranges
            .Select(r => (Start: Math.Clamp(r.Start, 0, length), End: Math.Clamp(r.End, 0, length)))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start)
            .ToList();
        if (clipped.Count == 0) return [];

        var merged = new List<EditRange>();
        var (start, end) = clipped[0];
        foreach (var (nextStart, nextEnd) in clipped.Skip(1))
        {
            if (nextStart <= end) { end = Math.Max(end, nextEnd); continue; }
            merged.Add(new EditRange(start, end - start));
            (start, end) = (nextStart, nextEnd);
        }

        merged.Add(new EditRange(start, end - start));
        return merged;
    }

    /// <summary>The character before the caret taken out, with <paramref name="raw"/> shown as written afterwards.</summary>
    private EditState DeleteBack(RawZone? raw) =>
        Written([new ContentWrite(Caret - 1, 1, string.Empty)], Caret - 1, raw);

    /// <summary>Keeps a raw zone over the same characters after an edit somewhere else moved them.</summary>
    private static RawZone? Shift(RawZone? zone, int at, int by)
    {
        if (zone is not { } z) return null;
        if (at <= z.Start) return z with { Start = z.Start + by, End = z.End + by };
        return at < z.End ? z with { End = z.End + by } : z;
    }

    private int Clamp(int offset) => Math.Clamp(offset, 0, Document.Length);
}
