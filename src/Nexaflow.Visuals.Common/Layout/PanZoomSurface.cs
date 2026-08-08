using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Common.Layout;

/// <summary>One box drawn on the overview minimap, in content coordinates.</summary>
public readonly record struct MiniMapItem(double X, double Y, double Width, double Height, Brush? Fill = null);

/// <summary>
/// A window onto content bigger than the space it has: drag to pan, wheel to zoom, zoom controls, and
/// an overview minimap showing where you are in the whole thing.
/// <para>
/// <see cref="PanZoomMiniMap"/> has long owned the arithmetic behind this — cursor-anchored zoom, the
/// minimap mapping and its inverse — but every surface that wanted it still hand-rolled the same
/// hundred lines of WPF around it: the transforms, the drag, the minimap redraw, the buttons. This is
/// that half, so a third surface is a few lines rather than a fourth copy.
/// </para>
/// <para>
/// Panning is always available, at any zoom. A surface where the gesture comes and goes depending on
/// whether the content currently happens to overflow is one the user cannot learn.
/// </para>
/// </summary>
public sealed class PanZoomSurface : UserControl
{
    private const double MiniW = 168, MiniH = 112, MiniPad = 8;

    private readonly Grid   _root  = new() { ClipToBounds = true };
    private readonly Canvas _clip  = new() { ClipToBounds = true };
    private readonly Canvas _mini  = new() { Width = MiniW, Height = MiniH, Background = Brushes.Transparent };
    private readonly Border _miniPanel;
    private readonly TextBlock _zoomLabel;
    private readonly ScaleTransform     _scale     = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);

    private FrameworkElement? _surface;
    private Size            _contentSize;
    private MiniMapMapping? _mapping;
    private Point  _dragFrom;
    private double _dragTx, _dragTy;
    private bool   _dragging, _miniDragging, _fitPending = true;

    public PanZoomSurface()
    {
        Focusable = false;

        // Hit-testable everywhere, not just where the content happens to be drawn. Without this the
        // wheel and a drag over empty surface fall straight through to whatever is behind.
        Background      = Brushes.Transparent;
        _root.Background = Brushes.Transparent;
        _clip.Background = Brushes.Transparent;

        _zoomLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2, 0, 6, 0),
            FontSize          = 11,
            MinWidth          = 34,
            TextAlignment     = TextAlignment.Right,
        };

        _miniPanel = new Border
        {
            Width               = MiniW,
            Height              = MiniH,
            BorderThickness     = new Thickness(1),
            CornerRadius        = new CornerRadius(4),
            Margin              = new Thickness(0, 0, MiniPad, MiniPad),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Visibility          = Visibility.Collapsed,
            Cursor              = Cursors.Hand,
            Child               = _mini,
        };

        _root.Children.Add(_clip);
        _root.Children.Add(_miniPanel);
        _root.Children.Add(BuildToolbar());
        Content = _root;

        _miniPanel.PreviewMouseLeftButtonDown += (_, e) => { _miniDragging = true; _miniPanel.CaptureMouse(); MoveToMiniPoint(e.GetPosition(_mini)); e.Handled = true; };
        _miniPanel.PreviewMouseMove           += (_, e) =>
        {
            if (!_miniDragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndMiniDrag(); return; }
            MoveToMiniPoint(e.GetPosition(_mini));
            e.Handled = true;
        };
        _miniPanel.PreviewMouseLeftButtonUp += (_, e) => { if (_miniDragging) { EndMiniDrag(); e.Handled = true; } };

        SizeChanged         += (_, _) => { if (_fitPending) FitToContent(automatic: true); else SyncMiniMap(); };
        PreviewMouseWheel   += OnWheel;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove           += OnMouseMove;
        MouseLeftButtonUp   += (s, e) => { if (_dragging) { OnMouseUp(); e.Handled = true; } };
        Cursor               = Cursors.SizeAll;

        Loaded += (_, _) => { ApplyChrome(); if (_fitPending) FitToContent(automatic: true); };
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Smallest and largest zoom. The floor matters for a big diagram: without it, "fit"
    /// on something enormous produces a scale at which nothing is legible anyway.</summary>
    public double MinScale { get; set; } = 0.1;
    public double MaxScale { get; set; } = 4.0;

    /// <summary>
    /// When false (the default) a plain wheel is left alone to scroll whatever page the surface sits
    /// on, and only Ctrl+wheel zooms. A surface that owns its whole viewport (a canvas tab rather than
    /// a block inside a document) sets this true.
    /// </summary>
    public bool ZoomOnPlainWheel { get; set; }

    /// <summary>Boxes to draw on the minimap — normally the laid-out items themselves, so the minimap
    /// shows the shape of the content and not just its bounding box. Null draws the bounds alone.</summary>
    public Func<IEnumerable<MiniMapItem>>? MiniMapItems { get; set; }

    /// <summary>Chrome colours. Left null they resolve from the active theme at load; a host with its
    /// own palette (markdown diagrams) sets them instead.</summary>
    public Brush? AccentBrush { get; set; }
    public Brush? ChromeBrush { get; set; }
    public Brush? OnChromeBrush { get; set; }

    /// <summary>Raised whenever the pan or zoom changes, so a host can remember where the reader was.</summary>
    public event Action<double, double, double>? ViewChanged;

    /// <summary>The current pan/zoom.</summary>
    public (double Scale, double X, double Y) View => (_scale.ScaleX, _translate.X, _translate.Y);

    /// <summary>Puts the view back where it was, instead of fitting.</summary>
    public void RestoreView(double scale, double x, double y)
    {
        if (scale <= 0) return;
        Apply(scale, x, y);
        _fitPending = false;
    }

    /// <summary>
    /// Puts <paramref name="content"/> on the surface. <paramref name="contentSize"/> is its size in
    /// content units — the extent pan, zoom and the minimap are all reckoned against.
    /// <para>
    /// <paramref name="preserveView"/> keeps the current pan and zoom rather than fitting the new
    /// content. Set it whenever the content changed <i>because of something the reader did</i>:
    /// re-centring the view under someone who just clicked a node throws away where they were, and
    /// they were probably looking at it.
    /// </para>
    /// </summary>
    public void SetSurfaceContent(FrameworkElement content, Size contentSize, bool preserveView = false)
    {
        _clip.Children.Clear();
        _surface     = content;
        _contentSize = contentSize;

        content.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
        _clip.Children.Add(content);

        if (preserveView && _scale.ScaleX > 0) { SyncMiniMap(); return; }

        _fitPending = true;
        if (IsLoaded) FitToContent(automatic: true);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    /// <summary>Scales the content to sit wholly inside the viewport, centred. Never scales up past
    /// 1:1 — a four-node diagram blown up to fill the panel looks broken, not helpful.</summary>
    public void FitToContent() => FitToContent(automatic: false);

    /// <summary>
    /// <paramref name="automatic"/> marks a fit the surface decided on for itself — the one on first
    /// layout, or after a resize — rather than one the reader asked for. Those are not reported as a
    /// view change, so a host remembering where the reader was does not mistake the default for a
    /// choice and then "restore" it instead of fitting the next thing it is shown.
    /// </summary>
    private void FitToContent(bool automatic)
    {
        if (_surface is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (_contentSize.Width <= 0 || _contentSize.Height <= 0) return;

        var (scale, x, y) = ViewportFit.FitScaled(_contentSize.Width, _contentSize.Height,
                                                  ActualWidth, ActualHeight, MinScale, 1.0);
        if (scale <= 0) return;

        Apply(scale, x, y, report: !automatic);
        _fitPending = false;   // …but only after a real fit, so an unmeasured first pass retries
    }

    /// <summary>Back to 1:1, top-centred — the size the content was laid out at.</summary>
    public void ActualSize()
    {
        if (_surface is null) return;
        Apply(1, (ActualWidth - _contentSize.Width) / 2, 8);
        _fitPending = false;
    }

    /// <summary>Zooms about the centre of the viewport (what the +/− buttons do).</summary>
    public void ZoomBy(double factor) => ZoomAt(factor, new Point(ActualWidth / 2, ActualHeight / 2));

    private void ZoomAt(double factor, Point anchor)
    {
        var (scale, x, y) = PanZoomMiniMap.ZoomAt(_scale.ScaleX, _translate.X, _translate.Y,
                                                  anchor.X, anchor.Y, factor, MinScale, MaxScale);
        Apply(scale, x, y);
        _fitPending = false;
    }

    private void Apply(double scale, double tx, double ty, bool report = true)
    {
        _scale.ScaleX = _scale.ScaleY = scale;
        _translate.X  = tx;
        _translate.Y  = ty;
        _zoomLabel.Text = $"{scale * 100:0}%";
        SyncMiniMap();
        if (report) ViewChanged?.Invoke(scale, tx, ty);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ZoomOnPlainWheel && (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(this));
        e.Handled = true;
    }

    // ── Pan ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A pointer press at a point in this element's coordinates, for a host that must dispatch the
    /// gesture itself because content embedded in a text container never receives its own mouse
    /// events. Routes to whatever is actually under the point — a zoom chip, the minimap, or a pan —
    /// so the chrome works on one click rather than needing a second to get past the host.
    /// </summary>
    public void BeginPointer(Point point)
    {
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(this, point)?.VisualHit;
        for (DependencyObject? d = hit; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is Button b && _chips.TryGetValue(b, out var act)) { _gesture = Gesture.None; act(); return; }
            if (ReferenceEquals(d, _miniPanel))
            {
                _gesture      = Gesture.MiniMap;
                _miniDragging = true;
                MoveToMiniPoint(TranslatePoint(point, _mini));
                return;
            }
            if (ReferenceEquals(d, this)) break;
        }

        _gesture = Gesture.Pan;
        BeginPan(point);
    }

    public void ExtendPointer(Point point)
    {
        switch (_gesture)
        {
            case Gesture.Pan:     ExtendPan(point); break;
            case Gesture.MiniMap: MoveToMiniPoint(TranslatePoint(point, _mini)); break;
        }
    }

    public void EndPointer()
    {
        if (_gesture == Gesture.MiniMap) EndMiniDrag();
        EndPan();
        _gesture = Gesture.None;
    }

    private enum Gesture { None, Pan, MiniMap }
    private Gesture _gesture;

    private void BeginPan(Point point)
    {
        _dragging = true;
        _dragFrom = point;
        _dragTx   = _translate.X;
        _dragTy   = _translate.Y;
    }

    private void ExtendPan(Point point)
    {
        if (!_dragging) return;
        Apply(_scale.ScaleX, _dragTx + (point.X - _dragFrom.X), _dragTy + (point.Y - _dragFrom.Y));
        _fitPending = false;
    }

    private void EndPan() => _dragging = false;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;            // something on the content already claimed this click
        BeginPan(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { OnMouseUp(); return; }
        ExtendPan(e.GetPosition(this));
    }

    private void OnMouseUp()
    {
        EndPan();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    // ── Minimap ───────────────────────────────────────────────────────────────

    private void SyncMiniMap()
    {
        var bounds = new CanvasBounds(0, 0, _contentSize.Width, _contentSize.Height);
        _mapping = PanZoomMiniMap.Compute(bounds, _scale.ScaleX, _translate.X, _translate.Y,
                                          ActualWidth, ActualHeight, MiniW, MiniH);

        if (_mapping is not { } m) { _miniPanel.Visibility = Visibility.Collapsed; return; }
        _miniPanel.Visibility = Visibility.Visible;
        _mini.Children.Clear();

        var accent = AccentBrush ?? Brushes.SteelBlue;

        // The items themselves, so the minimap shows the shape of the content rather than one blank
        // rectangle you cannot navigate by.
        var items = MiniMapItems?.Invoke()
                 ?? [new MiniMapItem(bounds.MinX, bounds.MinY, bounds.Width, bounds.Height)];
        foreach (var item in items)
        {
            var (x, y, w, h) = PanZoomMiniMap.Box(m, item.X, item.Y, item.Width, item.Height, minSize: 1.5);
            _mini.Children.Add(Box(x, y, w, h, item.Fill ?? Fade(accent, 0xAA), null));
        }

        var (vx, vy, vw, vh) = PanZoomMiniMap.ViewportBox(m, m.ViewLeft, m.ViewTop);
        _mini.Children.Add(Box(vx, vy, vw, vh, Fade(OnChromeBrush ?? Brushes.White, 0x22),
                               OnChromeBrush ?? Brushes.White));
    }

    private static Rectangle Box(double x, double y, double w, double h, Brush fill, Brush? stroke)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = stroke is null ? 0 : 1.4 };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private void MoveToMiniPoint(Point p)
    {
        if (_mapping is not { } m) return;
        var (x, y, _, _) = PanZoomMiniMap.TranslateForPoint(m, p.X, p.Y, _scale.ScaleX, ActualWidth, ActualHeight);
        Apply(_scale.ScaleX, x, y);
        _fitPending = false;
    }

    private void EndMiniDrag()
    {
        _miniDragging = false;
        if (_miniPanel.IsMouseCaptured) _miniPanel.ReleaseMouseCapture();
        SyncMiniMap();
    }

    // ── Chrome ────────────────────────────────────────────────────────────────

    private readonly Dictionary<Button, Action> _chips = [];

    private UIElement BuildToolbar()
    {
        var bar = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin              = new Thickness(0, 6, 6, 0),
        };

        bar.Children.Add(_zoomLabel);
        bar.Children.Add(Chip("−", "Zoom out",                  () => ZoomBy(1 / 1.25)));
        bar.Children.Add(Chip("+",      "Zoom in",                   () => ZoomBy(1.25)));
        bar.Children.Add(Chip("Fit",    "Fit the whole view",        FitToContent));
        bar.Children.Add(Chip("1:1",    "Show at natural size",      ActualSize));
        return bar;
    }

    private Button Chip(string text, string tip, Action act)
    {
        var b = new Button
        {
            Content         = text,
            ToolTip         = new TextBlock { Text = tip, TextAlignment = TextAlignment.Left },
            Padding         = new Thickness(7, 1, 7, 2),
            Margin          = new Thickness(4, 0, 0, 0),
            FontSize        = 11,
            MinWidth        = 26,
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
            Focusable       = false,
        };
        b.Click += (_, e) => { e.Handled = true; act(); };
        _chips[b] = act;
        return b;
    }

    /// <summary>
    /// Resolves the chrome colours. A caller-supplied brush wins; otherwise the active theme's tokens
    /// are read at load time, and only if even those are missing does a literal stand in.
    /// </summary>
    private void ApplyChrome()
    {
        var accent = AccentBrush   ??= Resource("AccentBrush",  Brushes.SteelBlue);
        var chrome = ChromeBrush   ??= Resource("SurfaceBrush", Brushes.Black);
        var onChrome = OnChromeBrush ??= Resource("TextBrush",  Brushes.White);

        _zoomLabel.Foreground     = Fade(onChrome, 0xAA);
        _miniPanel.Background     = Fade(chrome, 0xDD);
        _miniPanel.BorderBrush    = Fade(accent, 0x66);

        foreach (var b in _chips.Keys)
        {
            b.Foreground  = onChrome;
            b.Background  = Fade(chrome, 0xDD);
            b.BorderBrush = Fade(accent, 0x66);
        }
        SyncMiniMap();
    }

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static Brush Fade(Brush source, byte alpha)
    {
        var c = (source as SolidColorBrush)?.Color ?? Colors.Gray;
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
