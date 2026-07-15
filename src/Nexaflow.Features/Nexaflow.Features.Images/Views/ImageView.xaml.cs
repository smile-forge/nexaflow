using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Features.Images.Views;

public partial class ImageView : UserControl, IPageView
{
    public ImageViewModel ViewModel { get; }

    private FullScreenImageWindow? _fullScreen;

    // Collage card footprint (mirrors the ItemContainerStyle size) — used for bounds & the minimap.
    private const double CollageCardW = 170, CollageCardH = 150;
    private const double CollageMinScale = 0.2, CollageMaxScale = 5.0;

    // ── Collage pan state (arm on press, begin once the pointer moves) ────
    private bool   _collagePanning;
    private bool   _collagePanArmed;
    private Point  _collagePanStart;
    private double _collagePanOffX, _collagePanOffY;
    private bool   _collageCentered;

    // ── Collage minimap mapping (frozen during a drag, like the scratchpad) ──
    private double     _mmScale, _mmOffX, _mmOffY, _mmMinX, _mmMinY;
    private bool       _mmHasMapping;
    private bool       _mmDragging;
    private Rectangle? _mmViewportRect;

    public ImageView(ImageViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The view's Loaded/Unloaded is the active-tab signal (the shell hosts the active page in one
        // ContentPresenter, so an inactive tab's content leaves the visual tree).
        Loaded += (_, _) => { ViewModel.OnActivated(); Focus(); };
        Unloaded += (_, _) =>
        {
            ViewModel.OnDeactivated();
            CloseFullScreen();
        };
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("view", out var view))
            ViewModel.ViewMode = ImageTabRegistration.ParseView(view);
    }

    // ── Keyboard (carousel navigation; Esc exits full-screen + stops auto) ──

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.ExitFullScreen();
                e.Handled = true;
                break;
            case Key.Space or Key.Right when ViewModel.IsCarousel:
                ViewModel.StepNext();
                e.Handled = true;
                break;
            case Key.Left when ViewModel.IsCarousel:
                ViewModel.StepPrevious();
                e.Handled = true;
                break;
        }
    }

    // ── Mouse wheel → step through images (carousel), clamped at the ends ──

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only the carousel steps on the wheel (and not while zoomed to actual size — then the
        // ScrollViewer pans). Album scrolls its grid, Explore scrolls its strip / steps on the image
        // side, Collage zooms — each handled by its own element, so leave those wheel events alone.
        if (!ViewModel.IsCarousel || !ViewModel.FitToWindow) return;
        if (e.Delta > 0) ViewModel.StepPrevious(); else ViewModel.StepNext();
        e.Handled = true;
    }

    // ── Thumbnail hit-testing (shared by album / explore / collage) ───────

    private static ImageThumbItem? ThumbAt(object? originalSource)
    {
        var dep = originalSource as DependencyObject;
        while (dep is not null && (dep as FrameworkElement)?.DataContext is not ImageThumbItem)
            dep = VisualTreeHelper.GetParent(dep);
        return (dep as FrameworkElement)?.DataContext as ImageThumbItem;
    }

    // Single-click a thumbnail (album / explore) → select it (shown in the carousel / explore pane).
    private void ThumbSelect_Click(object sender, MouseButtonEventArgs e)
    {
        if (ThumbAt(e.OriginalSource) is { } item)
            ViewModel.Select(item.Index);
    }

    // Album double-click → open that image in the carousel.
    private void AlbumThumb_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ThumbAt(e.OriginalSource) is { } item)
            ViewModel.OpenInCarousel(item.Index);
    }

    // Explore right pane wheel → step through images (updates the selection), clamped at the ends.
    private void ExploreImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) ViewModel.StepPrevious(); else ViewModel.StepNext();
        e.Handled = true;
    }

    // ── Collage pan / zoom ────────────────────────────────────────────────

    private void Collage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_collageCentered && CollageHost.ActualWidth > 0 && ViewModel.Thumbnails.Count > 0)
        {
            CenterCollage();
            _collageCentered = true;
        }
        UpdateCollageMiniMap();
    }

    private void Collage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor   = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newScale = Math.Clamp(CollageScale.ScaleX * factor, CollageMinScale, CollageMaxScale);
        var applied  = newScale / CollageScale.ScaleX;
        var p        = e.GetPosition(CollageHost);

        // Keep the point under the cursor fixed while zooming.
        CollageTranslate.X = p.X - (p.X - CollageTranslate.X) * applied;
        CollageTranslate.Y = p.Y - (p.Y - CollageTranslate.Y) * applied;
        CollageScale.ScaleX = newScale;
        CollageScale.ScaleY = newScale;
        UpdateCollageMiniMap();
        e.Handled = true;
    }

    private void Collage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (CollageMiniMap.IsVisible && CollageMiniMap.IsMouseOver) return;   // minimap handles its own drag

        // Double-click a card → open it in the carousel.
        if (e.ClickCount == 2 && ThumbAt(e.OriginalSource) is { } item)
        {
            ViewModel.OpenInCarousel(item.Index);
            return;
        }

        // Arm a pan from anywhere (background or a card); it begins once the pointer actually moves.
        _collagePanArmed = true;
        _collagePanStart = e.GetPosition(CollageHost);
        _collagePanOffX  = CollageTranslate.X;
        _collagePanOffY  = CollageTranslate.Y;
    }

    private void Collage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_collagePanArmed && !_collagePanning)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _collagePanArmed = false; return; }
            var p0 = e.GetPosition(CollageHost);
            if (Math.Abs(p0.X - _collagePanStart.X) + Math.Abs(p0.Y - _collagePanStart.Y) > 3)
            {
                _collagePanning = true;
                CollageHost.CaptureMouse();
                CollageHost.Cursor = Cursors.SizeAll;
            }
        }

        if (!_collagePanning) return;
        var p = e.GetPosition(CollageHost);
        CollageTranslate.X = _collagePanOffX + (p.X - _collagePanStart.X);
        CollageTranslate.Y = _collagePanOffY + (p.Y - _collagePanStart.Y);
        UpdateCollageMiniMap();
    }

    private void Collage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _collagePanArmed = false;
        if (!_collagePanning) return;
        _collagePanning = false;
        CollageHost.ReleaseMouseCapture();
        CollageHost.Cursor = null;
    }

    private (double MinX, double MinY, double MaxX, double MaxY)? CollageContentBounds()
    {
        var thumbs = ViewModel.Thumbnails;
        if (thumbs.Count == 0) return null;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var t in thumbs)
        {
            minX = Math.Min(minX, t.CollageX);
            minY = Math.Min(minY, t.CollageY);
            maxX = Math.Max(maxX, t.CollageX + CollageCardW);
            maxY = Math.Max(maxY, t.CollageY + CollageCardH);
        }
        return (minX, minY, maxX, maxY);
    }

    private void CenterCollage()
    {
        if (CollageContentBounds() is not { } b) return;
        double cw = b.MaxX - b.MinX, ch = b.MaxY - b.MinY;
        CollageScale.ScaleX = CollageScale.ScaleY = 1;
        CollageTranslate.X = (CollageHost.ActualWidth  - cw) / 2 - b.MinX;
        CollageTranslate.Y = (CollageHost.ActualHeight - ch) / 2 - b.MinY;
    }

    // ── Collage minimap (mirrors the scratchpad: thumbnail rects + a viewport box, drag to move) ──

    private void UpdateCollageMiniMap()
    {
        if (CollageContentBounds() is not { } bb) { CollageMiniMap.Visibility = Visibility.Collapsed; return; }

        double s = CollageScale.ScaleX, tx = CollageTranslate.X, ty = CollageTranslate.Y;
        double vw = CollageHost.ActualWidth, vh = CollageHost.ActualHeight;
        if (vw <= 0 || vh <= 0 || s <= 0) { CollageMiniMap.Visibility = Visibility.Collapsed; return; }

        // Viewport rectangle in content space.
        double viewLeft = -tx / s, viewTop = -ty / s;
        double viewRight = viewLeft + vw / s, viewBottom = viewTop + vh / s;

        // Only show the minimap when something is off-screen.
        if (bb.MinX >= viewLeft && bb.MinY >= viewTop && bb.MaxX <= viewRight && bb.MaxY <= viewBottom)
        {
            CollageMiniMap.Visibility = Visibility.Collapsed;
            return;
        }

        double minX = Math.Min(bb.MinX, viewLeft), minY = Math.Min(bb.MinY, viewTop);
        double maxX = Math.Max(bb.MaxX, viewRight), maxY = Math.Max(bb.MaxY, viewBottom);
        double cw = maxX - minX, ch = maxY - minY;
        if (cw <= 0 || ch <= 0) return;

        double mmW = CollageMiniMapCanvas.Width, mmH = CollageMiniMapCanvas.Height;
        double scale = Math.Min(mmW / cw, mmH / ch);
        double offX = (mmW - cw * scale) / 2, offY = (mmH - ch * scale) / 2;
        _mmScale = scale; _mmOffX = offX; _mmOffY = offY; _mmMinX = minX; _mmMinY = minY; _mmHasMapping = true;

        var thumbBrush = (Brush)FindResource("TextMutedBrush");
        var viewBrush  = (Brush)FindResource("AccentBrush");

        CollageMiniMapCanvas.Children.Clear();
        foreach (var t in ViewModel.Thumbnails)
        {
            var r = new Rectangle
            {
                Width   = Math.Max(2, CollageCardW * scale),
                Height  = Math.Max(2, CollageCardH * scale),
                Fill    = thumbBrush,
                Opacity = 0.85,
            };
            Canvas.SetLeft(r, offX + (t.CollageX - minX) * scale);
            Canvas.SetTop(r,  offY + (t.CollageY - minY) * scale);
            CollageMiniMapCanvas.Children.Add(r);
        }

        var vp = new Rectangle
        {
            Width           = Math.Max(4, (viewRight - viewLeft) * scale),
            Height          = Math.Max(4, (viewBottom - viewTop) * scale),
            Stroke          = viewBrush,
            StrokeThickness = 1.5,
            Fill            = Brushes.Transparent,
        };
        Canvas.SetLeft(vp, offX + (viewLeft - minX) * scale);
        Canvas.SetTop(vp,  offY + (viewTop  - minY) * scale);
        CollageMiniMapCanvas.Children.Add(vp);
        _mmViewportRect = vp;

        CollageMiniMap.Visibility = Visibility.Visible;
    }

    private void CollageMiniMap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_mmHasMapping) return;
        _mmDragging = true;
        CollageMiniMap.CaptureMouse();
        MoveCollageViewportTo(e.GetPosition(CollageMiniMapCanvas));
        e.Handled = true;
    }

    private void CollageMiniMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mmDragging) return;
        MoveCollageViewportTo(e.GetPosition(CollageMiniMapCanvas));
        e.Handled = true;
    }

    private void CollageMiniMap_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mmDragging) return;
        _mmDragging = false;
        CollageMiniMap.ReleaseMouseCapture();
        UpdateCollageMiniMap();
        e.Handled = true;
    }

    private void MoveCollageViewportTo(Point mm)
    {
        if (!_mmHasMapping || _mmScale <= 0) return;
        double s = CollageScale.ScaleX;

        // Invert the frozen mapping: minimap pixel → canvas point, then centre the viewport on it.
        double canvasX = _mmMinX + (mm.X - _mmOffX) / _mmScale;
        double canvasY = _mmMinY + (mm.Y - _mmOffY) / _mmScale;
        double viewLeft = canvasX - CollageHost.ActualWidth  / s / 2;
        double viewTop  = canvasY - CollageHost.ActualHeight / s / 2;

        CollageTranslate.X = -viewLeft * s;
        CollageTranslate.Y = -viewTop  * s;

        if (_mmViewportRect is { } vp)
        {
            Canvas.SetLeft(vp, _mmOffX + (viewLeft - _mmMinX) * _mmScale);
            Canvas.SetTop(vp,  _mmOffY + (viewTop  - _mmMinY) * _mmScale);
        }
    }

    // ── Full-screen window lifecycle (a view concern, driven by the VM flag) ──

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImageViewModel.IsFullScreen)) return;

        if (ViewModel.IsFullScreen) OpenFullScreen();
        else CloseFullScreen();
    }

    private void OpenFullScreen()
    {
        if (_fullScreen is not null) return;

        _fullScreen = new FullScreenImageWindow(ViewModel) { Owner = Window.GetWindow(this) };
        _fullScreen.Closed += (_, _) =>
        {
            _fullScreen = null;
            if (ViewModel.IsFullScreen) ViewModel.ExitFullScreen();   // closed via Alt+F4 etc.
        };
        _fullScreen.Show();
        _fullScreen.Activate();
    }

    private void CloseFullScreen()
    {
        if (_fullScreen is null) return;
        var w = _fullScreen;
        _fullScreen = null;
        w.Close();
    }
}
