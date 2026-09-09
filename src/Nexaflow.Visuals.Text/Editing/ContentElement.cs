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
/// The surface every piece of embedded, rendered, editable content is drawn on: it lays the content out,
/// paints it, and owns the pointer, the selection and the caret.
///
/// <para>
/// <strong>There is one of these, whatever is in it.</strong> A tune, a formula and a barcode were three
/// elements doing the same work three times — the same wash, the same wave, the same blinking bar, the
/// same drag grown out to whole constructs — because each had its own idea of what a laid-out thing was.
/// They no longer do: a builder makes a <see cref="Laid"/> and everything after that is this. Something
/// nobody has thought of yet costs a builder and an <see cref="IContent"/>, and nothing here changes.
/// </para>
/// <para>
/// The gesture is split into <see cref="BeginPointerSelect"/> / <see cref="ExtendPointerSelect"/> /
/// <see cref="EndPointerSelect"/> rather than being driven from this element's own mouse events, because
/// content is usually hosted inside a <c>RichTextBox</c>, where an embedded element does <em>not</em>
/// reliably receive mouse input — the text container attributes the click to itself, to the FlowDocument,
/// or even to a neighbouring paragraph. The host hit-tests geometrically and drives the three methods.
/// </para>
/// </summary>
public sealed class ContentElement : FrameworkElement, IEditableBlock
{
    private readonly IContent _content;
    private readonly Brush _ink;
    private readonly Brush _wash;

    private Laid? _laid;
    private double _ppd = 1.0;
    private double _laidFor;

    private Piece _anchor;

    /// <summary>
    /// The far end of the selection — where the last step left it, and where the next one starts from.
    ///
    /// <para>
    /// Kept beside <see cref="_anchor"/> so a keyboard selection grows the way a drag does: one end
    /// pinned, the other walking. Without it every keystroke would re-anchor and the selection could only
    /// ever be one piece long.
    /// </para>
    /// </summary>
    private Piece _reach;

    private IReadOnlyList<(int Start, int Length)> _selection = [];

    /// <summary>
    /// The pieces the selection is <em>of</em>, where it came from pointing at them. Null for one handed
    /// in as a stretch of source, which is all a search hit or a host has to give.
    /// </summary>
    private ContentSelection? _chosen;
    private bool _dragging;

    private CaretPlace _caret;
    private bool _hasCaret;
    private bool _caretOn;
    private DispatcherTimer? _blink;

    /// <summary>Raised whenever what is selected inside the content changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the reader's own editing changed the source.</summary>
    public event EventHandler? SourceChanged;

    /// <summary>Raised when a caret movement ran off an end — the host puts it in the prose beside.</summary>
    public event EventHandler<BlockExit>? Exited;

    public ContentElement(IContent content, MarkdownPalette palette)
    {
        _content = content;
        _ink = palette.Text;
        _wash = Wash(palette);

        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;

        // Never take keyboard focus. Content is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text,
        // and faults deep inside the splay tree.
        Focusable = false;
    }

    /// <summary>What is inside it — for the host, which sometimes has a reason to ask.</summary>
    public IContent Content => _content;

    public string Source => _content.Source;

    public int SourceStart => _content.SourceStart;

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

    private double Scale => Math.Clamp(Zoom, 0.2, 4.0);

    /// <summary>What is laid out, or nothing before it has been measured.</summary>
    public Laid? Laid => _laid;

    public Piece Root => _laid?.Root ?? default;

    public IReadOnlyList<(int Start, int Length)> Selection => _selection;

    public IReadOnlyList<Diagnostic> Diagnostics => _laid?.Trouble ?? [];

    /// <summary>
    /// Whether the caret belongs in this content at all — whether any of what it draws is the source.
    ///
    /// <para>
    /// Asked of the tree rather than declared: a piece that names a stretch of source is a piece somebody
    /// typed, and content with none of those is content nobody can type into. A barcode that prints a
    /// number it worked out is the case that needs it, and nothing about the question is barcode-shaped.
    /// </para>
    /// </summary>
    public bool AcceptsCaret =>
        _laid is { } laid && laid.Root.SelfAndDescendants().Any(piece => piece.Part is { Length: > 0 });

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token — never a literal.</summary>
    private static Brush Wash(MarkdownPalette palette)
    {
        if (palette.Accent is not SolidColorBrush accent) return palette.Marked;

        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }

    // ── Laying out and painting ─────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_ppd <= 0) _ppd = 1.0;

