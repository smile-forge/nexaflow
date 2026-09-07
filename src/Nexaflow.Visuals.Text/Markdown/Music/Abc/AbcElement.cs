using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// The drawing surface for one ```abc block. It engraves in <see cref="MeasureOverride"/>, paints in
/// <see cref="OnRender"/> by walking the layout tree, and owns pointer selection.
///
/// <para>
/// The gesture is split into <see cref="BeginPointerSelect"/> / <see cref="ExtendPointerSelect"/> /
/// <see cref="EndPointerSelect"/> rather than being driven from this element's own mouse events, because a
/// score is usually hosted inside a <c>RichTextBox</c>, where an embedded element does <em>not</em>
/// reliably receive mouse input — the text container attributes the click to itself, to the FlowDocument,
/// or even to a neighbouring paragraph. The host hit-tests geometrically and drives these three methods.
/// </para>
/// <para>
/// What is selected is a set of <em>parts</em>, not a range of note indices. Dragging across a beamed run
/// selects the run, because <see cref="ContentSelection"/> grows a set of nodes to the largest whole
/// constructs it covers and the beam is a construct the source names. Nothing here decides that; it comes
/// out of the shared queries as soon as the layout says what each piece was drawn from.
/// </para>
/// </summary>
public sealed partial class AbcElement : FrameworkElement
{
    private string _abc;
    private readonly Brush _ink;
    private readonly Brush _wash;

    private AbcLayout? _layout;
    private double _ppd = 1.0;
    private double _engravedFor;

    private ILayoutNode? _anchor;

    /// <summary>
    /// The far end of the selection — where the last step left it, and where the next one starts from.
    ///
    /// <para>
    /// Kept beside <see cref="_anchor"/> so a keyboard selection grows the way a drag does: one end
    /// pinned, the other walking. Without it every keystroke would re-anchor and the selection could only
    /// ever be one piece long.
    /// </para>
    /// </summary>
    private ILayoutNode? _reach;
    private IReadOnlyList<(int Start, int Length)> _selection = [];

    /// <summary>
    /// The pieces the selection is <em>of</em>, where it came from pointing at them. Null for one handed
    /// in as a stretch of source, which is all a search hit or a host has to give.
    /// </summary>
    private ContentSelection? _chosen;
    private bool _dragging;

    /// <summary>Raised whenever what is selected inside the tune changes.</summary>
    public event EventHandler? SelectionChanged;

    public AbcElement(string abc, MarkdownPalette palette)
    {
        _abc = abc;
        _ink = palette.Text;
        _wash = Wash(palette);

        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;

        // Never take keyboard focus. A score is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text,
        // and faults deep inside the splay tree.
        Focusable = false;
    }

    /// <summary>The ABC this stands for.</summary>
    public string Source => _abc;

    /// <summary>
    /// Where <see cref="Source"/> sits inside the markdown block that produced this one — the fence body's
    /// offset, which is what an edit has to splice against. Set by the handler, which is the only thing
    /// holding both strings.
    /// </summary>
    public int SourceStart { get; init; }

    /// <summary>
    /// How large the notation is drawn, as a multiple of its natural size. 1 is the size the engraver was
    /// designed at; 0.8 is a page that fits more music and asks more of the reader's eyes.
    ///
    /// <para>
    /// It is a <em>render</em> scale rather than a bitmap one, which is the whole point: the tune is
    /// engraved into the room a smaller notation leaves — <c>available / zoom</c> — so zooming out fits
    /// more bars on a line rather than shrinking a picture of the same line breaks. That is what a reader
    /// dragging a zoom control expects, and it is why the layout has to be told rather than the painter.
    /// </para>
    /// </summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>The zoom, clamped to what can actually be drawn.</summary>
    private double Scale => Math.Clamp(Zoom, 0.2, 4.0);

    /// <summary>
    /// How much air to leave between things, or null for what the engraver normally uses — see
    /// <see cref="ScoreSpacing"/>. Set only to compare two engravings without the comparison being about
    /// this.
    /// </summary>
    public ScoreSpacing? Spacing { get; init; }

    /// <summary>What is selected inside it, in the tune's own offsets.</summary>
    public IReadOnlyList<(int Start, int Length)> Selection => _selection;

    /// <summary>Whatever could not be read or drawn.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _layout?.Diagnostics ?? [];

    /// <summary>The engraved tune, for the shared queries — or null before it has been measured.</summary>
    internal AbcLayout? Layout => _layout;

    /// <summary>A translucent wash from the theme accent, falling back to the highlight token — never a literal.</summary>
    private static Brush Wash(MarkdownPalette palette)
    {
        if (palette.Accent is not SolidColorBrush accent) return palette.Marked;

        var brush = new SolidColorBrush(Color.FromArgb(0x3A, accent.Color.R, accent.Color.G, accent.Color.B));
        brush.Freeze();
        return brush;
    }

    // ── Engraving and painting ──────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_ppd <= 0) _ppd = 1.0;

        var available = availableSize.Width;
        if (double.IsInfinity(available) || double.IsNaN(available) || available <= 0) available = 900;

