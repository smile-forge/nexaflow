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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Shows content being written and passes on what the reader does to it: paints what the engine laid — with the caret, what
/// is picked out and the wave under what could not be read — and hands every key to the engine, which holds the content, its
/// reading, its layout and the state of writing it (<see cref="ContentEngine"/>).
/// <para>
/// The pointer gesture is split into <see cref="BeginPointerSelect(Point)"/> / <see cref="ExtendPointerSelect"/> /
/// <see cref="EndPointerSelect"/> rather than driven by this element's own mouse events, because content hosted inside a
/// <c>RichTextBox</c> does not reliably receive mouse input — the host hit-tests geometrically and drives these methods instead.
/// </para>
/// </summary>
public class ContentElement : FrameworkElement
{
    private static readonly TimeSpan BlinkRate = TimeSpan.FromMilliseconds(600);

    private readonly Brush _wash;

    private DispatcherTimer? _blink;
    private bool _caretVisible = true;

    /// <summary>Set while the element is being measured, when laying out again needs no second measure.</summary>
    private bool _measuring;
    private Point _pressedAt;
    /// <summary>Whether a press is held, so a move far enough from it is a drag.</summary>
    private bool _pressing;

    /// <summary>Whether the pointer is on the corner's buttons, which are faint until it is.</summary>
    private bool _onCorner;

    /// <summary>How faint the corner is until the pointer is on it.</summary>
    private const double Faint = 0.55;
    /// <summary>Raised whenever the caret moves inside the content.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised whenever what is selected inside the content changes.</summary>
    public event EventHandler<ContentSelectionChange>? SelectionChanged;

    /// <summary>Raised when the reader's own editing changed the source.</summary>
    public event EventHandler<ContentSourceChange>? SourceChanged;

    /// <param name="engine">What lays the content out, and holds it as it is being written.</param>
    /// <param name="language">What the content is written in — markdown, where nothing names a language.</param>
    /// <param name="actions">What answers what the content's pieces mean by a gesture — null where nothing here answers one.</param>
    public ContentElement(string source, StyleFormat palette, ContentEngine engine, string? language = null, ILayoutActions? actions = null)
    {
        Palette = palette;
        _engine = engine;
        _language = language;
        _wash = Wash(palette);

        _engine.Show(language, palette);
        _engine.Start(source ?? string.Empty);
        _engine.Answers(actions);

        _engine.Changed += OnChanged;
        _engine.PreRender += OnLaid;
        _engine.CaretTaken += OnCaretTaken;
        _engine.CaretMoved += OnCaretMoved;
        _engine.SelectionChanged += OnSelectionChanged;
        _engine.SourceChanged += OnSourceChanged;
        _engine.Revealing += OnRevealing;
        _engine.Rebound += OnRebound;
        _engine.Reread += OnReread;

        SnapsToDevicePixels = true;
        Cursor = Cursors.IBeam;

        // Never take keyboard focus. Content is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text,
        // and faults deep inside the splay tree.
        Focusable = false;

        Unloaded += (_, _) =>
        {
            StopBlinking();
            Tip(null);
        };
    }

    /// <summary>
    /// Stops showing the engine's content — for an element made again over the same engine, which is the one that shows it
    /// from then on.
    /// </summary>
    internal void Release()
    {
        _engine.Changed -= OnChanged;
        _engine.PreRender -= OnLaid;
        _engine.CaretTaken -= OnCaretTaken;
        _engine.CaretMoved -= OnCaretMoved;
        _engine.SelectionChanged -= OnSelectionChanged;
        _engine.SourceChanged -= OnSourceChanged;
        _engine.Revealing -= OnRevealing;
        _engine.Rebound -= OnRebound;
        _engine.Reread -= OnReread;

        StopBlinking();
    }

    private void OnChanged(object? sender, bool held)
    {
        // Never blinked out mid-keystroke.
        if (held) HoldCaretVisible();
        InvalidateVisual();
    }

