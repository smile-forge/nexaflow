using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What moving part of some content came to: the source it produced, where the caret goes, and the
/// stretch it wrote — what to mark out while it is still being carried.
/// </summary>
public readonly record struct Moved(string Source, int Caret, EditRange Wrote);

/// <summary>
/// The surface every piece of embedded, rendered, editable content is drawn on: it lays the content out,
/// paints it, and owns the pointer, the selection and the caret.
///
/// <para>
/// <strong>There is one of these, whatever is in it.</strong> A tune, a formula and a barcode were three
/// elements doing the same work three times — the same wash, the same wave, the same blinking bar, the
/// same drag grown out to whole constructs — because each had its own idea of what a laid-out thing was.
/// They no longer do: a builder makes a <see cref="Laid"/>, and everything after that is here.
/// </para>
/// <para>
/// The editing model is <see cref="EditState"/>: source, caret, selection, and the stretch shown as
/// itself, with every operation over them. Typing is typing regardless of the source, so a note letter
/// and a backslash arrive by the same road and differ only in what <see cref="Typing"/> makes of them.
/// </para>
/// <para>
/// What a kind of content still gets to say is the short list below — the questions a parse tree can
/// answer and a layout cannot. Every one declines by default, so content with nothing to say says
/// nothing and gets the ordinary behaviour.
/// </para>
/// <para>
/// <strong>A plain string may yet want a sibling.</strong> Three of the shared answers are a walk of the
/// whole tree per keystroke — the caret stops, the rectangles a selection washes, and snapping a range
/// out to whole things. For a formula that is forty pieces and free; for a paragraph it would be one
/// piece per character and the walk would be the cost of typing. Where prose arrives, the answer is a
/// sibling of this that overrides those three with what a string can answer directly — stops are every
/// offset, the wash is a run measure, and snapping is word boundaries — rather than a flag in here.
/// </para>
/// <para>
/// The gesture is split into <see cref="BeginPointerSelect"/> / <see cref="ExtendPointerSelect"/> /
/// <see cref="EndPointerSelect"/> rather than being driven from this element's own mouse events, because
/// content is usually hosted inside a <c>RichTextBox</c>, where an embedded element does <em>not</em>
/// reliably receive mouse input — the text container attributes the click to itself, to the FlowDocument,
/// or even to a neighbouring paragraph. The host hit-tests geometrically and drives the three methods.
/// </para>
/// </summary>
public abstract class ContentElement : FrameworkElement, IEditableBlock
{
    private static readonly TimeSpan BlinkRate = TimeSpan.FromMilliseconds(600);

    private readonly Brush _wash;

    private EditState _state;
    private Laid _laid = Laid.Nothing;
    private double _ppd = 1.0;
    private double _laidFor;

    private DispatcherTimer? _blink;
    private bool _caretVisible = true;

    /// <summary>
    /// Which of the bars at the caret's offset it is drawn as — see <see cref="CaretPlace"/>. Kept beside
    /// the state because it is about the picture rather than the text: the same offset can be drawn in
    /// two places, and which one the reader is at survives a keystroke.
    /// </summary>
    private int _level;

    /// <summary>
    /// Which of the bars at the caret sits at — nought is innermost. Content that writes through its own
    /// structure needs it: a caret that has stepped out of a construct is past it, so what is typed there
    /// follows the construct rather than joining it.
    /// </summary>
    protected int Level => _level;

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

