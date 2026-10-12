using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.ViewModels;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace Nexaflow.Features.Images.Views;

public partial class ImageView : UserControl, IPageView
{
    public ImageViewModel ViewModel { get; }

    private FullScreenImageWindow? _fullScreen;

    private bool _collageCentered;

    public ImageView(ImageViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        ConfigureCollageSurface();

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

    // ── Keyboard: carousel navigation, edit history and saving, edit mode in and out; Esc exits full-screen ──

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase) return;   // a text box's own keys are its own
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool featured = ViewModel.IsCarousel || ViewModel.IsExplore || ViewModel.IsEditing;
        var editor = ViewModel.Editor;

        switch (e.Key)
        {
            case Key.Z when ctrl && shift && featured:
            case Key.Y when ctrl && featured:
                ViewModel.RedoEditCommand.Execute(null);
                break;
            case Key.Z when ctrl && featured:
                ViewModel.UndoEditCommand.Execute(null);
                break;
            case Key.S when ctrl && shift && ViewModel.IsEditing:
                editor.SaveCopyCommand.Execute(null);
                break;
            case Key.S when ctrl && ViewModel.IsEditing:
                editor.SaveCommand.Execute(null);
                break;
            case Key.E when Keyboard.Modifiers == ModifierKeys.None && (ViewModel.IsCarousel || ViewModel.IsExplore):
                ViewModel.EnterEditModeCommand.Execute(null);
                break;
            case Key.Delete when ViewModel.IsEditing && editor.IsMarkupTool:
                editor.DeleteSelectedCommand.Execute(null);
                break;
            case Key.Enter when ViewModel.IsEditing && editor.IsCropTool:
                editor.Tool = EditTool.Adjust;   // the crop is applied: show the cropped picture
                break;
            case Key.Escape when ViewModel.IsEditing:
                if (!(editor.IsCropTool && editor.CancelCrop())) ViewModel.ExitEditModeCommand.Execute(null);
                break;
            case Key.Escape:
                ViewModel.ExitFullScreen();
                break;
            case Key.Space or Key.Right when ViewModel.IsCarousel:
                ViewModel.StepNext();
                break;
            case Key.Left when ViewModel.IsCarousel:
                ViewModel.StepPrevious();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ── The featured image's surface reports its size, so it is rendered for the screen ──

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e) => ReportViewport((FrameworkElement)sender);

    private void ImageHost_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => ReportViewport((FrameworkElement)sender);

    /// <summary>The visible image host's room for the picture (inside its 20 px margin), and the screen's density.</summary>
    private void ReportViewport(FrameworkElement host)
    {
        if (!host.IsVisible || host.ActualWidth <= 40 || host.ActualHeight <= 40) return;
        ViewModel.SetViewport(host.ActualWidth - 40, host.ActualHeight - 40, VisualTreeHelper.GetDpi(host).PixelsPerDip);
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
    //
    // The gesture, the transform and the overview are the shared PanZoomSurface's. What is the
    // collage's own is what it is a window onto: cards of a fixed footprint scattered across an
    // unbounded canvas, whose extent is wherever the layout happened to put them.

    private void ConfigureCollageSurface()
    {
        CollageSurface.ContentExtent = () =>
            CollageContentBounds() is { } b ? b.ToCanvasBounds() : null;

        CollageSurface.MiniMapItems = () =>
            CollageGeometry.MiniMapItems(ViewModel.Thumbnails.Select(t => (t.CollageX, t.CollageY)),
                                         (Brush)FindResource("TextMutedBrush"));

        // The collage opens centred at natural size rather than fitted: these are photographs, and a
        // big pile of them scaled down to fit is a pile of thumbnails of thumbnails. Setting the view
        // up front also tells the surface not to fit it for us later.
        CollageSurface.RestoreView(1, 0, 0);
    }

    private void Collage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_collageCentered || CollageSurface.ActualWidth <= 0) return;
        if (CollageContentBounds() is not { } bounds) return;

        var (x, y) = CollageGeometry.CentreOn(bounds, CollageSurface.ActualWidth, CollageSurface.ActualHeight);
        CollageSurface.RestoreView(1, x, y);
        _collageCentered = true;
    }

    // Double-click a card → open it in the carousel. Handled here, on the content, so the surface
    // leaves the press alone; a single click anywhere still pans.
    private void CollageCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || ThumbAt(e.OriginalSource) is not { } item) return;
        ViewModel.OpenInCarousel(item.Index);
        e.Handled = true;
    }

    private CollageBounds? CollageContentBounds() =>
        CollageGeometry.ContentBounds(ViewModel.Thumbnails.Select(t => (t.CollageX, t.CollageY)));

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