    private void OnLaid(object? sender, EventArgs args)
    {
        if (!_measuring) InvalidateMeasure();
    }

    /// <summary>Bound content changed on whatever thread it walked on; it is laid out again here, on this element's.</summary>
    private void OnRebound(object? sender, IBoundContent changed) =>
        Dispatcher.BeginInvoke(() => _engine.Rebind(changed));

    /// <summary>A slower reading of what is shown landed on whatever thread read it; the content is laid out again here, on this element's.</summary>
    private void OnReread(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Refresh);

    private void OnCaretTaken(object? sender, EventArgs args)
    {
        HasCaret = !IsReadOnly;
        if (HasCaret) StartBlinking();
        InvalidateVisual();
    }

    private void OnCaretMoved(object? sender, EventArgs args) => CaretMoved?.Invoke(this, EventArgs.Empty);

    private void OnSelectionChanged(object? sender, ContentSelectionChange change) => SelectionChanged?.Invoke(this, change);

    private void OnSourceChanged(object? sender, ContentSourceChange change) => SourceChanged?.Invoke(this, change);

    private void OnRevealing(object? sender, Rect shown) =>
        BringIntoView(new Rect(shown.X * Zoom, shown.Y * Zoom, Math.Max(shown.Width * Zoom, 1), Math.Max(shown.Height * Zoom, 1)));

    /// <summary>The theme, for the ink, the accent and the two colours trouble is drawn in.</summary>
    protected StyleFormat Palette { get; }

    /// <summary>What is laid out. Always something: a builder always makes a layout.</summary>
    public Laid Laid => _engine.Laid;

    /// <summary>The editing model — source, caret, selection, and what is shown as written.</summary>
    protected EditState State => _engine.State;

    /// <summary>What lays the content out and holds it as it is being written — see <see cref="ContentEngine"/>.</summary>
    private readonly ContentEngine _engine;

    /// <summary>What the content is written in, or null for markdown.</summary>
    private readonly string? _language;

    /// <summary>What the content is written in, or null for markdown.</summary>
    protected string? WrittenIn => _language;

    /// <summary>
    /// What pointing at a piece means: the first ancestor that names a stretch of source. A bracket is drawn
    /// by the fence that holds it and cannot be selected alone — a bracket without its partner can't be read —
    /// so pointing at one selects the thing it belongs to.
    /// </summary>
    protected static Piece Pointing(Piece piece) => piece.Selectable();

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
    public bool IsReadOnly
    {
        get => _engine.IsReadOnly;
        init => _engine.IsReadOnly = value;
    }

    /// <summary>Whether this element currently owns the caret.</summary>
    public bool HasCaret { get; private set; }

    /// <summary>
    /// Whether there is a caret to draw: where something is being carried, the place it would land; otherwise the caret this has
    /// taken, in content that is written in — and not while whole things are chosen, a slice or a node, which is no place to write.
    /// </summary>
    internal bool ShowsCaret => !IsReadOnly && (_engine.Dropping is not null || (HasCaret && !_engine.ChoseWhole));

    /// <summary>Where the caret sits, as an offset into <see cref="Source"/>.</summary>
    public int Caret => State.Caret;

    /// <summary>The start of the selected source range.</summary>
    public int SelectionStart => State.SelectionStart;

    /// <summary>How much source is selected; zero when nothing is.</summary>
    public int SelectionLength => State.SelectionLength;

    /// <summary>The selected source, or empty.</summary>
    public string SelectedText => State.SelectedText;

    /// <summary>Whether any of the source could not be read.</summary>
    public bool HasError => Laid.Trouble.Count > 0;

    /// <summary>
    /// The stretch being shown as the characters written rather than set — a command mid-spelling — or
    /// null when all of it is read.
    /// </summary>
    public (int Start, int Length)? ShownAsWritten =>
        State.Raw is { Length: > 0 } zone ? (zone.Start, zone.Length) : null;

