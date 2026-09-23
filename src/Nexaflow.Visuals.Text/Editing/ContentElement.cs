using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Shared surface that lays out, paints and edits any embedded content (formula, tune, barcode, …) via
/// one <see cref="Laid"/>-producing builder instead of each kind reimplementing caret/selection/drag.
/// Editing state is <see cref="EditState"/>; content-specific behaviour is the small set of optional
/// hooks below. Plain-text content overrides the shared tree-walk queries (stops/wash/snap) with direct
/// string math — a per-keystroke tree walk is free for a formula but O(n) per character for prose.
/// <para>
/// The pointer gesture is split into <see cref="BeginPointerSelect"/> / <see cref="ExtendPointerSelect"/>
/// / <see cref="EndPointerSelect"/> rather than driven by this element's own mouse events, because content
/// hosted inside a <c>RichTextBox</c> does not reliably receive mouse input — the host hit-tests
/// geometrically and drives these methods instead.
/// </para>
/// </summary>
public class ContentElement : FrameworkElement, IEditableBlock
{
    private static readonly TimeSpan BlinkRate = TimeSpan.FromMilliseconds(600);

    private readonly Brush _wash;

    private EditState _state;
    private Laid _laid = Laid.Nothing;
    private double _laidFor;

    private DispatcherTimer? _blink;
    private bool _caretVisible = true;


    /// <summary>
    /// What the caret is standing at: an index into the layout's places, or -1 for a caret standing
    /// somewhere none of them is. Kept beside the state because it is about the picture rather than the
    /// text — one offset can be several places — and it is only ever true of the tree it was taken from,
    /// so a rebuild works it out again from the offset, which is what survives an edit.
    /// </summary>
    private int _at = -1;

    private int _anchor;
    private Piece _anchorNode;
    private Point _pressedAt;
    private bool _dragging;

    private bool _moving;
    private int _dropAt;
    private Point? _dropPoint;
    private Laid? _preview;
    private Moved? _previewOf;
    private (int Start, int End) _previewMoved;

    /// <summary>Raised whenever the caret moves inside the content.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised whenever what is selected inside the content changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the reader's own editing changed the source.</summary>
    public event EventHandler? SourceChanged;

    /// <summary>Raised when a caret movement ran off an end — the host puts it in the prose beside.</summary>
    public event EventHandler<BlockExit>? Exited;

    /// <summary>The ordinary case: a new kind of content costs only a builder.</summary>
    /// <param name="lay">Handed the whole <see cref="EditState"/>, not just the string, since what is being typed changes what is drawn.</param>
    public ContentElement(string source, StyleFormat palette, Func<EditState, double, Laid> lay)
        : this(source, palette, Content.Of(lay)) { }

    public ContentElement(string source, StyleFormat palette, IContent content)
    {
        Palette = palette;
        _content = content;
        _wash = Wash(palette);
        _state = EditState.For(source ?? string.Empty);

        SnapsToDevicePixels = true;
        Cursor = Cursors.IBeam;

        // Never take keyboard focus. Content is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text,
        // and faults deep inside the splay tree.
        Focusable = false;

        Unloaded += (_, _) => StopBlinking();
    }

    /// <summary>The theme, for the ink, the accent and the two colours trouble is drawn in.</summary>
    protected StyleFormat Palette { get; }

    /// <summary>What is laid out. Always something: a builder always makes a layout.</summary>
    public Laid Laid => _laid;

    /// <summary>The editing model — source, caret, selection, and what is shown as written.</summary>
    protected EditState State => _state;

    // ── What a kind of content gets to say ──────────────────────────────────

    /// <summary>Lays the source out to fit the room given. Takes the whole state, not just the string, since a stretch shown as its own characters is set into the layout rather than painted over it.</summary>
    private Laid Lay(EditState state, double room) => _content.Lay(state, room, IsReadOnly);

    /// <summary>The whole chain from source to picture; everything that differs by kind of content is behind it — see <see cref="IContent"/>.</summary>
    private readonly IContent _content;

    /// <summary>Where an edit is landing, for the content to make what it will of it.</summary>
    private Landing Landing => new(_state, _laid, _at);

    /// <summary>
    /// What writing <paramref name="text"/> means, given what it lands in — the content's rule, not the
    /// element's. See <see cref="IContent.Typing"/>.
    /// </summary>
    private EditState? Typing(EditState state, string text) => _content.Typing(Landing, text);

    /// <summary>
    /// Backspace behind a construct drawn from more source than it shows (fraction, root, matrix) un-renders
    /// it to the characters that spelled it, rather than removing a brace nobody can see and leaving LaTeX
    /// that no longer parses. Behind something atomic (an α) there is nothing hidden, so it is simply taken.
    /// </summary>
    protected virtual EditState? Backspacing(EditState state)
    {
        if (state.HasSelection || state.Raw is not null) return null;

        var at = _laid.Root.StopAt(state.Caret);
        if (at < 0 || _laid.Places[at] is not { Trailing: true } place) return null;

        // A run of text is characters, so backspace takes one of them. Taking the whole run would delete a slice's
        // value because the reader wanted its last digit gone.
        if (place.Against.Words is { Maps: true }) return null;

        // One character has nothing hidden behind it — the ordinary backspace is already right.
        var sits = place.Against.Sits();
        if (sits.Length <= 1) return null;

        return place.Against.Holds()
            ? state.Backspace((sits.Start, sits.Length))
            : state.Remove(sits.Start, sits.Length);
    }

