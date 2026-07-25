using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.ViewModels;
using Nexaflow.Visuals.Common;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Features.Dicom.Views;

/// <summary>
/// The DICOM viewer tab. Left pane lists the content tree; the right pane renders the selected image with a
/// pan/zoom stage, a window/level right-drag, a measurement overlay and a pixel probe. All colours resolve
/// from theme tokens; the overlay is redrawn (not transformed) so strokes stay crisp at any zoom.
/// </summary>
public partial class DicomView : UserControl, IPageView
{
    private DicomViewModel ViewModel { get; }

    // Image → screen transform (scale + translate), kept off the visual-tree transform so overlay strokes
    // don't scale.
    private Matrix _view = Matrix.Identity;
    private int _lastImageW = -1, _lastImageH = -1;
    private bool _needsFit;   // a new image awaits its first fit-to-window (once the stage is measured)

    // Interaction state.
    private bool _panning;
    private bool _windowing;
    private Point _dragStart;
    private double _startWidth, _startCenter;
    private readonly List<Point> _pendingPoints = [];   // image-space points for the in-progress measurement

    private WindowMinimizeWatcher? _minimizeWatcher;

    public DicomView(DicomViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Measure.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MeasurementViewModel.Current)) HookMeasurements();
        };

        Loaded += (_, _) =>
        {
            ViewModel.OnActivated();
            _minimizeWatcher = WindowMinimizeWatcher.Attach(this, min => { if (min) ViewModel.OnDeactivated(); else ViewModel.OnActivated(); });
        };
        Unloaded += (_, _) =>
        {
            ViewModel.OnDeactivated();
            _minimizeWatcher?.Dispose();
            _minimizeWatcher = null;
        };
    }

    IPageViewModel IPageView.ViewModel => ViewModel;

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DicomNode node) ViewModel.SelectedNode = node;
    }

    // Scroll the selected item into view — so stepping the series with the wheel keeps the current slice visible.
    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item) item.BringIntoView();
    }

    // ── Tag drawer: width (resizable, remembered across toggles) + copy ────

    private double _drawerWidth = 340;

    private void OnTagsDrawerToggled()
    {
        if (ViewModel.TagsOpen)
        {
            DrawerCol.Width = new GridLength(_drawerWidth);
        }
        else
        {
            if (DrawerCol.ActualWidth > 40) _drawerWidth = DrawerCol.ActualWidth;   // remember the resized width
            DrawerCol.Width = new GridLength(0);
        }
    }

    private void OnCopyTagValue(object sender, RoutedEventArgs e) => CopyTag(sender, t => t.Value);
    private void OnCopyTagName(object sender, RoutedEventArgs e) => CopyTag(sender, t => t.Name);
    private void OnCopyTagId(object sender, RoutedEventArgs e) => CopyTag(sender, t => t.Tag);
    private void OnCopyTagRow(object sender, RoutedEventArgs e) => CopyTag(sender, t => $"{t.Tag}\t{t.Name}\t{t.Value}");

    private static void CopyTag(object sender, System.Func<DicomTagItem, string> pick)
    {
        // The menu item's ContextMenu is opened over the tag row; its DataContext is the DicomTagItem.
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: DicomTagItem item } } })
            try { Clipboard.SetText(pick(item) ?? string.Empty); } catch { /* clipboard busy */ }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DicomViewModel.CurrentBitmap))
            OnBitmapChanged();
        else if (e.PropertyName == nameof(DicomViewModel.TagsOpen))
            OnTagsDrawerToggled();
    }

    // ── Bitmap / transform ────────────────────────────────────────────────

    private void OnBitmapChanged()
    {
        if (ViewModel.CurrentBitmap is not { } bmp)
        {
            Overlay.Children.Clear();
            return;
        }

        // Fit only when the image dimensions change (a new instance); window/level re-renders keep the view.
        if (bmp.PixelWidth != _lastImageW || bmp.PixelHeight != _lastImageH)
        {
            _lastImageW = bmp.PixelWidth;
            _lastImageH = bmp.PixelHeight;
            _needsFit = true;
            HookMeasurements();
            // Background priority so it runs AFTER layout (when Stage has a real size); if the stage still
            // isn't measured, OnRenderSizeChanged fits it once _needsFit is set.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(FitToWindow));
        }
        else
        {
            RedrawOverlay();
        }
    }

    private void ApplyTransform()
    {
        Frame.RenderTransform = new MatrixTransform(_view);
        RedrawOverlay();
    }

    private void FitToWindow()
    {
        if (_lastImageW <= 0 || Stage.ActualWidth <= 0 || Stage.ActualHeight <= 0) return;
        _view = ViewportFit.Fit(_lastImageW, _lastImageH, Stage.ActualWidth, Stage.ActualHeight);
        _needsFit = false;
        ApplyTransform();
    }

    private void ActualSize()
    {
        if (_lastImageW <= 0) return;
        _view = ViewportFit.ActualSize(_lastImageW, _lastImageH, Stage.ActualWidth, Stage.ActualHeight);
        ApplyTransform();
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitToWindow();
    private void OnActualSize(object sender, RoutedEventArgs e) => ActualSize();

    private Point ToImage(Point screen) => ViewportFit.ToContent(_view, screen);

    private Point ToScreen(Point image) => _view.Transform(image);

    // ── Mouse: pan / window-level / measurement ───────────────────────────

    private void OnStageWheel(object sender, MouseWheelEventArgs e)
    {
        if (_lastImageW <= 0) return;
        e.Handled = true;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+wheel = zoom, anchored at the cursor.
            _view = ViewportFit.ZoomAt(_view, e.GetPosition(Stage), e.Delta > 0 ? 1.15 : 1 / 1.15);
            ApplyTransform();
        }
        else
        {
            // Plain wheel = step through the series stack (wheel up → previous slice); zoom/pan is preserved.
            ViewModel.StepImage(e.Delta > 0 ? -1 : 1);
        }
    }

    private void OnStageLeftDown(object sender, MouseButtonEventArgs e)
    {
        Stage.Focus();
        var tool = ViewModel.Measure.ActiveTool;
        if (tool is MeasurementTool.None or MeasurementTool.Probe)
        {
            _panning = true;
            _dragStart = e.GetPosition(Stage);
            Stage.CaptureMouse();
            return;
        }

        // Collect a measurement point (image space).
        _pendingPoints.Add(ToImage(e.GetPosition(Stage)));
        if (_pendingPoints.Count >= ViewModel.Measure.PointsNeeded)
        {
            ViewModel.Measure.Commit(tool, new List<Point>(_pendingPoints));
            _pendingPoints.Clear();
        }
        RedrawOverlay();
    }

    private void OnStageLeftUp(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        Stage.ReleaseMouseCapture();
    }

    private void OnStageRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.HasImage) return;
        _windowing = true;
        _dragStart = e.GetPosition(Stage);
        _startWidth = ViewModel.WindowWidth;
        _startCenter = ViewModel.WindowCenter;
        Stage.CaptureMouse();
        e.Handled = true;
    }

    private void OnStageRightUp(object sender, MouseButtonEventArgs e)
    {
        _windowing = false;
        Stage.ReleaseMouseCapture();
    }

    private void OnStageMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Stage);

        if (_panning)
        {
            var d = pos - _dragStart;
            _dragStart = pos;
            _view.Translate(d.X, d.Y);
            ApplyTransform();
            return;
        }

        if (_windowing)
        {
            // Horizontal = width, vertical = level (up brightens → lower centre).
            var d = pos - _dragStart;
            ViewModel.WindowWidth = System.Math.Max(1, _startWidth + d.X);
            ViewModel.WindowCenter = _startCenter - d.Y;
            ViewModel.NudgeWindowLevel(0, 0);   // trigger a re-render at the new W/L
            return;
        }

        if (ViewModel.Measure.ActiveTool == MeasurementTool.Probe)
            ViewModel.ProbeAt(ToImage(pos));

        // Live rubber-band for an in-progress measurement.
        if (_pendingPoints.Count > 0)
            RedrawOverlay(ToImage(pos));
    }

    // ── Overlay drawing ───────────────────────────────────────────────────

    private INotifyCollectionChanged? _hooked;

    private void HookMeasurements()
    {
        if (_hooked is not null) _hooked.CollectionChanged -= OnMeasurementsChanged;
        _hooked = ViewModel.Measure.Current;
        if (_hooked is not null) _hooked.CollectionChanged += OnMeasurementsChanged;
        RedrawOverlay();
    }

    private void OnMeasurementsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawOverlay();

    private void RedrawOverlay(Point? previewNext = null)
    {
        Overlay.Children.Clear();
        if (ViewModel.CurrentBitmap is null) return;

        var stroke = (Brush?)TryFindResource("Dicom.AnnotationBrush") ?? Brushes.Gold;

        // Labels use the same annotation colour as the lines — white washed out over bright regions.
        foreach (var m in ViewModel.Measure.Current)
            DrawMeasurement(m.Tool, m.Points, m.Label, stroke, stroke);

        // In-progress: draw the pending points plus a preview to the cursor.
        if (_pendingPoints.Count > 0)
        {
            var pts = new List<Point>(_pendingPoints);
            if (previewNext is { } p) pts.Add(p);
            DrawMeasurement(ViewModel.Measure.ActiveTool, pts, null, stroke, stroke);
        }
    }

    private void DrawMeasurement(MeasurementTool tool, IReadOnlyList<Point> imgPts, string? label, Brush stroke, Brush textBrush)
    {
        if (imgPts.Count == 0) return;
        var pts = new List<Point>(imgPts.Count);
        foreach (var p in imgPts) pts.Add(ToScreen(p));

        switch (tool)
        {
            case MeasurementTool.Length when pts.Count >= 2:
                AddLine(pts[0], pts[1], stroke);
                if (label is not null) AddLabel(Mid(pts[0], pts[1]), label, textBrush);
                break;

            case MeasurementTool.Angle when pts.Count >= 2:
                AddLine(pts[0], pts[1], stroke);
                if (pts.Count >= 3)
                {
                    AddLine(pts[1], pts[2], stroke);
                    if (label is not null) AddLabel(pts[1], label, textBrush);
                }
                break;

            case MeasurementTool.Rectangle when pts.Count >= 2:
                AddRect(pts[0], pts[1], stroke, ellipse: false);
                if (label is not null) AddLabel(pts[1], label, textBrush);
                break;

            case MeasurementTool.Ellipse when pts.Count >= 2:
                AddRect(pts[0], pts[1], stroke, ellipse: true);
                if (label is not null) AddLabel(pts[1], label, textBrush);
                break;

            default:
                foreach (var p in pts) AddDot(p, stroke);
                break;
        }
    }

    private void AddLine(Point a, Point b, Brush stroke)
        => Overlay.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = stroke, StrokeThickness = 1.5 });

    private void AddDot(Point p, Brush stroke)
    {
        var e = new Ellipse { Width = 5, Height = 5, Fill = stroke };
        Canvas.SetLeft(e, p.X - 2.5);
        Canvas.SetTop(e, p.Y - 2.5);
        Overlay.Children.Add(e);
    }

    private void AddRect(Point a, Point b, Brush stroke, bool ellipse)
    {
        var x = System.Math.Min(a.X, b.X);
        var y = System.Math.Min(a.Y, b.Y);
        var w = System.Math.Abs(a.X - b.X);
        var h = System.Math.Abs(a.Y - b.Y);
        Shape shape = ellipse
            ? new Ellipse { Width = w, Height = h }
            : new Rectangle { Width = w, Height = h };
        shape.Stroke = stroke;
        shape.StrokeThickness = 1.5;
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        Overlay.Children.Add(shape);
    }

    private void AddLabel(Point at, string text, Brush brush)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(tb, at.X + 6);
        Canvas.SetTop(tb, at.Y + 6);
        Overlay.Children.Add(tb);
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    // ── IPageView ─────────────────────────────────────────────────────────

    void IPageView.Reinitialize(Dictionary<string, string> pageParams) { }

    // Fit a freshly-loaded image once the stage is actually measured, then re-project the overlay on resize.
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_needsFit) FitToWindow();
        RedrawOverlay();
    }
}