    // Somewhere written, or a hole standing where something is still to be written - which is somewhere to write too.

    /// <summary>Whether the caret belongs in this content at all. Asked of the tree rather than declared, since a piece naming a stretch of source is one somebody typed — a barcode that only prints a worked-out number has none.</summary>
    public bool AcceptsCaret =>
            State.Source.Length == 0 || Laid.Root.SelfAndDescendants().Any(piece => piece.Part is { Length: > 0 } || (piece.Part is not null && piece.Kind == LayoutText.HoleKind));

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token.</summary>
    private static Brush Wash(StyleFormat palette)
    {
        if (palette.Accent is not SolidColorBrush accent) return palette.Marked;

        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }

    // ── What is being written ───────────────────────────────────────────────

    /// <inheritdoc />
    public string Source => State.Source;

    /// <inheritdoc />
    public Piece Root => Laid.Root;

    /// <inheritdoc />
    public IReadOnlyList<(int Start, int Length)> Selection =>
        [.. State.Selection.Select(range => (range.Start, range.Length))];

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> Diagnostics => Laid.Trouble;

    // ── The caret ───────────────────────────────────────────────────────────

    /// <summary>Gives this content the caret at <paramref name="offset"/>, innermost of the places there.</summary>
    public void TakeCaret(int offset) => TakeCaret(offset, -1);

    /// <summary>Gives this content the caret at one particular place — what a press and a step both mean.</summary>
    public void TakeCaret(int offset, int at) => _engine.TakeCaret(offset, at);

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

    /// <summary>Moves the caret one stop. False when it ran off an end, where there is nowhere further to go.</summary>
    public bool MoveCaret(bool forward, bool extend = false) => _engine.MoveCaret(forward, extend);

    /// <summary>Up and down — across a fraction bar, out of a script, from a note to the word under it.</summary>
    public bool MoveCaretVertically(bool up, bool extend = false) => _engine.MoveCaretVertically(up, extend);

    /// <summary>
    /// Puts the caret at <paramref name="offset"/>, or stretches what is picked out to it — Home and End, and a caret put
    /// somewhere by whoever is hosting this.
    /// </summary>
    public void MoveCaretTo(int offset, bool extend = false) => _engine.MoveCaretTo(offset, extend);

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Type(char character) => Write(character.ToString());

    /// <summary>
    /// Writes text at the caret: whatever the content makes of it, and failing that the characters
    /// themselves, spliced in where the caret is.
    /// </summary>
    protected void Write(string text) => _engine.Write(text);

    /// <summary>
    /// Writes <paramref name="text"/> over a stretch of the source as typing it there would — the content's rule, through the
    /// same edit handling a key goes through — and leaves the caret where it was. What a press means when it is an edit
    /// somewhere other than the caret: a tick on a task's box.
    /// </summary>
    protected void WriteOver(int start, int length, string text) => _engine.WriteOver(start, length, text);

    /// <summary>
    /// Inserts text at the caret, replacing any selection — how a palette key types itself.
    /// <paramref name="caretBack"/> walks the caret into a template's first hole.
    /// </summary>
    public void Insert(string text, int caretBack = 0) => _engine.Insert(text, caretBack);

    /// <summary>Wraps the selection, or inserts the pair at the caret.</summary>
    public void Wrap(string before, string after) => _engine.Wrap(before, after);

    /// <summary>
    /// <paramref name="words"/> pasted where the caret is, as the language there says they are written — and whether it said anything;
    /// where it said nothing, they are the host's to write as it writes them.
    /// </summary>
    public bool Paste(string words) => _engine.Pasted(words);

    /// <summary>Backspace; un-renders a construct drawn from more source than it shows rather than deleting a character of it. False (nothing to delete) is the host's cue to remove the content itself.</summary>
    public bool Backspace() => _engine.Backspace();

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    public bool Delete() => _engine.Delete();

