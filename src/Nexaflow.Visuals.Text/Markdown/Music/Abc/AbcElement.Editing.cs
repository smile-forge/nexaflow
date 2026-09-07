using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// The half of the score that is written on: a caret, the keys that move it, and the gestures that change
/// a note.
///
/// <para>
/// Everything a document needs to drive this is <see cref="IEditableBlock"/>, so the caret crosses into a
/// tune, selects, types and leaves through the same host code that drives a formula and a barcode. What is
/// here beyond that is only what is genuinely not shared: an octave, an accidental and a length, which are
/// facts about music and about nothing else on the page.
/// </para>
/// </summary>
public sealed partial class AbcElement : IEditableBlock
{
    private CaretPlace _caret;
    private bool _hasCaret;
    private bool _caretOn;
    private DispatcherTimer? _blink;

    /// <summary>Raised when the reader's own editing changed the tune.</summary>
    public event EventHandler? AbcChanged;

    event EventHandler? IEditableBlock.SourceChanged
    {
        add => AbcChanged += value;
        remove => AbcChanged -= value;
    }

    /// <summary>Raised when a caret movement ran off an end — the host puts it in the prose beside.</summary>
    public event EventHandler<BlockExit>? Exited;

    ILayoutNode? IEditableBlock.Root => _layout?.Root;

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Types a character. A note letter becomes a note in the octave the one before it was in; an
    /// accidental or a length key is a gesture on the note already there; anything else is spliced as
    /// written, because a tune is text and half of what a reader types is a field or a bar line.
    /// </summary>
    public void Type(char character)
    {
        if (_layout is null) return;

        switch (character)
        {
            case '#':
                Gesture(notes => AbcEdit.Accidental(_layout.Reading, notes, 1));
                return;

            case '_' when Target().Count > 0:
                Gesture(notes => AbcEdit.Accidental(_layout.Reading, notes, -1));
                return;

            case '+':
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, 1));
                return;

