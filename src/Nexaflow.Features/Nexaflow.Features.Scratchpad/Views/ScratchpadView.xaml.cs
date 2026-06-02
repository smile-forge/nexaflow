using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.Converters;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Specialized;
using System.Globalization;
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
public partial class ScratchpadView : System.Windows.Controls.UserControl, IKeyboardHandler, IDropTarget, IPageView
{
    private ScratchpadViewModel Vm => (ScratchpadViewModel)DataContext;

    public double CanvasScale => CanvasScaleTransform.ScaleX;

    // ── Pan state ─────────────────────────────────────────────────────────
    private bool   _isPanning;
    private bool   _panArmed;   // left-pressed on empty space; pan begins once the pointer moves
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
        var content = BestDropContent(data);
        if (!string.IsNullOrEmpty(content))
            Vm.AddNoteWithContent(content, ViewportCenter());
    }

    /// <summary>
    /// Markdown (custom format) wins; HTML is converted to markdown; otherwise plain
    /// text; finally a file-drop list. See <see cref="MarkdownClipboard.ReadBestMarkdown"/>.
    /// </summary>
    private static string? BestDropContent(IDataObject data)
    {
        var markdown = MarkdownClipboard.ReadBestMarkdown(data);
        if (!string.IsNullOrEmpty(markdown)) return markdown;

        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files)
            return string.Join("\n", files);

        return null;
    }

    // ── Note mini-ribbon (rendered at ScratchpadView level, never rotated) ─

    public void ShowNoteRibbon(PostItViewModel vm, Point screenDevicePos)
    {
        _ribbonTarget = vm;

        // Update shape button active state
        Style ShapeStyle(string shape) => vm.Shape == shape
            ? (Style)Resources["RibbonBtnActive"]
            : (Style)Resources["RibbonBtn"];
        RibbonShapeSquare.Style    = ShapeStyle("Square");
        RibbonShapeRounded.Style   = ShapeStyle("Rounded");
        RibbonShapeDiagonals.Style = ShapeStyle("DiagonalsRounded");
        RibbonShapeBubble.Style    = ShapeStyle("SpeechBubble");

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

        // Middle button pans immediately.
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning    = true;
            _panStart     = e.GetPosition(CanvasHost);
            _panStartOffX = CanvasTranslateTransform.X;
            _panStartOffY = CanvasTranslateTransform.Y;
            CanvasHost.CaptureMouse();
            CanvasHost.Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        // Left button on empty space arms a pan; it only starts once the pointer
        // actually moves, so a plain click (or the first half of a double-click)
        // still behaves normally. Clicks on a note are handled by the note itself.
        if (e.ChangedButton == MouseButton.Left && !IsWithinNote(e.OriginalSource))
        {
            _panArmed     = true;
            _panStart     = e.GetPosition(CanvasHost);
            _panStartOffX = CanvasTranslateTransform.X;
            _panStartOffY = CanvasTranslateTransform.Y;
        }

        Focus();
    }

    private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panArmed && !_isPanning)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _panArmed = false; return; }
            var p = e.GetPosition(CanvasHost);
            if (Math.Abs(p.X - _panStart.X) + Math.Abs(p.Y - _panStart.Y) > 3)
            {
                _isPanning = true;
                CanvasHost.CaptureMouse();
                CanvasHost.Cursor = Cursors.SizeAll;
            }
        }

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
        _panArmed = false;
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        CanvasHost.Cursor = null;
    }

    // True when the click landed on a post-it (so the canvas should not pan).
    private static bool IsWithinNote(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is PostItControl) return true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
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
        var content = BestDropContent(e.Data);
        if (string.IsNullOrEmpty(content)) return;

        // Use actual drop position for placement — unavailable via IDropTarget.Drop
        var canvasPt = ScreenToCanvas(e.GetPosition(CanvasHost));
        Vm.AddNoteWithContent(content, canvasPt);
        e.Handled = true;
    }

    // ── Canvas right-click → new post-it ──────────────────────────────────

    private Point _newNoteCanvasPt;

    private void CanvasHost_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        => _newNoteCanvasPt = ScreenToCanvas(Mouse.GetPosition(CanvasHost));

    private void NewNoteHere_Click(object sender, RoutedEventArgs e)
        => Vm.AddNote(_newNoteCanvasPt);

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
        // Open the attached ContextMenu below the button, right-aligned to the
        // button's right edge so it never spills past the window edge.
        var btn  = (Button)sender;
        var menu = btn.ContextMenu;
        if (menu == null) return;

        menu.PlacementTarget  = btn;
        menu.Placement        = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.HorizontalOffset = 0;

        void OnOpened(object? s, RoutedEventArgs _)
        {
            menu.Opened -= OnOpened;
            // Shift left by (menu width − button width) to right-align the edges.
            menu.HorizontalOffset = btn.ActualWidth - menu.ActualWidth;
        }
        menu.Opened += OnOpened;
        menu.IsOpen  = true;
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

    // ── Zoom % preset picker ──────────────────────────────────────────────

    private void ZoomLabel_Click(object sender, MouseButtonEventArgs e)
    {
        ZoomPopup.IsOpen = true;
        e.Handled = true;
    }

    private void ZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag &&
            double.TryParse(tag, NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
            SetZoom(target);
        ZoomPopup.IsOpen = false;
    }

    private void SetZoom(double target)
    {
        var current = CanvasScaleTransform.ScaleX;
        if (current <= 0) return;
        ZoomAt(target / current, new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));
    }

    // ── Minimap ───────────────────────────────────────────────────────────

    // Last drawn minimap→canvas mapping, captured so a drag gesture can convert a
    // minimap pixel back into a canvas point without recomputing (which would shift
    // under the cursor as the viewport moves).
    private double _mmScale, _mmOffX, _mmOffY, _mmMinX, _mmMinY;
    private bool   _mmHasMapping;
    private bool   _mmDragging;
    private Rectangle? _mmViewportRect;

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

        // Capture the mapping so a minimap drag can invert it (minimap px → canvas).
        _mmScale = scale; _mmOffX = offX; _mmOffY = offY; _mmMinX = minX; _mmMinY = minY;
        _mmHasMapping = true;

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
            Stroke          = (Brush)FindResource("Scratchpad.MinimapViewportStroke"),
            StrokeThickness = 1,
            Fill            = (Brush)FindResource("Scratchpad.MinimapViewportFill")
        };
        Canvas.SetLeft(vp, offX + (viewLeft - minX) * scale);
        Canvas.SetTop(vp,  offY + (viewTop  - minY) * scale);
        MiniMapCanvas.Children.Add(vp);
        _mmViewportRect = vp;

        MiniMapBorder.Visibility = Visibility.Visible;
    }

    // ── Minimap drag-to-reposition ────────────────────────────────────────
    // Dragging anywhere on the minimap recentres the viewport on the cursor, so the
    // blue viewport rectangle follows the pointer. The mapping is frozen for the
    // gesture (captured by UpdateMiniMap) and only fully redrawn on release.

    private void MiniMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_mmHasMapping) return;
        _mmDragging = true;
        MiniMapBorder.CaptureMouse();
        MoveViewportTo(e.GetPosition(MiniMapCanvas));
        e.Handled = true;
    }

    private void MiniMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mmDragging) return;
        MoveViewportTo(e.GetPosition(MiniMapCanvas));
        e.Handled = true;
    }

    private void MiniMap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mmDragging) return;
        _mmDragging = false;
        MiniMapBorder.ReleaseMouseCapture();
        UpdateMiniMap();   // resync mapping + visibility now the gesture is done
        e.Handled = true;
    }

    private void MoveViewportTo(Point mm)
    {
        if (!_mmHasMapping || _mmScale <= 0) return;

        var s = CanvasScaleTransform.ScaleX;

        // Minimap pixel → canvas point under the cursor (invert the captured mapping).
        var canvasX = _mmMinX + (mm.X - _mmOffX) / _mmScale;
        var canvasY = _mmMinY + (mm.Y - _mmOffY) / _mmScale;

        // Centre the viewport on that canvas point.
        var viewW = CanvasHost.ActualWidth  / s;
        var viewH = CanvasHost.ActualHeight / s;
        var viewLeft = canvasX - viewW / 2;
        var viewTop  = canvasY - viewH / 2;

        CanvasTranslateTransform.X = -viewLeft * s;
        CanvasTranslateTransform.Y = -viewTop  * s;
        UpdateVmOffset();

        // Move the viewport rectangle live using the frozen mapping (no full redraw,
        // which would shift the mapping under the cursor mid-drag).
        if (_mmViewportRect is { } vp)
        {
            Canvas.SetLeft(vp, _mmOffX + (viewLeft - _mmMinX) * _mmScale);
            Canvas.SetTop(vp,  _mmOffY + (viewTop  - _mmMinY) * _mmScale);
        }
    }

    // Reuse the note-fill converter so the minimap draws from the SAME (themed) colour source as the
    // notes — no second copy of the palette to keep in sync.
    private static readonly PostItColorToBrushConverter _fillConverter = new();

    private static Brush NoteColorToBrush(string color)
        => (Brush)_fillConverter.Convert(color, typeof(Brush), null!, CultureInfo.InvariantCulture);

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => Vm;

    // ── Coordinate helpers ────────────────────────────────────────────────

    private Point ScreenToCanvas(Point hostPt)
        => new((hostPt.X - CanvasTranslateTransform.X) / CanvasScaleTransform.ScaleX,
               (hostPt.Y - CanvasTranslateTransform.Y) / CanvasScaleTransform.ScaleY);

    private Point ViewportCenter()
        => ScreenToCanvas(new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));
}
