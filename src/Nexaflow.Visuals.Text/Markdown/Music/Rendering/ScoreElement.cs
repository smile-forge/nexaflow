using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The drawing surface for one score. It lays out in <see cref="MeasureOverride"/>, paints in
/// <see cref="OnRender"/>, and owns mouse selection: click a note to select it, click a measure's background to
/// select the bar, drag to select a run.
///
/// The gesture is deliberately split into <see cref="BeginPointerSelect"/> / <see cref="ExtendPointerSelect"/> /
/// <see cref="EndPointerSelect"/> rather than being driven purely from this element's own mouse events, because
/// a score is usually hosted inside a <c>RichTextBox</c> (the markdown editor), where an embedded element does
/// <em>not</em> reliably receive mouse input — the text container attributes the click to itself, to the
/// FlowDocument, or even to a neighbouring paragraph. The host hit-tests geometrically and drives these three
/// methods; the preview handlers below only matter when the element is hosted directly.
/// </summary>
public sealed class ScoreElement : FrameworkElement, IInteractiveBlock
{
    private readonly Score _score;
    private readonly Brush _ink;
    private readonly Brush _selFill;

    private double _ppd = 1.0;
    private double _noteW = 1.18 * S;
    private ScoreLayout? _layout;

    private int? _selA, _selB;
    private int _anchor;
    private bool _dragging;
    private Point _down;

    /// <summary>Raised whenever the selection changes (click, drag, or clear).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The selected event range as inclusive reading-order indices, or null when nothing is selected.</summary>
    public (int Start, int End)? SelectedRange =>
        _selA is int a && _selB is int b ? (Math.Min(a, b), Math.Max(a, b)) : null;

    /// <summary>Right-edge x of each engraved system after justification — for layout tests and diagnostics.
    /// Every system but a short final one shares one value: the chosen line width.</summary>
    public IReadOnlyList<double> SystemRightEdges
    {
        get
        {
            var r = new List<double>();
            if (_layout is not null)
                foreach (var s in _layout.Systems)
                    r.Add(s.RightX);
            return r;
        }
    }

    public ScoreElement(Score score, MarkdownPalette palette)
    {
        _score = score;
        _ink = palette.Text;
        _selFill = SelectionBrush(palette);
        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;

        // Never take keyboard focus. A score is hosted inside a RichTextBox's FlowDocument, and an embedded
        // element that can be focused ends up as the focus target the window restores to on re-activation —
        // at which point the RichTextBox reconciles its caret against a text-tree node that holds no text, and
        // faults deep inside the splay tree. Nothing here needs focus anyway: the host drives the whole
        // selection gesture through IInteractiveBlock, and the element captures the mouse for a standalone drag.
        Focusable = false;
    }

