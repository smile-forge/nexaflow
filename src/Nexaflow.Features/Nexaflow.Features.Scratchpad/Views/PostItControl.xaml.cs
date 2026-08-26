using Nexaflow.Features.Scratchpad.Converters;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Scratchpad.Views;

public partial class PostItControl : System.Windows.Controls.UserControl
{
    private PostItViewModel Vm => (PostItViewModel)DataContext;

    // ── Drag (move) state ─────────────────────────────────────────────────
    private bool   _isDragging;
    private Point  _dragStart;   // canvas coords
    private double _noteStartX;
    private double _noteStartY;

    // ── Resize state ──────────────────────────────────────────────────────
    private bool   _isResizing;
    private string _resizeEdge = string.Empty;
    private Point  _resizeStart;  // canvas coords
    private double _resizeStartX;
    private double _resizeStartY;
    private double _resizeStartW;
    private double _resizeStartH;

    // ── Rotate state ──────────────────────────────────────────────────────
    private bool   _isRotating;
    private double _rotateStartAngle;
    private double _rotateStartMouseAngle;

    private static readonly ShapeToClipConverter _clipConverter = new();

    public PostItControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged        += OnSizeChanged;

        // A clicked link (file path or URL) is dispatched by the shell to whichever feature
        // claims it; if none does, returning false lets the renderer open it in the OS browser.
        Editor.LinkNavigate = url => Vm.OpenUrl?.Invoke(url) ?? false;

