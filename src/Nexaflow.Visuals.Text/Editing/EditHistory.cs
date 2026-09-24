using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What has been written, so it can be taken back — and what was taken back, so it can be written again.
///
/// <para>
/// <strong>Whole states, not operations.</strong> A step is the document as it stood before the step: its source, where
/// the caret was and what was picked out. Taking a step back is putting that state back and reading the document again
/// from it, which is the one path every edit already goes down. Nothing has to know how to reverse a rename carried
/// through a diagram, a fraction un-rendered by backspace or a list renumbered by Enter, because none of them is
/// reversed: the document is simply what it was.
/// </para>
/// <para>
/// <strong>A step is a stretch of writing in one place.</strong> Typing a sentence is one thing done, not forty, so an
/// edit that carries on from where the last one left the caret joins it. Putting the caret anywhere else and writing
/// there starts the next step — which is what a reader means by "the last thing I did".
/// </para>
/// </summary>
public sealed class EditHistory
{
    /// <summary>How many steps are kept. Past it the oldest go, since nobody undoes their way back to the morning.</summary>
    public const int Limit = 200;

    private readonly List<EditState> _undo = [];
    private readonly List<EditState> _redo = [];

    /// <summary>Where the last step left the document, while writing there could still join it.</summary>
    private EditState? _open;

    /// <summary>Raised when what can be undone or redone has changed, for a host showing buttons for either.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Says that the document went from <paramref name="before"/> to <paramref name="after"/> because somebody wrote in it
    /// — joining the step being written where it carries on from where that step left the caret.
    /// </summary>
    public void Record(EditState before, EditState after)
    {
        if (string.Equals(before.Source, after.Source, StringComparison.Ordinal)) return;

        _redo.Clear();

        if (_open is not { } open || !ReferenceEquals(open, before) || !Carries(before, after))
        {
            _undo.Add(before);
            if (_undo.Count > Limit) _undo.RemoveAt(0);
        }

        _open = after;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The state to go back to from <paramref name="now"/>, or null where there is nothing to take back. Whatever was
    /// being written is finished: the next edit is a step of its own.
    /// </summary>
    public EditState? Undo(EditState now) => Step(_undo, _redo, now);

    /// <summary>The state to go forward to again from <paramref name="now"/>, or null where nothing was taken back.</summary>
    public EditState? Redo(EditState now) => Step(_redo, _undo, now);

    /// <summary>Ends the step being written, so the next edit starts one of its own however near it lands.</summary>
    public void Break() => _open = null;

    /// <summary>Forgets everything — what a different document being shown means.</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0 && _open is null) return;

        _undo.Clear();
        _redo.Clear();
        _open = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private EditState? Step(List<EditState> from, List<EditState> to, EditState now)
    {
        if (from.Count == 0) return null;

        var state = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(now);

        _open = null;
        Changed?.Invoke(this, EventArgs.Empty);

        return state;
    }

    /// <summary>
    /// Whether an edit carries on from where the one before it left the caret — written at it, or taken back from it —
    /// rather than somewhere the caret was put since.
    /// </summary>
    private static bool Carries(EditState before, EditState after)
    {
        var (start, end) = Touched(before.Source, after.Source);

        return start <= before.Caret && before.Caret <= end;
    }

    /// <summary>The stretch of <paramref name="before"/> an edit wrote over.</summary>
    private static (int Start, int End) Touched(string before, string after)
    {
        var shorter = Math.Min(before.Length, after.Length);

        var start = 0;
        while (start < shorter && before[start] == after[start]) start++;

        var same = 0;
        while (same < shorter - start && before[before.Length - 1 - same] == after[after.Length - 1 - same]) same++;

        return (start, before.Length - same);
    }
}