    /// <summary>
    /// What pointing at a piece means: the first ancestor that names a stretch of source. A bracket is drawn
    /// by the fence that holds it and cannot be selected alone — a bracket without its partner can't be read —
    /// so pointing at one selects the thing it belongs to.
    /// </summary>
    protected static Piece Pointing(Piece piece) => piece.Selectable();

    /// <summary>The places still waiting to be written in, in reading order — what Tab walks. Read off the layout, not the text: a hole covers no characters, so there is nothing in the source to find it by.</summary>
    protected IReadOnlyList<Piece> Holes() => _laid.Holes;

    /// <summary>
    /// What moving the selected stretches to <paramref name="to"/> would produce: cut out, reinserted at the
    /// drop, and the whole thing read and built again — kind-agnostic since it works on source text only.
    /// Null when nothing is selected, or when the drop is inside what is being moved (cutting first would
    /// leave nowhere to put it).
    /// </summary>
    private Moved? Moving(EditState state, int to)
    {
        var ranges = state.Selection
            .Where(range => range.Length > 0 && range.Start >= 0 && range.End <= state.Source.Length)
            .OrderBy(range => range.Start)
            .ToList();

        if (ranges.Count == 0) return null;
        if (ranges.Any(range => to > range.Start && to < range.End)) return null;

        var carried = string.Concat(ranges.Select(range => state.Source.Substring(range.Start, range.Length)));

        // Cut last first, so removing one stretch never moves the offsets of those still to go — the same
        // reason a selection of several stretches can be deleted at all.
        var left = state.Source;
        foreach (var range in Enumerable.Reverse(ranges)) left = left.Remove(range.Start, range.Length);

        var drop = Math.Clamp(Shift(to, ranges), 0, left.Length);

        return new Moved(
            string.Concat(left.AsSpan(0, drop), carried, left.AsSpan(drop)),
            drop + carried.Length,
            new EditRange(drop, carried.Length));

        // An offset in the source as it stands, read as an offset into what the cut left behind. A stretch
        // wholly in front of it takes its whole length off; one the offset falls inside takes only the part in
        // front, because the rest of it is still to come. Left out, that second case runs an offset backwards
        // past a stretch that straddles it.
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

    // ── Shape and colour ────────────────────────────────────────────────────

    /// <summary>
    /// How large the content is drawn, as a multiple of its natural size. Magnification, and nothing else: the
    /// content is laid out into the room it has, at its own standard size, and scaled as it is painted. So
    /// zooming settles nothing about the layout — the same tree, the same line breaks, drawn larger.
    ///
    /// <para>
    /// A render scale, not a bitmap one. The transform goes on the drawing context, so text and every other mark
    /// is drawn at the size it ends up, and a pointer coming the other way is divided back (<c>Unscaled</c>).
    /// </para>
    /// <para>
    /// How big the content itself is set — a formula's text size, a score's staff size — is a different question
    /// with a different answer: that is a fact about the content, and it reaches the builder as one.
    /// </para>
    /// </summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>The render scale, kept somewhere a reader could plausibly want to be.</summary>
    protected double Scale => Math.Clamp(Zoom, 0.2, 4.0);

    /// <summary>How far a selection wash reaches past the ink it marks — a box the exact size of a glyph reads poorly as "selected" (a selected <c>i</c> needs the wash over the line box, not its own outline).</summary>
    public double WashPad { get; init; } = 2.0;

    /// <summary>Whether the caret is shown. A read-only surface still allows selecting and copying.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Whether this element currently owns the caret.</summary>
    public bool HasCaret { get; private set; }

    /// <summary>Where the caret sits, as an offset into <see cref="Source"/>.</summary>
    public int Caret => _state.Caret;

    /// <summary>The start of the selected source range.</summary>
    public int SelectionStart => _state.SelectionStart;

    /// <summary>How much source is selected; zero when nothing is.</summary>
    public int SelectionLength => _state.SelectionLength;

    /// <summary>The selected source, or empty.</summary>
    public string SelectedText => _state.SelectedText;

    /// <summary>Whether any of the source could not be read.</summary>
    public bool HasError => _laid.Trouble.Count > 0;

    /// <summary>
    /// The stretch being shown as the characters written rather than set — a command mid-spelling — or
    /// null when all of it is read.
    /// </summary>
    public (int Start, int Length)? ShownAsWritten =>
        _state.Raw is { Length: > 0 } zone ? (zone.Start, zone.Length) : null;

    /// <summary>Whether the caret belongs in this content at all. Asked of the tree rather than declared, since a piece naming a stretch of source is one somebody typed — a barcode that only prints a worked-out number has none.</summary>
    public bool AcceptsCaret =>
            _state.Source.Length == 0 || _laid.Root.SelfAndDescendants().Any(piece => piece.Part is { Length: > 0 });

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token.</summary>
    private static Brush Wash(StyleFormat palette)
    {
        if (palette.Accent is not SolidColorBrush accent) return palette.Marked;

        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }

    // ── What the document around it needs (IEditableBlock) ──────────────────

    /// <inheritdoc />
    public string Source => _state.Source;

    /// <summary>
    /// Where this content's source sits inside the block that produced it, delimiters excluded — what a
    /// host needs to put an edit back where it came from. Negative when the whole block is this content.
    /// </summary>
    public int SourceStart { get; set; } = -1;

    /// <summary>How much of the block's source this occupies. Kept current as it is edited.</summary>
    public int SourceLength { get; set; }

    /// <summary>Whether the whole markdown block is this content rather than a run inside one.</summary>
    public bool IsWholeBlock => SourceStart < 0;

    /// <inheritdoc />
    public Piece Root => _laid.Root;

    /// <inheritdoc />
    public IReadOnlyList<(int Start, int Length)> Selection =>
        [.. _state.Selection.Select(range => (range.Start, range.Length))];

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> Diagnostics => _laid.Trouble;

    // ── The caret ───────────────────────────────────────────────────────────

    /// <summary>Gives this content the caret at <paramref name="offset"/>, innermost of the places there.</summary>
    public void TakeCaret(int offset) => TakeCaret(offset, -1);

    /// <summary>Gives this content the caret at one particular place — what a press and a step both mean.</summary>
    public void TakeCaret(int offset, int at)
    {
        HasCaret = !IsReadOnly;
        Apply(_state.MoveCaretTo(Snap(offset)), notify: false, at);
        if (HasCaret) StartBlinking();
    }


    public void TakeCaretArriving(CaretArrival arrival)
    {
        // Nothing drawn here is source, so the caret passes straight through — arrowed over like a word,
        // not into content where no key would do anything.
        if (!AcceptsCaret)
        {
            Exited?.Invoke(this, arrival.Edge == BlockExit.Before ? BlockExit.After : BlockExit.Before);
            return;
        }

        var stops = _laid.Stops;
        if (stops.Count == 0)
        {
            // Empty content has no stops only because there is nothing yet to stand against; the caret still
            // belongs at 0. Non-empty content with no stop really has nowhere, so it's passed on.
            if (_state.Source.Length == 0) TakeCaret(0);
            else Exited?.Invoke(this, arrival.Edge);
            return;
        }

        // Content wide enough for a column to mean something takes a caret arriving from the line above
        // under where it left, rather than at the beginning.
        if (arrival is { Step: CaretStep.Line, Column: { } column }) { TakeCaret(Nearest(column)); return; }
        if (arrival.Edge == BlockExit.Before) { TakeCaret(stops[0]); return; }

        // Takes the outermost place at the end — landing on the innermost could put it inside a trailing
        // exponent instead of past it.
        var end = Snap(stops[^1]);
                TakeCaret(end, _laid.Root.StopAt(end, outermost: true));
    }

    /// <summary>The caret stop nearest a column, for a caret arriving from another line.</summary>
    private int Nearest(double column)
    {
        var best = 0;
        var distance = double.MaxValue;

        foreach (var piece in _laid.Root.Leaves())
        {
            var at = piece.Sits();
            var where = piece.Bounds;

            foreach (var (offset, x) in new[] { (at.Start, where.X), (at.End, where.Right) })
            {
                var away = Math.Abs(x - column);
                if (away >= distance) continue;

                distance = away;
                best = offset;
            }
        }

        return best;
    }

    /// <inheritdoc />
    public void ReleaseCaret()
    {
        if (!HasCaret) return;
        HasCaret = false;
        StopBlinking();
        InvalidateVisual();
    }

    /// <summary>
    /// Shows the caret where it already stands, without putting it at a place first — what gaining the keyboard means, and
    /// what a host restoring a state it saved means. A caret put at a place is <see cref="TakeCaret(int)"/>.
    /// </summary>
    public void ShowCaret()
    {
        HasCaret = !IsReadOnly;
        if (HasCaret) StartBlinking();
        InvalidateVisual();
    }

    /// <summary>Blinks the caret. Runs only while this content holds the caret and is torn down on unload, so a page of them leaves no timers behind.</summary>
    private void StartBlinking()
    {
        _caretVisible = true;
        if (_blink is not null) { _blink.Stop(); _blink.Start(); return; }

        _blink = new DispatcherTimer(BlinkRate, DispatcherPriority.Normal, OnBlink, Dispatcher);
        _blink.Start();
    }

    private void StopBlinking()
    {
        _blink?.Stop();
        _blink = null;
        _caretVisible = true;
    }

    private void OnBlink(object? sender, EventArgs e)
    {
        if (!HasCaret) { StopBlinking(); return; }
        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    /// <summary>Shows the caret and restarts the cycle — it must never be mid-blink while you type.</summary>
    protected void HoldCaretVisible()
    {
        if (!HasCaret) return;
        _caretVisible = true;
        _blink?.Stop();
        _blink?.Start();
    }

    /// <summary>Moves the caret one stop. False when it ran off an end, having raised <see cref="Exited"/> for the host to take over.</summary>
    public bool MoveCaret(bool forward, bool extend = false)
    {
        // A stretch being shown as its characters is text, and moves like text: one character at a time.
        // Stepping by layout stops cannot reach into it — every position inside maps to the one point
        // where it sits in the laid-out content — so the caret jumped clean over the thing the reader had
        // just asked to see, which is the only place they wanted to edit.
        if (Typed(_state.Caret) is { } typed)
        {
            var step = _state.Caret + (forward ? 1 : -1);
            if (step >= typed.Start && step <= typed.End) { MoveTo(step, -1, extend); return true; }
        }

        // One step along the places, or — for a caret standing where none of them is, which is what a
        // stretch shown as its own characters leaves behind — the nearest one past the offset it is at.
        var next = _at >= 0 ? _laid.Step(_at, forward) : Rejoining(forward);

        if (next is not { } landed)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        MoveTo(_laid.Places[landed].Offset, landed, extend);
        return true;
    }

    /// <summary>The place a caret standing nowhere rejoins the declared ones at, or null at the edge.</summary>
    private int? Rejoining(bool forward) =>
        _laid.Root.StopPast(_state.Caret, forward) is >= 0 and var at ? at : null;

    /// <summary>Up and down — across a fraction bar, out of a script, from a note to the word under it.</summary>
    public bool MoveCaretVertically(bool up, bool extend = false)
    {
        if (_laid.Root.StepVertical(_state.Caret, up) is not { } next) return false;

        MoveTo(next, _laid.Root.StopAt(next), extend);
        return true;
    }

    /// <summary>
    /// Puts the caret at <paramref name="offset"/>, or stretches what is picked out to it — Home and End, and a caret put
    /// somewhere by whoever is hosting this.
    /// </summary>
    public void MoveCaretTo(int offset, bool extend = false) => MoveTo(Snap(offset), -1, extend);

    private void MoveTo(int offset, int at, bool extend)
    {
        // Extending is about a stretch of source, and a stretch has no places — which of the marks at its
        // far end the caret would have been drawn as says nothing about what is picked out.
        if (extend) ExtendSelectionTo(offset);
        else Apply(_state.MoveCaretTo(offset), notify: false, at);
    }

    private int Snap(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _state.Source.Length);

        // Inside the stretch being written every character is its own stop, so the caret goes exactly
        // where it was put; the settled content snaps to the places a caret may rest.
        return Typed(clamped) is not null ? clamped : _laid.NearestStop(clamped);
    }

    /// <summary>
    /// The stretch of source a caret at this offset steps through a character at a time, or null where it
    /// stands among the declared places. Covers both a stretch mid-typing and an ordinary run of text — neither
    /// has a layout stop per character, so in both the caret goes exactly where it was put.
    /// </summary>
    private (int Start, int End)? Typed(int offset)
    {
        if (_state.Raw is { } zone && zone.Holds(offset)) return (zone.Start, zone.End);

        return _laid.Root.WordsAt(offset) is { Part: { } part } ? (part.Start, part.End()) : null;
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Type(char character) => Write(character.ToString());

    /// <summary>
    /// Writes text at the caret: whatever the content makes of it, and failing that the characters
    /// themselves, spliced in where the caret is.
    /// </summary>
    protected void Write(string text)
    {
        if (IsReadOnly) return;

        Apply(Typing(_state, text) ?? _state.Write(text), notify: true);
    }

    /// <summary>
    /// Inserts text at the caret, replacing any selection — how a palette key types itself.
    /// <paramref name="caretBack"/> walks the caret into a template's first hole.
    /// </summary>
    public void Insert(string text, int caretBack = 0)
    {
        if (IsReadOnly) return;

        // Something picked out and a construct with a hole in it: what you picked goes in the hole.
        if (_state.HasSelection && WrapSelectionInto(text, caretBack)) return;

        // A palette key and a pasted formula land in a construct the same way a typed character does.
        // Only when the template wants the caret walked back into a hole of its own, which is about the
        // text and not the structure.
        if (caretBack == 0 && Typing(_state, text) is { } written) { Apply(written, notify: true); return; }

        Apply(_state.Insert(text, caretBack), notify: true);
    }

    /// <summary>
    /// Puts what is selected into the hole of <paramref name="template"/> the caret would have gone to.
    /// Which hole needs no new information: <paramref name="caretBack"/> already says where a key expects
    /// to be typed next — a <c>\frac</c> pressed over a selected <c>3+7</c> puts it in the numerator.
    /// </summary>
    private bool WrapSelectionInto(string template, int caretBack)
    {
        var at = template.Length - caretBack;
        if (at <= 0 || at >= template.Length) return false;
        if (template[at - 1] != '{' || template[at] != '}') return false;

        Apply(_state.Insert(template[..at] + _state.SelectedText + template[at..]), notify: true);

        // The template's other arguments are still empty, and the builder has just drawn a hole in each.
        // Selecting the first is what makes the next keystroke fill it.
        SelectNextPlaceholder();
        return true;
    }

    /// <summary>Wraps the selection, or inserts the pair at the caret.</summary>
    public void Wrap(string before, string after)
    {
        if (IsReadOnly) return;
        Apply(_state.Wrap(before, after), notify: true);
    }

    /// <summary>Backspace; un-renders a construct drawn from more source than it shows rather than deleting a character of it. False (nothing to delete) is the host's cue to remove the content itself.</summary>
    public bool Backspace()
    {
        if (IsReadOnly) return false;
        if (_content.Erasing(Landing, forward: false) is { } erased) { Apply(erased, notify: true); return true; }
        if (_state is { Caret: 0, SelectionLength: 0 }) return false;

        Apply(Backspacing(_state) ?? _state.Backspace(), notify: true);
        return true;
    }

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    public bool Delete()
    {
        if (IsReadOnly) return false;

        // Asked before the end of the source is: past the last thing written in a diagram is its end, and a delete handed
        // back to the document from there takes whatever the document has next.
        if (_content.Erasing(Landing, forward: true) is { } erased) { Apply(erased, notify: true); return true; }
        if (_state.Caret >= _state.Source.Length && !_state.HasSelection) return false;

        Apply(_state.Delete(), notify: true);
        return true;
    }

    /// <summary>Settles whatever is half-written, as space or Enter does — just writes the character; the ending rule lives with the content's typing rule, not here, to avoid two rules disagreeing.</summary>
    bool IEditableBlock.Commit(string text) { if (!IsReadOnly) Settle(text); return true; }

    /// <summary>
    /// Ends whatever is half-written — what space and Enter mean. The content's rule, not the element's;
    /// see <see cref="IContent.Settle"/>.
    /// </summary>
    protected void Settle(string separator) =>
        Apply(_content.Settle(Landing, separator), notify: true);

    /// <summary>Selects the next place still waiting to be written in, so an inserted construct can be filled by typing and tabbing. False when there is none.</summary>
    public bool SelectNextPlaceholder(bool forward = true)
    {
        if (IsReadOnly) return false;

        // Read off what was drawn rather than off the text: a hole is a symbol the builder put there, and
        // the source it stands over is the empty braces the reader actually wrote.
        var holes = Holes();
        if (holes.Count == 0) return false;

        // From wherever the caret is, wrapping round — the last hole tabs back to the first, because a
        // construct being filled in is a loop until it is finished.
        var here = _state.HasSelection ? _state.SelectionStart : _state.Caret;
        var next = forward
            ? holes.FirstOrDefault(hole => hole.Sits().Start > here, holes[0])
            : holes.LastOrDefault(hole => hole.Sits().Start < here, holes[^1]);

        // The caret goes into the hole rather than over it. A hole covers nothing — that is what makes it
        // a hole — so what gets typed lands inside the braces and it stops being one.
        TakeCaret(next.Sits().Start);
        return true;
    }

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>Selects a source range, snapped out to whole constructs.</summary>
    public void Select(int start, int length)
    {
        if (length <= 0) { ClearSelection(); return; }

        var (from, snapped) = _laid.Root.Snap(start, length);

        var next = _state.Select(from, snapped);
        if (next.Selection.SequenceEqual(_state.Selection)) return;

        Apply(next, notify: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SelectRange(int start, int length) => Select(start, length);

    /// <summary>Selects everything — what the host asks for when a selection sweeps straight over it.</summary>
    public void SelectAll() => Select(0, _state.Source.Length);

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (!_state.HasSelection) return;

        Apply(_state.Select(0, 0), notify: false);
        InteractiveSelection.Release(this);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // Where nothing is picked out yet, what is picked out runs from the caret: Shift and an arrow start there, wherever the
    // caret was put since the last press.
    private void ExtendSelectionTo(int offset)
    {
        if (!_state.HasSelection) _anchor = _state.Caret;

        Select(Math.Min(_anchor, offset), Math.Abs(offset - _anchor));
    }

    /// <summary>Takes a selection worked out over the layout tree, in the source's own offsets.</summary>
    private void SelectNodes(ContentSelection selection)
    {
        if (selection.IsEmpty) { ClearSelection(); return; }

        var next = _state.Select([.. selection.Ranges.Select(range => new EditRange(range.Start, range.Length))]);
        if (next.Selection.SequenceEqual(_state.Selection)) return;

        Apply(next, notify: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Pointer, driven by the host ─────────────────────────────────────────

    /// <summary>
    /// A press landed at <paramref name="at"/>. True where it meant something — which is the end of it, and no caret is
    /// placed and nothing is selected. Nothing, unless the content declares what its pieces answer to.
    /// </summary>
    protected virtual bool Pressed(Point at, ModifierKeys modifiers) => false;

    /// <summary>Two presses landed at <paramref name="at"/>. True where that meant something.</summary>
    protected virtual bool Chosen(Point at) => false;

    /// <summary>A piece was picked by a press at <paramref name="at"/>. It is chosen either way; this is only the telling.</summary>
    protected virtual void Picked(Point at) { }

    /// <inheritdoc />
    public void BeginPointerSelect(Point pointInElement) => BeginPointerSelect(pointInElement, ModifierKeys.None);

    /// <inheritdoc />
    public void BeginPointerSelect(Point pointInElement, ModifierKeys modifiers)
    {
        InteractiveSelection.Own(this);

        var at = Unscaled(pointInElement);
        _pressedAt = pointInElement;
        _moving = false;

        // A piece that answers to a press means what it answers with, and not a place to put the caret.
        if (Pressed(at, modifiers)) return;

        // Several things chosen at once: what Ctrl presses is added to what is chosen, or taken back out of it.
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            _dragging = false;
            Toggle(at);
            return;
        }

        // From where the choosing started to the press, as a drag from there would choose — from the caret, where nothing is
        // chosen yet — and a drag after it goes on choosing from the same place.
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (!_state.HasSelection)
            {
                _anchor = _state.Caret;
                _anchorNode = _laid.Root.WordsAt(_anchor);
            }

            _dragging = true;
            ChooseTo(at);
            return;
        }

        _anchor = _laid.OffsetAt(at);
        _anchorNode = _laid.PieceAt(at);
        _dragging = true;

        // Pressing on what is already selected is how a move begins — the reader is picking the term up,
        // not starting a new selection over it. The selection is kept until the button comes back up, so a
        // press that turns out to be an ordinary click can still fall through to placing the caret.
        if (Covers(_anchor)) { _moving = true; _dropAt = _anchor; return; }

        ClearSelection();

        // A press squarely on something means that thing; a press at a stop — between two things, or at the
        // edge of one — means the place. One rule for every kind of content: a note pressed is a note
        // picked, and a letter pressed at its edge is a caret put down beside it.
        // A run of text is written in rather than picked up, so a press inside one is a caret between two of its
        // letters — including the press that has to show it as written before there is anywhere to put one.
        if (Writing(_anchorNode, at)) return;

        if (On(_anchorNode, at)) { SelectNodes(ContentSelection.Of(_anchorNode)); Picked(at); return; }

        TakeCaret(_laid.Root.OffsetAt(at), _laid.StopNear(at));
    }

    /// <summary>Adds what a press lands on to what is chosen — or, where all of it is chosen already, takes it back out.</summary>
    private void Toggle(Point at)
    {
        var piece = Pointing(_laid.PieceAt(at));
        if (!piece.Exists || piece.Sits() is not { Length: > 0 } sits) return;

        _anchor = sits.Start;
        _anchorNode = piece;

        var pressed = new EditRange(sits.Start, sits.Length);
        var chosen = _state.Selection;

        IReadOnlyList<EditRange> next = chosen.Any(range => range.Start <= pressed.Start && range.End >= pressed.End)
            ? [.. chosen.SelectMany(range => Outside(range, pressed))]
            : [.. chosen, pressed];

        if (next.Count == 0) { ClearSelection(); return; }

        var state = _state.Select(next);
        if (state.Selection.SequenceEqual(_state.Selection)) return;

        Apply(state, notify: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Picked(at);
    }

    /// <summary>What of <paramref name="range"/> lies outside <paramref name="taken"/>.</summary>
    private static IEnumerable<EditRange> Outside(EditRange range, EditRange taken)
    {
        if (taken.End <= range.Start || taken.Start >= range.End)
        {
            yield return range;
            yield break;
        }

        if (taken.Start > range.Start) yield return new EditRange(range.Start, taken.Start - range.Start);
        if (taken.End < range.End) yield return new EditRange(taken.End, range.End - taken.End);
    }

    /// <summary>
    /// Puts the caret inside a run of text that was pressed, and says whether it did. A run showing something
    /// worked out (a rounded value, a percentage) has nowhere to put a caret since what is drawn isn't what
    /// was written — pressing it reveals the source first, then re-answers the press against that.
    /// </summary>
    private bool Writing(Piece piece, Point at)
    {
        if (piece.Words is not { } words || piece.Part is not { } part) return false;

        if (!words.Maps)
        {
            // A run that only says something about its part — the share of a pie a slice takes — is not written in: the
            // press means the slice, which is what the ordinary rules already do with it.
            if (IsReadOnly || !words.Writes) return false;

            Apply(_state.MoveCaretTo(part.Start) with { Raw = new RawZone(part.Start, part.End()) }, notify: false);
        }

        TakeCaret(_laid.Root.OffsetAt(Unscaled(_pressedAt)), -1);
        return true;
    }

    /// <summary>How far past a written run the pointer still counts as inside it — just over half the widest gap on a formula's line, so moving along one never flickers to an arrow between glyphs.</summary>
    private const double PointerReach = 4.0;

    /// <summary>What the pointer should be at a point: see <see cref="OnMouseMove"/>.</summary>
    protected virtual Cursor Pointing(Point at) =>
        !IsReadOnly && Laid.Root.Writable(at, PointerReach) ? Cursors.IBeam : Cursors.Arrow;

    /// <inheritdoc/>
    Cursor? IInteractiveBlock.PointerCursor(Point pointInElement) => Pointing(Unscaled(pointInElement));

    /// <summary>
    /// Whether a press lands on <paramref name="piece"/> itself rather than at one of its stops. The reach is
    /// capped at a quarter of the piece's width so a letter as narrow as an "i" still has a place at each side.
    /// </summary>
    private static bool On(Piece piece, Point at)
    {
        if (!piece.Exists) return false;

        var box = piece.Ink();
        if (box.IsEmpty || !box.Contains(at)) return false;

        var reach = Math.Min(CaretReach, box.Width / 4);
        return at.X - box.Left > reach && box.Right - at.X > reach;
    }

    /// <summary>How near a stop a press has to be to mean the stop rather than the thing, in layout pixels.</summary>
    private const double CaretReach = 3.0;

    /// <summary>Whether <paramref name="offset"/> falls inside one of the selected stretches.</summary>
    private bool Covers(int offset) =>
        _state.Selection.Any(range => offset >= range.Start && offset <= range.End);

    /// <summary>
    /// The pieces that answer to a gesture and are covered by what is picked out — empty where nothing is, which is
    /// what makes "for one item" and "for a selection of them" the same question asked twice.
    /// </summary>
    protected IReadOnlyList<Piece> Selected() =>
        [.. _laid.Root.SelfAndDescendants()
                 .Where(piece => piece.Acts is not null && piece.Sits() is { Length: > 0 } sits && Covers(sits.Start))];

    /// <inheritdoc />
    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging) return;

        // A click is not a drag. The pointer moves a pixel or two under any real hand, and treating that
        // as a selection meant clicking after a number selected it — so the next key typed replaced the
        // number instead of following it.
        if (!HasDragged(pointInElement)) return;

        var at = Unscaled(pointInElement);

        // Carrying something: the content is shown as it would read if it were let go here, with the
        // carried part marked out, so the reader is choosing between finished results.
        if (_moving)
        {
            var drop = _laid.OffsetAt(at);
            if (drop == _dropAt) return;

            _dropAt = drop;
            _dropPoint = at;
            BuildPreview();
            HoldCaretVisible();
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        ChooseTo(at);
    }

    /// <summary>Chooses from the anchor to <paramref name="at"/>: what a drag there chooses, and what Shift and a press there choose.</summary>
    private void ChooseTo(Point at)
    {
        // Inside one run of text it picks out characters, because that is what dragging through text means. Everywhere else
        // it is whole pieces — see below.
        if (_anchorNode.Words is { Maps: true } && _laid.PieceAt(at) == _anchorNode)
        {
            ExtendSelectionTo(_laid.OffsetAt(at));
            return;
        }

        // What was dragged over is a set of pieces, not a stretch of text. Inside a matrix that is what makes a drag down a
        // column select the column rather than everything written between its top cell and its bottom one.
        if (Pointing(_anchorNode) is { Exists: true } from && Pointing(_laid.PieceAt(at)) is { Exists: true } focus)
        {
            // Through whatever owns each end. Landing on a bracket means the group it opens or closes: half a pair is not a
            // smaller selection, it is one that cannot be read.
            SelectNodes(ContentSelection.Between(_laid.Root, from, focus));
            return;
        }

        ExtendSelectionTo(_laid.OffsetAt(at));
    }

    /// <summary>
    /// Whether the pointer has moved far enough from the press for this to be a drag rather than a click.
    /// The system's own thresholds, so it matches every other drag the reader makes.
    /// </summary>
    private bool HasDragged(Point pointInElement) =>
        Math.Abs(pointInElement.X - _pressedAt.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(pointInElement.Y - _pressedAt.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <inheritdoc />
    public void EndPointerSelect()
    {
        _dragging = false;
        if (!_moving) return;

        _moving = false;
        var settled = _previewOf;
        ClearPreview();

        if (IsReadOnly) return;

        // The press never became a drag: an ordinary click on the selection, which places the caret there
        // and drops the selection, as clicking a selection does everywhere.
        if (settled is not { } moved) { ClearSelection(); TakeCaret(_anchor); return; }

        // Exactly what was on screen a moment ago — settling is letting go of it, not recomputing
        // something the reader has to check.
        Apply(new EditState(moved.Source, moved.Caret), notify: true);
    }

    /// <summary>Lays the content out as it would read if what is carried were dropped where it is now.</summary>
    private void BuildPreview()
    {
        ClearPreview();

        if (Moving(_state, _dropAt) is not { } moved) return;

        _previewOf = moved;
        _previewMoved = (moved.Wrote.Start, moved.Wrote.End);
        _preview = Lay(new EditState(moved.Source, moved.Caret), Room());
    }

    private void ClearPreview()
    {
        _preview = null;
        _previewOf = null;
        _previewMoved = default;
    }

    /// <inheritdoc />
    public bool PointerDoubleClick(Point pointInElement)
    {
        // Select the thing under the pointer rather than letting the host drop the whole block into
        // source-edit mode: inside content, "the word you clicked" is the symbol you clicked.
        var at = Unscaled(pointInElement);
        if (Chosen(at)) return true;

        var here = _laid.OffsetAt(at);

        var under = Pointing(_laid.PieceAt(at));

        // In a run of text, the word you pressed is a word of it rather than the whole run.
        if (under is { Words: { Maps: true } words, Part: { } part })
        {
            var (from, to) = words.WordAt(here - part.Start);
            Select(part.Start + from, to - from);
        }
        else if (under.Exists && under.Sits() is { Length: > 0 } sits) Select(sits.Start, sits.Length);
        else Select(Math.Max(0, here - 1), 1);

        return true;
    }

    // Hosted in a plain panel (the read-only markdown view), the element does get its own mouse events.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { PointerDoubleClick(e.GetPosition(this)); return; }

        BeginPointerSelect(e.GetPosition(this));
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // The pointer says what can be done where it is: a bar over what can be written in, an arrow over a wedge of a
        // pie, a barcode, a staff line — drawing nobody types into.
        Cursor = Pointing(Unscaled(e.GetPosition(this)));
        ForceCursor = true;

        if (_dragging) ExtendPointerSelect(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        EndPointerSelect();
    }

    // ── Applying an edit ────────────────────────────────────────────────────

    /// <param name="at">
    /// Which place the caret is standing at, when a step has just said. -1 otherwise, which puts it back at
    /// the innermost place at its offset — where a reader who has just typed, clicked or jumped is.
    /// </param>
    protected void Apply(EditState next, bool notify, int at = -1)
    {
        if (notify && next.Source != _state.Source) next = _content.Edited(Landing, next);
        next = Left(next);

        var resized = next.Source != _state.Source || next.Raw != _state.Raw;
        var was = (_state.Caret, _at);
        var changed = next.Source != _state.Source;

        _state = next;

        if (resized) { Rebuild(); InvalidateMeasure(); }

        // An index is only ever true of the tree it was taken from, so one handed in from before a rebuild
        // says nothing about the tree there is now — and the caret goes back to the innermost place at its
        // offset. That is also why nothing has to remember to clear it.
        _at = resized || at < 0 || at >= _laid.Places.Count ? _laid.Root.StopAt(_state.Caret) : at;

        var moved = was != (_state.Caret, _at);

        if (moved || changed) HoldCaretVisible();   // never blink out mid-keystroke
        InvalidateVisual();

        if (moved) CaretMoved?.Invoke(this, EventArgs.Empty);
        if (notify && changed) SourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops showing a run of text as written once the caret has left it. Only a stretch that is exactly a run
    /// of text — one the content itself opened (a command half typed) is the content's own business to close.
    /// </summary>
    private EditState Left(EditState next)
    {
        if (next.Raw is not { } zone || zone.Holds(next.Caret)) return next;

        var run = _laid.Root.SelfAndDescendants().Any(piece =>
            piece.Words is not null && piece.Part is { } part && part.Start == zone.Start && part.End() == zone.End);

        return run ? next with { Raw = null } : next;
    }

    /// <summary>Lays the content out again, because something outside it changed.</summary>
    public void Refresh()
    {
        Rebuild();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>How much room the content has, in its own coordinates — which is the room it was given.</summary>
    protected double Room() => _laidFor > 0 ? _laidFor : 680;

    /// <summary>Lays it out again from the state as it now stands.</summary>
    protected void Rebuild() => _laid = Lay(_state, Room());

    /// <summary>A picture of the content at its shown size/density, with nothing drawn only for the writer: no caret, selection, hole, trouble squiggle or raw-typed text.</summary>
    /// <param name="ground">What it is drawn on, or null for nothing behind what the content draws.</param>
    public BitmapSource Picture(Brush? ground = null)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var laid = _content.Lay(_state with { Selected = null, Raw = null }, Room(), readOnly: true);
        var size = new Size(Math.Max(1, Math.Ceiling(laid.Size.Width * Scale)), Math.Max(1, Math.Ceiling(laid.Size.Height * Scale)));

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            if (ground is not null) dc.DrawRectangle(ground, null, new Rect(size));

            var scaled = Math.Abs(Scale - 1.0) > 0.001;
            if (scaled) dc.PushTransform(new ScaleTransform(Scale, Scale));
            LayoutPainter.Paint(dc, laid.Root, Palette.Text);
            if (scaled) dc.Pop();
        }

        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width * pixelsPerDip), (int)Math.Ceiling(size.Height * pixelsPerDip),
                                             96 * pixelsPerDip, 96 * pixelsPerDip, PixelFormats.Pbgra32);
        picture.Render(drawing);
        picture.Freeze();
        return picture;
    }

    // ── Layout and painting ─────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var room = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 680 : availableSize.Width;
        if (Math.Abs(room - _laidFor) > 0.5 || _laid.Tree.Count == 0)
        {
            _laidFor = room;
            Rebuild();
        }

        // While something is being carried, what is on screen is what it would become, so that is what has
        // to fit — otherwise the preview is clipped at the settled content's width.
        var size = _preview?.Size ?? _laid.Size;
        return new Size(Math.Ceiling(size.Width * Scale), Math.Ceiling(size.Height * Scale));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A transparent fill makes the whole element hit-testable, gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        // Everything below is in the content's own coordinates. The scale is pushed once, here, so nothing
        // that reads the tree has to know about it — and a pointer coming the other way is divided by it.
        var scaled = Math.Abs(Scale - 1.0) > 0.001;
        if (scaled) dc.PushTransform(new ScaleTransform(Scale, Scale));

        if (_preview is { } preview) PaintPreview(dc, preview);
        else PaintContent(dc);

        if (scaled) dc.Pop();
    }

    private void PaintContent(DrawingContext dc)
    {
        LayoutPainter.Paint(dc, _laid.Root, Palette.Text);

        // One shape for the whole selection, joined across the spacing between what it holds — and not one box
        // around it all: a column of a matrix washed from its first cell to its last would highlight the lot.
        if (_state.HasSelection) dc.DrawGeometry(_wash, null, _laid.Root.Wash(_state.Selection, WashPad));

        // A wave under whatever could not be read, drawn over the content rather than instead of it: the
        // parts that did read are still worth looking at, and the reader needs to see which part is not.
        foreach (var trouble in _laid.Trouble)
        {
            var runs = LayoutQuery.Clusters(_laid.Root.RangeRects(trouble.Start, trouble.Length), 0);
            if (runs.Count == 0) continue;

            var wave = new Pen(trouble.Severity == DiagnosticSeverity.Error ? Palette.Danger : Palette.Warning, 1.0);
            wave.Freeze();
            dc.DrawGeometry(null, wave, Squiggle.Under(runs));
        }

        PaintOver(dc);

        if ((!HasCaret && !_moving) || IsReadOnly || !_caretVisible) return;

        // While something is being carried the caret shows where it would land, not where it was picked
        // up from — that is the one thing the reader needs to see before letting go.
        var caret = _moving || _at < 0
            ? _laid.Root.CaretRect(_moving ? _dropAt : _state.Caret)
            : _laid.Places[_at].CaretRect();

        DrawCaret(dc, caret.X, caret.Y, caret.Height);
    }

    /// <summary>Anything the content draws over the shared picture (e.g. a strike-through on a symbol that won't encode). Drawn after the ink and wash, before the caret.</summary>
    protected virtual void PaintOver(DrawingContext dc) { }

    /// <summary>Draws the content as it would read after the drop, with the carried part in the accent colour — once it's merged in (braces, spacing and all) nothing else would distinguish it.</summary>
    private void PaintPreview(DrawingContext dc, Laid preview)
    {
        LayoutPainter.Paint(dc, preview.Root, Palette.Text);

        // Over the top rather than instead of: painting all of it and then the carried part again is what
        // keeps this to two calls, and the second colour is the one that shows.
        foreach (var piece in Carried(preview))
            LayoutPainter.PaintOne(dc, piece, Palette.Accent);
    }

    /// <summary>The outermost pieces of <paramref name="preview"/> lying wholly inside what is carried — outermost so a piece and its children aren't painted twice.</summary>
    private IEnumerable<Piece> Carried(Laid preview)
    {
        var (start, end) = _previewMoved;
        if (end <= start) yield break;

        var taken = new List<Piece>();
        foreach (var piece in preview.Root.SelfAndDescendants())
        {
            if (piece.Sits() is not { Length: > 0 } at || at.Start < start || at.End > end) continue;
            if (taken.Any(already => piece.Ancestors().Contains(already))) continue;

            taken.Add(piece);
            yield return piece;
        }
    }

    /// <summary>A wash a little larger than what it marks — see <see cref="WashPad"/>.</summary>
    private Rect Marked(Rect rect)
    {
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return rect;

        rect.Inflate(WashPad, WashPad);
        return rect;
    }

    /// <summary>The caret itself. One place, so content that is empty draws the same one as any other.</summary>
    private void DrawCaret(DrawingContext dc, double x, double y, double height)
    {
        var pen = new Pen(Palette.Accent, 1.4);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, y), new Point(x, y + Math.Max(height, 1)));
    }

    /// <summary>A point in the element's own pixels, read as a point in the content's.</summary>
    protected Point Unscaled(Point point) =>
        Math.Abs(Scale - 1.0) < 0.001 ? point : new Point(point.X / Scale, point.Y / Scale);
}
