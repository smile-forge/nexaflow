using Nexaflow.Features.Scratchpad.Converters;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Visuals.Text.Markdown;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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

        // Links open in an in-app web tab; fall back to the OS browser if unwired.
        Editor.LinkNavigate = url =>
        {
            var open = Vm.OpenUrl;
            if (open is null) return false;
            open(url);
            return true;
        };
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

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
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
        var dx     = pos.X - _resizeStart.X;
        var dy     = pos.Y - _resizeStart.Y;
        const double minSize = 80;

        // The note rotates about its centre, so resizing in canvas (screen) axes would
        // both skew the drag and shift the note as the pivot moves. Work in the note's
        // local axes and pin the un-dragged corner in canvas space.
        var theta = Vm.Rotation * Math.PI / 180.0;
        var cos   = Math.Cos(theta);
        var sin   = Math.Sin(theta);

        // Project the screen drag onto the note's local axes (rotate by -θ).
        var localDx =  dx * cos + dy * sin;
        var localDy = -dx * sin + dy * cos;

        var w = _resizeStartW;
        var h = _resizeStartH;
        if (_resizeEdge.Contains('E')) w = Math.Max(minSize, _resizeStartW + localDx);
        if (_resizeEdge.Contains('W')) w = Math.Max(minSize, _resizeStartW - localDx);
        if (_resizeEdge.Contains('S')) h = Math.Max(minSize, _resizeStartH + localDy);
        if (_resizeEdge.Contains('N')) h = Math.Max(minSize, _resizeStartH - localDy);

        // Anchor = the fixed corner (centre offset of the side(s) not being dragged),
        // captured in canvas space from the start geometry…
        var ax0 = _resizeEdge.Contains('W') ?  _resizeStartW / 2 : -_resizeStartW / 2;
        var ay0 = _resizeEdge.Contains('N') ?  _resizeStartH / 2 : -_resizeStartH / 2;
        var cx0 = _resizeStartX + _resizeStartW / 2;
        var cy0 = _resizeStartY + _resizeStartH / 2;
        var anchorX = cx0 + (ax0 * cos - ay0 * sin);
        var anchorY = cy0 + (ax0 * sin + ay0 * cos);

        // …then solve for the new centre that keeps that same corner pinned.
        var ax1 = _resizeEdge.Contains('W') ?  w / 2 : -w / 2;
        var ay1 = _resizeEdge.Contains('N') ?  h / 2 : -h / 2;
        var cx1 = anchorX - (ax1 * cos - ay1 * sin);
        var cy1 = anchorY - (ax1 * sin + ay1 * cos);

        Vm.Width  = w;
        Vm.Height = h;
        Vm.X      = cx1 - w / 2;
        Vm.Y      = cy1 - h / 2;

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
