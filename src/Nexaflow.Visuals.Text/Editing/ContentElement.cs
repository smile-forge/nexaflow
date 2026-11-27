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
using System.Windows.Automation.Peers;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using System.Windows.Interop;

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
    public ContentElement(string? source, LayoutStyle palette, ContentEngine engine, string? language = null, ILayoutActions? actions = null)
    {
        Palette = palette;
        _engine = engine;
        _language = language;
        _wash = Wash(palette);

        _engine.Show(language, palette);
        _engine.Start(source);
        _engine.Answers(actions);

        _engine.Changed += OnChanged;
        _engine.PreRender += OnLaid;
        _engine.CaretTaken += OnCaretTaken;
        _engine.Remarked += OnRemarked;
        _engine.CaretMoved += OnCaretMoved;
        _engine.SelectionChanged += OnSelectionChanged;
        _engine.SourceChanged += OnSourceChanged;
        _engine.Revealing += OnRevealing;
        _engine.Rebound += OnRebound;
        _engine.Reread += OnReread;
        _engine.Grew += OnGrew;
        _engine.ViewMoved += OnViewMoved;
        _engine.Bringing += OnBringing;
        RenderTransform = _slid;

        SnapsToDevicePixels = true;
        Cursor = Cursors.IBeam;

        // Never take keyboard focus. Content is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text,
        // and faults deep inside the splay tree.
        Focusable = false;

        Loaded += (_, _) => HookSideways();
        Unloaded += (_, _) =>
        {
            StopBlinking();
            Tip(null);
            UnhookSideways();
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
        _engine.Remarked -= OnRemarked;
        _engine.CaretMoved -= OnCaretMoved;
        _engine.SelectionChanged -= OnSelectionChanged;
        _engine.SourceChanged -= OnSourceChanged;
        _engine.Revealing -= OnRevealing;
        _engine.Rebound -= OnRebound;
        _engine.Reread -= OnReread;
        _engine.Grew -= OnGrew;
        _engine.ViewMoved -= OnViewMoved;
        _engine.Bringing -= OnBringing;

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

        // Laid out again, so the links a screen reader was told of are not the ones there now.
        UIElementAutomationPeer.FromElement(this)?.ResetChildrenCache();
    }

    /// <summary>Bound content changed on whatever thread it walked on; it is laid out again here, on this element's.</summary>
    private void OnRebound(object? sender, IBoundContent changed) =>
        Dispatcher.BeginInvoke(() => _engine.Rebind(changed));

    /// <summary>A slower reading of what is shown landed on whatever thread read it; the content is laid out again here, on this element's.</summary>
    private void OnReread(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Relanded);

    /// <summary>Lays again what a slower reading that landed was asked for by, and nothing else.</summary>
    private void Relanded()
    {
        _engine.RelayLanded();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// A long document has more to lay — the reader came near what was not laid yet, or more of it has been read — on whatever
    /// thread said so. It is laid here, on this element's, keeping everything already laid.
    /// </summary>
    private void OnGrew(object? sender, EventArgs args) => Dispatcher.BeginInvoke(Grown);

    private void Grown()
    {
        _engine.Relay();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCaretTaken(object? sender, EventArgs args)
    {
        HasCaret = !IsReadOnly;
        if (HasCaret) StartBlinking();
        InvalidateVisual();
    }

    private void OnRemarked(object? sender, EventArgs args) => InvalidateVisual();

    /// <summary>
    /// The engine's view moved or was sized again. While it stays inside what was painted round it last time, the picture is only slid — a
    /// move of no more than a screen or two, which drawing keeps exactly — and nothing is painted; once it leaves that, the content is painted
    /// again, measured from where the view now stands.
    /// </summary>
    private void OnViewMoved(object? sender, EventArgs args)
    {
        var origin = _engine.ViewOrigin;

        if (_painted is { } painted && _engine.OnScreen is { } view && painted.Contains(view))
        {
            Slide(origin);
            return;
        }

        // Until it is painted again, what was painted is slid as far as it goes; the paint measures from the view as it then stands.
        Slide(origin);
        InvalidateVisual();
    }

    /// <summary>Slides what was painted from <see cref="_paintedFrom"/> to stand under the view at <paramref name="origin"/>.</summary>
    private void Slide(LayoutVector origin)
    {
        var (x, y) = ((_paintedFrom.X - origin.X) * Scale, (_paintedFrom.Y - origin.Y) * Scale);
        if (_slid.X == x && _slid.Y == y) return;

        _slid.X = x;
        _slid.Y = y;
    }

    /// <summary>
    /// Where the engine's view stood when what is on the element was painted, in the content's own units — what everything drawn, and every
    /// point on the element, is measured from (<see cref="ViewShift"/>). Nought for content shown whole.
    /// </summary>
    private LayoutVector _paintedFrom;

    /// <summary>How far what was painted is slid to stand under the view as it now stands — a screen or two at most.</summary>
    private readonly TranslateTransform _slid = new();

    /// <summary>
    /// How far along the page is drawn from where it stands: minus where the engine's view stood when it was painted
    /// (<see cref="_paintedFrom"/>), where the content scrolls itself — so what is drawn stands within a screen or two of nought however far
    /// down a long document the view is — and nothing where the content is shown whole.
    /// </summary>
    private LayoutVector ViewShift => new(-_paintedFrom.X, -_paintedFrom.Y);

    private void OnCaretMoved(object? sender, EventArgs args) => CaretMoved?.Invoke(this, EventArgs.Empty);

    private void OnSelectionChanged(object? sender, ContentSelectionChange change) => SelectionChanged?.Invoke(this, change);

    private void OnSourceChanged(object? sender, ContentSourceChange change) => SourceChanged?.Invoke(this, change);

    private void OnRevealing(object? sender, LayoutRect shown) => _engine.BringOnto(shown);

    /// <summary>
    /// The engine asks for a stretch of content shown whole, in the content's own units, to be brought onto the screen of whatever holds this
    /// (<see cref="ContentEngine.BringOnto"/>).
    /// </summary>
    private void OnBringing(object? sender, LayoutRect box) =>
        BringIntoView(new Rect(box.X * Zoom, box.Y * Zoom, Math.Max(box.Width * Zoom, 1), Math.Max(box.Height * Zoom, 1)));

    /// <summary>Brings a stretch of the source onto the page and picks it out (<see cref="ContentEngine.ShowPlace"/>).</summary>
    protected bool ShowPlace((int Start, int Length) what, bool choose) => _engine.ShowPlace(what, choose);

    /// <summary>The theme, for the ink, the accent and the two colours trouble is drawn in.</summary>
    protected LayoutStyle Palette { get; }

    /// <summary>What is laid out. Always something: a builder always makes a layout.</summary>
    public Laid Laid => _engine.Laid;

    /// <summary>What the content's links are to UI Automation — see <see cref="ContentElementAutomationPeer"/>.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new ContentElementAutomationPeer(this);

    /// <summary>The links in what is shown, as the engine lays them out.</summary>
    internal IReadOnlyList<LaidLink> LaidLinks() => _engine.LaidLinks();

    /// <summary>Whether <paramref name="link"/> still describes what is shown.</summary>
    internal bool Holds(LaidLink link) => _engine.Holds(link);

    /// <summary>Follows <paramref name="link"/> as a click on it would.</summary>
    internal bool Open(LaidLink link) => _engine.Open(link);

    /// <summary>A place in the content's own units, on this element — the way back from <c>Unscaled</c>, measured from the view where there is one.</summary>
    internal Point FromContent(Point content)
    {
        var by = ViewShift;
        return new((content.X + by.X) * Scale, (content.Y + by.Y) * Scale);
    }

    /// <summary>A stretch of the content in its own units, on this element.</summary>
    internal Rect FromContent(Rect content) =>
        content.IsEmpty ? Rect.Empty : new Rect(FromContent(content.TopLeft), FromContent(content.BottomRight));

    /// <summary>A place in the content's own units, on this element.</summary>
    internal Point FromContent(LayoutPoint content) => FromContent(content.ToWpf());

    /// <summary>A stretch of the content in its own units, on this element.</summary>
    internal Rect FromContent(LayoutRect content) => FromContent(content.ToWpf());

    /// <summary>Whether any of a stretch of the content is on screen — everything is, where nothing has said what the screen is.</summary>
    internal bool ReachesScreen(LayoutRect content) => _engine.OnScreen is not { } screen || screen.IntersectsWith(content);

    /// <summary>The editing model — source, caret, selection, and what is shown as written.</summary>
    protected EditState State => _engine.State;

    /// <summary>The document as it is shown, at the version it is at.</summary>
    public ContentDocument Document => State.Document;

    /// <summary>Opens whatever of the document is folded away over <paramref name="offset"/> (<see cref="ContentEngine.Unfolded"/>).</summary>
    protected bool Unfold(int offset) => _engine.Unfolded(offset);

    /// <summary>Lays a long document as far as <paramref name="offset"/> and brings it onto the page once it is (<see cref="ContentEngine.Uncover"/>).</summary>
    protected bool Uncover(int offset) => _engine.Uncover(offset);

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
    protected static LayoutPiece Pointing(LayoutPiece piece) => piece.Selectable();

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

    /// <summary>
    /// Whether content given no width at all is laid out with none to keep to — a diagram at its own size however wide it
    /// comes to, and a paragraph on one line — rather than in the room a page of reading is set in. For a host that is the
    /// whole of a canvas the reader pans and zooms, which fits what it is shown itself; anywhere else what is given no width is
    /// still read as a page.
    /// </summary>
    public bool UnlimitedWidth { get; init; }

    /// <summary>Whether this element currently owns the caret.</summary>
    public bool HasCaret { get; private set; }

    /// <summary>
    /// Whether there is a caret to draw: where something is being carried, the place it would land; otherwise the caret this has
    /// taken, in content that is written in — and not while whole things are chosen, a slice or a node, which is no place to write.
    /// </summary>
    internal bool ShowsCaret => _engine.DrawsCaret(HasCaret);

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

    /// <summary>Where on the page what is being carried would go into stands, where its language outlines that rather than showing the result.</summary>
    internal LayoutRect? Marking => _engine.Marking;

    /// <summary>Whether what is being carried is shown as the content would read once let go.</summary>
    internal bool ShowsCarriedResult => _engine.Preview is not null;

    // Somewhere written, or a hole standing where something is still to be written - which is somewhere to write too.

    /// <summary>Whether the caret belongs in this content at all (<see cref="ContentEngine.AcceptsCaret"/>).</summary>
    public bool AcceptsCaret => _engine.AcceptsCaret;

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token.</summary>
    private static Brush Wash(LayoutStyle palette)
    {
        if (palette.Accent.Brush() is not SolidColorBrush accent) return palette.Marked.Brush();

        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }

    // ── What is being written ───────────────────────────────────────────────

    /// <inheritdoc />
    public LayoutPiece Root => Laid.Root;

    /// <inheritdoc />
    public IReadOnlyList<(int Start, int Length)> Selection =>
        [.. State.Selection.Select(range => (range.Start, range.Length))];

    /// <inheritdoc />
    public IReadOnlyList<LayoutDiagnostic> Diagnostics => Laid.Trouble;

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

    /// <summary>Loads <paramref name="text"/> into the document shown, in place of what it held — as a page loading it would.</summary>
    protected void Load(string text) => _engine.EditedDocument.Load(text);

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
    public void SelectAll() => Select(0, State.Document.Length);

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

        if (!_engine.Input(new ContentPress(Unscaled(pointInElement), 1, WpfInput.Held(modifiers)))) return false;

        _pressing = true;
        return true;
    }

    /// <summary>
    /// What the pointer is over <paramref name="at"/>, and whatever the piece there says while it is pointed at (<see cref="ContentEngine.PointedAt"/>).
    /// </summary>
    protected virtual Cursor Pointing(LayoutPoint at)
    {
        var (shape, tip) = _engine.PointedAt(at);
        Tip(tip);

        return WpfInput.Pointer(shape);
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

    /// <summary>The pieces that answer to a gesture and are covered by what is picked out (<see cref="ContentEngine.ActsSelected"/>).</summary>
    protected IReadOnlyList<LayoutPiece> Selected() => _engine.ActsSelected();

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

    /// <summary>How far one notch of the wheel slides a box sideways, in the content's own units.</summary>
    private const double SlidePerNotch = 48;

    /// <summary>
    /// The wheel turned sideways over a point on this content, <paramref name="notches"/> notches towards the end of its lines — a touchpad's
    /// sweep being parts of one — taken by the engine as a slide of the box there. False where nothing there slides.
    /// </summary>
    public bool PointerSlid(Point pointInElement, double notches) =>
        _engine.Input(new ContentSlide(Unscaled(pointInElement), notches * SlidePerNotch));

    /// <summary>Brings <paramref name="offset"/> into the box it is set in, where that box has slid it out of view — see <see cref="ContentEngine.SlideOnto"/>.</summary>
    protected bool SlideOnto(int offset) => _engine.SlideOnto(offset);

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!e.Handled && WheeledSideways(e)) e.Handled = true;
    }

    /// <summary>
    /// The wheel turned with Shift held, which turns it sideways as it does everywhere on Windows — turned up, back towards the start of the
    /// lines — taken as a slide of the box under the pointer. Whether a box slid.
    /// </summary>
    internal bool WheeledSideways(MouseWheelEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.Shift && PointerSlid(e.GetPosition(this), -e.Delta / (double)Mouse.MouseWheelDeltaForOneLine);

    /// <summary>The window this is shown in, while it hands this the wheel turned sideways — which WPF hands nothing.</summary>
    private HwndSource? _sideways;

    /// <summary>What Windows says when the wheel is tilted, or a touchpad swept, sideways.</summary>
    private const int WmMouseHWheel = 0x020E;

    /// <summary>Listens to the window this is shown in for the wheel turned sideways.</summary>
    private void HookSideways()
    {
        if (_sideways is not null || PresentationSource.FromVisual(this) is not HwndSource window) return;

        _sideways = window;
        window.AddHook(Sideways);
    }

    /// <summary>Stops listening for the wheel turned sideways.</summary>
    private void UnhookSideways()
    {
        _sideways?.RemoveHook(Sideways);
        _sideways = null;
    }

    /// <summary>The wheel turned sideways while the pointer is over this content: the box under it slid, tilted right being towards the end of its lines.</summary>
    private IntPtr Sideways(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMouseHWheel || handled || !IsMouseOver) return IntPtr.Zero;

        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        if (PointerSlid(Mouse.GetPosition(this), delta / (double)Mouse.MouseWheelDeltaForOneLine)) handled = true;

        return IntPtr.Zero;
    }

    /// <summary>What the piece under a point means by <paramref name="gesture"/>, or null where nothing there means anything by it.</summary>
    protected LayoutAct? Offered(LayoutPoint at, LayoutGesture gesture) => _engine.Offered(at, gesture);

    /// <summary>Whether the host answered the gesture, which is the end of it.</summary>
    protected bool Answered(LayoutAct act) => _engine.Answered(act);

    /// <summary>What the host offers where a gesture landed, beside what the content itself offers.</summary>
    protected IReadOnlyList<LayoutIntent> Hosted(LayoutAct act) => _engine.Hosted(act);

    /// <summary>What the language drawn at a point on the content offers there — see <see cref="ContentEngine.Asked"/>.</summary>
    protected IReadOnlyList<MiniRibbonControl> Asked(LayoutPoint at) => _engine.Asked(at);

    /// <summary>Uses a control of the ribbon opened at a point, its callback run against the content as it stands — see <see cref="ContentEngine.Used"/>.</summary>
    protected bool Use(LayoutPoint at, Func<ContentEdit, ContentChange?> made) => _engine.Used(at, made);

    // Hosted in a plain panel (the read-only markdown view), the element does get its own mouse events.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Tip(null);
        if (e.ClickCount == 2) { PointerDoubleClick(e.GetPosition(this)); return; }

        // Claimed only where the content took the press, so a press it made nothing of is left unhandled and uncaptured
        // for whatever shows the content — which is how a viewport knows it may pan from there. Capturing regardless and
        // then losing it to the host is what left the drag going with no button-up ever coming to end it.
        // With the keys held as it was pressed: Ctrl adds what it lands on to what is picked out, and Shift picks out to it.
        if (!BeginPointerSelect(e.GetPosition(this), Keyboard.Modifiers)) return;

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
        var onCorner = _engine.OnCorner(over);
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
            WpfPainter.Paint(dc, laid.Root, Palette.Text.Brush());
            if (scaled) dc.Pop();
        }

        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width * pixelsPerDip), (int)Math.Ceiling(size.Height * pixelsPerDip),
                                             96 * pixelsPerDip, 96 * pixelsPerDip, PixelFormats.Pbgra32);
        picture.Render(drawing);
        picture.Freeze();
        return picture;
    }

    // ── Layout and painting ─────────────────────────────────────────────────

    /// <summary>The width the content is laid in while what holds it slides sideways (<see cref="ContentEngine.SlidingRoom"/>).</summary>
    internal double? SlidingRoom
    {
        get => _engine.SlidingRoom;
        set => _engine.SlidingRoom = value;
    }

    /// <summary>A point in this element's own pixels as a point in the content, measured from the view where there is one.</summary>
    internal LayoutPoint InContent(Point pointInElement) => Unscaled(pointInElement);

    protected override Size MeasureOverride(Size availableSize)
    {
        var room = _engine.RoomOffered(availableSize.Width, UnlimitedWidth);

        _measuring = true;
        try { _engine.Fit(room); }
        finally { _measuring = false; }

        // While something is being carried, what is on screen is what it would become, so that is what has
        // to fit — otherwise the preview is clipped at the settled content's width.
        var size = _engine.Preview?.Laid.Size ?? Laid.Size;
        var (wide, tall) = (Math.Ceiling(size.Width * Scale), Math.Ceiling(size.Height * Scale));

        // Content that scrolls itself is as large as its view, which it draws the part of the page under: as large as it is given.
        if (_engine.Scrolls)
            return new Size(double.IsPositiveInfinity(availableSize.Width) ? wide : availableSize.Width,
                            double.IsPositiveInfinity(availableSize.Height) ? tall : availableSize.Height);

        return new Size(wide, tall);
    }

    /// <summary>Content that scrolls itself tells the engine how large its view is, in the content's own units.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_engine.Scrolls) _engine.ViewSized(finalSize.Width / Scale, finalSize.Height / Scale);

        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A transparent fill makes the whole element hit-testable, gaps between glyphs included — and, for content that scrolls itself, the
        // screen above and below it that is painted with it, which a slide can bring under the view before it is painted again.
        var (wide, tall) = (RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(Brushes.Transparent, null, _engine.Scrolls ? new Rect(0, -tall, wide, tall * 3) : new Rect(0, 0, wide, tall));

        // Everything below is in the content's own coordinates. The scale is pushed once, here, so nothing
        // that reads the tree has to know about it — and a pointer coming the other way is divided by it.
        var scaled = Math.Abs(Scale - 1.0) > 0.001;
        if (scaled) dc.PushTransform(new ScaleTransform(Scale, Scale));

        // Measured from where the view stands as it is painted — whatever asked for the paint — and slid back under it: see ViewShift.
        _paintedFrom = _engine.ViewOrigin;
        Slide(_paintedFrom);
        var by = ViewShift;

        if (_engine.Preview is { } preview) PaintPreview(dc, preview.Laid, by);
        else PaintContent(dc, by);

        // Over everything, the buttons in the corner of the block the pointer is over.
        if (_engine.Corner is { } corner)
        {
            if (!_onCorner) dc.PushOpacity(Faint);
            WpfPainter.Paint(dc, corner.Root, Palette.Text.Brush(), by: by);
            if (!_onCorner) dc.Pop();
        }

        if (scaled) dc.Pop();
    }

    /// <summary>How thick the outline round what a carry would go into is drawn, and how far outside it.</summary>
    private const double MarkThickness = 2;

    /// <summary>The content, and everything drawn over it, measured from where the page is drawn from (<paramref name="by"/>).</summary>
    private void PaintContent(DrawingContext dc, LayoutVector by)
    {
        var laid = Laid;
        var state = State;

        // Only what is near the part on screen, where something says which part that is — in the content's own units, which is what the
        // tree is measured in.
        var shown = _engine.OnScreen is { } showing ? showing.ToWpf() : (Rect?)null;

        _painted = shown is { } near ? WpfPainter.Around(near).ToLayout() : null;
        WpfPainter.Paint(dc, laid.Root, Palette.Text.Brush(), shown, by);

        // One shape for the whole selection, joined across the spacing between what it holds — and not one box
        // around it all: a column of a matrix washed from its first cell to its last would highlight the lot.
        // Only what is near the screen of it: a selection running down a long document is washed where it can be seen.
        if (state.HasSelection && laid.Root.Wash(state.Selection, WashPad, _painted, by) is WpfShape washed) dc.DrawGeometry(_wash, null, washed.Geometry);

        // What of content a binding supplied is picked out stands for no source, so it is washed piece by piece —
        // over the shape it stands in where it has one, as a range of source is (see RangeRegions). A box is right
        // for a box, and wrong for an arrow: the box round a diagonal one is mostly not the arrow.
        foreach (var picked in _engine.PickedWhole)
        {
            if (picked.OnPage(by) is WpfShape standing) dc.DrawGeometry(_wash, null, standing.Geometry);
            else if (picked.Ink() is { IsEmpty: false } ink) dc.DrawRectangle(_wash, null, Rect.Inflate(LayoutRect.Offset(ink, by).ToWpf(), WashPad, WashPad));
        }

        Waves(dc, by);

        PaintOver(dc);
        PaintMarks(dc, laid, state, by);

        // What a carry would go into, outlined on the content as it stands: the drop is chosen against what is on screen.
        if (_engine.Marking is { } marked)
            dc.DrawRectangle(null, new Pen(Palette.Accent.Brush(), MarkThickness), Rect.Inflate(LayoutRect.Offset(marked, by).ToWpf(), MarkThickness, MarkThickness));

        if (!ShowsCaret || !_caretVisible) return;

        // Where the engine says the carets stand: where something carried would land, and a caret on every line of a block of columns.
        foreach (var caret in _engine.CaretsShown()) DrawCaret(dc, caret.X + by.X, caret.Y + by.Y, caret.Height);
    }

    /// <summary>The theme token the line the caret is on is washed in.</summary>
    private const string CaretLineToken = "TextSwatch.CurrentLine";

    /// <summary>Whether the line the caret is on is washed across the page's width, as a code editor shows where it is writing.</summary>
    internal bool HighlightsCaretLine
    {
        get => _highlightsCaretLine;
        set
        {
            if (_highlightsCaretLine == value) return;

            _highlightsCaretLine = value;
            InvalidateVisual();
        }
    }

    private bool _highlightsCaretLine;

    /// <summary>
    /// The marks laid over the view of the document (<see cref="ContentEditor.Marks"/>), each drawn as it says in the ink its token is in the
    /// theme — asked only about the stretch near the screen (<see cref="ContentEngine.MarksNear"/>) — and, first, the line the caret is on where
    /// that is washed.
    /// </summary>
    private void PaintMarks(DrawingContext dc, Laid laid, EditState state, LayoutVector by)
    {
        if (_engine.NearScreen(_painted) is not { } near) return;

        var caretLine = HighlightsCaretLine && !state.HasSelection;
        var marks = _engine.MarksNear(near);
        if ((marks is null || marks.Count == 0) && !caretLine) return;

        // A wash across the page runs from its left edge on screen to past the end of its longest line, however far it has slid.
        var width = Math.Max(RenderSize.Width / Scale, laid.Size.Width + by.X);
        var inks = new Dictionary<string, Brush>(StringComparer.Ordinal);

        if (caretLine)
            foreach (var (top, tall) in _engine.RowsOfLines(state.Caret, state.Caret, near))
                dc.DrawRectangle(Ink(inks, CaretLineToken, Palette.TextMuted), null, new Rect(0, top + by.Y, width, tall));

        if (marks is null || marks.Count == 0) return;

        Pen? hairline = null;

        foreach (var mark in marks)
        {
            switch (mark.Kind)
            {
                case ContentMarkKind.Wash:
                    foreach (var run in LayoutQuery.Clusters(laid.Root.RangeRects(mark.Start, mark.Length), 0))
                        dc.DrawRectangle(Ink(inks, mark.Token, Palette.Marked), null, LayoutRect.Offset(run, by).ToWpf());
                    break;

                case ContentMarkKind.Outline:
                    var pen = new Pen(Ink(inks, mark.Token, Palette.Accent), Math.Max(1, Palette.TextSize / 13.5));
                    pen.Freeze();
                    foreach (var run in LayoutQuery.Clusters(laid.Root.RangeRects(mark.Start, mark.Length), 0)) dc.DrawRectangle(null, pen, LayoutRect.Offset(run, by).ToWpf());
                    break;

                case ContentMarkKind.Underbar:
                    if ((mark.Colour is { } colour ? Colour(colour) : Named(inks, mark.Token)) is not { } bar) break;

                    // Edged by a hairline, so a white or nearly clear colour is still a mark rather than nothing.
                    hairline ??= Hairline(Ink(inks, "BorderBrush", Palette.TextMuted));
                    var thick = Math.Max(2, Palette.TextSize / 6.75);
                    foreach (var run in laid.Root.RangeRects(mark.Start, mark.Length))
                        dc.DrawRectangle(bar, hairline, new Rect(run.X + by.X, run.Y + by.Y + run.Height - thick, run.Width, thick));
                    break;

                case ContentMarkKind.LineWash or ContentMarkKind.Veil:
                    var ground = Ink(inks, mark.Token, Palette.TextMuted);
                    foreach (var (top, tall) in _engine.RowsOfLines(mark.Start, mark.End, near))
                        dc.DrawRectangle(ground, null, new Rect(0, top + by.Y, width, tall));
                    break;
            }
        }
    }

    /// <summary>The brush <paramref name="token"/> names in the theme — <paramref name="fallback"/> where it names none — found once a paint.</summary>
    private Brush Ink(Dictionary<string, Brush> found, string token, LayoutInk fallback)
    {
        if (found.TryGetValue(token, out var known)) return known;

        return found[token] = TryFindResource(token) as Brush ?? fallback.Brush();
    }

    /// <summary>A colour the document names, as a brush.</summary>
    private static Brush Colour(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();

        return brush;
    }

    /// <summary>What the theme names <paramref name="token"/> — a brush, or a colour — or null where it names nothing so.</summary>
    private Brush? Named(Dictionary<string, Brush> found, string token)
    {
        if (found.TryGetValue(token, out var known)) return known;

        var named = TryFindResource(token) switch
        {
            Brush brush => brush,
            Color colour => Colour((uint)((colour.A << 24) | (colour.R << 16) | (colour.G << 8) | colour.B)),
            _ => null,
        };

        if (named is not null) found[token] = named;
        return named;
    }

    /// <summary>A hairline in <paramref name="ink"/>.</summary>
    private static Pen Hairline(Brush ink)
    {
        var pen = new Pen(ink, 0.6);
        pen.Freeze();

        return pen;
    }

    /// <summary>
    /// A wave under whatever could not be read, drawn over the content rather than instead of it: the parts that did read are still worth looking
    /// at, and the reader needs to see which part is not. Only the waves the engine says are near what is painted (<see cref="ContentEngine.WavesNear"/>),
    /// each measured from where the page is drawn from (<paramref name="by"/>).
    /// </summary>
    private void Waves(DrawingContext dc, LayoutVector by)
    {
        foreach (var wave in _engine.WavesNear(_painted))
            dc.DrawGeometry(null, wave.Severity == LayoutSeverity.Error ? ErrorWave : WarningWave,
                            Squiggle.Under(wave.Runs.Select(run => LayoutRect.Offset(run, by).ToWpf())));
    }
    /// <summary>What a wave is drawn with, in the colour of what it is under.</summary>
    private Pen ErrorWave => _errorPen ??= Frozen(new Pen(Palette.Danger.Brush(), 1.0));
    private Pen WarningWave => _warningPen ??= Frozen(new Pen(Palette.Warning.Brush(), 1.0));
    private Pen? _errorPen;
    private Pen? _warningPen;

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
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

            _engine.OnScreen = value is { } seen ? new LayoutRect(seen.X / Scale, seen.Y / Scale, seen.Width / Scale, seen.Height / Scale) : null;

            // Painted again only once what is shown leaves what was painted round it last time.
            if (value is not { } shown || _painted is not { } painted
                || !painted.Contains(new LayoutRect(shown.X / Scale, shown.Y / Scale, shown.Width / Scale, shown.Height / Scale)))
                InvalidateVisual();
        }
    }

    private Rect? _onScreen;

    /// <summary>What was painted round what was shown, the last time anything was painted — in the content's own units.</summary>
    private LayoutRect? _painted;

    /// <summary>Anything the content draws over the shared picture (e.g. a strike-through on a symbol that won't encode). Drawn after the ink and wash, before the caret.</summary>
    protected virtual void PaintOver(DrawingContext dc) { }

    /// <summary>Draws the content as it would read after the drop, with the carried part in the accent colour — once it's merged in (braces, spacing and all) nothing else would distinguish it.</summary>
    private void PaintPreview(DrawingContext dc, Laid preview, LayoutVector by)
    {
        WpfPainter.Paint(dc, preview.Root, Palette.Text.Brush(), by: by);

        // Over the top rather than instead of: painting all of it and then the carried part again is what
        // keeps this to two calls, and the second colour is the one that shows.
        foreach (var piece in _engine.PiecesCarried())
            WpfPainter.PaintOne(dc, piece, Palette.Accent.Brush(), by);
    }

    /// <summary>The caret itself. One place, so content that is empty draws the same one as any other.</summary>
    private void DrawCaret(DrawingContext dc, double x, double y, double height) =>
        dc.DrawLine(CaretPen, new Point(x, y), new Point(x, y + Math.Max(height, 1)));

    /// <summary>What the caret is drawn with, in the accent.</summary>
    private Pen CaretPen => _caretPen ??= Frozen(new Pen(Palette.Accent.Brush(), 1.4));
    private Pen? _caretPen;

    /// <summary>A point in the element's own pixels, read as a point in the content's — on the page, where the view stands over part of it.</summary>
    protected LayoutPoint Unscaled(Point point)
    {
        var by = ViewShift;
        return Math.Abs(Scale - 1.0) < 0.001
            ? new LayoutPoint(point.X - by.X, point.Y - by.Y)
            : new LayoutPoint((point.X / Scale) - by.X, (point.Y / Scale) - by.Y);
    }
}
