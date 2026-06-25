using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>One node fed to a <see cref="SunburstControl"/>. Colour-agnostic: the caller supplies the
/// arc <see cref="Fill"/> and an optional <see cref="StatusFill"/> drawn as a small bar near the arc's
/// outer edge. Label colour is auto-chosen (black/white) for contrast against the fill.</summary>
public sealed class SunburstNode
{
    public required string Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public double Weight { get; init; } = 1;
    public Brush Fill { get; init; } = Brushes.Gray;
    public Brush? StatusFill { get; init; }
    public bool HasChildren { get; init; }
    public IReadOnlyList<SunburstNode> Children { get; init; } = [];
}

/// <summary>Right-click payload: the node id and the screen point to place a context menu.</summary>
public sealed record SunburstRightClick(string Id, Point Screen);

/// <summary>
/// A zoomable two-layer sunburst drawn via <see cref="OnRender"/>. The focused node is the centre disc,
/// its children the inner ring, grandchildren the outer ring — contiguous, bordered cells. Labels run
/// radially, centred, in a contrast colour. Navigating animates: arcs tween between layouts keyed by node
/// id, so the clicked node expands toward the centre and parent entries fade/grow into the freed space.
/// </summary>
public sealed class SunburstControl : FrameworkElement
{
    public static readonly DependencyProperty RootProperty =
        DependencyProperty.Register(nameof(Root), typeof(SunburstNode), typeof(SunburstControl),
            new FrameworkPropertyMetadata(null, OnRootChanged));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(SunburstControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public SunburstNode? Root { get => (SunburstNode?)GetValue(RootProperty); set => SetValue(RootProperty, value); }

    /// <summary>The cell border colour (defaults to white).</summary>
    public Brush? StrokeBrush { get => (Brush?)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }

    public event EventHandler? CentreClicked;
    public event EventHandler<string>? ArcClicked;
    public event EventHandler<SunburstRightClick>? ArcRightClicked;

    private const double R0 = 0.30, R1 = 0.66, R2 = 1.0;     // contiguous, wide rings (fractions of rMax)
    private const double DurationMs = 380;

    private static readonly SolidColorBrush Black = Freeze(Colors.Black);
    private static readonly SolidColorBrush White = Freeze(Color.FromRgb(0xF4, 0xF4, 0xF4));

    private sealed class Arc
    {
        public required string Id;
        public int Ring;
        public double F0, F1, RIn, ROut, Alpha = 1;
        public required Brush Fill;
        public Brush? Status;
        public string Label = string.Empty;
        public bool HasChildren;
    }

    private List<Arc> _to = [];
    private List<Arc> _from = [];
    private List<Arc> _displayed = [];
    private string? _centerId;
    private bool _animating;
    private readonly Stopwatch _clock = new();
    private Point _centre;
    private double _rMax;

    public SunburstControl()
    {
        Focusable = false;
        Unloaded += (_, _) => StopAnim();
    }

    private static void OnRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SunburstControl)d).RootChanged();

    private void RootChanged()
    {
        var root = Root;
        var layout = root is null ? [] : Layout(root);
        var newCenter = root?.Id;

        if (_to.Count == 0 || newCenter == _centerId || _displayed.Count == 0)
        {
            _to = _from = _displayed = layout;
            _centerId = newCenter;
            StopAnim();
            InvalidateVisual();
            return;
        }

        _from = _displayed;
        _to = layout;
        _centerId = newCenter;
        StartAnim();
    }

    private void StartAnim()
    {
        _animating = true;
        _clock.Restart();
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnim()
    {
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
        _clock.Reset();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (_clock.Elapsed.TotalMilliseconds >= DurationMs)
        {
            _from = _displayed = _to;
            StopAnim();
            InvalidateVisual();
        }
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private static List<Arc> Layout(SunburstNode root)
    {
        var arcs = new List<Arc>
        {
            new() { Id = root.Id, Ring = 0, F0 = 0, F1 = 1, RIn = 0, ROut = R0,
                    Fill = root.Fill, Label = root.Label, HasChildren = root.HasChildren }
        };
        LayoutRing(root.Children, 0, 1, 1, arcs);
        return arcs;
    }

    private static void LayoutRing(IReadOnlyList<SunburstNode> nodes, double f0, double f1, int ring, List<Arc> arcs)
    {
        if (nodes.Count == 0 || ring > 2) return;
        var (rIn, rOut) = ring == 1 ? (R0, R1) : (R1, R2);
        var total = nodes.Sum(n => Math.Max(n.Weight, 0.0001));
        var f = f0;
        foreach (var n in nodes)
        {
            var span = (f1 - f0) * Math.Max(n.Weight, 0.0001) / total;
            var a = f; var b = f + span; f = b;
            arcs.Add(new Arc { Id = n.Id, Ring = ring, F0 = a, F1 = b, RIn = rIn, ROut = rOut,
                Fill = n.Fill, Status = n.StatusFill, Label = n.Label, HasChildren = n.HasChildren });
            LayoutRing(n.Children, a, b, ring + 1, arcs);
        }
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // hit-test backdrop
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        _centre = new Point(ActualWidth / 2, ActualHeight / 2);
        _rMax = size / 2 - 2;

        var pen = StrokeBrush is { } sb ? new Pen(sb, 1) : null;
        pen?.Freeze();

        var frame = _animating
            ? Interpolate(_from, _to, EaseOutCubic(Math.Min(1, _clock.Elapsed.TotalMilliseconds / DurationMs)))
            : _to;
        _displayed = frame;

        foreach (var arc in frame)
        {
            if (arc.Alpha <= 0.01) continue;
            if (arc.Alpha < 0.999) dc.PushOpacity(arc.Alpha);
            DrawArc(dc, arc, pen);
            if (arc.Alpha < 0.999) dc.Pop();
        }

        if (!_animating)
            foreach (var arc in frame)
                if (arc.Ring == 0) DrawCentreLabel(dc, arc);
                else DrawRadialLabel(dc, arc);
    }

    private void DrawArc(DrawingContext dc, Arc arc, Pen? pen)
    {
        var riPx = arc.RIn * _rMax;
        var roPx = arc.ROut * _rMax;
        DrawBand(dc, riPx, roPx, arc.F0, arc.F1, arc.Fill, pen);

        if (arc is { Status: not null, Ring: > 0 })
        {
            var outer = roPx - 3.5;
            var inner = outer - 4;
            if (inner > riPx + 1)
            {
                var pad = (arc.F1 - arc.F0) * 0.2;
                DrawBand(dc, inner, outer, arc.F0 + pad, arc.F1 - pad, arc.Status!, null);
            }
        }
    }

    private void DrawBand(DrawingContext dc, double ri, double ro, double f0, double f1, Brush fill, Pen? pen)
    {
        if (f1 - f0 >= 0.9999)   // full circle: draw a disc / annulus, never split (a split leaves a seam)
        {
            if (ri <= 0.5) { dc.DrawEllipse(fill, pen, _centre, ro, ro); return; }
            var ring = new StreamGeometry();
            using (var rc = ring.Open())
            {
                CircleFigure(rc, ro, SweepDirection.Clockwise);
                CircleFigure(rc, ri, SweepDirection.Counterclockwise); // EvenOdd → hole, no radial seam
            }
            ring.Freeze();
            dc.DrawGeometry(fill, pen, ring);
            return;
        }
        if (f1 - f0 < 1e-5) return;

        var o0 = Polar(ro, f0); var o1 = Polar(ro, f1);
        var i1 = Polar(ri, f1); var i0 = Polar(ri, f0);
        var large = (f1 - f0) > 0.5;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(i0, true, true);
            ctx.LineTo(o0, true, false);
            ctx.ArcTo(o1, new Size(ro, ro), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(i1, true, false);
            if (ri > 0.01) ctx.ArcTo(i0, new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private Point Polar(double r, double frac)
    {
        var theta = -Math.PI / 2 + frac * 2 * Math.PI;
        return new Point(_centre.X + r * Math.Cos(theta), _centre.Y + r * Math.Sin(theta));
    }

    private void CircleFigure(StreamGeometryContext ctx, double r, SweepDirection dir)
    {
        var top = new Point(_centre.X, _centre.Y - r);
        var bottom = new Point(_centre.X, _centre.Y + r);
        ctx.BeginFigure(top, true, true);
        ctx.ArcTo(bottom, new Size(r, r), 0, true, dir, true, false);
        ctx.ArcTo(top, new Size(r, r), 0, true, dir, true, false);
    }

    private void DrawCentreLabel(DrawingContext dc, Arc arc)
    {
        if (string.IsNullOrWhiteSpace(arc.Label)) return;
        var ft = Text(arc.Label, 13, true, R0 * _rMax * 1.7, Contrast(arc.Fill));
        dc.DrawText(ft, new Point(_centre.X - ft.Width / 2, _centre.Y - ft.Height / 2));
    }

    private void DrawRadialLabel(DrawingContext dc, Arc arc)
    {
        if (string.IsNullOrWhiteSpace(arc.Label)) return;
        var fmid = (arc.F0 + arc.F1) / 2;
        var rmid = (arc.RIn + arc.ROut) / 2 * _rMax;
        var ringWidth = (arc.ROut - arc.RIn) * _rMax;
        var thickness = (arc.F1 - arc.F0) * 2 * Math.PI * rmid;
        if (thickness < 11 || ringWidth < 24) return;

        var ft = Text(arc.Label, 11, false, ringWidth - 12, Contrast(arc.Fill));
        var rot = -90 + fmid * 360 + (fmid > 0.5 ? 180 : 0);   // radial, flipped on the left so it reads upright
        var c = Polar(rmid, fmid);
        dc.PushTransform(new RotateTransform(rot, c.X, c.Y));
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));   // centre the ACTUAL glyphs at the centroid
        dc.Pop();
    }

    private FormattedText Text(string s, double size, bool bold, double maxWidth, Brush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, maxWidth), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    private static SolidColorBrush Contrast(Brush fill)
    {
        var c = (fill as SolidColorBrush)?.Color ?? Colors.Gray;
        var luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return luminance > 150 ? Black : White;
    }

    private static SolidColorBrush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ── Interpolation ───────────────────────────────────────────────────────────

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static List<Arc> Interpolate(List<Arc> from, List<Arc> to, double t)
    {
        var fromById = new Dictionary<string, Arc>();
        foreach (var a in from) fromById[a.Id] = a;
        var toIds = new HashSet<string>(to.Select(a => a.Id));
        var result = new List<Arc>(to.Count + from.Count);

        foreach (var ta in to)
            result.Add(fromById.TryGetValue(ta.Id, out var fa) ? Tween(fa, ta, t, 1) : Tween(Collapsed(ta), ta, t, t));

        foreach (var fa in from)
            if (!toIds.Contains(fa.Id))
                result.Add(Tween(fa, Collapsed(fa), t, 1 - t));

        return result;
    }

    private static Arc Collapsed(Arc a)
    {
        var mid = (a.F0 + a.F1) / 2;
        return new Arc { Id = a.Id, Ring = a.Ring, F0 = mid, F1 = mid, RIn = a.RIn, ROut = a.ROut,
            Fill = a.Fill, Status = a.Status, Label = a.Label, HasChildren = a.HasChildren };
    }

    private static Arc Tween(Arc a, Arc b, double t, double alpha) => new()
    {
        Id = b.Id, Ring = b.Ring,
        F0 = Lerp(a.F0, b.F0, t), F1 = Lerp(a.F1, b.F1, t),
        RIn = Lerp(a.RIn, b.RIn, t), ROut = Lerp(a.ROut, b.ROut, t),
        Fill = b.Fill, Status = b.Status, Label = b.Label, HasChildren = b.HasChildren, Alpha = alpha
    };

    // ── Hit-testing (against the at-rest layout) ─────────────────────────────────

    private string? HitTest(Point p)
    {
        var dx = p.X - _centre.X; var dy = p.Y - _centre.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var frac = (Math.Atan2(dy, dx) + Math.PI / 2) / (2 * Math.PI);
        frac -= Math.Floor(frac);

        foreach (var a in _to)
        {
            if (dist < a.RIn * _rMax || dist > a.ROut * _rMax) continue;
            if (a.Ring == 0 || (frac >= a.F0 && frac < a.F1)) return a.Id;
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = HitTest(e.GetPosition(this)) is null ? Cursors.Arrow : Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var id = HitTest(e.GetPosition(this));
        if (id is not null)
        {
            if (_to.FirstOrDefault(a => a.Ring == 0)?.Id == id) CentreClicked?.Invoke(this, EventArgs.Empty);
            else ArcClicked?.Invoke(this, id);
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var id = HitTest(e.GetPosition(this));
        if (id is not null)
            ArcRightClicked?.Invoke(this, new SunburstRightClick(id, PointToScreen(e.GetPosition(this))));
        base.OnMouseRightButtonUp(e);
    }
}
