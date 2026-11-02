using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.ViewModels;
using SkiaSharp;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// Artist's editor: the picture and its editing chrome on the shared pan/zoom surface, and the tool panel beside it.
/// </summary>
/// <remarks>
/// The picture is laid out at the size the viewer chose for it (<see cref="EditCanvas.Size"/>) and rendered at the
/// density the zoom asks for, so zooming in sharpens rather than blurs. The surface is refitted when what is shown
/// changes shape — entering or leaving the crop tool, a new crop — and otherwise keeps the reader's pan and zoom.
/// </remarks>
public partial class ImageEditView : UserControl
{
    /// <summary>Room left around the picture when it is fitted.</summary>
    private const double FitMargin = 40;

    /// <summary>The closest the canvas zooms in: sixteen screen pixels to one of the picture's.</summary>
    private const double MaxZoom = 16;

    /// <summary>The checker's tile on screen, in pixels: each square half of it, whatever the zoom.</summary>
    private const double CheckerTile = 16;

    private readonly Image _picture = new() { Stretch = Stretch.Fill };
    // What is see-through in the picture shows as a checkerboard beneath it, rather than as the page behind.
    private readonly Rectangle _checker = new() { IsHitTestVisible = false };
    private readonly EditOverlay _overlay = new();
    private readonly LayerToolsOverlay _layerTools = new();
    private readonly Grid _content = new();
    private ArtistViewModel? _viewer;
    private SKSizeI _contentSize;
    private RenderFrame? _contentFrame;
    private double _zoom;

    public ImageEditView()
    {
        InitializeComponent();
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        if (TryFindResource("TransparencyChecker") is DrawingBrush checker) _checker.Fill = checker.Clone();
        _content.Children.Add(_checker);
        _content.Children.Add(_picture);
        _content.Children.Add(_overlay);
        _content.Children.Add(_layerTools);   // over the chrome, taking the pointer only while Paint or Move is out

        DataContextChanged += (_, e) => Attach(e.NewValue as ArtistViewModel);
        IsVisibleChanged   += (_, _) => ReportViewport();
        Surface.LayoutUpdated += (_, _) => FollowZoom();
        Unloaded += (_, _) => Attach(null);
        Loaded   += (_, _) => Attach(DataContext as ArtistViewModel);
    }

    private void Attach(ArtistViewModel? viewer)
    {
        if (ReferenceEquals(viewer, _viewer)) return;
        if (_viewer is { } old)
        {
            old.PropertyChanged -= OnViewerChanged;
            old.Editor.TextEditRequested -= FocusMarkupText;
            _overlay.Detach();
            _layerTools.Detach();
        }
        _viewer = viewer;
        if (viewer is null) return;

        viewer.PropertyChanged += OnViewerChanged;
        viewer.Editor.TextEditRequested += FocusMarkupText;
        _overlay.Attach(viewer);
        _layerTools.Attach(viewer);
        _picture.Source = viewer.CurrentImage;
        if (viewer.Canvas is { } canvas) ShowCanvas(canvas);
    }

    private void OnViewerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewer is null) return;
        switch (e.PropertyName)
        {
            case nameof(ArtistViewModel.CurrentImage):
                _picture.Source = _viewer.CurrentImage;
                break;
            case nameof(ArtistViewModel.Canvas) when _viewer.Canvas is { } canvas && _viewer.IsEditing:
                ShowCanvas(canvas);
                break;
            case nameof(ArtistViewModel.IsEditing) when _viewer.IsEditing:
                ReportViewport();
                break;
        }
    }

    /// <summary>Lays the picture out at the size it was rendered for; refits only when that changed shape.</summary>
    private void ShowCanvas(EditCanvas canvas)
    {
        bool sameShape = canvas.Size == _contentSize && canvas.Frame == _contentFrame && ReferenceEquals(Surface.SurfaceContent, _content);
        _contentSize  = canvas.Size;
        _contentFrame = canvas.Frame;
        _content.Width  = canvas.Size.Width;
        _content.Height = canvas.Size.Height;
        // The picture is laid out at LayoutScale of its own pixels to a screen unit: 1:1 and the readout count its pixels on screen.
        Surface.NaturalScale = 1 / (canvas.LayoutScale * VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Surface.MaxScale     = Surface.NaturalScale * MaxZoom;
        if (!sameShape) Surface.SetSurfaceContent(_content, new Size(canvas.Size.Width, canvas.Size.Height));
        _overlay.InvalidateVisual();
    }

    private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e) => ReportViewport();

    // Pictures dropped on the canvas become layers, the photo a layered picture first.
    private void CanvasHost_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPictures(e) is { Count: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void CanvasHost_Drop(object sender, DragEventArgs e)
    {
        if (_viewer is not { } viewer || DroppedPictures(e) is not { Count: > 0 } pictures) return;
        e.Handled = true;
        await viewer.Pictures.AddFilesAsync(pictures);
    }

    private static IReadOnlyList<string>? DroppedPictures(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) is string[] files ? [.. files.Where(ImageFileTypes.IsImage)] : null;

    private void ReportViewport()
    {
        if (_viewer is null || !IsVisible || CanvasHost.ActualWidth <= FitMargin || CanvasHost.ActualHeight <= FitMargin) return;
        _viewer.SetViewport(CanvasHost.ActualWidth - FitMargin, CanvasHost.ActualHeight - FitMargin,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>
    /// The surface's zoom, read after every layout: its own fits are not reported as view changes, and the picture
    /// has to be re-rendered at whatever density they leave it at.
    /// </summary>
    private void FollowZoom()
    {
        double zoom = Surface.View.Scale;
        if (zoom <= 0 || Math.Abs(zoom - _zoom) < 0.001) return;
        _zoom = zoom;
        if (_checker.Fill is DrawingBrush checker) checker.Viewport = new Rect(0, 0, CheckerTile / zoom, CheckerTile / zoom);
        _overlay.Zoom = zoom;
        _layerTools.Zoom = zoom;
        _viewer?.SetEditZoom(zoom);
    }

    // ── A slider drag is one undo step ──────────────────────────────────────

    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _viewer?.Editor.BeginGesture();
        if (IsWithin(e.OriginalSource, StraightenSlider)) _overlay.ShowAlignmentGrid = true;
    }

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _overlay.ShowAlignmentGrid = false;
        _viewer?.Editor.EndGesture();
    }

    private static bool IsWithin(object source, DependencyObject ancestor)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    // ── Text annotations ────────────────────────────────────────────────────

    /// <summary>A new text annotation was placed: its words are typed in the panel, so the panel's box takes focus
    /// once it has been shown.</summary>
    private void FocusMarkupText() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(MarkupTextBox));

    private void MarkupTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewer?.Editor.EndTextEdit();
}
