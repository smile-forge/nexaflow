using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using Nexaflow.Visuals.Text.Editing;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A typeset formula you can click into and type in: it draws the maths, a selection wash over any part
/// of it, a caret shaped like whatever it stands beside, and any half-written command shown literally
/// where it will end up.
/// <para>
/// It owns pixels and gestures only. Where things are is <see cref="LatexLayout"/>'s answer and what an
/// edit means is <see cref="LatexEditState"/>'s; this holds no second opinion about either, which is what
/// keeps clicking, arrowing, selecting and typing from each developing their own idea of the formula.
/// </para>
/// <para>
/// It follows <c>ScoreElement</c>, the other code-drawn block embedded in markdown, and for the same
/// reasons: a <see cref="UIElement"/> inside a <c>RichTextBox</c> does not reliably receive mouse input,
/// so the host hit-tests geometrically and drives <see cref="IInteractiveBlock"/>; and it must never take
/// focus, or the <c>RichTextBox</c> faults reconciling its caret against a node holding no text. Keys
/// arrive from the host for the same reason. The direct mouse handlers below only matter when it is
/// hosted in a plain panel, as the read-only markdown view does.
/// </para>
/// </summary>
public sealed class FormulaElement : FrameworkElement, IEditableBlock
{
    private readonly MarkdownPalette _palette;
    private readonly Brush _wash;
    private readonly double _scale;
    private readonly bool _inline;

    private LatexEditState _state;
    private LatexLayout? _layout;
    private DispatcherTimer? _blink;
    private bool _caretVisible = true;
    private int _anchor;
    private ILayoutNode? _anchorNode;
    private Point _pressedAt;
    private bool _dragging;

    /// <summary>A term is being carried to a new place; <see cref="_dropAt"/> is where it would land.</summary>
    private bool _moving;
    private int _dropAt;

    /// <summary>Where the pointer is, which says things an offset cannot - that a block is being held
    /// between two columns of a matrix rather than over one of its cells.</summary>
    private Point _dropPoint;

    /// <summary>
    /// The formula as it would read if the carried term were let go here, typeset for real and drawn
    /// instead of the settled one — so the reader is choosing between finished formulas rather than
    /// imagining what an insertion bar would produce. A full parse and typeset costs a few
    /// milliseconds and is only paid when the drop point crosses a caret stop, not per pixel of mouse
    /// movement, so it comfortably keeps up with a hand.
    /// </summary>
    private LatexLayout? _preview;
    private LatexWrite? _previewOf;
    private (int Start, int End) _previewMoved;

    /// <summary>
    /// Windows' own caret rate. WPF does not surface <c>GetCaretBlinkTime</c>, and a P/Invoke for a
    /// number that has been 530ms since Windows 95 is not worth the trouble.
    /// </summary>
    private static readonly TimeSpan BlinkRate = TimeSpan.FromMilliseconds(530);

    /// <summary>Raised when the caret moves, so a host can follow it.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised when the selection changes, including when it is cleared.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised whenever the source changes, so the host can fold it back into its own model.</summary>
    public event EventHandler? LatexChanged;

    /// <summary>
    /// Raised when a caret movement ran off the end. The host answers by moving the caret into the text
    /// on that side — this element has no idea what surrounds it.
    /// </summary>
    public event EventHandler<BlockExit>? Exited;

    public FormulaElement(string latex, MarkdownPalette palette, double scale, bool inline = false)
    {
        _state = LatexEditState.For(latex ?? string.Empty);
        _palette = palette;
        _scale = scale;
        _inline = inline;
        _wash = Wash(palette);

        SnapsToDevicePixels = true;
        Cursor = Cursors.IBeam;

        // Never take keyboard focus — see the class remarks.
        Focusable = false;

        // Subscribed once, here, rather than wherever the timer happens to be created: a formula that is
        // clicked into and left repeatedly would otherwise collect a handler per visit.
        Unloaded += (_, _) => StopBlinking();

        Rebuild();
    }

    /// <summary>The source. Setting it re-typesets and puts the caret at the end.</summary>
    public string Latex
    {
        get => _state.Latex;
        set
        {
            var next = value ?? string.Empty;
            if (_state.Latex == next) return;
            Apply(LatexEditState.For(next), notify: false);
        }
    }

