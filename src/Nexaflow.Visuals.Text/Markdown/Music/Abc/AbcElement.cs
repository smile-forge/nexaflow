using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

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
public sealed class AbcElement : FrameworkElement, IInteractiveBlock
{
    private readonly string _abc;
    private readonly Brush _ink;
    private readonly Brush _wash;

    private AbcLayout? _layout;
    private double _ppd = 1.0;
    private double _engravedFor;

    private ILayoutNode? _anchor;
    private IReadOnlyList<(int Start, int Length)> _selection = [];
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

        if (_layout is null || Math.Abs(_engravedFor - available) > 0.5)
        {
            _layout = AbcLayout.Build(_abc, available, _ink, _ppd);
            _engravedFor = available;
        }

        return new Size(Math.Ceiling(_layout.Size.Width), Math.Ceiling(_layout.Size.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layout = _layout ??= AbcLayout.Build(_abc, 680, _ink, _ppd);

        // A transparent fill makes the whole element hit-testable — the gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        PaintSelection(dc, layout);
        layout.Paint(dc, _ink);
        PaintDiagnostics(dc, layout);
    }

    private void PaintSelection(DrawingContext dc, AbcLayout layout)
    {
        if (_selection.Count == 0) return;

        foreach (var node in layout.Root.Ink())
        {
            var at = node.Sits();
            if (at.Length <= 0) continue;
            if (!_selection.Any(range => at.Start >= range.Start && at.Start + at.Length <= range.Start + range.Length))
                continue;

            var bounds = node.Bounds;
            bounds.Inflate(0.25 * ScoreWash, 0.25 * ScoreWash);
            dc.DrawRectangle(_wash, null, bounds);
        }
    }

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
        _anchor = _layout.Root.NodeAt(pointInElement);
        _dragging = true;
        Select(_anchor, _anchor);
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging || _layout is null) return;
        Select(_anchor, _layout.Root.NodeAt(pointInElement));
    }

    public void EndPointerSelect() => _dragging = false;

    public void ClearSelection()
    {
        _dragging = false;
        _anchor = null;
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

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
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