    /// <summary>Ends whatever is half-written — what Space and Enter mean, and the content's to say.</summary>
    public void Settle(string separator) => _engine.Settle(separator);

    /// <summary>Selects the next place still waiting to be written in, so an inserted construct can be filled by typing and tabbing. False when there is none.</summary>
    public bool SelectNextPlaceholder(bool forward = true) => _engine.SelectNextPlaceholder(forward);

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>Selects a source range, snapped out to whole constructs.</summary>
    public void Select(int start, int length) => _engine.Select(start, length);

    /// <inheritdoc />
    public void SelectRange(int start, int length) => Select(start, length);

    /// <summary>Selects everything — what the host asks for when a selection sweeps straight over it.</summary>
    public void SelectAll() => Select(0, Source.Length);

    /// <inheritdoc />
    public void ClearSelection() => _engine.ClearSelection();

    // ── Pointer, driven by the host ─────────────────────────────────────────

    /// <inheritdoc />
    public void BeginPointerSelect(Point pointInElement) => BeginPointerSelect(pointInElement, ModifierKeys.None);

    /// <summary>
    /// A press at a point on this content, held with <paramref name="modifiers"/> — taken by the engine as what it means
    /// there. True where the content took it; false where the press landed on nothing of it, which whatever shows the
    /// content may want for itself.
    /// </summary>
    public bool BeginPointerSelect(Point pointInElement, ModifierKeys modifiers)
    {
        _pressedAt = pointInElement;

        if (!_engine.Input(new ContentPress(Unscaled(pointInElement), 1, modifiers))) return false;

        _pressing = true;
        return true;
    }

    /// <summary>How far past a written run the pointer still counts as inside it — just over half the widest gap on a formula's line, so moving along one never flickers to an arrow between glyphs.</summary>
    private const double PointerReach = 4.0;

    /// <summary>
    /// What the pointer is over <paramref name="at"/>: a bar over what can be written in and an arrow elsewhere, and whatever
    /// the piece there says while it is pointed at (<see cref="Roles.Tip"/>).
    /// </summary>
    protected virtual Cursor Pointing(Point at)
    {
        Tip(Says(Laid.Root.PieceAt(at)));

        return !IsReadOnly && Laid.Root.Writable(at, PointerReach) ? Cursors.IBeam : Cursors.Arrow;
    }

    /// <summary>
    /// What <paramref name="piece"/> says while pointed at: the tip hung on the part of the source it was drawn from, or on
    /// what holds that part. Asked of the tree when the pointer arrives — nothing of it is laid out.
    /// </summary>
    private static string? Says(Piece piece)
    {
        if (!piece.Exists || piece.Naming() is not ContentPart named) return null;

        for (var part = named; part is not null; part = part.Parent)
            foreach (var child in part.Node.Children)
                if (child.IsDerived)
                    foreach (var held in child.Children)
                        if (held.Role == Roles.Tip && held.Held is string tip) return tip;

        return null;
    }

    /// <summary>What the piece under the pointer says while it is pointed at (<see cref="Tip"/>); null where it says nothing.</summary>
    public string? Saying { get; private set; }

    /// <summary>
    /// Says <paramref name="says"/> beside the pointer once it has rested there, or takes what was said away.
    ///
    /// <para>
    /// <strong>Opened here rather than left to <see cref="FrameworkElement.ToolTip"/>.</strong> WPF looks for a tip only when
    /// the pointer crosses onto another element, and a whole document is one element — so a tip that changes as the pointer
    /// moves along the words would never be looked for. The element keeps a tip of its own instead, and opens it once the
    /// pointer has rested on what says it for as long as any tip waits.
    /// </para>
    /// </summary>
    protected void Tip(string? says)
    {
        if (Equals(Saying, says)) return;

        Saying = says;
        _resting?.Stop();
        if (_tip is not null) _tip.IsOpen = false;

        if (says is null) return;

        _resting ??= Resting();
        _resting.Start();
    }