    /// <summary>The map behind the typeset part, or null while none of it will typeset.</summary>
    public LatexLayout? Layout => _layout;

    /// <summary>
    /// Whether any of the source could not be read. It may still be drawing perfectly well around the
    /// trouble — a formula stops being typeset entirely only when none of it could be laid out at all.
    /// </summary>
    public bool HasError => _layout is null || _layout.Tree.Diagnostics.Count > 0;

    /// <summary>Whether the caret is shown. A read-only surface still allows selecting and copying.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Where this formula's LaTeX sits inside its markdown block, delimiters excluded — what a host
    /// needs to put an edit back where it came from. Negative when the whole block is the formula, as a
    /// <c>$$…$$</c> block is, and there is nothing to splice around.
    /// </summary>
    public int SourceStart { get; set; } = -1;

    /// <summary>How much of the block's source this formula occupies. Kept current as it is edited.</summary>
    public int SourceLength { get; set; }

    /// <summary>Whether the whole markdown block is this formula rather than a run inside one.</summary>
    public bool IsWholeBlock => SourceStart < 0;

    /// <summary>Whether this element currently owns the caret.</summary>
    public bool HasCaret { get; private set; }

    /// <summary>Where the caret sits, as an offset into <see cref="Latex"/>.</summary>
    public int Caret => _state.Caret;

    /// <summary>The start of the selected source range.</summary>
    public int SelectionStart => _state.SelectionStart;

    /// <summary>How much source is selected; zero when nothing is.</summary>
    public int SelectionLength => _state.SelectionLength;

    /// <summary>The selected source, or empty.</summary>
    public string SelectedText => _state.SelectedText;

    /// <summary>
    /// The stretch being shown as the characters written rather than typeset — a command mid-spelling,
    /// or a construct un-rendered to be edited — or null when all of it is set as maths.
    /// <para>
    /// It is in the formula's own offsets, because the formula it is part of is typeset around it
    /// rather than without it.
    /// </para>
    /// </summary>
    public (int Start, int Length)? ShownAsWritten =>
        _state.Raw is { Length: > 0 } zone ? (zone.Start, zone.Length) : null;

    // ── What the document around it needs (IEditableBlock) ──────────────────

    /// <inheritdoc />
    public string Source => _state.Latex;

    /// <inheritdoc />
    public ILayoutNode? Root => _layout?.Tree.Root;

    /// <inheritdoc />
    IReadOnlyList<(int Start, int Length)> IEditableBlock.Selection =>
        [.. _state.Selection.Select(r => (r.Start, r.Length))];

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> Diagnostics => _layout?.Tree.Diagnostics ?? [];

    /// <inheritdoc />
    public void SelectRange(int start, int length) => Select(start, length);

    /// <inheritdoc />
    /// <remarks>
    /// A formula is one expression, read from its start — so only a step <em>along</em> the text can
    /// land anywhere but the beginning of it, and then only at the end, which is the character you
    /// stepped back onto. Up and down both land at the start, because a line step goes to where the
    /// line begins and this whole formula is that line. <see cref="CaretArrival.Column"/> is ignored
    /// for the same reason: landing part-way along because that is where the column fell would drop the
    /// reader into the middle of a subscript they were only passing over.
    /// </remarks>
    public void TakeCaretArriving(CaretArrival arrival) =>
        TakeCaret(arrival is { Step: CaretStep.Character, Edge: BlockExit.After } ? _state.Latex.Length : 0);

    // ── Caret ownership ─────────────────────────────────────────────────────

    /// <summary>Gives this formula the caret at <paramref name="offset"/>.</summary>
    public void TakeCaret(int offset)
    {
        HasCaret = !IsReadOnly;
        Apply(_state.MoveCaretTo(Snap(offset)), notify: false);
        if (HasCaret) StartBlinking();
    }

    /// <summary>
    /// Blinks the caret, because a still one is easy to lose among the glyphs. It runs only while this
    /// formula holds the caret and is torn down on unload, so a page of formulas leaves no timers behind.
    /// </summary>
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
    private void HoldCaretVisible()
    {
        if (!HasCaret) return;
        _caretVisible = true;
        _blink?.Stop();
        _blink?.Start();
    }

