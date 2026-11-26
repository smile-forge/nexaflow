using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What has been written, so it can be taken back — and what was taken back, so it can be written again.
///
/// <para>
/// <strong>Changes, not copies.</strong> A step is the change that was written and the change that takes it back — what every
/// edit already is by the time it is made, both ways on the version it left (<see cref="ContentDocument.ChangesFrom"/>) — with
/// where the caret was, what was picked out and what was shown as written either side of it. Taking a step back makes the
/// change that takes it back and reads the document again from what that comes to, which is the one path every edit already
/// goes down. Nothing has to know how to reverse a rename carried through a diagram, a fraction un-rendered by backspace or a
/// list renumbered by Enter, because what the change wrote over is in the change. A step holds what it wrote and what it wrote
/// over, never the document.
/// </para>
/// <para>
/// <strong>A step is a stretch of writing in one place.</strong> Typing a sentence is one thing done, not forty, so an
/// edit that carries on from where the last one left the caret joins it — on every line at once, for a block of columns
/// (<see cref="ColumnRanges"/>). Putting the caret anywhere else and writing there starts the next step — which is what a
/// reader means by "the last thing I did".
/// </para>
/// <para>
/// <strong>The changes are true only of the document they were left at.</strong> Taking a step back is asked of the document as
/// the history last left it; one that says something else was changed by something that wrote without being remembered, so the
/// history is let go rather than its changes written into the wrong place.
/// </para>
/// </summary>
public sealed class EditHistory
{
    /// <summary>How many steps are kept. Past it the oldest go, since nobody undoes their way back to the morning.</summary>
    public const int Limit = 200;

    /// <summary>
    /// How many characters the steps kept may hold between them, written and written over — so a handful of pastes the size of a
    /// book cannot hold what a whole morning of typing would not.
    /// </summary>
    public const int TextLimit = 16 * 1024 * 1024;

    private readonly List<EditStep> _undo = [];
    private readonly List<EditStep> _redo = [];

    /// <summary>The step writing could still join — the last one taken — while it can.</summary>
    private EditStep? _open;

    /// <summary>The document as the history last left it: what taking a step back or forward is asked of.</summary>
    private ContentDocument? _left;

    /// <summary>How many steps had been taken when what is written was last saved, or -1 where that can no longer be got back to.</summary>
    private int _saved;

    /// <summary>Raised when what can be undone or redone has changed, for a host showing buttons for either.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Whether what is written is what was last saved (<see cref="MarkSaved"/>) or shown afresh (<see cref="Clear"/>): the steps
    /// taken since have all been taken back, or written again to it.
    /// </summary>
    public bool IsAtSaved => _saved == _undo.Count;

    /// <summary>
    /// Says that the document went from <paramref name="before"/> to <paramref name="after"/> because somebody wrote in it
    /// — joining the step being written where it carries on from where that step left the caret.
    /// </summary>
    public void Record(EditState before, EditState after)
    {
        if (ReferenceEquals(before.Document, after.Document)) return;

        var (forward, backward) = after.Document.ChangesFrom(before.Document);
        if (forward.Writes.All(write => write.Length == 0 && write.WrittenLength == 0)) return;

        _redo.Clear();
        if (_saved > _undo.Count) _saved = -1;

        if (_open is { } open && ReferenceEquals(_left, before.Document) && open.After.Caret == before.Caret
            && Joined(open, forward, before) is { } joined)
        {
            _undo[^1] = _open = joined with { After = Placed(after) };
        }
        else
        {
            _undo.Add(_open = new EditStep(forward, backward, Placed(before), Placed(after)));
        }

        _left = after.Document;
        Trimmed();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The state to go back to from <paramref name="now"/>, or null where there is nothing to take back. Whatever was
    /// being written is finished: the next edit is a step of its own.
    /// </summary>
    public EditState? Undo(EditState now)
    {
        if (_undo.Count == 0 || !Applies(now)) return null;

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);

        return Taken(now.Document.Apply(step.Backward), step.Before);
    }

    /// <summary>The state to go forward to again from <paramref name="now"/>, or null where nothing was taken back.</summary>
    public EditState? Redo(EditState now)
    {
        if (_redo.Count == 0 || !Applies(now)) return null;

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);