            case '-' when Target().Count > 0:
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, -1));
                return;

            default:
                Splice(AbcParser.IsNoteLetter(character)
                    ? AbcEdit.NoteAt(_layout.Reading, Caret(), character)
                    : character.ToString());
                return;
        }
    }

    public bool Backspace()
    {
        if (_layout is null) return false;

        if (_selection.Count > 0) { Cut(); return true; }

        var at = Caret();
        if (at <= 0) return false;

        Replace(at - 1, 1, "", at - 1);
        return true;
    }

    public bool Delete()
    {
        if (_layout is null) return false;

        if (_selection.Count > 0) { Cut(); return true; }

        var at = Caret();
        if (at >= _abc.Length) return false;

        Replace(at, 1, "", at);
        return true;
    }

    /// <summary>Everything selected, taken out, leaving the caret where it was.</summary>
    private void Cut()
    {
        var ranges = _selection.OrderByDescending(r => r.Start).ToList();
        var source = _abc;

        foreach (var (start, length) in ranges)
            source = source.Remove(start, Math.Min(length, source.Length - start));

        Apply(source, ranges[^1].Start);
    }

    private void Splice(string text)
    {
        if (_selection.Count > 0) { Cut(); if (text.Length == 0) return; }

        var at = Caret();
        Replace(at, 0, text, at + text.Length);
    }

    private void Replace(int start, int length, string with, int caret)
    {
        start = Math.Clamp(start, 0, _abc.Length);
        length = Math.Clamp(length, 0, _abc.Length - start);

        Apply(string.Concat(_abc.AsSpan(0, start), with, _abc.AsSpan(start + length)), caret);
    }

    // ── The gestures ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a note gesture to the selection, or to the note before the caret where nothing is
    /// selected. <strong>The tree it hands back is provisional</strong> — the stages between the parser
    /// and the builder do not re-derive themselves — so it is printed and read again rather than drawn
    /// from directly.
    /// </summary>
    private void Gesture(Func<IReadOnlyList<ContentPart>, AstWrite?> change)
    {
        if (Target() is not { Count: > 0 } notes) return;
        if (change(notes) is not { } write) return;

        Apply(write.Tree.Print(), write.End, keepSelection: _selection.Count > 0 ? write : null);
    }

    /// <summary>
    /// The notes a gesture acts on: everything selected, or the note the caret is in or has just passed.
    /// <para>
    /// Two answers to one question, and the second is what makes the keys usable — a reader who has typed
    /// a note and wants it an octave up should not have to select it first.
    /// </para>
    /// </summary>
    private IReadOnlyList<ContentPart> Target()
    {
        if (_layout is null) return [];

        var notes = _layout.Reading.Root.SelfAndDescendants()
            .Where(p => p.Kind == AbcKinds.Note && !p.Derived && p.Part(AbcRoles.Letter) is not null)
            .ToList();

        if (_selection.Count > 0)
            return [.. notes.Where(n => _selection.Any(r => n.Start >= r.Start && n.End <= r.Start + r.Length))];

        var at = Caret();
        var here = notes.Where(n => at > n.Start && at <= n.End).ToList();
        if (here.Count > 0) return here;

        var before = notes.Where(n => n.End <= at).OrderByDescending(n => n.End).FirstOrDefault();
        return before is null ? [] : [before];
    }

    /// <summary>
    /// The keys a score claims for itself. Page Up and Page Down are the octave, because nothing else on
    /// a page wants them and an octave is the gesture a reader reaches for most.
    /// </summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (_layout is null || modifiers.HasFlag(ModifierKeys.Control)) return false;

        switch (key)
        {
            case Key.PageUp:
                Gesture(notes => AbcEdit.Octave(_layout.Reading, notes, 1));
                return true;

            case Key.PageDown:
                Gesture(notes => AbcEdit.Octave(_layout.Reading, notes, -1));
                return true;

            // The shift-less spellings of + and -, which arrive as keys rather than as text on a numeric
            // keypad and on the top row of most layouts.
            case Key.Add:
            case Key.OemPlus when modifiers.HasFlag(ModifierKeys.Shift):
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, 1));
                return true;

            case Key.Subtract:
            case Key.OemMinus when Target().Count > 0:
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, -1));
                return true;

            default:
                return false;
        }
    }

    // ── Putting the answer back ─────────────────────────────────────────────

    /// <summary>
    /// The tune as it now stands: re-read, re-engraved, and handed to the document that holds it.
    /// <para>
    /// One path, always taken. An edit can put anything anywhere — a bar line that re-bars the rest of the
    /// line, a field that changes the key under every note after it — so asking whether a change was
    /// contained is not worth answering cheaply.
    /// </para>
    /// </summary>
    private void Apply(string abc, int caret, AstWrite? keepSelection = null)
    {
        _abc = abc;
        _layout = AbcLayout.Build(_abc, _engravedFor > 0 ? _engravedFor : 680, _ink, _ppd);

        _selection = keepSelection is { Length: > 0 } wrote ? [(wrote.Start, wrote.Length)] : [];
        _caret = CaretPlace.At(Math.Clamp(caret, 0, _abc.Length));
        _hasCaret = true;

        InvalidateMeasure();
        InvalidateVisual();

        AbcChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── The caret ───────────────────────────────────────────────────────────

    private int Caret() => Math.Clamp(_caret.Offset, 0, _abc.Length);

    /// <summary>
    /// The caret one place along, or the selection one piece along when it is being extended.
    ///
    /// <para>
    /// <strong>Both live here, and deliberately.</strong> The host asks this for every left and right
    /// arrow, with <paramref name="extend"/> saying whether Shift was down; a block that also claimed the
    /// key for itself would leave two handlers writing one selection from two different ideas of what a
    /// selection is — which is the shape of every keyboard bug this app has had.
    /// </para>
    /// <para>
    /// Extending walks the run the piece belongs to, so Shift and an arrow sweeps a verse or a line of
    /// notes exactly as dragging along it would. Where nothing declares a run there is nothing to walk,
    /// and it falls back to moving the caret — which is what it has always done.
    /// </para>
    /// </summary>
    public bool MoveCaret(bool forward, bool extend)
    {
        if (_layout is null) return false;

        if (extend && Extend(vertical: false, forward)) return true;

        if (_layout.Root.Step(_caret, forward) is not { } next)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        _caret = next;
        if (!extend) _selection = [];
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Up and down: the selection through what sounds together when it is being extended, and otherwise
    /// the caret. Same seam, same reason as <see cref="MoveCaret"/>.
    /// </summary>
    bool IEditableBlock.MoveCaretVertically(bool up, bool extend)
    {
        if (extend && Extend(vertical: true, forward: !up)) return true;

        if (_layout?.Root.StepVertical(Caret(), up) is not { } next) return false;

        _caret = CaretPlace.At(next);
        if (!extend) _selection = [];
        InvalidateVisual();
        return true;
    }

    public void SelectRange(int start, int length)
    {
        if (_layout is null) return;

        _selection = length <= 0 ? [] : [(Math.Max(0, start), Math.Min(length, _abc.Length - Math.Max(0, start)))];
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes the caret from the prose beside it, at the place the reader was coming from. A score is wide
    /// enough for the column to mean something, so a caret arriving from the line above lands under where
    /// it left rather than at the beginning.
    /// </summary>
    public void TakeCaretArriving(CaretArrival arrival)
    {
        if (_layout is null) return;

        var stops = _layout.Root.CaretStops();
        if (stops.Count == 0) { Exited?.Invoke(this, arrival.Edge); return; }

        _caret = CaretPlace.At(arrival switch
        {
            { Step: CaretStep.Line, Column: { } column } => Nearest(column),
            { Edge: BlockExit.Before } => stops[0],
            _ => stops[^1],
        });

        _hasCaret = true;
        _selection = [];
        Blinking(true);
        InvalidateVisual();
    }

    /// <summary>The caret stop nearest a column, for a caret arriving onto the score from another line.</summary>
    private int Nearest(double column)
    {
        var best = 0;
        var distance = double.MaxValue;

        foreach (var node in _layout!.Root.Ink())
        {
            foreach (var (offset, x) in new[] { (node.Sits().Start, node.Bounds.X), (node.Sits().End, node.Bounds.Right) })
            {
                var away = Math.Abs(x - column);
                if (away >= distance) continue;

                distance = away;
                best = offset;
            }
        }

        return best;
    }

    public void ReleaseCaret()
    {
        _hasCaret = false;
        Blinking(false);
        InvalidateVisual();
    }

    private void Blinking(bool on)
    {
        if (!on)
        {
            _blink?.Stop();
            _caretOn = false;
            return;
        }

        _caretOn = true;
        _blink ??= Ticking();
        _blink.Stop();
        _blink.Start();
    }

    private DispatcherTimer Ticking()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        timer.Tick += (_, _) => { _caretOn = !_caretOn; InvalidateVisual(); };
        return timer;
    }

    /// <summary>The caret bar, taking its height from whatever ink it abuts.</summary>
    private void PaintCaret(DrawingContext dc, AbcLayout layout)
    {
        if (!_hasCaret || !_caretOn) return;

        var bar = layout.Root.CaretRect(_caret);
        dc.DrawRectangle(_ink, null, new Rect(bar.X, bar.Y, Math.Max(bar.Width, 1.2), Math.Max(bar.Height, 4)));
    }

    // ── The mini ribbon ─────────────────────────────────────────────────────

    /// <summary>
    /// The actions a score offers on what is selected, or on the note that was clicked. The same set the
    /// keys reach, because a reader who cannot remember which key sharpens a note should not have to.
    /// </summary>
    FrameworkElement? IEditableBlock.BuildRibbon() =>
        _layout is null ? null : new AbcRibbon(action =>
        {
            switch (action)
            {
                case AbcAction.OctaveUp: Gesture(n => AbcEdit.Octave(_layout.Reading, n, 1)); return;
                case AbcAction.OctaveDown: Gesture(n => AbcEdit.Octave(_layout.Reading, n, -1)); return;
                case AbcAction.Longer: Gesture(n => AbcEdit.Length(_layout.Reading, n, 1)); return;
                case AbcAction.Shorter: Gesture(n => AbcEdit.Length(_layout.Reading, n, -1)); return;
                case AbcAction.Sharpen: Gesture(n => AbcEdit.Accidental(_layout.Reading, n, 1)); return;
                case AbcAction.Flatten: Gesture(n => AbcEdit.Accidental(_layout.Reading, n, -1)); return;
            }
        });
}
