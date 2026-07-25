using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Services;
using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Features.Images.ViewModels;
using System;
using System.Linq;
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

    // ── Collage pan state (arm on press, begin once the pointer moves) ────
    private bool   _collagePanning;
    private bool   _collagePanArmed;
    private Point  _collagePanStart;
    private double _collagePanOffX, _collagePanOffY;
    private bool   _collageCentered;

    // ── Collage minimap mapping (frozen during a drag, like the scratchpad) ──
    private MiniMapMapping? _mmMapping;
    private bool            _mmDragging;
    private Rectangle?      _mmViewportRect;

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
        var p = e.GetPosition(CollageHost);
        var (scale, x, y) = CollageGeometry.ZoomAt(
            CollageScale.ScaleX, CollageTranslate.X, CollageTranslate.Y, p.X, p.Y, zoomIn: e.Delta > 0);

        CollageTranslate.X  = x;
        CollageTranslate.Y  = y;
        CollageScale.ScaleX = scale;
        CollageScale.ScaleY = scale;
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

    private CollageBounds? CollageContentBounds() =>
        CollageGeometry.ContentBounds(ViewModel.Thumbnails.Select(t => (t.CollageX, t.CollageY)));

    private void CenterCollage()
    {
        if (CollageContentBounds() is not { } b) return;
        CollageScale.ScaleX = CollageScale.ScaleY = 1;
        (CollageTranslate.X, CollageTranslate.Y) =
            CollageGeometry.CentreOn(b, CollageHost.ActualWidth, CollageHost.ActualHeight);
    }

    // ── Collage minimap (mirrors the scratchpad: thumbnail rects + a viewport box, drag to move) ──

    private void UpdateCollageMiniMap()
    {
        var mapping = CollageContentBounds() is { } bb
            ? CollageGeometry.MiniMap(bb, CollageScale.ScaleX, CollageTranslate.X, CollageTranslate.Y,
                                      CollageHost.ActualWidth, CollageHost.ActualHeight,
                                      CollageMiniMapCanvas.Width, CollageMiniMapCanvas.Height)
            : null;

        _mmMapping = mapping;
        if (mapping is not { } m) { CollageMiniMap.Visibility = Visibility.Collapsed; return; }

        var thumbBrush = (Brush)FindResource("TextMutedBrush");
        var viewBrush  = (Brush)FindResource("AccentBrush");

        CollageMiniMapCanvas.Children.Clear();
        foreach (var t in ViewModel.Thumbnails)
        {
            var (x, y, w, h) = CollageGeometry.CardBox(m, t.CollageX, t.CollageY);
            var r = new Rectangle { Width = w, Height = h, Fill = thumbBrush, Opacity = 0.85 };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            CollageMiniMapCanvas.Children.Add(r);
        }

        var (vx, vy, vw, vh) = CollageGeometry.ViewportBox(m, m.ViewLeft, m.ViewTop);
        var vp = new Rectangle
        {
            Width           = vw,
            Height          = vh,
            Stroke          = viewBrush,
            StrokeThickness = 1.5,
            Fill            = Brushes.Transparent,
        };
        Canvas.SetLeft(vp, vx);
        Canvas.SetTop(vp, vy);
        CollageMiniMapCanvas.Children.Add(vp);
        _mmViewportRect = vp;

        CollageMiniMap.Visibility = Visibility.Visible;
    }

    private void CollageMiniMap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mmMapping is null) return;
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

    // Inverts the frozen mapping: minimap pixel → canvas point, then centres the viewport on it.
    private void MoveCollageViewportTo(Point mm)
    {
        if (_mmMapping is not { } m) return;

        var (x, y, viewLeft, viewTop) = CollageGeometry.TranslateForMiniMapPoint(
            m, mm.X, mm.Y, CollageScale.ScaleX, CollageHost.ActualWidth, CollageHost.ActualHeight);

        CollageTranslate.X = x;
        CollageTranslate.Y = y;

        if (_mmViewportRect is { } vp)
        {
            var (bx, by, _, _) = CollageGeometry.ViewportBox(m, viewLeft, viewTop);
            Canvas.SetLeft(vp, bx);
            Canvas.SetTop(vp, by);
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