    /// <summary>A translucent wash from the theme accent (falling back to the highlight token) — never a literal.</summary>
    private static Brush SelectionBrush(MarkdownPalette palette)
    {
        if (palette.Accent is not SolidColorBrush scb) return palette.Marked;
        var c = scb.Color;
        var b = new SolidColorBrush(Color.FromArgb(0x3A, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_ppd <= 0) _ppd = 1.0;

        double avail = availableSize.Width;
        if (double.IsInfinity(avail) || double.IsNaN(avail) || avail <= 0) avail = 900;

        var engine = new ScoreLayoutEngine(_score, S, _ppd);
        _noteW = engine.NoteheadWidth;
        _layout = engine.Build(avail);
        return new Size(Math.Ceiling(_layout.Width), Math.Ceiling(_layout.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layout = _layout ??= new ScoreLayoutEngine(_score, S, _ppd).Build(680);

        // A transparent fill makes the whole element hit-testable — the gaps between glyphs included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        DrawSelection(dc, layout);
        new ScorePainter(_score, layout, _ink, _ppd).Paint(dc);
    }

    // ── Selection wash ──────────────────────────────────────────────────────

    // How far a single note's wash reaches when nothing constrains it. It is then capped at the midpoint to the
    // neighbouring note, so a selection never spills more than halfway toward a note that isn't selected.
    private const double SelCellHalf = 1.2 * S;
    private const double SelVPad = 0.6 * S;

    private void DrawSelection(DrawingContext dc, ScoreLayout layout)
    {
        if (SelectedRange is not (int a, int b)) return;
        bool Sel(PlacedEvent pe) => pe.Index >= a && pe.Index <= b;

        foreach (var sys in layout.Systems)
            foreach (var ml in sys.Measures)
            {
                var evs = ml.Events;
                if (evs.Count == 0) continue;

                bool any = false, all = true;
                foreach (var pe in evs) { bool s = Sel(pe); any |= s; all &= s; }
                if (!any) continue;

                if (all)
                {
                    var (t, bo) = VerticalSpan(sys, evs, 0, evs.Count);
                    Wash(dc, ml.StartX, ml.EndX, t, bo);
                    continue;
                }

                for (int i = 0; i < evs.Count;)
                {
                    if (!Sel(evs[i])) { i++; continue; }
                    int j = i;
                    while (j < evs.Count && Sel(evs[j])) j++;
                    var (t, bo) = VerticalSpan(sys, evs, i, j);
                    Wash(dc, CellLeft(ml, evs, i), CellRight(ml, evs, j - 1), t, bo);
                    i = j;
                }
            }
    }

    private double CenterX(PlacedEvent pe) => pe.HeadX + _noteW / 2;

    private double CellLeft(MeasureLayout ml, List<PlacedEvent> evs, int i)
    {
        double c = CenterX(evs[i]);
        double left = c - SelCellHalf;
        return i > 0 ? Math.Max(left, (c + CenterX(evs[i - 1])) / 2) : Math.Max(left, ml.StartX);
    }

    private double CellRight(MeasureLayout ml, List<PlacedEvent> evs, int i)
    {
        double c = CenterX(evs[i]);
        double right = c + SelCellHalf;
        return i < evs.Count - 1 ? Math.Min(right, (c + CenterX(evs[i + 1])) / 2) : Math.Min(right, ml.EndX);
    }

    /// <summary>The wash covers the staff plus every head in the run, so it reaches out to notes on ledger
    /// lines above or below rather than clipping them.</summary>
    private static (double top, double bot) VerticalSpan(SystemLayout sys, List<PlacedEvent> evs, int from, int to)
    {
        double top = sys.TopLineY, bot = sys.BottomLineY;
        for (int k = from; k < to; k++)
        {
            var (lo, hi) = HeadSpan(evs[k], sys);
            top = Math.Min(top, hi);
            bot = Math.Max(bot, lo);
        }
        return (top, bot);
    }

    /// <summary>The (lowest y, highest y) an event's heads occupy — note that "lo" is the larger y.</summary>
    private static (double loY, double hiY) HeadSpan(PlacedEvent pe, SystemLayout sys)
    {
        double mid = sys.BottomLineY - 2 * S;
        switch (pe.Ev)
        {
            case Note n:
                double y = sys.BottomLineY - sys.Geom.HalfSpacesAbove(n.Pitch) * (S / 2);
                return (y, y);
            case Chord c when c.Notes.Count > 0:
                double lo = double.MinValue, hi = double.MaxValue;
                foreach (var cn in c.Notes)
                {
                    double cy = sys.BottomLineY - sys.Geom.HalfSpacesAbove(cn.Pitch) * (S / 2);
                    lo = Math.Max(lo, cy);
                    hi = Math.Min(hi, cy);
                }
                return (lo, hi);
            default:
                return (mid, mid);
        }
    }

    private void Wash(DrawingContext dc, double left, double right, double top, double bot) =>
        dc.DrawRoundedRectangle(_selFill, null,
            new Rect(left, top - SelVPad, Math.Max(2, right - left), bot - top + 2 * SelVPad), 3, 3);

    // ── Pointer gestures ────────────────────────────────────────────────────

    /// <summary>Begins a click/drag: on a note head it selects that note, in a measure's background it selects
    /// the measure. Deliberately does not capture the mouse — a RichTextBox host drives the whole gesture.</summary>
    public void BeginPointerSelect(Point p)
    {
        _down = p;
        _dragging = true;

        var hit = HitTest(p);
        if (hit.measure is null || hit.measure.Events.Count == 0) { ClearSelection(); _dragging = false; return; }

        var ev = hit.ev ?? hit.measure.Events[0];
        _anchor = ev.Index;
        if (hit.sys is not null && IsOnHead(p, ev, hit.sys))
        {
            _selA = _selB = ev.Index;
        }
        else
        {
            _selA = hit.measure.Events[0].Index;
            _selB = hit.measure.Events[^1].Index;
        }
        InteractiveSelection.Own(this);
        InvalidateVisual();
    }

    /// <summary>Continues the drag: selects from the anchor to the event under the pointer.</summary>
    public void ExtendPointerSelect(Point p)
    {
        if (!_dragging) return;
        if ((p - _down).Length < 4) return;          // below the drag threshold — this is still a click
        ExtendSelectionTo(p);
    }

    /// <summary>Ends the gesture and raises <see cref="SelectionChanged"/>.</summary>
    public void EndPointerSelect()
    {
        if (!_dragging) return;
        _dragging = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;               // a double-click belongs to the host (it edits the source)
        BeginPointerSelect(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured) return;
        ExtendPointerSelect(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured) return;
        ReleaseMouseCapture();
        EndPointerSelect();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => _dragging = false;

    /// <summary>Selects the whole measure under <paramref name="p"/>. Public so hosts and tests can drive
    /// selection without synthesising mouse input.</summary>
    public void SelectMeasureAt(Point p)
    {
        var hit = HitTest(p);
        if (hit.measure is null || hit.measure.Events.Count == 0) { ClearSelection(); return; }
        _anchor = hit.ev?.Index ?? hit.measure.Events[0].Index;
        _selA = hit.measure.Events[0].Index;
        _selB = hit.measure.Events[^1].Index;
        InteractiveSelection.Own(this);
        InvalidateVisual();
    }

    /// <summary>Extends the selection from the anchor to the event nearest <paramref name="p"/>.</summary>
    public void ExtendSelectionTo(Point p)
    {
        var hit = HitTest(p);
        if (hit.ev is null) return;
        _selA = _anchor;
        _selB = hit.ev.Index;
        InteractiveSelection.Own(this);
        InvalidateVisual();
    }

    /// <summary>Centre of the note head of the event with reading-order <paramref name="index"/>, in element
    /// coordinates — a testing aid for driving precise note clicks.</summary>
    public Point? HeadCenterOf(int index)
    {
        if (_layout is null) return null;
        foreach (var pe in _layout.Order)
            if (pe.Index == index)
                return new Point(CenterX(pe), HeadSpan(pe, pe.System).loY);
        return null;
    }

    public void ClearSelection()
    {
        InteractiveSelection.Release(this);
        if (_selA is null && _selB is null) return;
        _selA = _selB = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when <paramref name="p"/> lands on the head itself. Anywhere else in the bar — blank staff,
    /// the far end of a stem, the gap between notes — counts as measure background, so a dense passage can still
    /// be clicked as a bar.</summary>
    private bool IsOnHead(Point p, PlacedEvent pe, SystemLayout sys)
    {
        var (lo, hi) = HeadSpan(pe, sys);
        return Math.Abs(p.X - CenterX(pe)) <= 1.3 * S && p.Y >= hi - 1.6 * S && p.Y <= lo + 1.6 * S;
    }

    private (PlacedEvent? ev, MeasureLayout? measure, SystemLayout? sys) HitTest(Point p)
    {
        var layout = _layout;
        if (layout is null || layout.Systems.Count == 0) return (null, null, null);

        SystemLayout? best = null;
        double bestDist = double.MaxValue;
        foreach (var sys in layout.Systems)
        {
            double top = sys.TopLineY - AbovePad, bot = sys.BottomLineY + BelowPad;
            double d = p.Y < top ? top - p.Y : p.Y > bot ? p.Y - bot : 0;
            if (d < bestDist) { bestDist = d; best = sys; }
        }
        if (best is null) return (null, null, null);

        MeasureLayout? measure = null;
        foreach (var ml in best.Measures)
            if (p.X >= ml.StartX && p.X <= ml.EndX) { measure = ml; break; }
        if (measure is null && best.Measures.Count > 0)
            measure = p.X < best.Measures[0].StartX ? best.Measures[0] : best.Measures[^1];
        if (measure is null || measure.Events.Count == 0) return (null, measure, best);

        var ev = measure.Events[0];
        foreach (var pe in measure.Events)
        {
            if (p.X >= pe.SlotLeft && p.X <= pe.SlotRight) { ev = pe; break; }
            if (p.X > pe.SlotRight) ev = pe;
        }
        return (ev, measure, best);
    }
}