    /// <summary>What waits for the pointer to rest before the tip is opened.</summary>
    private DispatcherTimer Resting()
    {
        var resting = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(ToolTipService.GetInitialShowDelay(this)),
        };

        resting.Tick += (_, _) =>
        {
            resting.Stop();
            if (Saying is not { } says || !IsMouseOver) return;

            _tip ??= new ToolTip { PlacementTarget = this, Placement = PlacementMode.Mouse };
            _tip.Content = says;
            _tip.IsOpen = true;
        };

        return resting;
    }

    private DispatcherTimer? _resting;
    private ToolTip? _tip;

    /// <summary>What the pointer is over a point on this content: a bar only where something can be written in.</summary>
    public Cursor? PointerCursor(Point pointInElement) => Pointing(Unscaled(pointInElement));

    /// <summary>
    /// The pieces that answer to a gesture and are covered by what is picked out — empty where nothing is, which is
    /// what makes "for one item" and "for a selection of them" the same question asked twice.
    /// </summary>
    protected IReadOnlyList<Piece> Selected() =>
        [.. Laid.Root.SelfAndDescendants()
                 .Where(piece => piece.Acts is not null && piece.Sits() is { Length: > 0 } sits && _engine.Covers(sits.Start))];

    /// <summary>The pointer moved while pressed.</summary>
    public void ExtendPointerSelect(Point pointInElement)
    {
        // A click is not a drag. The pointer moves a pixel or two under any real hand, and treating that
        // as a selection meant clicking after a number selected it — so the next key typed replaced the
        // number instead of following it.
        if (!_pressing || !HasDragged(pointInElement)) return;

        _engine.Input(new ContentDrag(Unscaled(pointInElement)));
    }

    /// <summary>
    /// Whether the pointer has moved far enough from the press for this to be a drag rather than a click.
    /// The system's own thresholds, so it matches every other drag the reader makes.
    /// </summary>
    private bool HasDragged(Point pointInElement) =>
        Math.Abs(pointInElement.X - _pressedAt.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(pointInElement.Y - _pressedAt.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>The press was let go.</summary>
    public void EndPointerSelect()
    {
        _pressing = false;

        _engine.Input(new ContentRelease());
    }

    /// <summary>Two presses at a point on this content — taken by the engine as what they mean there.</summary>
    public bool PointerDoubleClick(Point pointInElement) => _engine.Input(new ContentPress(Unscaled(pointInElement), 2));

    /// <summary>What the piece under a point means by <paramref name="gesture"/>, or null where nothing there means anything by it.</summary>
    protected LayoutAct? Offered(Point at, LayoutGesture gesture) => _engine.Offered(at, gesture);

    /// <summary>Whether the host answered the gesture, which is the end of it.</summary>
    protected bool Answered(LayoutAct act) => _engine.Answered(act);

    /// <summary>What the host offers where a gesture landed, beside what the content itself offers.</summary>
    protected IReadOnlyList<LayoutIntent> Hosted(LayoutAct act) => _engine.Hosted(act);

    /// <summary>What the language drawn at a point on the content offers there — see <see cref="ContentEngine.Asked"/>.</summary>
    protected IReadOnlyList<LayoutIntent> Asked(Point at) => _engine.Asked(at);

    /// <summary>Does one of what the language offered at a point, as its edit handler says — see <see cref="ContentEngine.Choose"/>.</summary>
    protected bool Choose(string verb, Point at) => _engine.Choose(verb, at);

    // Hosted in a plain panel (the read-only markdown view), the element does get its own mouse events.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Tip(null);
        if (e.ClickCount == 2) { PointerDoubleClick(e.GetPosition(this)); return; }

        // Claimed only where the content took the press, so a press it made nothing of is left unhandled and uncaptured
        // for whatever shows the content — which is how a viewport knows it may pan from there. Capturing regardless and
        // then losing it to the host is what left the drag going with no button-up ever coming to end it.
        if (!BeginPointerSelect(e.GetPosition(this), ModifierKeys.None)) return;

        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// The capture taken away, by a host that has decided the gesture is its own after all. The press is over as far as
    /// this content goes: the button-up will be delivered to whoever holds the capture, so without this the drag would
    /// run on and every later move would extend a selection nobody started.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_pressing) EndPointerSelect();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // The pointer says what can be done where it is: a bar over what can be written in, an arrow over a wedge of a
        // pie, a barcode, a staff line — drawing nobody types into.
        Cursor = Pointing(Unscaled(e.GetPosition(this)));
        ForceCursor = true;

        var over = Unscaled(e.GetPosition(this));
        _engine.Input(new ContentHover(over));

        // The corner is faint until the pointer is on it, and whole once it is.
        var onCorner = _engine.Corner?.Root.Bounds.Contains(over) == true;
        if (onCorner != _onCorner) { _onCorner = onCorner; InvalidateVisual(); }

        if (_pressing) ExtendPointerSelect(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        EndPointerSelect();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Tip(null);

        _onCorner = false;
        _engine.Input(new ContentHover(null));
    }

    // ── Applying an edit ────────────────────────────────────────────────────

    /// <param name="at">
    /// Which place the caret is standing at, when a step has just said. -1 otherwise, which puts it back at
    /// the innermost place at its offset — where a reader who has just typed, clicked or jumped is.
    /// </param>
    protected void Apply(EditState next, bool notify, int at = -1) => _engine.Apply(next, notify, at);

    /// <summary>Lays the content out again, because something outside it changed.</summary>
    public void Refresh()
    {
        _engine.Refresh();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>A picture of the content at its shown size/density, with nothing drawn only for the writer: no caret, selection, hole, trouble squiggle or raw-typed text.</summary>
    /// <param name="ground">What it is drawn on, or null for nothing behind what the content draws.</param>
    public BitmapSource Picture(Brush? ground = null)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Through the engine's own runner, so a language that falls over costs the picture its drawing rather than the window.
        var laid = _engine.LaidOut(State with { Selected = null, Raw = null }, readOnly: true);
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

        _measuring = true;
        try { _engine.Fit(room); }
        finally { _measuring = false; }

        // While something is being carried, what is on screen is what it would become, so that is what has
        // to fit — otherwise the preview is clipped at the settled content's width.
        var size = _engine.Preview?.Laid.Size ?? Laid.Size;
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

        if (_engine.Preview is { } preview) PaintPreview(dc, preview.Laid, (preview.Start, preview.End));
        else PaintContent(dc);

        // Over everything, the buttons in the corner of the block the pointer is over.
        if (_engine.Corner is { } corner)
        {
            if (!_onCorner) dc.PushOpacity(Faint);
            LayoutPainter.Paint(dc, corner.Root, Palette.Text);
            if (!_onCorner) dc.Pop();
        }

        if (scaled) dc.Pop();
    }

    private void PaintContent(DrawingContext dc)
    {
        var laid = Laid;
        var state = State;

        // Only what is near the part on screen, where whatever holds this says which part that is — in the content's own
        // units, which is what the tree is measured in.
        var shown = _onScreen is { } showing
            ? new Rect(showing.X / Scale, showing.Y / Scale, showing.Width / Scale, showing.Height / Scale)
            : (Rect?)null;

        _painted = shown is { } near ? LayoutPainter.Around(near) : null;
        LayoutPainter.Paint(dc, laid.Root, Palette.Text, shown);

        // One shape for the whole selection, joined across the spacing between what it holds — and not one box
        // around it all: a column of a matrix washed from its first cell to its last would highlight the lot.
        if (state.HasSelection) dc.DrawGeometry(_wash, null, laid.Root.Wash(state.Selection, WashPad));

        // What of content a binding supplied is picked out stands for no source, so it is washed piece by piece —
        // over the shape it stands in where it has one, as a range of source is (see RangeRegions). A box is right
        // for a box, and wrong for an arrow: the box round a diagonal one is mostly not the arrow.
        foreach (var picked in _engine.PickedWhole)
        {
            if (picked.OnPage() is { } standing) dc.DrawGeometry(_wash, null, standing);
            else if (picked.Ink() is { IsEmpty: false } ink) dc.DrawRectangle(_wash, null, Rect.Inflate(ink, WashPad, WashPad));
        }

        Waves(dc, laid);

        PaintOver(dc);

        var dropping = _engine.Dropping;
        if (!ShowsCaret || !_caretVisible) return;

        // While something is being carried the caret shows where it would land, not where it was picked
        // up from — that is the one thing the reader needs to see before letting go.
        var caret = dropping is not null || _engine.At < 0
            ? laid.Root.CaretRect(dropping ?? state.Caret)
            : laid.Places[_engine.At].CaretRect();

        DrawCaret(dc, caret.X, caret.Y, caret.Height);
    }

    /// <summary>
    /// A wave under whatever could not be read, drawn over the content rather than instead of it: the parts that did read are
    /// still worth looking at, and the reader needs to see which part is not.
    /// </summary>
    private void Waves(DrawingContext dc, Laid laid)
    {
        foreach (var trouble in laid.Trouble)
        {
            var runs = LayoutQuery.Clusters(laid.Root.RangeRects(trouble.Start, trouble.Length), 0);
            if (runs.Count == 0) continue;

            var wave = new Pen(trouble.Severity == DiagnosticSeverity.Error ? Palette.Danger : Palette.Warning, 1.0);
            wave.Freeze();
            dc.DrawGeometry(null, wave, Squiggle.Under(runs));
        }
    }

    /// <summary>
    /// The part of the element on screen, in its own coordinates — what whatever scrolls it says it is showing — or null where
    /// all of it may be looked at. Only the blocks near it are painted, and the pictures kept of blocks far from it are let
    /// go, so a document costs about a screen of pictures however long it is.
    /// </summary>
    public Rect? OnScreen
    {
        get => _onScreen;
        set
        {
            if (_onScreen == value) return;
            _onScreen = value;

            _engine.OnScreen = value is { } seen ? new Rect(seen.X / Scale, seen.Y / Scale, seen.Width / Scale, seen.Height / Scale) : null;

            // Painted again only once what is shown leaves what was painted round it last time.
            if (value is not { } shown || _painted is not { } painted
                || !painted.Contains(new Rect(shown.X / Scale, shown.Y / Scale, shown.Width / Scale, shown.Height / Scale)))
                InvalidateVisual();
        }
    }

    private Rect? _onScreen;

    /// <summary>What was painted round what was shown, the last time anything was painted — in the content's own units.</summary>
    private Rect? _painted;

    /// <summary>Anything the content draws over the shared picture (e.g. a strike-through on a symbol that won't encode). Drawn after the ink and wash, before the caret.</summary>
    protected virtual void PaintOver(DrawingContext dc) { }

    /// <summary>Draws the content as it would read after the drop, with the carried part in the accent colour — once it's merged in (braces, spacing and all) nothing else would distinguish it.</summary>
    private void PaintPreview(DrawingContext dc, Laid preview, (int Start, int End) moved)
    {
    LayoutPainter.Paint(dc, preview.Root, Palette.Text);

    // Over the top rather than instead of: painting all of it and then the carried part again is what
    // keeps this to two calls, and the second colour is the one that shows.
    foreach (var piece in Carried(preview, moved))
            LayoutPainter.PaintOne(dc, piece, Palette.Accent);
    }

    /// <summary>The outermost pieces of <paramref name="preview"/> lying wholly inside what is carried — outermost so a piece and its children aren't painted twice.</summary>
    private static IEnumerable<Piece> Carried(Laid preview, (int Start, int End) moved)
    {
    var (start, end) = moved;
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