        // Dropping an image / file / url / text onto a note inserts it as a block at the drop point.
        // The editor handles drops over its own (RichTextBox) area; PostItControl covers the rest of the
        // note (header, grips). Both insert a block rather than letting the drop create a new post-it.
        Editor.ContentDropped = InsertDropped;
        Editor.ContentPasted   = OnContentPasted;   // same rich-content handling for Ctrl+V / right-click Paste
        AllowDrop = true;
        DragOver += PostIt_DragOver;
        Drop     += PostIt_Drop;
    }

    // ── Drag-and-drop onto the note ───────────────────────────────────────

    private void PostIt_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop)
               || e.Data.GetDataPresent(DataFormats.Bitmap)
               || e.Data.GetDataPresent(DataFormats.UnicodeText)
               || e.Data.GetDataPresent(DataFormats.Text);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PostIt_Drop(object sender, DragEventArgs e)
    {
        InsertDropped(e.Data, e.GetPosition(Editor));
        e.Handled = true;   // consume so the canvas doesn't also create a new post-it
    }

    /// <summary>Drop hook: claim image / file / URL as a block at the drop point. Returning false when
    /// there was nothing of ours to insert lets the editor drop the text itself, as a paste would.</summary>
    private bool InsertDropped(IDataObject data, Point editorPoint)
        => InsertContent(data, md => Editor.InsertMarkdownAt(md, editorPoint));

    /// <summary>Paste hook: claim image / file / URL (insert as a block, like a drop); plain text falls
    /// back to the editor's inline paste (return false).</summary>
    private bool OnContentPasted(IDataObject data)
    {
        bool rich = DroppedMedia.TryGetSingleUrl(data, out _)
                 || data.GetDataPresent(DataFormats.FileDrop)
                 || data.GetDataPresent(DataFormats.Bitmap);
        return rich && InsertContent(data, md => Editor.InsertMarkdownAtCaret(md));
    }

    /// <summary>Turns dropped/pasted content into a note block via <paramref name="insert"/>: a URL is
    /// inserted now and then swapped for its fetched preview card; images are copied + embedded; files
    /// become links; text is inserted as-is. Returns false when there was nothing to insert.</summary>
    private bool InsertContent(IDataObject data, Action<string> insert)
    {
        if (DroppedMedia.TryGetSingleUrl(data, out var url))
        {
            insert(url);
            Vm.RequestUrlPreview?.Invoke(url, md => Editor.ReplaceBlock(url, md));
            return true;
        }

        var markdown = DropToMarkdown(data);
        if (string.IsNullOrEmpty(markdown)) return false;
        insert(markdown);
        return true;
    }

    /// <summary>Builds the markdown block(s) for dropped/pasted content: images are copied into this
    /// note's attachment folder and embedded; files/folders become links; text is inserted as-is.</summary>
    private string? DropToMarkdown(IDataObject data)
    {
        var dir = Vm.AttachmentDirectory;

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var blocks = new List<string>();
            foreach (var f in files)
            {
                if (DroppedMedia.IsImageFile(f) && dir is not null && DroppedMedia.CopyImageInto(f, dir) is { } name)
                    blocks.Add($"![]({name})");
                else
                    blocks.Add(DroppedMedia.FileLinkMarkdown(f));
            }
            return string.Join("\n\n", blocks);
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp
            && dir is not null && DroppedMedia.SaveBitmapInto(bmp, dir) is { } bitmapName)
            return $"![]({bitmapName})";

        return MarkdownClipboard.ReadBestMarkdown(data);   // text / url inserted as-is
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PostItViewModel old)
            old.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is PostItViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            MigrateLegacyContent(vm);
            UpdateClip();
            UpdateTail();

            if (vm.StartInEdit)
            {
                vm.StartInEdit = false;
                Dispatcher.BeginInvoke(() => Editor.BeginEdit(),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    /// <summary>Paints (or clears) the page search's matches inside this note's body. The view-model has no
    /// document to paint and the editor has no idea what a page search is, so the control is what joins
    /// them — the same rendered-text painter the markdown viewer and the email body use.</summary>
    private void ApplySearchHighlight(PostItViewModel vm)
    {
        if (vm.SearchMatcher is { } matcher) Editor.FindInRendered(matcher);
        else                                 Editor.ClearSearch();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PostItViewModel.SearchMatcher):
                if (sender is PostItViewModel searched) ApplySearchHighlight(searched);
                break;

            case nameof(PostItViewModel.Shape):
            case nameof(PostItViewModel.Width):
            case nameof(PostItViewModel.Height):
                UpdateClip();
                UpdateTail();
                break;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClip();
        UpdateTail();
    }

    private void UpdateClip()
    {
        if (DataContext is not PostItViewModel vm) return;
        var clip = _clipConverter.Convert(
            [vm.Shape, vm.Width, vm.Height],
            typeof(Geometry), parameter: null!,
            System.Globalization.CultureInfo.InvariantCulture)
            as Geometry;
        RootGrid.Clip = clip;
    }

    // Builds the speech-bubble tail that hangs below the body's bottom edge. The tail
    // overlaps a few px up into the body so there's no seam (the body is drawn on top).
    private void UpdateTail()
    {
        if (DataContext is not PostItViewModel vm ||
            vm.Shape != "SpeechBubble" || vm.Width <= 0 || vm.Height <= 0)
        {
            TailPath.Visibility = Visibility.Collapsed;
            TailPath.Data       = null;
            return;
        }

        double w = vm.Width, h = vm.Height;
        double depth   = Math.Min(20, h * 0.14);
        const double overlap = 3;

        // Tail leaning left: apex sits left of its base → "slightly angled".
        double baseLeft  = w * 0.30;
        double baseRight = w * 0.42;
        double apexX     = w * 0.22;
        double apexY     = h + depth;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(baseLeft, h - overlap), isFilled: true, isClosed: true);
            c.LineTo(new Point(baseRight, h - overlap), true, false);
            c.LineTo(new Point(apexX, apexY),           true, false);
        }
        g.Freeze();

        TailPath.Data       = g;
        TailPath.Visibility = Visibility.Visible;
    }

    // ── Legacy content migration ──────────────────────────────────────────

    /// <summary>
    /// Notes created before the markdown switch stored serialized RichTextBox XAML.
    /// Convert such content to plain text once, in place, so the editor shows text
    /// rather than raw markup.
    /// </summary>
    private static void MigrateLegacyContent(PostItViewModel vm)
    {
        var content = vm.Content;
        if (!string.IsNullOrEmpty(content) &&
            content.TrimStart().StartsWith("<Section", StringComparison.Ordinal) &&
            XamlToPlainText(content) is string migrated)
        {
            vm.Content = migrated;
        }
    }

    private static string? XamlToPlainText(string xaml)
    {
        try
        {
            var doc   = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
            range.Load(ms, DataFormats.Xaml);
            return new TextRange(doc.ContentStart, doc.ContentEnd).Text.TrimEnd();
        }
        catch { return null; }
    }

    // ── Header right-click → delegate to ScratchpadView-level ribbon ──────
    // The ribbon lives in ScratchpadView (above any transform) so it always
    // appears horizontal regardless of the note's rotation.

    private void Header_RightClick(object sender, MouseButtonEventArgs e)
    {
        var sv = FindAncestor<ScratchpadView>();
        if (sv == null) return;
        // Pass screen-device coordinates; ScratchpadView converts to logical pixels
        var screenPos = PointToScreen(e.GetPosition(this));
        sv.ShowNoteRibbon(Vm, screenPos);
        e.Handled = true;
    }

    // ── Header drag ───────────────────────────────────────────────────────

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        var canvas  = FindAncestor<Canvas>();
        _dragStart  = canvas != null ? e.GetPosition(canvas) : e.GetPosition(Parent as UIElement);
        _noteStartX = Vm.X;
        _noteStartY = Vm.Y;
        ((UIElement)sender).CaptureMouse();
        Vm.BringToFrontCommand.Execute(null);
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var canvas = FindAncestor<Canvas>();
        var pos    = canvas != null ? e.GetPosition(canvas) : e.GetPosition(Parent as UIElement);
        Vm.X = _noteStartX + (pos.X - _dragStart.X);
        Vm.Y = _noteStartY + (pos.Y - _dragStart.Y);
        e.Handled = true;
    }

    private void Header_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Resize ────────────────────────────────────────────────────────────

    private void Resize_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isResizing   = true;
        _resizeEdge   = ((FrameworkElement)sender).Tag as string ?? "SE";
        var canvas    = FindAncestor<Canvas>();
        _resizeStart  = canvas != null ? e.GetPosition(canvas) : e.GetPosition(Parent as UIElement);
        _resizeStartX = Vm.X;
        _resizeStartY = Vm.Y;
        _resizeStartW = Vm.Width;
        _resizeStartH = Vm.Height;
        ((UIElement)sender).CaptureMouse();
        Vm.BringToFrontCommand.Execute(null);
        e.Handled = true;
    }

    private void Resize_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;
        var canvas = FindAncestor<Canvas>();
        var pos    = canvas != null ? e.GetPosition(canvas) : e.GetPosition(Parent as UIElement);
        var start  = new NoteRect(_resizeStartX, _resizeStartY, _resizeStartW, _resizeStartH);

        var rect = PostItGeometry.Resize(_resizeEdge, Vm.Rotation, start,
                                         pos.X - _resizeStart.X, pos.Y - _resizeStart.Y);

        Vm.Width  = rect.Width;
        Vm.Height = rect.Height;
        Vm.X      = rect.X;
        Vm.Y      = rect.Y;

        e.Handled = true;
    }

    private void Resize_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Rotate ────────────────────────────────────────────────────────────

    private void RotateHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isRotating       = true;
        _rotateStartAngle = Vm.Rotation;
        var canvas = FindAncestor<Canvas>();
        if (canvas != null)
        {
            var center = new Point(Vm.X + Vm.Width / 2, Vm.Y + Vm.Height / 2);
            var mouse  = e.GetPosition(canvas);
            _rotateStartMouseAngle = Math.Atan2(mouse.Y - center.Y, mouse.X - center.X) * (180.0 / Math.PI);
        }
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void RotateHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRotating) return;
        var canvas = FindAncestor<Canvas>();
        if (canvas == null) return;
        var center       = new Point(Vm.X + Vm.Width / 2, Vm.Y + Vm.Height / 2);
        var mouse        = e.GetPosition(canvas);
        var currentAngle = Math.Atan2(mouse.Y - center.Y, mouse.X - center.X) * (180.0 / Math.PI);
        Vm.Rotation      = _rotateStartAngle + (currentAngle - _rotateStartMouseAngle);
        e.Handled        = true;
    }

    private void RotateHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRotating) return;
        _isRotating = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Timer badge ───────────────────────────────────────────────────────

    private void TimerBadge_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            e.Handled = true;
    }

    private void TimerBadge_Click(object sender, MouseButtonEventArgs e)
    {
        Vm.TogglePinCommand.Execute(null);
        e.Handled = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private T? FindAncestor<T>() where T : DependencyObject
    {
        DependencyObject? current = this;
        while (current != null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T match) return match;
        }
        return null;
    }
}
