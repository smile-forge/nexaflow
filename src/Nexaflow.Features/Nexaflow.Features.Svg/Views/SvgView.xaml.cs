using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Svg.ViewModels;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Features.Svg.Views;

/// <summary>
/// The SVG viewer page: a checkerboard/solid canvas hosting the frozen vector image, with wheel-zoom (crisp
/// at any scale — the drawing re-tessellates, it isn't a bitmap) and drag-to-pan. The view-model loads the
/// artwork; this code-behind fits it to the window on first show and drives the zoom/pan transform.
/// </summary>
public partial class SvgView : UserControl, IPageView
{
    private const double MinScale = 0.05;
    private const double MaxScale = 64.0;

    private readonly SvgViewModel _vm;
    private bool _fitted;

    // Armed-on-press pan (begins once the pointer actually moves, so a click still selects).
    private bool _panArmed;
    private bool _panning;
    private Point _panStart;
    private double _panOffX, _panOffY;

    IPageViewModel? IPageView.ViewModel => _vm;

    public SvgView(SvgViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vm.IsLoaded) await _vm.LoadAsync();
        TryFit();
    }

    // ── Fit-to-window ─────────────────────────────────────────────────────────────────────────────
    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_fitted) TryFit();
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => Fit();

    private void TryFit()
    {
        if (_fitted) return;
        if (_vm.Artifact is null || _vm.NaturalWidth <= 0 || _vm.NaturalHeight <= 0) return;
        if (Host.ActualWidth <= 0 || Host.ActualHeight <= 0) return; // laid out yet? SizeChanged will retry
        Fit();
        _fitted = true;
    }

    /// <summary>Scales the artwork to fit the host and centres it.</summary>
    private void Fit()
    {
        var (scale, x, y) = ViewportFit.FitScaled(_vm.NaturalWidth, _vm.NaturalHeight,
                                                  Host.ActualWidth, Host.ActualHeight, MinScale, MaxScale);
        if (scale <= 0) return;   // nothing loaded, or the host hasn't been measured yet

        Scale.ScaleX = Scale.ScaleY = scale;
        Translate.X = x;
        Translate.Y = y;
    }

    // ── Zoom (keep the point under the cursor fixed) ──────────────────────────────────────────────
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(Host);
        var (scale, x, y) = PanZoomMiniMap.ZoomAt(Scale.ScaleX, Translate.X, Translate.Y, p.X, p.Y,
                                                  e.Delta > 0 ? 1.1 : 1 / 1.1, MinScale, MaxScale);
        if (scale == Scale.ScaleX) return;   // already at a zoom limit

        Scale.ScaleX = Scale.ScaleY = scale;
        Translate.X = x;
        Translate.Y = y;
        e.Handled = true;
    }

    // ── Pan ───────────────────────────────────────────────────────────────────────────────────────
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
}