    protected ContentElement(string source, MarkdownPalette palette)
    {
        Palette = palette;
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
    protected MarkdownPalette Palette { get; }

    /// <summary>What is laid out. Always something: a builder always makes a layout.</summary>
    public Laid Laid => _laid;

    /// <summary>The editing model — source, caret, selection, and what is shown as written.</summary>
    protected EditState State => _state;

    /// <summary>How many pixels of device per pixel of layout, for anything measuring its own text.</summary>
    protected double PixelsPerDip => _ppd;

    // ── What a kind of content gets to say ──────────────────────────────────

    /// <summary>
    /// Lays the source out to fit the room it is given.
    ///
    /// <para>
    /// The <em>state</em> rather than the string, because what is being typed changes what is drawn: a
    /// stretch shown as its own characters is set into the layout rather than painted over it, which is
    /// the only way the rest of the content can be laid out knowing it is there.
    /// </para>
    /// </summary>
    protected abstract Laid Lay(EditState state, double room, double pixelsPerDip);

    /// <summary>
    /// What writing <paramref name="text"/> means, where this content has something to say about it.
    ///
    /// <para>
    /// Two kinds of answer live here and they are the same kind: a rule about how the source is
    /// <em>written</em> — a backslash opens a command and letters extend it — and a rule about what the
    /// text means to the <em>structure</em>, which is how a 3 typed after <c>x^2</c> becomes twenty-three
    /// rather than an x squared beside a 3.
    /// </para>
    /// <para>
    /// Null leaves it to the element, which splices the characters in where the caret is.
    /// </para>
    /// </summary>
    protected virtual EditState? Typing(EditState state, string text) => null;

    /// <summary>
    /// What backspace means behind something drawn from more source than it shows — un-rendering a
    /// command back to the characters that spelled it. Null for the ordinary answer, which is to take one
    /// character.
    /// </summary>
    protected virtual EditState? Backspacing(EditState state) => null;

    /// <summary>
    /// What pointing at a piece means: the first thing above it that names a stretch of source.
    ///
    /// <para>
    /// A bracket is drawn by the fence that holds it, a bar by the fraction, the three glyphs of an operator
    /// name by the name. None of them names source of its own, and none can be pointed at, taken, carried or
    /// deleted alone — a bracket without its partner cannot be read at all. So pointing at one means the
    /// thing it is part of, and where nothing above it names anything, nothing there is selectable.
    /// </para>
    /// <para>
    /// No content declares any of this. It is the second of the two rules the layout runs on, and it is the
    /// same climb whether what was pressed is a delimiter, a beam or a letter of a word.
    /// </para>
    /// </summary>
    protected static Piece Pointing(Piece piece) => piece.Selectable();

    /// <summary>
    /// The places still waiting to be written in, in reading order — what Tab walks.
    ///
    /// <para>
    /// Read off what was drawn rather than off the text, because there is nothing in the text to read: a hole
    /// covers no characters, which is exactly what makes it one. The builder is the only thing that can say
    /// so, and it does, and nothing else about a piece needs declaring.
    /// </para>
    /// </summary>
    protected IReadOnlyList<Piece> Holes() => _laid.Holes;

    /// <summary>
    /// What moving the selected stretches to <paramref name="to"/> would produce — a term carried to a new
    /// place in a formula, a column dragged across a matrix.
    /// <para>
    /// Merged into where it lands rather than dropped there, which is why only the content can answer:
    /// what has to be re-braced, re-spaced or re-separated is a fact about its structure. Null from
    /// content where dragging a selection means nothing, and then nothing is carried.
    /// </para>
    /// </summary>
    protected virtual Moved? Moving(EditState state, int to, Point? at) => null;

    // ── Shape and colour ────────────────────────────────────────────────────

    /// <summary>
    /// How large the content is drawn, as a multiple of its natural size.
    ///
    /// <para>
    /// A <em>render</em> scale rather than a bitmap one, which is the whole point: the content is laid out
    /// into the room a smaller drawing leaves — <c>available / zoom</c> — so zooming out fits more of it on
    /// a line rather than shrinking a picture of the same line breaks. That is what a reader dragging a
    /// zoom control expects, and it is why the builder has to be told rather than the painter.
    /// </para>
    /// </summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>The render scale, kept somewhere a reader could plausibly want to be.</summary>
    protected double Scale => Math.Clamp(Zoom, 0.2, 4.0);

    /// <summary>
    /// How far a selection wash reaches past the ink it marks.
    ///
    /// <para>
    /// A box the exact size of a glyph is a poor way to say "this is picked out". Text does not do it
    /// either: a selected character is washed over the whole line box, not over its own outline, which is
    /// why a selected <c>i</c> reads as selected at all. Stated by the content, because it belongs to the
    /// type size, which is the content's.
    /// </para>
    /// </summary>
    protected virtual double WashPad => 2.0;

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

    /// <summary>
    /// Whether the caret belongs in this content at all — whether any of what it draws is the source.
    ///
    /// <para>
    /// Asked of the tree rather than declared: a piece that names a stretch of source is a piece somebody
    /// typed, and content with none of those is content nobody can type into. A barcode that prints a
    /// number it worked out is the case that needs it, and nothing about the question is barcode-shaped.
    /// </para>
    /// </summary>
    public bool AcceptsCaret => _laid.Root.SelfAndDescendants().Any(piece => piece.Part is { Length: > 0 });

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token.</summary>
    private static Brush Wash(MarkdownPalette palette)
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

    /// <summary>Gives this content the caret at <paramref name="offset"/>.</summary>
    /// <param name="level">Which of the bars drawn there — see <see cref="CaretPlace"/>.</param>
    public void TakeCaret(int offset, int level = 0)
    {
        HasCaret = !IsReadOnly;
        Apply(_state.MoveCaretTo(Snap(offset)), notify: false, level: level);
        if (HasCaret) StartBlinking();
    }

    /// <inheritdoc />
    public virtual void TakeCaretArriving(CaretArrival arrival)
    {
        // Nothing drawn here is the source, so there is nowhere in it to stand. The caret is handed
        // straight on the way it was already going, and the reader arrows over the content as they would
        // over a word — rather than into it, to find that no key does anything.
        if (!AcceptsCaret)
        {
            Exited?.Invoke(this, arrival.Edge == BlockExit.Before ? BlockExit.After : BlockExit.Before);
            return;
        }

        var stops = _laid.Stops;
        if (stops.Count == 0) { Exited?.Invoke(this, arrival.Edge); return; }

        // Content wide enough for a column to mean something takes a caret arriving from the line above
        // under where it left, rather than at the beginning.
        if (arrival is { Step: CaretStep.Line, Column: { } column }) { TakeCaret(Nearest(column)); return; }
        if (arrival.Edge == BlockExit.Before) { TakeCaret(stops[0]); return; }

        // Arriving from the text after it, the caret is outside everything in the content — so it takes
        // the outermost bar at the end. Landing on the innermost instead would put it inside a trailing
        // exponent, raised and half-height, having been walked into from the far side.
        var end = stops[^1];
        TakeCaret(end, level: Math.Max(0, _laid.Root.CaretBars(Snap(end)).Count - 1));
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
    /// Blinks the caret, because a still one is easy to lose among the glyphs. It runs only while this
    /// content holds the caret and is torn down on unload, so a page of them leaves no timers behind.
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
    protected void HoldCaretVisible()
    {
        if (!HasCaret) return;
        _caretVisible = true;
        _blink?.Stop();
        _blink?.Start();
    }

    /// <summary>
    /// Moves the caret one stop. False when it ran off an end, having raised <see cref="Exited"/> — the
    /// host then takes over.
    /// </summary>
    public bool MoveCaret(bool forward, bool extend = false)
    {
        // A stretch being shown as its characters is text, and moves like text: one character at a time.
        // Stepping by layout stops cannot reach into it — every position inside maps to the one point
        // where it sits in the laid-out content — so the caret jumped clean over the thing the reader had
        // just asked to see, which is the only place they wanted to edit.
        if (_state.Raw is { } zone && zone.Holds(_state.Caret))
        {
            var step = _state.Caret + (forward ? 1 : -1);
            if (step >= zone.Start && step <= zone.End) { MoveTo(CaretPlace.At(step), extend); return true; }
        }

        if (_laid.Root.Step(new CaretPlace(_state.Caret, _level), forward) is not { } next)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        MoveTo(next, extend);
        return true;
    }

    /// <summary>Up and down — across a fraction bar, out of a script, from a note to the word under it.</summary>
    public bool MoveCaretVertically(bool up, bool extend = false)
    {
        if (_laid.Root.StepVertical(_state.Caret, up) is not { } next) return false;

        MoveTo(CaretPlace.At(next), extend);
        return true;
    }

    private void MoveTo(CaretPlace place, bool extend)
    {
        // Extending is about a stretch of source, and a stretch has no levels — which of the bars at its
        // far end the caret would have been drawn as says nothing about what is picked out.
        if (extend) ExtendSelectionTo(place.Offset);
        else Apply(_state.MoveCaretTo(place.Offset), notify: false, level: place.Level);
    }

    private int Snap(int offset)
    {
        var clamped = Math.Clamp(offset, 0, _state.Source.Length);

        // Inside the stretch being written every character is its own stop, so the caret goes exactly
        // where it was put; the settled content snaps to the places a caret may rest.
        return _state.Raw is { } zone && zone.Holds(clamped) ? clamped : _laid.NearestStop(clamped);
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
    /// Puts what is selected into the hole of <paramref name="template"/> the caret would have gone to,
    /// filling its other holes with boxes.
    /// <para>
    /// Which hole is not a new thing to know: <paramref name="caretBack"/> already says where a key
    /// expects to be typed next, and that is the same place — a <c>\frac</c> pressed over a selected
    /// <c>3+7</c> means a fraction <em>of</em> <c>3+7</c>, in its numerator, because the numerator is
    /// where you would have typed it.
    /// </para>
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

    /// <summary>
    /// Backspace. Behind something drawn from more source than it shows, this un-renders it rather than
    /// deleting a character of it. False when there was nothing to delete, which is the host's cue that
    /// backspace should now remove the content itself.
    /// </summary>
    public bool Backspace()
    {
        if (IsReadOnly) return false;
        if (_state is { Caret: 0, SelectionLength: 0 }) return false;

        Apply(Backspacing(_state) ?? _state.Backspace(), notify: true);
        return true;
    }

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    public bool Delete()
    {
        if (IsReadOnly) return false;
        if (_state.Caret >= _state.Source.Length && !_state.HasSelection) return false;

        Apply(_state.Delete(), notify: true);
        return true;
    }

    /// <summary>
    /// Settles whatever is half-written, as space or Enter does — which is to say, writes the character.
    /// <para>
    /// There is nothing else to settling: a non-letter after a control word ends it, and that rule lives
    /// with the content's typing rule where it belongs. A separate "commit" was a second way to say the
    /// same thing, and the two could disagree.
    /// </para>
    /// </summary>
    bool IEditableBlock.Commit(string text) { if (!IsReadOnly) Settle(text); return true; }

    /// <summary>
    /// Ends whatever is half-written. Typing the character, which is all settling is — content whose source
    /// needs something <em>added</em> to say where a half-written thing stopped overrides this, and should
    /// not have to.
    /// </summary>
    protected virtual void Settle(string separator) => Write(separator);

    /// <summary>
    /// Selects the next place still waiting to be written in, so a construct inserted whole can be filled
    /// by typing and tabbing rather than by aiming at each hole. False when there is none.
    /// </summary>
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

    private void ExtendSelectionTo(int offset) =>
        Select(Math.Min(_anchor, offset), Math.Abs(offset - _anchor));

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

    /// <inheritdoc />
    public void BeginPointerSelect(Point pointInElement)
    {
        InteractiveSelection.Own(this);

        var at = Unscaled(pointInElement);
        _anchor = _laid.OffsetAt(at);
        _anchorNode = _laid.PieceAt(at);
        _pressedAt = pointInElement;
        _dragging = true;

        // Pressing on what is already selected is how a move begins — the reader is picking the term up,
        // not starting a new selection over it. The selection is kept until the button comes back up, so a
        // press that turns out to be an ordinary click can still fall through to placing the caret.
        if (Covers(_anchor)) { _moving = true; _dropAt = _anchor; return; }

        ClearSelection();

        var place = _laid.PlaceAt(at);
        TakeCaret(place.Offset, place.Level);
    }

    /// <summary>Whether <paramref name="offset"/> falls inside one of the selected stretches.</summary>
    private bool Covers(int offset) =>
        _state.Selection.Any(range => offset >= range.Start && offset <= range.End);

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

        // What was dragged over is a set of pieces, not a stretch of text. Inside a matrix that is what
        // makes a drag down a column select the column rather than everything written between its top
        // cell and its bottom one.
        if (Pointing(_anchorNode) is { Exists: true } from && Pointing(_laid.PieceAt(at)) is { Exists: true } focus)
            {
            // Through whatever owns each end. Landing on a bracket means the group it opens or closes:
            // half a pair is not a smaller selection, it is one that cannot be read.
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

        if (Moving(_state, _dropAt, _dropPoint) is not { } moved) return;

        _previewOf = moved;
        _previewMoved = (moved.Wrote.Start, moved.Wrote.End);
        _preview = Lay(new EditState(moved.Source, moved.Caret), Room(), _ppd);
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
        var here = _laid.OffsetAt(at);

        var under = Pointing(_laid.PieceAt(at));
        if (under.Exists && under.Sits() is { Length: > 0 } sits) Select(sits.Start, sits.Length);
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

    // ── Applying an edit ────────────────────────────────────────────────────

    /// <param name="level">
    /// Which bar at the caret's offset. Innermost unless a step says otherwise, which is what makes an
    /// edit, a click or a jump put the caret back inside whatever it is in.
    /// </param>
    protected void Apply(EditState next, bool notify, int level = 0)
    {
        var resized = next.Source != _state.Source || next.Raw != _state.Raw;
        var moved = next.Caret != _state.Caret || level != _level;
        var changed = next.Source != _state.Source;

        _state = next;
        _level = level;

        if (resized) { Rebuild(); InvalidateMeasure(); }
        if (moved || changed) HoldCaretVisible();   // never blink out mid-keystroke
        InvalidateVisual();

        if (moved) CaretMoved?.Invoke(this, EventArgs.Empty);
        if (notify && changed) SourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lays the content out again, because something outside it changed.</summary>
    public void Refresh()
    {
        Rebuild();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>How much room the content has, in its own coordinates.</summary>
    protected double Room() => (_laidFor > 0 ? _laidFor : 680) / Scale;

    /// <summary>Lays it out again from the state as it now stands.</summary>
    protected void Rebuild() => _laid = Lay(_state, Room(), _ppd);

    // ── Layout and painting ─────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

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

        // Every stretch washes itself. A column of a matrix is three of them with the rest of the matrix
        // in between, and washing from the first to the last would highlight the lot.
        foreach (var range in _state.Selection)
            foreach (var rect in _laid.Root.RangeRects(range.Start, range.Length))
                dc.DrawRectangle(_wash, null, Marked(rect));

        // A wave under whatever could not be read, drawn over the content rather than instead of it: the
        // parts that did read are still worth looking at, and the reader needs to see which part is not.
        foreach (var trouble in _laid.Trouble)
        {
            var runs = _laid.Root.RangeRects(trouble.Start, trouble.Length);
            if (runs.Count == 0) continue;

            var wave = new Pen(trouble.Severity == DiagnosticSeverity.Error ? Palette.Danger : Palette.Warning, 1.0);
            wave.Freeze();
            dc.DrawGeometry(null, wave, Squiggle.Under(runs));
        }

        PaintOver(dc);

        if ((!HasCaret && !_moving) || IsReadOnly || !_caretVisible) return;

        // While something is being carried the caret shows where it would land, not where it was picked
        // up from — that is the one thing the reader needs to see before letting go.
        var caret = _laid.Root.CaretRect(
            _moving ? CaretPlace.At(_dropAt) : new CaretPlace(_state.Caret, _level));

        DrawCaret(dc, caret.X, caret.Y, caret.Height);
    }

    /// <summary>
    /// Anything the content draws over the shared picture — a strike through a symbol that will not
    /// encode. Drawn after the ink and the wash, and before the caret.
    /// </summary>
    protected virtual void PaintOver(DrawingContext dc) { }

    /// <summary>
    /// Draws the content as it would read after the drop, with the carried part in the accent colour so
    /// it can be picked out of something it has already merged into — by then it is set in place, braces
    /// and spacing and all, and nothing else would distinguish it.
    /// </summary>
    private void PaintPreview(DrawingContext dc, Laid preview)
    {
        LayoutPainter.Paint(dc, preview.Root, Palette.Text);

        // Over the top rather than instead of: painting all of it and then the carried part again is what
        // keeps this to two calls, and the second colour is the one that shows.
        foreach (var piece in Carried(preview))
            LayoutPainter.PaintOne(dc, piece, Palette.Accent);
    }

    /// <summary>
    /// The outermost pieces of <paramref name="preview"/> lying wholly inside what is carried. Outermost
    /// so that nothing is painted twice over — a piece and its own children are one drawing.
    /// </summary>
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
