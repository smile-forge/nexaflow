using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nexaflow.Features.Common;
using Nexaflow.Features.GraphViewer.ViewModels;

namespace Nexaflow.Features.GraphViewer.Views;

/// <summary>
/// The Graph viewer page: a transformed <see cref="Canvas"/> hosting edge + node ItemsControls (both under one
/// Scale/Translate transform so they pan/zoom together), with wheel-zoom-to-cursor and armed-on-press pan copied
/// from the SVG viewer. The view-model loads + lays out the graph; this code-behind fits it and drives the transform.
/// </summary>
public partial class GraphView : UserControl, IPageView
{
    private const double MinScale = 0.05;
    private const double MaxScale = 8.0;

    private readonly GraphViewModel _vm;
    private bool _fitted;

    // Armed-on-press pan (begins once the pointer moves, so a plain click still selects a node).
    private bool _panArmed;
    private bool _panning;
    private Point _panStart;
    private double _panOffX, _panOffY;

    IPageViewModel? IPageView.ViewModel => _vm;

    private bool _tweenOn;

    public GraphView(GraphViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _vm.LayoutChanged += OnLayoutChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => StopTween();
    }

    // Fresh view → frame it; incremental expand → keep the transform. Either way, ease nodes to their targets.
    private void OnLayoutChanged(bool fit)
    {
        if (fit) { _fitted = false; TryFit(); }
        EnsureTween();
    }