        return Taken(now.Document.Apply(step.Forward), step.After);
    }

    /// <summary>
    /// The document as it stood before the step <paramref name="now"/> is part of — what taking that step back would put back — and the one
    /// stretch written over that makes <paramref name="now"/>'s document of it, where <paramref name="last"/> is where the last thing written
    /// left it: the step still being written, where <paramref name="now"/> carries on from it, else <paramref name="last"/> itself. Null where
    /// nothing is written since <paramref name="last"/> and no step is open.
    /// </summary>
    public (ContentDocument Before, ContentWrite Written)? Begun(EditState last, EditState now)
    {
        var open = _open is { } step && ReferenceEquals(_left, last.Document) && _undo.Count > 0 && ReferenceEquals(_undo[^1], step) ? step : null;

        var forward = ReferenceEquals(last.Document, now.Document) ? null
                    : now.Document.MadeFrom(last.Document) ? now.Document.Change!
                    : now.Document.ChangesFrom(last.Document).Forward;

        if (forward is null || forward.Writes.All(write => write.Length == 0 && write.WrittenLength == 0))
            return open is null ? null : (last.Document.Apply(open.Backward), Spanned(open.Forward, last.Document));

        if (open is not null && open.After.Caret == last.Caret && Joined(open, forward, last) is { } joined)
            return (last.Document.Apply(open.Backward), Spanned(joined.Forward, now.Document));

        return (last.Document, Spanned(forward, now.Document));
    }

    /// <summary>Every stretch <paramref name="change"/> writes over, as the one stretch from the first to the end of the last, what it says read from <paramref name="after"/>.</summary>
    private static ContentWrite Spanned(ContentChange change, ContentDocument after)
    {
        var (from, to) = (change.Writes.Min(write => write.Start), change.Writes.Max(write => write.End));
        var reached = to + change.Writes.Sum(write => write.WrittenLength - write.Length);

        return new ContentWrite(from, to - from, after.Slice(from, reached - from));
    }

    /// <summary>Ends the step being written, so the next edit starts one of its own however near it lands.</summary>
    public void Break() => _open = null;

    /// <summary>
    /// Says that the document the history last left, <paramref name="from"/>, is now <paramref name="now"/>: the same characters held another
    /// way, or grown past where any step writes. Every step still says what it did where it did it, so none is lost.
    /// </summary>
    public void Restate(ContentDocument from, ContentDocument now)
    {
        if (ReferenceEquals(_left, from)) _left = now;
    }

    /// <summary>Says that what is written now has been saved, so <see cref="IsAtSaved"/> is true here and until something else is written.</summary>
    public void MarkSaved()
    {
        _saved = _undo.Count;

        // Writing after a save is a step of its own, so taking it back is getting back to what was saved.
        _open = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets everything — what a different document being shown means. What is shown now is what was saved.</summary>
    public void Clear()
    {
        var had = _undo.Count > 0 || _redo.Count > 0 || _open is not null;

        _undo.Clear();
        _redo.Clear();
        _open = null;
        _left = null;
        _saved = 0;

        if (had) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether <paramref name="now"/> is the document the history last left, so its changes can be made to it. Where it is not,
    /// something wrote without being remembered, and the steps are let go.
    /// </summary>
    private bool Applies(EditState now)
    {
        if (_left is null || ReferenceEquals(_left, now.Document) || _left.Equals(now.Document)) return true;

        _undo.Clear();
        _redo.Clear();
        _open = null;
        _left = null;
        _saved = -1;
        Changed?.Invoke(this, EventArgs.Empty);

        return false;
    }

    /// <summary>A step taken back or forward: <paramref name="document"/>, with the caret, selection and shown stretch <paramref name="place"/> had.</summary>
    private EditState Taken(ContentDocument document, EditPlace place)
    {
        _left = document;
        _open = null;
        Changed?.Invoke(this, EventArgs.Empty);

        return new EditState(document, place.Caret, place.Selected, place.Raw);
    }

    /// <summary>The oldest steps let go while more are kept than <see cref="Limit"/>, or than <see cref="TextLimit"/> holds — the last always kept.</summary>
    private void Trimmed()
    {
        var held = _undo.Sum(step => step.Held) + _redo.Sum(step => step.Held);

        while (_undo.Count > 1 && (_undo.Count > Limit || held > TextLimit))
        {
            held -= _undo[0].Held;
            _undo.RemoveAt(0);
            _saved = _saved > 0 ? _saved - 1 : -1;
        }
    }

    /// <summary>
    /// <paramref name="open"/> carried on by <paramref name="forward"/>, written at <paramref name="before"/> — or null where the
    /// two are not one stretch of writing: they write as many stretches as each other, the caret is at one of the second's, and each
    /// of the second's is written against or inside what its counterpart in the first wrote — one stretch, as typing does, or one on
    /// every line of a block of columns. The step that comes of it writes each whole stretch the two touched, from what it was before
    /// the first.
    /// </summary>
    private static EditStep? Joined(EditStep open, ContentChange forward, EditState before)
    {
        var (wrote, undone, next) = (open.Forward.Writes, open.Backward.Writes, forward.Writes);
        if (wrote.Count == 0 || undone.Count != wrote.Count || next.Count != wrote.Count) return null;

        // Typing joins what it typed; a stretch held as a document's leaves is a long edit of its own, never part of a run of typing.
        if (wrote.Any(write => write.Held is not null) || undone.Any(write => write.Held is not null) || next.Any(write => write.Held is not null)) return null;
        if (!next.Any(write => before.Caret >= write.Start && before.Caret <= write.End)) return null;

        var written = before.Document;
        var forwards = new ContentWrite[wrote.Count];
        var backwards = new ContentWrite[wrote.Count];

        // How far the open step moved what follows each stretch, how far the joined one does, and where the last stretch joined ended.
        var (shifted, grown, reached) = (0, 0, 0);

        for (var at = 0; at < wrote.Count; at++)
        {
            var (first, then) = (wrote[at], next[at]);
            if (undone[at].Text.Length != first.Length) return null;

            // What the open step wrote here, where it stands in the document the next write is made to.
            var (wroteFrom, wroteTo) = (first.Start + shifted, first.Start + shifted + first.Text.Length);
            if (then.End < wroteFrom || then.Start > wroteTo) return null;

            var (from, to) = (Math.Min(wroteFrom, then.Start), Math.Max(wroteTo, then.End));

            // Outside what the open step wrote, the document it was written to reads as the one before it did; inside, as what it
            // wrote over.
            var was = written.Slice(from, wroteFrom - from) + undone[at].Text + written.Slice(wroteTo, to - wroteTo);
            var now = written.Slice(from, then.Start - from) + then.Text + written.Slice(then.End, to - then.End);
            var start = from - shifted;
            if (start < reached) return null;

            forwards[at] = new ContentWrite(start, was.Length, now);
            backwards[at] = new ContentWrite(start + grown, now.Length, was);

            shifted += first.Text.Length - first.Length;
            grown += now.Length - was.Length;
            reached = start + was.Length;
        }

        return open with
        {
            Forward = new ContentChange(forwards, forward.Caret),
            Backward = new ContentChange(backwards, backwards[0].Start + backwards[0].Text.Length),
        };
    }

    private static EditPlace Placed(EditState state) => new(state.Caret, state.Selected, state.Raw);

    /// <summary>Where the caret was, what was picked out and what was shown as written — a state without its document.</summary>
    private readonly record struct EditPlace(int Caret, IReadOnlyList<EditRange>? Selected, RawZone? Raw);

    /// <summary>One step: the change written and the change that takes it back, and the state either side of it without its document.</summary>
    private sealed record EditStep(ContentChange Forward, ContentChange Backward, EditPlace Before, EditPlace After)
    {
        /// <summary>
        /// How many characters it holds in memory, written and written over — a stretch kept as a document's leaves counting only those
        /// held in memory, so a long stretch of a file costs what keeping its place costs.
        /// </summary>
        public long Held => Forward.Writes.Sum(Kept) + Backward.Writes.Sum(Kept);

        private static long Kept(ContentWrite write) => write.Held?.HeldCharacters ?? write.WrittenLength;
    }
}