    /// <inheritdoc />
    /// <summary>Gives up the caret (the host moved it into the text, or to another formula).</summary>
    public void ReleaseCaret()
    {
        if (!HasCaret) return;
        HasCaret = false;
        StopBlinking();
        InvalidateVisual();
    }

    /// <summary>
    /// Moves the caret one stop. Returns false when it ran off an end, having raised
    /// <see cref="Exited"/> — the host then takes over.
    /// </summary>
    public bool MoveCaret(bool forward, bool extend = false)
    {
        // A stretch being shown as its characters is text, and moves like text: one character at a
        // time. Stepping by layout stops cannot reach into it — every position inside maps to the one
        // point where it sits in the typeset formula — so the caret jumped clean over the thing the
        // reader had just asked to see, which is the only place they wanted to edit.
        if (_state.Raw is { } zone && zone.Holds(_state.Caret))
        {
            var step = _state.Caret + (forward ? 1 : -1);
            if (step >= zone.Start && step <= zone.End) { MoveTo(step, extend); return true; }
        }

        var here = _state.Caret;
        var next = _layout?.Tree.Step(here, forward);
        if (next is null)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        MoveTo(next.Value, extend);
        return true;
    }

    /// <summary>Moves the caret to the line above or below — across a fraction bar, out of a script.</summary>
    public bool MoveCaretVertically(bool up, bool extend = false)
    {
        var next = _layout?.Tree.StepVertical(_state.Caret, up);
        if (next is null) return false;

        MoveTo(next.Value, extend);
        return true;
    }

    private void MoveTo(int offset, bool extend)
    {
        if (extend) ExtendSelectionTo(offset);
        else Apply(_state.MoveCaretTo(offset), notify: false);
    }

    // ── Editing ─────────────────────────────────────────────────────────────

    /// <summary>Types one character at the caret. No-op when read-only.</summary>
    public void Type(char character)
    {
        if (IsReadOnly) return;
        if (WriteThroughTree(character.ToString())) return;

        Apply(_state.Type(character), notify: true);
    }

    /// <summary>
    /// Lets the tree make the edit, when the caret is somewhere a construct has an opinion about — the
    /// 3 after <c>x^2</c> belongs in the exponent, and only the construct holding it can say so. Returns
    /// false when the position belongs to no construct in particular and the caller should write the
    /// text itself.
    /// </summary>
    /// <remarks>
    /// Deliberately declined mid-command and mid-selection. A half-written command is being shown as
    /// the characters it is spelled with, so the layout is a step behind the source and the tree would
    /// be answering about a formula the reader is not looking at; a selection is a replacement, which
    /// is a different edit. Whitespace is declined too — a space is how you say "out of this script",
    /// so it must never be the thing that grows one.
    /// </remarks>
    private bool WriteThroughTree(string text)
    {
        if (_layout is null || _state.HasSelection || _state.Raw is not null) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (_layout.Tree.Write(_state.Caret, text) is not { } written) return false;

        // The source coming back changed is the tree changed: applying it re-reads, re-lays out and
        // repaints, so one call carries the edit all the way to the picture.
        Apply(new LatexEditState(written.Latex, written.Caret), notify: true);
        return true;
    }

    /// <summary>
    /// Settles a half-written command, as space or Enter does. <paramref name="separator"/> is kept in
    /// the source — see <see cref="LatexEditState.Commit"/>.
    /// </summary>
    public void Commit(string separator = " ")
    {
        if (IsReadOnly) return;

        Apply(_state.Commit(separator), notify: true);
    }

