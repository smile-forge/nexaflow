using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.Converters;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Views;

/// <summary>
/// IDropTarget is implemented for intra-app drops from the FileSystemView.
/// Note: IDropTarget.Drop does not receive drop coordinates, so notes land at the viewport
/// centre. External OS-level drops (from other applications) are handled by the XAML
/// DragOver/Drop event handlers on the surface, which DO have position information.
/// </summary>
public partial class ScratchpadView : System.Windows.Controls.UserControl, IKeyboardHandler, IDropTarget, IPageView
{
    private ScratchpadViewModel Vm => (ScratchpadViewModel)DataContext;

    // ── Mini-ribbon target ────────────────────────────────────────────────
    private PostItViewModel? _ribbonTarget;

    public ScratchpadView(ScratchpadViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // The board's extent is wherever the notes happen to be — it has no size of its own, and its
        // origin is negative as soon as someone drags a note up and left.
        Surface.ContentExtent = () => PanZoomMiniMap.Bounds(vm.Notes.Select(n => (n.X, n.Y, n.Width, n.Height)));

        // Each note draws on the overview in its own paper colour, from the same converter the notes
        // themselves use — no second copy of the palette to keep in sync.
        Surface.MiniMapItems = () => vm.Notes.Select(
            n => new MiniMapItem(n.X, n.Y, n.Width, n.Height, NoteColorToBrush(n.Color)));

        Surface.MiniMapViewportStroke = TryFindResource("Scratchpad.MinimapViewportStroke") as Brush;
        Surface.MiniMapViewportFill   = TryFindResource("Scratchpad.MinimapViewportFill")   as Brush;

        // Where the reader left the board is the view-model's to remember, so switching tabs and back
        // returns to it rather than to the origin.
        Surface.ViewChanged += OnViewChanged;

        // A press on empty board takes keyboard focus off whichever post-it was being edited, so it
        // commits and leaves edit mode. Preview, because the surface claims the press for its own pan;
        // a press that landed on a note is the note's, and stealing its focus would end the edit.
        Surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!IsWithinNote(e.OriginalSource)) Keyboard.Focus(this);
        };

        Loaded   += (_, _) => { Focus(); Surface.RestoreView(vm.Scale, vm.OffsetX, vm.OffsetY); };
        Unloaded += (_, _) => vm.Dispose();

        // A "?" search asks for a note to be brought into view; the arithmetic is the view-model's, the
        // viewport size is ours.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ScratchpadViewModel.ScrollToNote)) return;
            if (vm.ScrollToNote is not { } note) return;
            vm.CenterOnWithViewport(note, Surface.ActualWidth, Surface.ActualHeight);
            Surface.RestoreView(vm.Scale, vm.OffsetX, vm.OffsetY);
        };

        vm.Notes.CollectionChanged += (_, e) =>
        {
            Surface.RefreshOverview();
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (PostItViewModel note in e.NewItems)
                    note.PropertyChanged += (_, _) => Surface.RefreshOverview();
            }
        };
    }

    private void OnViewChanged(double scale, double offsetX, double offsetY)
    {
        Vm.Scale   = scale;
        Vm.OffsetX = offsetX;
        Vm.OffsetY = offsetY;
        ZoomLabel.Text = $"{(int)(scale * 100)}%";
    }

    // ── IKeyboardHandler ─────────────────────────────────────────────────
    // The shell dispatches this for the active page from the window's PreviewKeyDown, and skips it while a
    // text box is focused — so a RichTextBox inside a PostIt keeps Ctrl+V for text paste, while Ctrl+V on the
    // canvas background lands here and pastes into the scratchpad.

    public bool CanProcessKey(Key key, ModifierKeys modifiers)
        => (key == Key.V        && modifiers == ModifierKeys.Control)
        || (key == Key.Add      && modifiers == ModifierKeys.Control)
        || (key == Key.Subtract && modifiers == ModifierKeys.Control)
        || (key == Key.D0       && modifiers == ModifierKeys.Control);

    public bool ProcessKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            // Same classification as a drop, so pasting an image / file / URL behaves consistently.
            try { HandleDrop(Clipboard.GetDataObject(), ViewportCenter()); } catch { }
            return true;
        }
        if (key == Key.Add      && modifiers == ModifierKeys.Control) { Surface.ZoomBy(1.15);      return true; }
        if (key == Key.Subtract && modifiers == ModifierKeys.Control) { Surface.ZoomBy(1 / 1.15);  return true; }
        if (key == Key.D0       && modifiers == ModifierKeys.Control) { Surface.RestoreView(1, 0, 0); return true; }
        return false;
    }

    // ── IDropTarget (intra-app drops from FileSystemView) ─────────────────

    public bool CanAcceptDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.Text)
        || data.GetDataPresent(DataFormats.UnicodeText)
        || data.GetDataPresent(DataFormats.FileDrop)
        || data.GetDataPresent(DataFormats.Bitmap);

    /// <summary>Nothing here is a file on disk, so no drop can land on its own source.</summary>
    public bool IsSelfDrop(IDataObject data, string destinationPath) => false;

    public string GetDropDescription(IDataObject data, string? targetFolderName, bool isMove)
        => "Create post-it";

    // IDropTarget.Drop carries no coordinates, so intra-app drops land at the viewport centre.
    public new void Drop(IDataObject data, string destinationPath, bool move)
        => HandleDrop(data, ViewportCenter());

    /// <summary>
    /// Classifies dropped/pasted data into the right kind of post-it (shared by both drop paths
    /// and Ctrl+V):
    /// image file → image note; non-image file → file-link note; raw bitmap → image note;
    /// a bare URL → URL note (with background preview); otherwise plain text/markdown.
    /// Multiple files cascade so they don't stack exactly.
    /// </summary>
    private void HandleDrop(IDataObject? data, Point canvasPt)
    {
        if (data is null) return;
        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            for (int i = 0; i < files.Length; i++)
            {
                var pt = new Point(canvasPt.X + i * 24, canvasPt.Y + i * 24);
                if (DroppedMedia.IsImageFile(files[i])) Vm.AddImageNote(files[i], pt);
                else                                    Vm.AddFileLinkNote(files[i], pt);
            }
            return;
        }

        // Browser image drag with no backing file → a raw bitmap.
        if (data.GetDataPresent(DataFormats.Bitmap) &&
            data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bmp)
        {
            Vm.AddBitmapNote(bmp, canvasPt);
            return;
        }

        if (DroppedMedia.TryGetSingleUrl(data, out var url))
        {
            Vm.AddUrlNote(url, canvasPt);
            return;
        }

        var content = MarkdownClipboard.ReadBestMarkdown(data);
        if (!string.IsNullOrEmpty(content))
            Vm.AddNoteWithContent(content, canvasPt);
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

    // ── Canvas ────────────────────────────────────────────────────────────
    //
    // Pan, zoom and the overview are the shared PanZoomSurface's; what stays here is what is the
    // board's own — where a drop or a new note lands, and keeping the view-model's copy of the
    // transform up to date. (New post-its come from the toolbar, the canvas right-click menu, or
    // paste — double-clicking the canvas deliberately does nothing.)

    // True when the press landed on a post-it, so the board leaves its editing focus alone.
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

    // ── External drag+drop (from other applications / OS) ────────────────

    private void Surface_DragOver(object sender, DragEventArgs e)
    {
        if (CanAcceptDrop(e.Data))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Surface_Drop(object sender, DragEventArgs e)
    {
        // Use actual drop position for placement — unavailable via IDropTarget.Drop
        HandleDrop(e.Data, ScreenToCanvas(e.GetPosition(Surface)));
        e.Handled = true;
    }

    // ── Canvas right-click → new post-it ──────────────────────────────────

    private Point _newNoteCanvasPt;

    private void Surface_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _newNoteCanvasPt = ScreenToCanvas(Mouse.GetPosition(Surface));
        CanvasPasteItem.IsEnabled = ClipboardHasContent();
    }

    private void NewNoteHere_Click(object sender, RoutedEventArgs e)
        => Vm.AddNote(_newNoteCanvasPt);

    private void PasteHere_Click(object sender, RoutedEventArgs e)
    {
        try { HandleDrop(Clipboard.GetDataObject(), _newNoteCanvasPt); } catch { }
    }

    private static bool ClipboardHasContent()
    {
        try { return Clipboard.ContainsText() || Clipboard.ContainsImage() || Clipboard.ContainsFileDropList(); }
        catch { return false; }
    }

    // ── Toolbar button handlers ───────────────────────────────────────────

    private void AddNote_Click(object sender, RoutedEventArgs e)
        => Vm.AddNote(ViewportCenter());

    private void ZoomToFit_Click(object sender, RoutedEventArgs e)
    {
        // The board's own fit, not the surface's: a corkboard fits with padding and will scale a
        // sparse board up, where a laid-out diagram never goes past 1:1.
        Vm.ZoomToFitWithViewport(Surface.ActualWidth, Surface.ActualHeight);
        Surface.RestoreView(Vm.Scale, Vm.OffsetX, Vm.OffsetY);
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
            Surface.ZoomTo(target);
        ZoomPopup.IsOpen = false;
    }

    // Reuse the note-fill converter so the overview draws from the SAME (themed) colour source as the
    // notes — no second copy of the palette to keep in sync.
    private static readonly PostItColorToBrushConverter _fillConverter = new();

    private static Brush NoteColorToBrush(string color)
        => (Brush)_fillConverter.Convert(color, typeof(Brush), null!, CultureInfo.InvariantCulture);

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => Vm;

    // ── Coordinate helpers ────────────────────────────────────────────────

    private Point ScreenToCanvas(Point hostPt)
    {
        var (scale, x, y) = Surface.View;
        return scale <= 0 ? hostPt : new Point((hostPt.X - x) / scale, (hostPt.Y - y) / scale);
    }

    private Point ViewportCenter()
        => ScreenToCanvas(new Point(Surface.ActualWidth / 2, Surface.ActualHeight / 2));
}