    // ── Per-frame position tween: ease each node's displayed X/Y toward its layout target ──────────
    private void EnsureTween()
    {
        if (_tweenOn) return;
        _tweenOn = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopTween()
    {
        if (!_tweenOn) return;
        CompositionTarget.Rendering -= OnRendering;
        _tweenOn = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double maxDelta = 0;
        foreach (var n in _vm.Nodes)
        {
            double dx = n.Tx - n.X, dy = n.Ty - n.Y;
            n.X += dx * 0.22;
            n.Y += dy * 0.22;
            maxDelta = Math.Max(maxDelta, Math.Abs(dx) + Math.Abs(dy));
        }
        if (maxDelta < 0.5)
        {
            foreach (var n in _vm.Nodes) { n.X = n.Tx; n.Y = n.Ty; }
            foreach (var h in _vm.HyperEdges) h.UpdateCentre();
            StopTween();
            return;
        }
        foreach (var h in _vm.HyperEdges) h.UpdateCentre();   // connector glyphs track their (moving) endpoints
    }

    // Grow newly-revealed nodes in (their container is created fresh, so Loaded fires once).
    private void OnNodeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not GraphNodeViewModel n || !n.IsNew) return;
        var scale = new ScaleTransform(0.2, 0.2);
        fe.RenderTransformOrigin = new Point(0.5, 0.5);
        fe.RenderTransform = scale;
        var anim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vm.IsLoaded) await _vm.LoadAsync();
        TryFit();
    }

    // ── Fit-to-window ───────────────────────────────────────────────────────────────────────────
    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_fitted) TryFit();
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => Fit();

    private void TryFit()
    {
        if (_fitted || _vm.Nodes.Count == 0 || Host.ActualWidth <= 0 || Host.ActualHeight <= 0) return;
        Fit();
        _fitted = true;
    }

    // Centre on the FOCUS node (not the bounding box) and scale so the farthest node fits — so "Root" and every
    // re-focus put the start node dead centre.
    private void Fit()
    {
        if (_vm.Nodes.Count == 0 || Host.ActualWidth <= 0 || Host.ActualHeight <= 0) return;

        var focus = _vm.Nodes.FirstOrDefault(n => n.Id == _vm.FocusNodeId) ?? _vm.Nodes[0];
        double fcx = focus.Tx + (focus.Size / 2), fcy = focus.Ty + (focus.Size / 2);

        double maxR = 1;
        foreach (var n in _vm.Nodes)
        {
            double cx = n.Tx + (n.Size / 2), cy = n.Ty + (n.Size / 2);
            var r = Math.Sqrt(((cx - fcx) * (cx - fcx)) + ((cy - fcy) * (cy - fcy))) + n.Size;
            if (r > maxR) maxR = r;
        }

        var scale = Math.Clamp(((Math.Min(Host.ActualWidth, Host.ActualHeight) / 2) - 40) / maxR, MinScale, MaxScale);
        Scale.ScaleX = Scale.ScaleY = scale;
        Translate.X = (Host.ActualWidth / 2) - (fcx * scale);
        Translate.Y = (Host.ActualHeight / 2) - (fcy * scale);
        _vm.ApplyLod(scale);
    }

    // ── Zoom (keep the point under the cursor fixed) ──────────────────────────────────────────────
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newScale = Math.Clamp(Scale.ScaleX * factor, MinScale, MaxScale);
        var applied = newScale / Scale.ScaleX;
        if (applied == 1) return;

        var p = e.GetPosition(Host);
        Translate.X = p.X - (p.X - Translate.X) * applied;
        Translate.Y = p.Y - (p.Y - Translate.Y) * applied;
        Scale.ScaleX = Scale.ScaleY = newScale;
        _vm.ApplyLod(newScale);   // re-run the 3-tier LOD for the new zoom
        e.Handled = true;
    }

    // ── Pan ─────────────────────────────────────────────────────────────────────────────────────
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _panArmed = true;
        _panStart = e.GetPosition(Host);
        _panOffX = Translate.X;
        _panOffY = Translate.Y;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panArmed && !_panning)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _panArmed = false; return; }
            var p0 = e.GetPosition(Host);
            if (Math.Abs(p0.X - _panStart.X) + Math.Abs(p0.Y - _panStart.Y) > 3)
            {
                _panning = true;
                Host.CaptureMouse();
                Host.Cursor = Cursors.SizeAll;
            }
        }

        if (!_panning) return;
        var p = e.GetPosition(Host);
        Translate.X = _panOffX + (p.X - _panStart.X);
        Translate.Y = _panOffY + (p.Y - _panStart.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _panArmed = false;
        if (!_panning) return;
        _panning = false;
        Host.ReleaseMouseCapture();
        Host.Cursor = null;
    }

    // ── Segments rail (a floating overlay) — keep its clicks/wheel off the canvas ──────────────────
    // The rail sits inside the pan/zoom Host, so a bare press on it would arm a pan and a wheel would zoom.
    // Swallow both: a press does nothing to the canvas, a wheel scrolls the rail's own list.
    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnOverlayWheel(object sender, MouseWheelEventArgs e)
    {
        SegmentsScroller.ScrollToVerticalOffset(SegmentsScroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnNodeClick(object sender, MouseButtonEventArgs e)
    {
        if (_panning) return;
        if ((sender as FrameworkElement)?.DataContext is not GraphNodeViewModel node) return;
        if (node.Id == _vm.FocusNodeId) { _vm.SelectedNode = node; return; }   // already centred; just select
        PanToCentre(node, () => _vm.SelectedNode = node);
    }

    // Smoothly pan the clicked node to the viewport centre, THEN re-focus on it — so the eye stays on the node
    // while its neighbourhood expands around it.
    private void PanToCentre(GraphNodeViewModel node, Action onComplete)
    {
        if (Host.ActualWidth <= 0 || Host.ActualHeight <= 0) { onComplete(); return; }

        var targetX = (Host.ActualWidth / 2) - (node.CenterX * Scale.ScaleX);
        var targetY = (Host.ActualHeight / 2) - (node.CenterY * Scale.ScaleY);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var ax = new DoubleAnimation(Translate.X, targetX, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        var ay = new DoubleAnimation(Translate.Y, targetY, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        ay.Completed += (_, _) =>
        {
            // Release the animations and pin the final transform so later pan/zoom keep working.
            Translate.BeginAnimation(TranslateTransform.XProperty, null);
            Translate.BeginAnimation(TranslateTransform.YProperty, null);
            Translate.X = targetX;
            Translate.Y = targetY;
            onComplete();
        };
        Translate.BeginAnimation(TranslateTransform.XProperty, ax);
        Translate.BeginAnimation(TranslateTransform.YProperty, ay);
    }
}