    /// <summary>
    /// Selects the next box still waiting to be written in, so a construct inserted whole can be filled
    /// by typing and tabbing rather than by aiming at each hole. False when there is none — the caller's
    /// cue to let Tab mean whatever it otherwise means.
    /// </summary>
    public bool SelectNextPlaceholder(bool forward = true)
    {
        if (IsReadOnly) return false;

        // Read off the drawn formula rather than the text: a hole is a symbol the typesetter put there,
        // and the source it stands over is the empty braces the reader actually wrote.
        var boxes = _layout?.Tree.Placeholders ?? [];
        if (boxes.Count == 0) return false;

        // From wherever the caret is, wrapping round — the last hole tabs back to the first, because
        // a construct being filled in is a loop until it is finished.
        var here = _state.HasSelection ? _state.SelectionStart : _state.Caret;
        var next = forward
            ? boxes.FirstOrDefault(b => b.SourceStart > here, boxes[0])
            : boxes.LastOrDefault(b => b.SourceStart < here, boxes[^1]);

        // The caret goes into the hole rather than over it. A hole covers nothing — that is what makes
        // it a hole — so there is nothing to select and nothing to delete first: what gets typed lands
        // inside the braces, and the hole stops being one because the argument is no longer empty.
        TakeCaret(next.SourceStart);
        return true;
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

        // A palette key and a pasted formula land in a construct the same way a typed character does —
        // \beta pressed after x^2 belongs in the exponent. Only when the template wants the caret
        // walked back into a hole of its own, which is about the text and not the structure.
        if (caretBack == 0 && WriteThroughTree(text)) return;

        Apply(_state.Insert(text, caretBack), notify: true);
    }

    /// <summary>
    /// Puts what is selected into the hole of <paramref name="template"/> the caret would have gone
    /// to, filling its other holes with boxes.
    /// <para>
    /// Which hole is not a new thing to know: <paramref name="caretBack"/> already says where a key
    /// expects to be typed into next, and that is the same place — a <c>\frac</c> pressed over a
    /// selected <c>3+7</c> means a fraction <em>of</em> <c>3+7</c>, in its numerator, because the
    /// numerator is where you would have typed it. Without this, every structural key replaced what
    /// was picked out instead of taking it, which is not a thing anyone has ever wanted a palette to do.
    /// </para>
    /// Returns false when the template has no hole there, and the key inserts as it otherwise would.
    /// </summary>
    private bool WrapSelectionInto(string template, int caretBack)
    {
        var at = template.Length - caretBack;
        if (at <= 0 || at >= template.Length) return false;
        if (template[at - 1] != '{' || template[at] != '}') return false;

        var built = template[..at] + _state.SelectedText + template[at..];
        Apply(_state.Insert(built), notify: true);

        // The template's other arguments are still empty, and the typesetter has just drawn a hole in
        // each. Selecting the first is what makes the next keystroke fill it.
        SelectNextPlaceholder();
        return true;
    }

    /// <summary>Wraps the selection, or inserts the pair at the caret.</summary>
    public void Wrap(string before, string after)
    {
        if (IsReadOnly) return;
        Apply(_state.Wrap(before, after), notify: true);
    }

    /// <summary>
    /// Backspace. Behind a rendered command this un-renders it rather than deleting a character of it —
    /// see <see cref="LatexEditState.Backspace"/>. Returns false when there was nothing to delete, which
    /// is the host's cue that backspace should now remove the formula itself.
    /// </summary>
    public bool Backspace()
    {
        if (IsReadOnly) return false;
        if (_state is { Caret: 0, SelectionLength: 0 }) return false;

        var here = _state.Caret;
        var symbol = _state.HasSelection || _state.Raw is not null ? null : _layout?.Tree.SymbolBefore(here);

        if (symbol is { SourceLength: > 1 })
        {
            var span = (Start: symbol.SourceStart, Length: symbol.SourceLength);

            // A construct goes back to the source it was written as — there is source to go back to. A
            // symbol has nothing hidden behind it, so it is simply taken: an α is one thing on the page
            // however many letters spelled it, and backspace over one thing removes it.
            Apply(_layout!.Tree.IsComposite(symbol) ? _state.Backspace(span) : _state.Remove(span.Start, span.Length),
                  notify: true);
            return true;
        }

        Apply(_state.Backspace(), notify: true);
        return true;
    }

