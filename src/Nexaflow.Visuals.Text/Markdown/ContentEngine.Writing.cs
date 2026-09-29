using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The content as it is being written: its source, the caret, what is picked out and what is shown as typed, the layout they
/// were laid to, and every edit made to them.
///
/// <para>
/// <strong>The engine's, because it holds everything an edit needs.</strong> An edit is asked of the language it lands in,
/// which the engine read; it lands on a place in the layout, which the engine laid; and what it comes to is read and laid
/// again, which only the engine does. Whatever shows the content paints what is here and says where the reader is — it keeps
/// none of it.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How wide content is laid out before anything has said how much room it has.</summary>
    private const double UnsaidRoom = 680;

    private string? _named;
    private StyleFormat _style = StyleFormat.Dark;
    private bool _readOnly;
    private double _laidFor;

    private EditState _state = EditState.For(string.Empty);
    private Laid _laid = Laid.Nothing;

    /// <summary>
    /// What the caret is standing at: an index into the layout's places, or -1 for a caret standing somewhere none of them
    /// is. Kept beside the state because it is about the picture rather than the text — one offset can be several places —
    /// and it is only ever true of the tree it was taken from, so laying out again works it out again from the offset, which
    /// is what survives an edit.
    /// </summary>
    private int _at = -1;

    /// <summary>Raised whenever the content as it is being written changes at all — true where the caret moved or the source changed.</summary>
    internal event EventHandler<bool>? Changed;

    /// <summary>Raised whenever the caret moves inside the content.</summary>
    internal event EventHandler? CaretMoved;

    /// <summary>Raised when the caret is put somewhere in the content, which is where whatever shows it shows it.</summary>
    internal event EventHandler? CaretTaken;

    /// <summary>Raised once the content has been laid out again, before anything is asked of the new layout or it is shown.</summary>
    public event EventHandler? PreRender;

    /// <summary>Says what the content being written is written in, what it is drawn in, and whether anybody may write in it.</summary>
    /// <param name="named">The language, or null for markdown.</param>
    internal void Show(string? named, StyleFormat style)
    {
        _named = named;
        _style = style;
    }

    /// <summary>Whether nobody may write in the content: what is read can still be picked out and copied.</summary>
    internal bool IsReadOnly
    {
        get => _readOnly;
        set => _readOnly = value;
    }

    /// <summary>Starts writing <paramref name="source"/> afresh, with nothing laid for it yet.</summary>
    internal void Start(string source)
    {
        _state = EditState.For(source);
        _at = -1;

        Begin();
    }

    /// <summary>The source, the caret, what is picked out and what is shown as typed.</summary>
    internal EditState State => _state;

    /// <summary>What the content is laid out as. Always something: a builder always makes a layout.</summary>
    internal Laid Laid => _laid;

    /// <summary>The place the caret stands at, or -1 where it stands at none of them.</summary>
    internal int At => _at;

    /// <summary>How much room the content has, in its own coordinates — which is the room it was given.</summary>
    internal double Room => _laidFor > 0 ? _laidFor : UnsaidRoom;

    /// <summary>Says how much room the content has, and lays it out again where that is not what it was laid for.</summary>
    internal void Fit(double room)
    {
        if (Math.Abs(room - _laidFor) <= 0.5 && _laid.Tree.Count > 0) return;

        _laidFor = room;
        Relay();
    }

    /// <summary>Lays the content out again, because something the source does not say has changed.</summary>
    internal void Refresh()
    {
        Forget();
        Relay();
    }

    /// <summary>Lays it out again from the state as it now stands.</summary>
    internal void Relay()
    {
        _laid = LaidOut(_state);
        Repick();
        Recornered();

        PreRender?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <paramref name="state"/> laid out as it is being shown, read only where <paramref name="readOnly"/> says so rather
    /// than as the content stands — which is what a picture of it is taken as.
    ///
    /// <para>
    /// A builder that falls over is its runner's to catch, and shows the block as written with why. What falls over before
    /// any builder has a tree to be given — the reading itself — is caught here, and shown as written with why the same
    /// way, by the builder that does nothing else (<see cref="UnreadBuilder"/>).
    /// </para>
    /// </summary>
    internal Laid LaidOut(EditState state, bool? readOnly = null)
    {
        var only = readOnly ?? _readOnly;

        try
        {
            return Lay(_named, state, _style, Room, only);
        }
        catch (Exception error)
        {
            return Unreadable(state.Source, 0, $"This could not be read: {error.Message}", _style, only, Room);
        }
    }

    /// <summary>Where an edit is landing, for the language it lands in to make what it will of it.</summary>
    internal Landing Landing => new(_state, _laid, _at);

    // ── Applying an edit ────────────────────────────────────────────────────

    /// <param name="at">
    /// Which place the caret is standing at, when a step has just said. -1 otherwise, which puts it back at the innermost place
    /// at its offset — where a reader who has just typed, clicked or jumped is.
    /// </param>
    internal void Apply(EditState next, bool notify, int at = -1)
    {
        next = Held(Left(next));

        var before = _state;
        var resized = next.Source != before.Source || next.Raw != before.Raw;
        var was = (before.Caret, _at);
        var changed = next.Source != before.Source;

        _state = next;

        if (resized) Relay();

        // An index is only ever true of the tree it was taken from, so one handed in from before laying out again says nothing
        // about the tree there is now — and the caret goes back to the innermost place at its offset. That is also why nothing
        // has to remember to clear it.
        _at = resized || at < 0 || at >= _laid.Places.Count ? _laid.Root.StopAt(_state.Caret) : at;

        var moved = was != (_state.Caret, _at);

        // What the reader wrote is a step to take back; anything that left what is written alone starts the next one.
        if (notify && changed) Recorded();
        else if (!changed) Stayed();

        Changed?.Invoke(this, moved || changed);

        if (moved) CaretMoved?.Invoke(this, EventArgs.Empty);
        if (notify && changed) SourceChanged?.Invoke(this, new ContentSourceChange(before, _state, ContentChangeKind.Written));
    }

    /// <summary>
    /// Stops showing a run of text as written once the caret has left it. Only a stretch that is exactly a run of text — one the
    /// content itself opened (a command half typed) is the content's own business to close.
    /// </summary>
    private EditState Left(EditState next)
    {
        if (next.Raw is not { } zone || zone.Holds(next.Caret)) return next;

        var run = _laid.Root.SelfAndDescendants().Any(piece =>
            piece.Words is not null && piece.Part is { } part && part.Start == zone.Start && part.End() == zone.End);

        return run ? next with { Raw = null } : next;
    }

    // ── The caret ───────────────────────────────────────────────────────────

    /// <summary>Puts the caret at one particular place — what a press and a step both mean — or, at -1, the innermost of the places at <paramref name="offset"/>.</summary>
    internal void TakeCaret(int offset, int at = -1)
    {
        Apply(_state.MoveCaretTo(Snap(offset)), notify: false, at);
        CaretTaken?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the caret one stop. False when it ran off an end, where there is nowhere further to go.</summary>
    internal bool MoveCaret(bool forward, bool extend = false)
    {
        // A stretch being shown as its characters is text, and moves like text: one character at a time. Stepping by layout
        // stops cannot reach into it — every position inside maps to the one point where it sits in the laid-out content — so
        // the caret jumped clean over the thing the reader had just asked to see, which is the only place they wanted to edit.
        if (Typed(_state.Caret) is { } typed)
        {
            var step = _state.Caret + (forward ? 1 : -1);
            if (step >= typed.Start && step <= typed.End) { MoveTo(step, -1, extend); return true; }
        }

        // One step along the places, or — for a caret standing where none of them is, which is what a stretch shown as its own
        // characters leaves behind — the nearest one past the offset it is at.
        var next = _at >= 0 ? _laid.Step(_at, forward) : Rejoining(forward);

        // Content a binding supplied is stepped over: nobody wrote it here, so there is nowhere in it to write.
        while (next is { } over && Supplied(_laid.Places[over].Against)) next = _laid.Step(over, forward);

        if (next is not { } landed) return false;

        MoveTo(_laid.Places[landed].Offset, landed, extend);
        return true;
    }

    /// <summary>The place a caret standing nowhere rejoins the declared ones at, or null at the edge.</summary>
    private int? Rejoining(bool forward) =>
        _laid.Root.StopPast(_state.Caret, forward) is >= 0 and var at ? at : null;

    /// <summary>Up and down — across a fraction bar, out of a script, from a note to the word under it.</summary>
    internal bool MoveCaretVertically(bool up, bool extend = false)
    {
        if (_laid.Root.StepVertical(_state.Caret, up) is not { } next) return false;

        MoveTo(next, _laid.Root.StopAt(next), extend);
        return true;
    }

    /// <summary>Puts the caret at <paramref name="offset"/>, or stretches what is picked out to it.</summary>
    internal void MoveCaretTo(int offset, bool extend = false) => MoveTo(Snap(offset), -1, extend);

    private void MoveTo(int offset, int at, bool extend)
    {
        // Extending is about a stretch of source, and a stretch has no places — which of the marks at its far end the caret
        // would have been drawn as says nothing about what is picked out.
        if (!extend && Unpick()) Chosen();

        if (extend) ExtendSelectionTo(offset);
        else Apply(_state.MoveCaretTo(offset), notify: false, at);
    }

    private int Snap(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _state.Source.Length);

        // Inside the stretch being written every character is its own stop, so the caret goes exactly where it was put; the
        // settled content snaps to the places a caret may rest.
        return Typed(clamped) is not null ? clamped : _laid.NearestStop(clamped);
    }

    /// <summary>
    /// The stretch of source a caret at this offset steps through a character at a time, or null where it stands among the
    /// declared places. Covers both a stretch mid-typing and an ordinary run of text — neither has a layout stop per character,
    /// so in both the caret goes exactly where it was put.
    /// </summary>
    private (int Start, int End)? Typed(int offset)
    {
        if (_state.Raw is { } zone && zone.Holds(offset)) return (zone.Start, zone.End);

        return _laid.Root.WordsAt(offset) is { Part: { } part } ? (part.Start, part.End()) : null;
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes text at the caret: whatever the language it lands in makes of it, and failing that the characters themselves,
    /// spliced in where the caret is.
    /// </summary>
    internal void Write(string text)
    {
        if (Unwritable) return;

        Apply(Edited(EditKind.Typing, text, Landing) ?? _state.Write(text), notify: true);
    }

    /// <summary>
    /// Writes <paramref name="text"/> over a stretch of the source as typing it there would — the language's rule, through the
    /// same edit handling a key goes through — and leaves the caret where it was. What a press means when it is an edit
    /// somewhere other than the caret: a tick on a task's box.
    /// </summary>
    internal void WriteOver(int start, int length, string text)
    {
        if (_readOnly) return;

        var over = _state.Select(start, length);
        var written = Edited(EditKind.Typing, text, new Landing(over, _laid, _at)) ?? over.Write(text);
        var caret = _state.Caret >= start + length ? _state.Caret + text.Length - length : _state.Caret;

        Apply(written.MoveCaretTo(caret), notify: true);
    }

    /// <summary>
    /// Inserts text at the caret, replacing any selection — how a palette key types itself. <paramref name="caretBack"/> walks
    /// the caret into a template's first hole.
    /// </summary>
    internal void Insert(string text, int caretBack = 0)
    {
        if (Unwritable) return;

        // Something picked out and a construct with a hole in it: what you picked goes in the hole.
        if (_state.HasSelection && WrapSelectionInto(text, caretBack)) return;

        // A palette key and a pasted formula land in a construct the same way a typed character does. Only when the template
        // wants the caret walked back into a hole of its own, which is about the text and not the structure.
        if (caretBack == 0 && Edited(EditKind.Typing, text, Landing) is { } written) { Apply(written, notify: true); return; }

        Apply(_state.Insert(text, caretBack), notify: true);
    }

    /// <summary>
    /// Puts what is selected into the hole of <paramref name="template"/> the caret would have gone to. Which hole needs no new
    /// information: <paramref name="caretBack"/> already says where a key expects to be typed next — a <c>\frac</c> pressed
    /// over a selected <c>3+7</c> puts it in the numerator.
    /// </summary>
    private bool WrapSelectionInto(string template, int caretBack)
    {
        var at = template.Length - caretBack;
        if (at <= 0 || at >= template.Length) return false;
        if (template[at - 1] != '{' || template[at] != '}') return false;

        Apply(_state.Insert(template[..at] + _state.SelectedText + template[at..]), notify: true);

        // The template's other arguments are still empty, and the builder has just drawn a hole in each. Selecting the first is
        // what makes the next keystroke fill it.
        SelectNextPlaceholder();
        return true;
    }

    /// <summary>Wraps the selection, or inserts the pair at the caret.</summary>
    internal void Wrap(string before, string after)
    {
        if (Unwritable) return;

        Apply(_state.Wrap(before, after), notify: true);
    }

    /// <summary>Backspace; un-renders a construct drawn from more source than it shows rather than deleting a character of it. False (nothing to delete) is the host's cue to remove the content itself.</summary>
    internal bool Backspace()
    {
        if (Unwritable) return false;
        if (Edited(EditKind.Erasing, string.Empty, Landing) is { } erased) { Apply(erased, notify: true); return true; }
        if (_state is { Caret: 0, SelectionLength: 0 }) return false;

        Apply(Backspacing(_state) ?? _state.Backspace(), notify: true);
        return true;
    }

    /// <summary>
    /// Backspace behind a construct drawn from more source than it shows (fraction, root, matrix) un-renders it to the characters
    /// that spelled it, rather than removing a brace nobody can see and leaving LaTeX that no longer parses. Behind something
    /// atomic (an α) there is nothing hidden, so it is simply taken.
    /// </summary>
    private EditState? Backspacing(EditState state)
    {
        if (state.HasSelection || state.Raw is not null) return null;

        var at = _laid.Root.StopAt(state.Caret);
        if (at < 0 || _laid.Places[at] is not { Trailing: true } place) return null;

        // A run of text is characters, so backspace takes one of them. Taking the whole run would delete a slice's value because
        // the reader wanted its last digit gone.
        if (place.Against.Words is { Maps: true }) return null;

        // One character has nothing hidden behind it — the ordinary backspace is already right.
        var sits = place.Against.Sits();
        if (sits.Length <= 1) return null;

        return place.Against.Holds()
            ? state.Backspace((sits.Start, sits.Length))
            : state.Remove(sits.Start, sits.Length);
    }

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    internal bool Delete()
    {
        if (Unwritable) return false;

        // Asked before the end of the source is: past the last thing written in a diagram is its end, and a delete handed back to
        // the document from there takes whatever the document has next.
        if (Edited(EditKind.Deleting, string.Empty, Landing) is { } erased) { Apply(erased, notify: true); return true; }
        if (_state.Caret >= _state.Source.Length && !_state.HasSelection) return false;

        Apply(_state.Delete(), notify: true);
        return true;
    }

    /// <summary>Ends whatever is half-written — what Space and Enter mean, and the language's to say; anywhere it says nothing, the separator itself.</summary>
    internal void Settle(string separator)
    {
        if (Unwritable) return;

        Apply(Edited(EditKind.Settling, separator, Landing) ?? _state.Write(separator), notify: true);
    }

    /// <summary>
    /// Breaks a line inside what is being written — Shift+Enter — as the language says; anywhere it says nothing, a backslash and a
    /// line break written as a palette key writes them.
    /// </summary>
    internal void Break()
    {
        if (Unwritable) return;

        if (Edited(EditKind.Breaking, "\n", Landing) is { } broken) Apply(broken, notify: true);
        else Insert("\\\n");
    }

    /// <summary>
    /// <paramref name="words"/> pasted where the caret is, as the language there says — and whether it said anything. Where it says
    /// nothing, they are the host's to write as it writes them.
    /// </summary>
    internal bool Pasted(string words)
    {
        if (Unwritable || Edited(EditKind.Pasting, words, Landing) is not { } pasted) return false;

        Apply(pasted, notify: true);
        return true;
    }

    /// <summary>Moves on to the next place to write in, or back to the one before, where the language says where that is.</summary>
    private bool Tabbed(bool forward)
    {
        if (Edited(forward ? EditKind.Tabbing : EditKind.TabbingBack, string.Empty, Landing) is not { } tabbed) return false;

        Apply(tabbed, notify: true);
        return true;
    }

    /// <summary>Puts something new where the caret is — the Insert key — where the language has something to put there.</summary>
    private bool Inserted()
    {
        if (Edited(EditKind.Inserting, string.Empty, Landing) is not { } inserted) return false;

        Apply(inserted, notify: true);
        return true;
    }

    /// <summary>
    /// Does what was chosen from what the language offered at <paramref name="at"/> (<see cref="Asked"/>): told to its edit handler
    /// as <see cref="EditKind.Choosing"/>, from the same piece — false where it said nothing to it.
    /// </summary>
    internal bool Choose(string verb, Point at)
    {
        if (Unwritable || Edited(EditKind.Choosing, verb, Landing, _laid.Root.PieceAt(at)) is not { } chosen) return false;

        Apply(chosen, notify: true);
        return true;
    }

    /// <summary>Puts the caret in the next place still waiting to be written in, so an inserted construct can be filled by typing and tabbing. False when there is none.</summary>
    internal bool SelectNextPlaceholder(bool forward = true)
    {
        if (_readOnly) return false;

        // Read off what was drawn rather than off the text: a hole is a symbol the builder put there, and the source it stands
        // over is the empty braces the reader actually wrote.
        var holes = _laid.Holes;
        if (holes.Count == 0) return false;

        // From wherever the caret is, wrapping round — the last hole tabs back to the first, because a construct being filled in
        // is a loop until it is finished.
        var here = _state.HasSelection ? _state.SelectionStart : _state.Caret;
        var next = forward
            ? holes.FirstOrDefault(hole => hole.Sits().Start > here, holes[0])
            : holes.LastOrDefault(hole => hole.Sits().Start < here, holes[^1]);

        // The caret goes into the hole rather than over it. A hole covers nothing — that is what makes it a hole — so what gets
        // typed lands inside the braces and it stops being one.
        TakeCaret(next.Sits().Start);
        return true;
    }

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>Where what is picked out runs from.</summary>
    internal int Anchor { get; set; }

    /// <summary>Selects a source range, snapped out to whole constructs.</summary>
    internal void Select(int start, int length)
    {
        if (length <= 0) { ClearSelection(); return; }

        var (from, snapped) = _laid.Root.Snap(start, length);

        var next = _state.Select(from, snapped);
        if (!Unpick() && next.Selection.SequenceEqual(_state.Selection)) return;

        Apply(next, notify: false);
        Chosen();
    }

    /// <summary>Picks nothing out.</summary>
    internal void ClearSelection()
    {
        var unpicked = Unpick();
        if (!_state.HasSelection && !unpicked) return;

        Apply(_state.Select(0, 0), notify: false);

        Chosen();
    }

    /// <summary>
    /// Stretches what is picked out to <paramref name="offset"/>. Where nothing is picked out yet, it runs from the caret: Shift
    /// and an arrow start there, wherever the caret was put since the last press.
    /// </summary>
    internal void ExtendSelectionTo(int offset)
    {
        if (!_state.HasSelection) Anchor = _state.Caret;

        Select(Math.Min(Anchor, offset), Math.Abs(offset - Anchor));
    }

    /// <summary>What was last chosen whole — pieces pressed, not characters dragged over — so what is picked out can say whether it still is.</summary>
    private IReadOnlyList<EditRange> _chosenWhole = [];

    /// <summary>
    /// Whether what is picked out is whole things drawn — a slice, a node — chosen as they are rather than characters dragged over.
    /// Nothing is being written in, so there is no caret.
    /// </summary>
    internal bool ChoseWhole => _state.HasSelection && _state.Selection.SequenceEqual(_chosenWhole);

    /// <summary>Takes a selection worked out over the layout tree, in the source's own offsets.</summary>
    internal void SelectNodes(ContentSelection selection)
    {
        if (selection.IsEmpty) { ClearSelection(); return; }

        SelectRanges([.. selection.Ranges.Select(range => new EditRange(range.Start, range.Length))]);
        _chosenWhole = _state.Selection;
    }

    /// <summary>Picks out exactly <paramref name="ranges"/>, several stretches at once — false where that is what was picked out already.</summary>
    internal bool SelectRanges(IReadOnlyList<EditRange> ranges)
    {
        var next = _state.Select(ranges);
        if (!Unpick() && next.Selection.SequenceEqual(_state.Selection)) return false;

        Apply(next, notify: false);
        Chosen();
        return true;
    }

    /// <summary>Whether <paramref name="offset"/> falls inside one of the selected stretches.</summary>
    internal bool Covers(int offset) =>
        _state.Selection.Any(range => offset >= range.Start && offset <= range.End);

    /// <summary>
    /// What moving the selected stretches to <paramref name="to"/> would produce: cut out, reinserted at the drop, and the whole
    /// thing read and built again — whatever the content is, since it works on source text only. Null when nothing is selected,
    /// or when the drop is inside what is being moved (cutting first would leave nowhere to put it).
    /// </summary>
    internal Moved? Moving(int to)
    {
        var ranges = _state.Selection
            .Where(range => range.Length > 0 && range.Start >= 0 && range.End <= _state.Source.Length)
            .OrderBy(range => range.Start)
            .ToList();

        if (ranges.Count == 0) return null;
        if (ranges.Any(range => to > range.Start && to < range.End)) return null;

        var carried = string.Concat(ranges.Select(range => _state.Source.Substring(range.Start, range.Length)));

        // Cut last first, so removing one stretch never moves the offsets of those still to go — the same reason a selection of
        // several stretches can be deleted at all.
        var left = _state.Source;
        foreach (var range in Enumerable.Reverse(ranges)) left = left.Remove(range.Start, range.Length);

        var drop = Math.Clamp(Shift(to, ranges), 0, left.Length);

        return new Moved(
            string.Concat(left.AsSpan(0, drop), carried, left.AsSpan(drop)),
            drop + carried.Length,
            new EditRange(drop, carried.Length));

        // An offset in the source as it stands, read as an offset into what the cut left behind. A stretch wholly in front of it
        // takes its whole length off; one the offset falls inside takes only the part in front, because the rest of it is still to
        // come. Left out, that second case runs an offset backwards past a stretch that straddles it.
        static int Shift(int offset, List<EditRange> cut)
        {
            var shifted = offset;

            foreach (var range in cut)
            {
                if (range.End <= offset) shifted -= range.Length;
                else if (range.Start < offset) shifted -= offset - range.Start;
            }

            return shifted;
        }
    }
}