        var available = availableSize.Width;
        if (double.IsInfinity(available) || double.IsNaN(available) || available <= 0) available = 900;

        var room = available / Scale;

        if (_laid is null || Math.Abs(_laidFor - room) > 0.5)
        {
            _laid = _content.Lay(room, _ppd);
            _laidFor = room;
        }

        return new Size(Math.Ceiling(_laid.Size.Width * Scale), Math.Ceiling(_laid.Size.Height * Scale));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var laid = _laid ??= _content.Lay(680, _ppd);

        // A transparent fill makes the whole element hit-testable — the gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        // Everything below is in the tree's own coordinates. The scale is pushed once, here, so nothing
        // that reads the tree has to know about it — and a pointer coming the other way is divided by it.
        var scaled = Math.Abs(Scale - 1.0) > 0.001;
        if (scaled) dc.PushTransform(new ScaleTransform(Scale, Scale));

        PaintSelection(dc, laid);
        LayoutPainter.Paint(dc, laid.Root, _ink);
        PaintDiagnostics(dc, laid);
        PaintCaret(dc, laid);

        _content.PaintOver(dc, laid, _ink);

        if (scaled) dc.Pop();
    }

    /// <summary>A point on the element, in the coordinates the content was laid out in.</summary>
    private Point Unscaled(Point at) => new(at.X / Scale, at.Y / Scale);

    private void PaintSelection(DrawingContext dc, Laid laid)
    {
        if (_selection.Count == 0) return;

        foreach (var piece in Washed(laid))
        {
            // Nothing with no area, because `Rect.Inflate` throws on an empty one — and an exception out
            // of OnRender does not lose a wash, it stops WPF drawing the element ever again. A drag that
            // happened to cover a piece that drew nothing took the whole score off the page.
            var bounds = piece.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

            bounds.Inflate(0.25 * WashPad, 0.25 * WashPad);
            dc.DrawRectangle(_wash, null, bounds);
        }
    }

    /// <summary>
    /// What to draw the wash over: the ink of the pieces that were actually chosen.
    ///
    /// <para>
    /// Asking instead which ink <em>lies inside the chosen stretch of source</em> gives a different and
    /// wrong answer, because a container names the whole run it holds. A beamed pair covers the same
    /// characters as the two notes in it, so a drag over the two washed the group's rectangle as well —
    /// a box reaching from the beam to whatever else the group had come to hold.
    /// </para>
    /// <para>
    /// A selection set from outside — a search hit, the host handing one in — has no pieces behind it, and
    /// there the stretch of source is genuinely all there is to go on.
    /// </para>
    /// </summary>
    private IEnumerable<Piece> Washed(Laid laid) =>
        _chosen is { IsEmpty: false } chosen
            ? chosen.Pieces.SelectMany(piece => piece.Ink())
            : laid.Root.Ink().Where(piece => piece.Sits() is { Length: > 0 } at
                  && _selection.Any(range => at.Start >= range.Start && at.End <= range.Start + range.Length));

    private const double WashPad = 6.0;

    private void PaintDiagnostics(DrawingContext dc, Laid laid)
    {
        foreach (var trouble in laid.Trouble)
            foreach (var piece in laid.Root.Ink().Where(trouble.Covers))
                dc.DrawGeometry(null, WavePen, Squiggle.Under(piece.Bounds));
    }

    private static readonly Pen WavePen = Frozen();

    private static Pen Frozen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xC0, 0xD0, 0x60, 0x60));
        brush.Freeze();
        var pen = new Pen(brush, 1.0);
        pen.Freeze();
        return pen;
    }

    /// <summary>The caret bar, taking its height from whatever ink it abuts.</summary>
    private void PaintCaret(DrawingContext dc, Laid laid)
    {
        if (!_hasCaret || !_caretOn) return;

        var bar = laid.Root.CaretRect(_caret);
        dc.DrawRectangle(_ink, null, new Rect(bar.X, bar.Y, Math.Max(bar.Width, 1.2), Math.Max(bar.Height, 4)));
    }

    // ── Pointer ─────────────────────────────────────────────────────────────

    public void BeginPointerSelect(Point pointInElement)
    {
        if (_laid is null) return;

        InteractiveSelection.Own(this);
        var at = Unscaled(pointInElement);

        _anchor = _laid.Root.PieceAt(at);
        _dragging = true;
        Select(_anchor, _anchor);

        // A press puts the caret down as well as picking something up. Without this content drew no caret
        // at all until something was changed, so a reader had to edit before there was any sign of where an
        // edit would go. The host has already handed this block the keys by the time it gets here, so the
        // caret is not a lie about where they are going.
        if (AcceptsCaret)
        {
            _caret = _laid.Root.PlaceAt(at);
            _hasCaret = true;
            Blinking(true);
        }
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging || _laid is null) return;
        Select(_anchor, _laid.Root.PieceAt(Unscaled(pointInElement)));
    }

    public void EndPointerSelect() => _dragging = false;

    public bool PointerDoubleClick(Point pointInElement)
    {
        if (_laid is null) return false;

        // The whole of whatever was pointed at, grown out to the largest construct that covers it — which
        // for content that is one word is the word, and for a tune is the note.
        var at = _laid.Root.PieceAt(Unscaled(pointInElement)).Selectable();
        if (!at.Exists) return false;

        InteractiveSelection.Own(this);
        Select(at, at);
        return true;
    }

    public void ClearSelection()
    {
        _dragging = false;
        _anchor = default;
        _reach = default;
        _chosen = null;
        if (_selection.Count == 0) return;

        _selection = [];
        InteractiveSelection.Release(this);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What a sweep from one piece to another means. The answer is the shared one: whole constructs, grown
    /// out of the ink between them, so a drag across half a beamed run comes back as the run.
    /// </summary>
    private void Select(Piece from, Piece to)
    {
        if (_laid is null || !from.Exists || !to.Exists) { ClearSelection(); return; }

        var chosen = ContentSelection.Between(_laid.Root, from, to);
        _selection = chosen.Ranges;
        _chosen = chosen;
        _anchor = from;
        _reach = to;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Grows the selection one piece along an axis — what Shift and an arrow key mean.
    ///
    /// <para>
    /// The same walk a drag takes, driven a step at a time instead of by a pointer: the anchor stays put
    /// and the far end moves, so holding an arrow sweeps a verse or a run of notes exactly as dragging
    /// along it would. False when there is nothing that way, which is the host's cue to do whatever it
    /// does with an arrow at the edge of a block.
    /// </para>
    /// </summary>
    public bool Extend(bool vertical, bool forward)
    {
        if (_laid is null) return false;

        var from = _reach.Exists ? _reach
                 : _anchor.Exists ? _anchor
                 : _laid.Root.PieceAt(new Point(0, 0));

        if (from.Selectable() is not { Exists: true } at) return false;
        if (at.Step(vertical, forward) is not { Exists: true } next) return false;

        Select(_anchor.Exists ? _anchor : at, next);

        // The caret goes where the eye went. Leaving it behind is what lets a plain arrow after a Shift
        // arrow jump back to somewhere the reader stopped looking three keystrokes ago.
        var sits = next.Sits();
        if (sits.Length > 0) _caret = CaretPlace.At(Math.Clamp(sits.Start + sits.Length, 0, Source.Length));

        return true;
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Types a character: whatever the content makes of it, and failing that the character itself, spliced
    /// in where the caret is.
    /// </summary>
    public void Type(char character)
    {
        if (_laid is null) return;

        if (_content.Type(character, Caret(), _selection) is { } edited) { Apply(edited); return; }

        Splice(_content.Typing(character, Caret()) ?? character.ToString());
    }

    public bool Backspace()
    {
        if (_laid is null) return false;
        if (_selection.Count > 0) { Cut(); return true; }

        var at = Caret();
        if (at <= 0) return false;

        Replace(at - 1, 1, "", at - 1);
        return true;
    }

    public bool Delete()
    {
        if (_laid is null) return false;
        if (_selection.Count > 0) { Cut(); return true; }

        var at = Caret();
        if (at >= Source.Length) return false;

        Replace(at, 1, "", at);
        return true;
    }

    /// <summary>Everything selected, taken out, leaving the caret where it was.</summary>
    private void Cut()
    {
        var ranges = _selection.OrderByDescending(range => range.Start).ToList();
        var source = Source;

        foreach (var (start, length) in ranges)
            source = source.Remove(start, Math.Min(length, source.Length - start));

        Apply(new Edited(source, ranges[^1].Start));
    }

    private void Splice(string text)
    {
        if (_selection.Count > 0) { Cut(); if (text.Length == 0) return; }

        var at = Caret();
        Replace(at, 0, text, at + text.Length);
    }

    private void Replace(int start, int length, string with, int caret)
    {
        var source = Source;

        start = Math.Clamp(start, 0, source.Length);
        length = Math.Clamp(length, 0, source.Length - start);

        Apply(new Edited(string.Concat(source.AsSpan(0, start), with, source.AsSpan(start + length)), caret));
    }

    /// <summary>
    /// The content as it now stands: re-read, laid out again, and handed to the document that holds it.
    ///
    /// <para>
    /// One path, always taken. An edit can put anything anywhere — a bar line that re-bars the rest of a
    /// line, a field that changes the key under every note after it — so asking whether a change was
    /// contained is not worth answering cheaply.
    /// </para>
    /// </summary>
    private void Apply(Edited edited)
    {
        _content.Source = edited.Source;
        _laid = _content.Lay(_laidFor > 0 ? _laidFor : 680, _ppd);

        _selection = edited.Select is { Length: > 0 } keep ? [keep] : [];
        _chosen = null;
        _caret = CaretPlace.At(Math.Clamp(edited.Caret, 0, Source.Length));
        _hasCaret = true;

        InvalidateMeasure();
        InvalidateVisual();

        SourceChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A key the content claims for itself — a score's Page Up for an octave.</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (_laid is null) return false;
        if (_content.Press(key, modifiers, Caret(), _selection) is not { } edited) return false;

        Apply(edited);
        return true;
    }

    FrameworkElement? IEditableBlock.BuildRibbon() => _content.Ribbon(Caret(), _selection, Apply);

    // ── The caret ───────────────────────────────────────────────────────────

    private int Caret() => Math.Clamp(_caret.Offset, 0, Source.Length);

    /// <summary>
    /// The caret one place along, or the selection one piece along when it is being extended.
    ///
    /// <para>
    /// <strong>Both live here, and deliberately.</strong> The host asks this for every left and right
    /// arrow, with <paramref name="extend"/> saying whether Shift was down; a block that also claimed the
    /// key for itself would leave two handlers writing one selection from two different ideas of what a
    /// selection is — which is the shape of every keyboard bug this app has had.
    /// </para>
    /// </summary>
    public bool MoveCaret(bool forward, bool extend)
    {
        if (_laid is null) return false;

        if (extend && Extend(vertical: false, forward)) return true;

        if (_laid.Root.Step(_caret, forward) is not { } next)
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
        if (_laid?.Root.StepVertical(Caret(), up) is not { } next) return false;

        _caret = CaretPlace.At(next);
        if (!extend) _selection = [];
        InvalidateVisual();
        return true;
    }

    public void SelectRange(int start, int length)
    {
        if (_laid is null) return;

        var from = Math.Max(0, start);
        _selection = length <= 0 ? [] : [(from, Math.Min(length, Source.Length - from))];
        _chosen = null;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes the caret from the prose beside it, at the place the reader was coming from. Content wide
    /// enough for a column to mean something takes a caret arriving from the line above under where it
    /// left, rather than at the beginning.
    /// </summary>
    public void TakeCaretArriving(CaretArrival arrival)
    {
        if (_laid is null) return;

        // Nothing drawn here is the source, so there is nowhere in it to stand. The caret is handed
        // straight on the way it was already going, and the reader arrows over the content as they would
        // over a word — rather than into it, to find that no key does anything.
        if (!AcceptsCaret)
        {
            Exited?.Invoke(this, arrival.Edge == BlockExit.Before ? BlockExit.After : BlockExit.Before);
            return;
        }

        var stops = _laid.Root.CaretStops();
        if (stops.Count == 0) { Exited?.Invoke(this, arrival.Edge); return; }

        InteractiveSelection.Own(this);

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

    /// <summary>The caret stop nearest a column, for a caret arriving from another line.</summary>
    private int Nearest(double column)
    {
        var best = 0;
        var distance = double.MaxValue;

        foreach (var piece in _laid!.Root.Ink())
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
        // Windows' own caret rate. WPF does not surface GetCaretBlinkTime, and a P/Invoke for one number
        // is not worth the trouble; this is the default every version has shipped.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        timer.Tick += (_, _) => { _caretOn = !_caretOn; InvalidateVisual(); };
        return timer;
    }

    /// <summary>Lays the content out again, because something outside it changed.</summary>
    public void Refresh()
    {
        _laid = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ── Hosted directly ─────────────────────────────────────────────────────
    //
    // These only matter when the element is not inside a RichTextBox — a read-only preview, or a test.

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        BeginPointerSelect(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) ExtendPointerSelect(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        EndPointerSelect();
        ReleaseMouseCapture();
    }
}