    /// <summary>Forward delete. Returns false when the caret is already at the end.</summary>
    public bool Delete()
    {
        if (IsReadOnly) return false;
        if (_state.Caret >= _state.Latex.Length && !_state.HasSelection) return false;
        Apply(_state.Delete(), notify: true);
        return true;
    }

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>Selects a source range, snapped out to whole constructs.</summary>
    public void Select(int start, int length)
    {
        if (length <= 0) { ClearSelection(); return; }

        var (from, snapped) = _layout is null ? (start, length) : _layout.Tree.SnapRange(start, length);

        var next = _state.Select(from, snapped);
        if (next.Selection.SequenceEqual(_state.Selection)) return;
        Apply(next, notify: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects everything — what the host asks for when a selection sweeps straight over it.</summary>
    public void SelectAll() => Select(0, _state.Latex.Length);

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (!_state.HasSelection) return;
        Apply(_state.Select(0, 0), notify: false);
        InteractiveSelection.Release(this);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ExtendSelectionTo(int offset) =>
        Select(Math.Min(_anchor, offset), Math.Abs(offset - _anchor));

    // ── Pointer, driven by the host ─────────────────────────────────────────

    /// <inheritdoc />
    public void BeginPointerSelect(Point pointInElement)
    {
        if (_layout is null) return;
        InteractiveSelection.Own(this);

        _anchor = _layout.Tree.OffsetAt(pointInElement);
        _anchorNode = _layout.Tree.NodeAt(pointInElement);
        _pressedAt = pointInElement;
        _dragging = true;

        // Pressing on what is already selected is how a move begins — the reader is picking the term
        // up, not starting a new selection over it. The selection is kept until the button comes back
        // up, so a press that turns out to be an ordinary click can still fall through to placing the
        // caret without the selection having flickered away in between.
        if (Covers(_anchor)) { _moving = true; _dropAt = _anchor; return; }

        ClearSelection();
        TakeCaret(_anchor);
    }

    /// <summary>Whether <paramref name="offset"/> falls inside one of the selected stretches.</summary>
    private bool Covers(int offset) =>
        _state.Selection.Any(r => offset >= r.Start && offset <= r.End);

    /// <inheritdoc />
    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging || _layout is null) return;

        // A click is not a drag. The pointer moves a pixel or two under any real hand, and treating
        // that as a selection meant clicking after a number selected it — so the next key typed
        // replaced the number instead of following it, and the formula could not be edited at all.
        // Nothing is selected until the pointer has travelled as far as the system asks of a drag.
        if (!HasDragged(pointInElement)) return;

        // Carrying a term: the formula is shown as it would read if it were let go here, with the
        // carried part marked out, so the reader is choosing between finished formulas.
        if (_moving)
        {
            var drop = _layout.Tree.OffsetAt(pointInElement);
            if (drop == _dropAt) return;

            _dropAt = drop;
            _dropPoint = pointInElement;
            BuildPreview();
            HoldCaretVisible();
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        // What was dragged over is a set of pieces, not a stretch of text. Inside a matrix that is what
        // makes a drag down a column select the column rather than everything written between its top
        // cell and its bottom one.
        if (_anchorNode is not null && _layout.Tree.NodeAt(pointInElement) is { } focus)
        {
            // Through whatever owns each end. Landing on a bracket means the group it opens or closes:
            // half a pair is not a smaller selection, it is one that cannot be read.
            SelectNodes(ContentSelection.Between(
                _layout.Tree.Root, _layout.Tree.Owning(_anchorNode), _layout.Tree.Owning(focus)));
            return;
        }

        ExtendSelectionTo(_layout.Tree.OffsetAt(pointInElement));
    }

    /// <summary>Takes a selection worked out over the layout tree, in the source's own offsets.</summary>
    private void SelectNodes(ContentSelection selection)
    {
        if (selection.IsEmpty) { ClearSelection(); return; }

        var ranges = selection.Ranges.Select(r => new LatexRange(r.Start, r.Length)).ToList();

        var next = _state.Select(ranges);
        if (next.Selection.SequenceEqual(_state.Selection)) return;

        Apply(next, notify: false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether the pointer has moved far enough from the press for this to be a drag rather than a
    /// click. The system's own thresholds, so it matches every other drag the reader makes.
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

        // The press never became a drag: an ordinary click on the selection, which places the caret
        // there and drops the selection, as clicking a selection does everywhere.
        if (settled is not { } moved) { ClearSelection(); TakeCaret(_anchor); return; }

        // Exactly the formula that was on screen a moment ago — settling is letting go of it, not
        // recomputing something the reader has to check.
        Apply(new LatexEditState(moved.Latex, moved.Caret), notify: true);
    }

    /// <summary>Typesets the formula as it would read if the carried term were dropped where it is now.</summary>
    private void BuildPreview()
    {
        ClearPreview();
        if (_layout is null) return;

        var ranges = _state.Selection.Select(r => (r.Start, r.Length)).ToList();

        if (_layout.Tree.Move(ranges, _dropAt, _dropPoint) is not { } moved) return;

        _previewOf = moved;
        _previewMoved = (moved.Wrote.Start, moved.Wrote.End);
        _preview = LatexLayout.Build(moved.Latex, _scale, _inline);
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
        if (_layout is null) return false;

        // Select the construct under the pointer rather than letting the host drop the whole block into
        // source-edit mode: inside a formula, "the word you clicked" is the symbol you clicked.
        var here = _layout.Tree.OffsetAt(pointInElement);
        var atom = _layout.Tree.SymbolBefore(here);
        if (atom is { SourceLength: > 0 }) Select(atom.SourceStart, atom.SourceLength);
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
        if (_dragging) ExtendPointerSelect(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        EndPointerSelect();
    }

    // ── Layout and painting ─────────────────────────────────────────────────

    private void Apply(LatexEditState next, bool notify)
    {
        var resized = next.Latex != _state.Latex || next.Raw != _state.Raw;
        var moved = next.Caret != _state.Caret;
        var changed = next.Latex != _state.Latex;

        _state = next;
        if (resized) { Rebuild(); InvalidateMeasure(); }
        if (moved || changed) HoldCaretVisible();   // never blink out mid-keystroke
        InvalidateVisual();

        if (moved) CaretMoved?.Invoke(this, EventArgs.Empty);
        if (notify && changed) LatexChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Typesets the whole formula, with the stretch being written set as the characters that were typed.
    /// <para>
    /// One layout over the real source, rather than a layout of the settled part with the raw characters
    /// painted over it afterwards. Painting over could only ever work while the stretch was the last
    /// thing in the formula: anywhere else it covered whatever followed, which is what un-rendering a
    /// fraction in the middle of an expression looked like. Set through the typesetter it takes up room
    /// like anything else, so the formula flows around it — and every offset the tree reports is an
    /// offset into the source the reader is editing, with no mapping in between.
    /// </para>
    /// </summary>
    private void Rebuild() => _layout = LatexLayout.Build(
        _state.Latex, _scale, _inline, shownAsWritten: _state.Raw, placeholders: !IsReadOnly);

    private int Snap(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _state.Latex.Length);
        if (_layout is null) return clamped;

        // Inside the stretch being written every character is its own stop, so the caret goes exactly
        // where it was put; the settled formula snaps to the places a caret may rest.
        return _state.Raw is { } zone && zone.Holds(clamped) ? clamped : _layout.Tree.NearestStop(clamped);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // While a term is being carried, the formula on screen is the one it would become, so that is
        // the one that has to fit — otherwise the preview is clipped at the settled formula's width.
        if (_preview is { } preview)
            return new Size(Math.Ceiling(preview.Tree.Size.Width), Math.Ceiling(preview.Tree.Size.Height));

        if (_layout is null)
        {
            var source = Mono(_state.Latex.Length == 0 ? " " : _state.Latex);
            return new Size(Math.Ceiling(source.WidthIncludingTrailingWhitespace), Math.Ceiling(source.Height));
        }

        // Whatever is being written is set into the formula rather than drawn over it, so the layout's
        // own size already accounts for it.
        return new Size(Math.Ceiling(_layout.Size.Width), Math.Ceiling(_layout.Size.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A transparent fill makes the whole element hit-testable, gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        if (_preview is not null) { PaintPreview(dc); return; }

        if (_layout is null)
        {
            // Nothing typeset — an empty formula, or one none of which could be laid out. It is still
            // a place the reader is writing in, so it still draws a caret. Not doing so is why a Latex
            // tab opened on an empty formula showed none until the first character was typed: that
            // keystroke was not summoning the caret, it was creating the layout the caret was being
            // drawn from.
            var source = Mono(_state.Latex.Length == 0 ? " " : _state.Latex);
            dc.DrawText(source, new Point(0, 0));

            if (!HasCaret || IsReadOnly || !_caretVisible) return;

            var typed = Mono(_state.Latex[..Math.Clamp(_state.Caret, 0, _state.Latex.Length)]);
            DrawCaret(dc, typed.WidthIncludingTrailingWhitespace, 0, source.Height);
            return;
        }

        // Every stretch washes itself. A column of a matrix is three of them with the rest of the matrix
        // in between, and washing from the first to the last would highlight the lot.
        foreach (var range in _state.Selection)
            foreach (var rect in _layout.Tree.RangeRects(range.Start, range.Length))
                dc.DrawRectangle(_wash, null, rect);

        _layout.Paint(dc, _palette.Text);

        // A wave under whatever could not be read, drawn over the formula rather than instead of it: the
        // parts that did parse are still worth looking at, and the reader needs to see which part is not.
        foreach (var trouble in _layout.Tree.Diagnostics)
        {
            var runs = _layout.Tree.RangeRects(trouble.Start, trouble.Length);
            if (runs.Count == 0) continue;

            var wave = new Pen(trouble.Severity == DiagnosticSeverity.Error ? _palette.Danger : _palette.Warning, 1.0);
            wave.Freeze();
            dc.DrawGeometry(null, wave, Squiggle.Under(runs));
        }

        // While a term is being carried the caret shows where it would land, not where it was picked
        // up from — that is the one thing the reader needs to see before letting go. Inside a stretch
        // being written it needs no special case: those characters are in the layout like any others,
        // so the tree already knows where each of them sits.
        var caret = _layout.Tree.CaretRect(_moving ? _dropAt : _state.Caret);

        if ((!HasCaret && !_moving) || IsReadOnly || !_caretVisible) return;
        DrawCaret(dc, caret.X, caret.Y, caret.Height);
    }

    /// <summary>The caret itself. One place, so a formula that is empty draws the same one as any other.</summary>
    private void DrawCaret(DrawingContext dc, double x, double y, double height)
    {
        var pen = new Pen(_palette.Accent, 1.4);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, y), new Point(x, y + Math.Max(height, 1)));
    }

    /// <summary>
    /// Draws the formula as it would read after the drop, with the carried term in the accent colour so
    /// it can be picked out of a formula it has already merged into — by then it is set in place,
    /// braces and spacing and all, and nothing else would distinguish it.
    /// </summary>
    private void PaintPreview(DrawingContext dc)
    {
        var preview = _preview!;
        preview.Paint(dc, _palette.Text);

        // Over the top rather than instead of: painting all of it and then the carried part again is
        // what keeps this to two calls, and the second colour is the one that shows.
        foreach (var node in Carried(preview))
            preview.Paint(dc, _palette.Accent, node);
    }

    /// <summary>
    /// The outermost pieces of <paramref name="preview"/> lying wholly inside the carried term.
    /// Outermost so that nothing is painted twice over — a piece and its own children are one drawing.
    /// </summary>
    private IEnumerable<ILayoutNode> Carried(LatexLayout preview)
    {
        var (start, end) = _previewMoved;
        if (end <= start) yield break;

        var taken = new List<ILayoutNode>();
        foreach (var node in preview.Tree.Root.SelfAndDescendants())
        {
            if (node.SourceLength <= 0 || node.SourceStart < start || node.SourceEnd() > end) continue;
            if (taken.Any(t => node.Ancestors().Contains(t))) continue;

            taken.Add(node);
            yield return node;
        }
    }

    /// <summary>Source shown as text: a half-written command, or a formula that will not typeset at all.</summary>
    private FormattedText Mono(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            _scale * 0.6,
            _palette.Accent,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// A translucent wash from the theme accent, falling back to the highlight token — never a literal.
    /// Mirrors <c>ScoreElement</c>, so a selected formula and a selected bar of music look alike.
    /// </summary>
    private static Brush Wash(MarkdownPalette palette)
    {
        if (palette.Accent is not SolidColorBrush accent) return palette.Marked;
        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }
}
