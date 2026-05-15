using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Features.Scratchpad.Views;

/// <summary>
/// IDropTarget is implemented for intra-app drops from the FileSystemView.
/// Note: IDropTarget.Drop does not receive drop coordinates, so notes land at the viewport
/// centre. External OS-level drops (from other applications) are handled by the XAML
/// DragOver/Drop event handlers on CanvasHost, which DO have position information.
/// </summary>
public partial class ScratchpadView : System.Windows.Controls.UserControl, IKeyboardHandler, IDropTarget
{
    private ScratchpadViewModel Vm => (ScratchpadViewModel)DataContext;

    public double CanvasScale => CanvasScaleTransform.ScaleX;

    // ── Pan state ─────────────────────────────────────────────────────────
    private bool   _isPanning;
    private Point  _panStart;
    private double _panStartOffX;
    private double _panStartOffY;

    // ── Mini-ribbon target ────────────────────────────────────────────────
    private PostItViewModel? _ribbonTarget;

    private const double MinScale = 0.08;
    private const double MaxScale = 4.0;

    public ScratchpadView(ScratchpadViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.ConfirmAction = (title, msg) =>
        {
            var result = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        };

        Loaded   += (_, _) => { Focus(); ApplyTransform(); };
        Unloaded += (_, _) => vm.Dispose();

        vm.Notes.CollectionChanged += (_, e) =>
        {
            UpdateMiniMap();
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (PostItViewModel note in e.NewItems)
                    note.PropertyChanged += (_, _) => UpdateMiniMap();
            }
        };
    }

    // ── IKeyboardHandler ─────────────────────────────────────────────────
    // Called by the framework when this view is active. OnKeyDown (below) also
    // calls ProcessKey for keyboard events that bubble up from unfocused children.

    public bool CanProcessKey(Key key, ModifierKeys modifiers)
        => (key == Key.V        && modifiers == ModifierKeys.Control)
        || (key == Key.Add      && modifiers == ModifierKeys.Control)
        || (key == Key.Subtract && modifiers == ModifierKeys.Control)
        || (key == Key.D0       && modifiers == ModifierKeys.Control);

    public bool ProcessKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            Vm.PasteAsNoteCommand.Execute(null);
            return true;
        }
        if (key == Key.Add      && modifiers == ModifierKeys.Control) { ZoomBy(1.15);       return true; }
        if (key == Key.Subtract && modifiers == ModifierKeys.Control) { ZoomBy(1 / 1.15);   return true; }
        if (key == Key.D0       && modifiers == ModifierKeys.Control) { ResetZoom();         return true; }
        return false;
    }

    // OnKeyDown handles Ctrl+V when the canvas background has focus (no RichTextBox focused).
    // When a RichTextBox inside a PostItControl handles Ctrl+V it marks the event Handled=true,
    // so it never bubbles up here — no interception needed.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && ProcessKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    // ── IDropTarget (intra-app drops from FileSystemView) ─────────────────

    public bool CanAcceptDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.Text)
        || data.GetDataPresent(DataFormats.UnicodeText)
        || data.GetDataPresent(DataFormats.FileDrop);

    public string GetDropDescription(IDataObject data, string? targetFolderName, bool isMove)
        => "Create post-it";

    public new void Drop(IDataObject data, string destinationPath, bool move)
    {
        string? content = null;
        if (data.GetDataPresent(DataFormats.UnicodeText))
            content = data.GetData(DataFormats.UnicodeText) as string;
        else if (data.GetDataPresent(DataFormats.Text))
            content = data.GetData(DataFormats.Text) as string;
        else if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            content = string.Join("\n", files);

        if (!string.IsNullOrEmpty(content))
            Vm.AddNoteWithContent(content, ViewportCenter());
    }

    // ── Note mini-ribbon (rendered at ScratchpadView level, never rotated) ─

    public void ShowNoteRibbon(PostItViewModel vm, Point screenDevicePos)
    {
        _ribbonTarget = vm;

        // Update shape button active state
        RibbonShapeSquare.Style  = vm.Shape == "Square"
            ? (Style)Resources["RibbonBtnActive"]
            : (Style)Resources["RibbonBtn"];
        RibbonShapeRounded.Style = vm.Shape == "Rounded"
            ? (Style)Resources["RibbonBtnActive"]
            : (Style)Resources["RibbonBtn"];

        // Convert screen device pixels → logical pixels for Popup placement
        var src = PresentationSource.FromVisual(this);
        Point logicalPos;
        if (src?.CompositionTarget != null)
            logicalPos = src.CompositionTarget.TransformFromDevice.Transform(screenDevicePos);
        else
            logicalPos = screenDevicePos;

        NoteRibbon.HorizontalOffset = logicalPos.X;
        NoteRibbon.VerticalOffset   = logicalPos.Y;
        NoteRibbon.IsOpen           = true;
    }

    private void RibbonColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_ribbonTarget == null) return;
        var color = ((FrameworkElement)sender).Tag as string;
        if (color != null)
            _ribbonTarget.ChangeColorCommand.Execute(color);
        NoteRibbon.IsOpen = false;
    }

    private void RibbonShapeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_ribbonTarget == null) return;
        var shape = ((FrameworkElement)sender).Tag as string;
        if (shape != null)
            _ribbonTarget.ChangeShapeCommand.Execute(shape);
        NoteRibbon.IsOpen = false;
    }

    private void RibbonBringToFront_Click(object sender, RoutedEventArgs e)
    {
        _ribbonTarget?.BringToFrontCommand.Execute(null);
        NoteRibbon.IsOpen = false;
    }

    private void RibbonSendToBack_Click(object sender, RoutedEventArgs e)
    {
        _ribbonTarget?.SendToBackCommand.Execute(null);
        NoteRibbon.IsOpen = false;
    }

    // ── Canvas mouse events ───────────────────────────────────────────────

    private void CanvasHost_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            var canvasPt = ScreenToCanvas(e.GetPosition(CanvasHost));
            Vm.AddNote(canvasPt);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left &&
             (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))))
        {
            _isPanning    = true;
            _panStart     = e.GetPosition(CanvasHost);
            _panStartOffX = CanvasTranslateTransform.X;
            _panStartOffY = CanvasTranslateTransform.Y;
            CanvasHost.CaptureMouse();
            CanvasHost.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
        else
        {
            Focus();
        }
    }

    private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(CanvasHost);
        CanvasTranslateTransform.X = _panStartOffX + (pos.X - _panStart.X);
        CanvasTranslateTransform.Y = _panStartOffY + (pos.Y - _panStart.Y);
        UpdateVmOffset();
        UpdateMiniMap();
        e.Handled = true;
    }

    private void CanvasHost_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        CanvasHost.Cursor = null;
    }

    private void CanvasHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        ZoomAt(factor, e.GetPosition(CanvasHost));
        e.Handled = true;
    }

    // ── External drag+drop (from other applications / OS) ────────────────

    private void CanvasHost_DragOver(object sender, DragEventArgs e)
    {
        if (CanAcceptDrop(e.Data))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void CanvasHost_Drop(object sender, DragEventArgs e)
    {
        string? content = null;

        if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            content = e.Data.GetData(DataFormats.UnicodeText) as string;
        else if (e.Data.GetDataPresent(DataFormats.Text))
            content = e.Data.GetData(DataFormats.Text) as string;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                 e.Data.GetData(DataFormats.FileDrop) is string[] files)
            content = string.Join("\n", files);

        if (string.IsNullOrEmpty(content)) return;

        // Use actual drop position for placement — unavailable via IDropTarget.Drop
        var canvasPt = ScreenToCanvas(e.GetPosition(CanvasHost));
        Vm.AddNoteWithContent(content, canvasPt);
        e.Handled = true;
    }

    // ── Toolbar button handlers ───────────────────────────────────────────

    private void AddNote_Click(object sender, RoutedEventArgs e)
        => Vm.AddNote(ViewportCenter());

    private void ZoomToFit_Click(object sender, RoutedEventArgs e)
    {
        Vm.ZoomToFitWithViewport(CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        ApplyTransform();
        UpdateMiniMap();
    }

    private void ToggleBin_Click(object sender, RoutedEventArgs e)
        => Vm.ToggleRecycleBinCommand.Execute(null);

    private void BinDrop_Click(object sender, RoutedEventArgs e)
    {
        // Open the attached ContextMenu directly below the button
        var btn = (Button)sender;
        if (btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void EmptyBin_Click(object sender, RoutedEventArgs e)
        => Vm.EmptyRecycleBinCommand.Execute(null);

    // ── Zoom helpers ─────────────────────────────────────────────────────

    private void ZoomBy(double factor)
        => ZoomAt(factor, new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));

    private void ResetZoom()
    {
        Vm.Scale   = 1.0;
        Vm.OffsetX = 0;
        Vm.OffsetY = 0;
        ApplyTransform();
        UpdateMiniMap();
    }

    private void ZoomAt(double factor, Point mouseOnHost)
    {
        var newScale     = Math.Clamp(CanvasScaleTransform.ScaleX * factor, MinScale, MaxScale);
        var actualFactor = newScale / CanvasScaleTransform.ScaleX;

        CanvasTranslateTransform.X  = mouseOnHost.X - (mouseOnHost.X - CanvasTranslateTransform.X) * actualFactor;
        CanvasTranslateTransform.Y  = mouseOnHost.Y - (mouseOnHost.Y - CanvasTranslateTransform.Y) * actualFactor;
        CanvasScaleTransform.ScaleX = newScale;
        CanvasScaleTransform.ScaleY = newScale;

        UpdateVmOffset();
        UpdateMiniMap();
    }

    private void ApplyTransform()
    {
        CanvasScaleTransform.ScaleX    = Vm.Scale;
        CanvasScaleTransform.ScaleY    = Vm.Scale;
        CanvasTranslateTransform.X     = Vm.OffsetX;
        CanvasTranslateTransform.Y     = Vm.OffsetY;
        UpdateZoomLabel();
    }

    private void UpdateVmOffset()
    {
        Vm.Scale   = CanvasScaleTransform.ScaleX;
        Vm.OffsetX = CanvasTranslateTransform.X;
        Vm.OffsetY = CanvasTranslateTransform.Y;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
        => ZoomLabel.Text = $"{(int)(CanvasScaleTransform.ScaleX * 100)}%";

    // ── Minimap ───────────────────────────────────────────────────────────

    private void UpdateMiniMap()
    {
        if (!IsLoaded || Vm.Notes.Count == 0)
        {
            MiniMapBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var s  = CanvasScaleTransform.ScaleX;
        var tx = CanvasTranslateTransform.X;
        var ty = CanvasTranslateTransform.Y;

        // Viewport bounds in canvas space
        var viewLeft   = -tx / s;
        var viewTop    = -ty / s;
        var viewRight  = viewLeft + CanvasHost.ActualWidth  / s;
        var viewBottom = viewTop  + CanvasHost.ActualHeight / s;

        // Bounding box of all notes
        var minX = Vm.Notes.Min(n => n.X);
        var minY = Vm.Notes.Min(n => n.Y);
        var maxX = Vm.Notes.Max(n => n.X + n.Width);
        var maxY = Vm.Notes.Max(n => n.Y + n.Height);

        // Only show minimap if any note is (at least partially) outside the viewport
        var allVisible = minX >= viewLeft && minY >= viewTop
                      && maxX <= viewRight && maxY <= viewBottom;
        if (allVisible)
        {
            MiniMapBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // Expand bounding box to include viewport
        minX = Math.Min(minX, viewLeft);
        minY = Math.Min(minY, viewTop);
        maxX = Math.Max(maxX, viewRight);
        maxY = Math.Max(maxY, viewBottom);

        var contentW = maxX - minX;
        var contentH = maxY - minY;
        if (contentW <= 0 || contentH <= 0) return;

        const double mmW = 158;
        const double mmH = 98;
        var scale = Math.Min(mmW / contentW, mmH / contentH);
        var offX  = (mmW - contentW * scale) / 2;
        var offY  = (mmH - contentH * scale) / 2;

        MiniMapCanvas.Children.Clear();

        foreach (var note in Vm.Notes)
        {
            var r = new Rectangle
            {
                Width   = Math.Max(2, note.Width  * scale),
                Height  = Math.Max(2, note.Height * scale),
                Fill    = NoteColorToBrush(note.Color),
                Opacity = 0.85
            };
            Canvas.SetLeft(r, offX + (note.X - minX) * scale);
            Canvas.SetTop(r,  offY + (note.Y - minY) * scale);
            MiniMapCanvas.Children.Add(r);
        }

        var vp = new Rectangle
        {
            Width           = Math.Max(4, (viewRight  - viewLeft)  * scale),
            Height          = Math.Max(4, (viewBottom - viewTop)   * scale),
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 100, 180, 255)),
            StrokeThickness = 1,
            Fill            = new SolidColorBrush(Color.FromArgb(30, 100, 180, 255))
        };
        Canvas.SetLeft(vp, offX + (viewLeft - minX) * scale);
        Canvas.SetTop(vp,  offY + (viewTop  - minY) * scale);
        MiniMapCanvas.Children.Add(vp);

        MiniMapBorder.Visibility = Visibility.Visible;
    }

    private static SolidColorBrush NoteColorToBrush(string color) => color switch
    {
        "Yellow" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0x9D)),
        "Blue"   => new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)),
        "Green"  => new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7)),
        "Pink"   => new SolidColorBrush(Color.FromRgb(0xF4, 0x8F, 0xB1)),
        "Orange" => new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x80)),
        "Purple" => new SolidColorBrush(Color.FromRgb(0xCE, 0x93, 0xD8)),
        _        => new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA))
    };

    // ── Coordinate helpers ────────────────────────────────────────────────

    private Point ScreenToCanvas(Point hostPt)
        => new((hostPt.X - CanvasTranslateTransform.X) / CanvasScaleTransform.ScaleX,
               (hostPt.Y - CanvasTranslateTransform.Y) / CanvasScaleTransform.ScaleY);

    private Point ViewportCenter()
        => ScreenToCanvas(new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));
}