        // Engraved into the room a smaller notation leaves, then drawn at that size — so zooming out
        // fits more bars per line instead of shrinking a picture of the same ones. The width itself is
        // whatever it was given: how wide a page is inside its margins is the block's business, not the
        // engraver's.
        var room = available / Scale;

        if (_layout is null || Math.Abs(_engravedFor - room) > 0.5)
        {
            _layout = AbcLayout.Build(_abc, room, _ink, _ppd, spacing: Spacing);
            _engravedFor = room;
        }

        return new Size(Math.Ceiling(_layout.Size.Width * Scale), Math.Ceiling(_layout.Size.Height * Scale));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layout = _layout ??= AbcLayout.Build(_abc, 680, _ink, _ppd);

        // A transparent fill makes the whole element hit-testable — the gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        // Everything below is in the layout's own coordinates. The scale is pushed once, here, so nothing
        // that reads the tree has to know about it — and a pointer coming the other way is divided by it.
        var scaled = Math.Abs(Scale - 1.0) > 0.001;
        if (scaled) dc.PushTransform(new ScaleTransform(Scale, Scale));

        PaintSelection(dc, layout);
        layout.Paint(dc, _ink);
        PaintDiagnostics(dc, layout);
        PaintCaret(dc, layout);

        if (scaled) dc.Pop();
    }

    /// <summary>A point on the element, in the coordinates the layout was built in.</summary>
    private Point Unscaled(Point at) => new(at.X / Scale, at.Y / Scale);

    private void PaintSelection(DrawingContext dc, AbcLayout layout)
    {
        if (_selection.Count == 0) return;

        foreach (var node in Washed(layout))
        {
            // Nothing with no area, because `Rect.Inflate` throws on an empty one — and an exception out
            // of OnRender does not lose a wash, it stops WPF drawing the element ever again. A drag that
            // happened to cover a piece that drew nothing took the whole score off the page.
            var bounds = node.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

            bounds.Inflate(0.25 * ScoreWash, 0.25 * ScoreWash);
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
    /// A selection set from outside — a search hit, the host handing one in — has no nodes behind it, and
    /// there the stretch of source is genuinely all there is to go on.
    /// </para>
    /// </summary>
    private IEnumerable<ILayoutNode> Washed(AbcLayout layout) =>
        _chosen is { IsEmpty: false } chosen
            ? chosen.Nodes.SelectMany(node => node.Ink())
            : layout.Root.Ink().Where(node => node.Sits() is { Length: > 0 } at
                  && _selection.Any(range => at.Start >= range.Start && at.End <= range.Start + range.Length));

    private const double ScoreWash = 6.0;

    private void PaintDiagnostics(DrawingContext dc, AbcLayout layout)
    {
        foreach (var trouble in layout.Diagnostics)
            foreach (var node in layout.Root.Ink().Where(trouble.Covers))
                dc.DrawGeometry(null, WavePen, Squiggle.Under(node.Bounds));
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

    // ── Pointer ─────────────────────────────────────────────────────────────

    public void BeginPointerSelect(Point pointInElement)
    {
        if (_layout is null) return;

        InteractiveSelection.Own(this);
        var at = Unscaled(pointInElement);

        _anchor = _layout.Root.NodeAt(at);
        _dragging = true;
        Select(_anchor, _anchor);

        // A press puts the caret down as well as picking something up. Without this a tune drew no caret at
        // all — `_hasCaret` was only ever set by an edit, so a reader had to change something before there
        // was any sign of where a change would go. The host has already handed this block the keys by the
        // time it gets here, so the caret is not a lie about where they are going.
        _caret = _layout.Root.PlaceAt(at);
        _hasCaret = true;
        Blinking(true);
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging || _layout is null) return;
        Select(_anchor, _layout.Root.NodeAt(Unscaled(pointInElement)));
    }

    public void EndPointerSelect() => _dragging = false;

    public void ClearSelection()
    {
        _dragging = false;
        _anchor = null;
        _reach = null;
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
    private void Select(ILayoutNode? from, ILayoutNode? to)
    {
        if (_layout is null || from is null || to is null) { ClearSelection(); return; }

        var chosen = ContentSelection.Between(_layout.Root, from, to);
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
    /// <para>
    /// With nothing selected yet it starts from the caret's own piece, so a reader who has just clicked
    /// into a tune can select from there without having to drag first.
    /// </para>
    /// </summary>
    public bool Extend(bool vertical, bool forward)
    {
        if (_layout is null) return false;

        var from = _reach ?? _anchor ?? _layout.Root.NodeAt(new Point(0, 0));
        if (from?.Selectable() is not { } at) return false;

        if (at.Step(vertical, forward) is not { } next) return false;

        Select(_anchor ?? at, next);

        // The caret goes where the eye went. Leaving it behind is what lets a plain arrow after a Shift
        // arrow jump back to somewhere the reader stopped looking three keystrokes ago.
        var sits = next.Sits();
        if (sits.Length > 0) _caret = CaretPlace.At(Math.Clamp(sits.Start + sits.Length, 0, _abc.Length));

        return true;
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
