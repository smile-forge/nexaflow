using Nexaflow.Features.Scratchpad.Converters;
using Nexaflow.Features.Scratchpad.ViewModels;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

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

    // ── Content sync ──────────────────────────────────────────────────────
    private bool _contentLoading;

    private static readonly ShapeToClipConverter _clipConverter = new();

    public PostItControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged        += OnSizeChanged;

        ContentBox.AddHandler(Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler(OnHyperlinkNavigate));
        ContentBox.TextChanged += ContentBox_TextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PostItViewModel old)
            old.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is PostItViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            LoadContent(vm.Content);
            UpdateClip();
            UpdatePinButtonText();
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
                break;
            case nameof(PostItViewModel.Content):
                if (!_contentLoading) LoadContent(Vm.Content);
                break;
            case nameof(PostItViewModel.ExpiresAt):
            case nameof(PostItViewModel.IsPinned):
                UpdatePinButtonText();
                break;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip();

    private void UpdateClip()
    {
        if (DataContext is not PostItViewModel vm) return;
        var clip = _clipConverter.Convert(
            [vm.Shape, vm.Width, vm.Height],
            typeof(Geometry), parameter: null!,
            System.Globalization.CultureInfo.InvariantCulture)
            as Geometry;
        RootGrid.Clip = clip;
        UpdateHeaderVisibility();
    }

    // ── Header visibility for complex shapes ──────────────────────────────

    private void UpdateHeaderVisibility()
    {
        if (DataContext is not PostItViewModel vm) return;
        if (vm.Shape is "Star" or "Heart" or "Cloud")
            Header.Visibility = Visibility.Collapsed;
        else
            Header.Visibility = Visibility.Visible;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (DataContext is PostItViewModel vm && vm.Shape is "Star" or "Heart" or "Cloud")
            Header.Visibility = Visibility.Visible;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (DataContext is PostItViewModel vm && vm.Shape is "Star" or "Heart" or "Cloud")
            Header.Visibility = Visibility.Collapsed;
    }

    // ── Content (RichTextBox) ─────────────────────────────────────────────

    private void LoadContent(string content)
    {
        _contentLoading = true;
        ContentBox.TextChanged -= ContentBox_TextChanged;
        try
        {
            if (!string.IsNullOrEmpty(content) &&
                content.TrimStart().StartsWith("<Section", StringComparison.Ordinal))
            {
                try
                {
                    var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    range.Load(ms, DataFormats.Xaml);
                    return;
                }
                catch { /* fall through to plain text */ }
            }

            // Plain text fallback
            ContentBox.Document.Blocks.Clear();
            if (!string.IsNullOrEmpty(content))
                ContentBox.Document.Blocks.Add(new Paragraph(new Run(content)));
        }
        finally
        {
            ContentBox.TextChanged += ContentBox_TextChanged;
            _contentLoading = false;
        }
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_contentLoading || DataContext is not PostItViewModel vm) return;
        _contentLoading = true;
        try
        {
            var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
            if (HasRichContent())
            {
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.Xaml);
                ms.Position = 0;
                vm.Content = new StreamReader(ms).ReadToEnd();
            }
            else
            {
                vm.Content = range.Text;
            }
        }
        finally
        {
            _contentLoading = false;
        }
    }

    private bool HasRichContent()
    {
        foreach (var block in ContentBox.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines)
            {
                if (inline is not Run) return true;
            }
        }
        return false;
    }

    private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Vm.OpenUrl?.Invoke(e.Uri.ToString());
        e.Handled = true;
    }

    // ── Header drag ───────────────────────────────────────────────────────

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        var canvas  = FindAncestorCanvas();
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
        var canvas = FindAncestorCanvas();
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
        var canvas    = FindAncestorCanvas();
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
        var canvas = FindAncestorCanvas();
        var pos    = canvas != null ? e.GetPosition(canvas) : e.GetPosition(Parent as UIElement);
        var dx     = pos.X - _resizeStart.X;
        var dy     = pos.Y - _resizeStart.Y;
        const double minSize = 80;

        // Horizontal axis
        if (_resizeEdge.Contains('W'))
        {
            var newW = Math.Max(minSize, _resizeStartW - dx);
            Vm.Width = newW;
            Vm.X     = (_resizeStartX + _resizeStartW) - newW;
        }
        else if (_resizeEdge.Contains('E'))
        {
            Vm.Width = Math.Max(minSize, _resizeStartW + dx);
        }

        // Vertical axis
        if (_resizeEdge.Contains('N'))
        {
            var newH = Math.Max(minSize, _resizeStartH - dy);
            Vm.Height = newH;
            Vm.Y      = (_resizeStartY + _resizeStartH) - newH;
        }
        else if (_resizeEdge.Contains('S'))
        {
            Vm.Height = Math.Max(minSize, _resizeStartH + dy);
        }

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
        var canvas = FindAncestorCanvas();
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
        var canvas = FindAncestorCanvas();
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

    // ── Mini ribbon popup ─────────────────────────────────────────────────

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        UpdateShapeButtons();
        UpdatePinButtonText();
        MiniRibbon.IsOpen = true;
        e.Handled = true;
    }

    private void ColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string color)
            Vm.Color = color;
        MiniRibbon.IsOpen = false;
    }

    private void ShapeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string shape)
            Vm.Shape = shape;
        UpdateShapeButtons();
        MiniRibbon.IsOpen = false;
    }

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        Vm.TogglePinCommand.Execute(null);
        MiniRibbon.IsOpen = false;
    }

    private void TimerBadge_Click(object sender, MouseButtonEventArgs e)
    {
        Vm.TogglePinCommand.Execute(null);
        e.Handled = true;
    }

    private void UpdateShapeButtons()
    {
        if (DataContext is not PostItViewModel vm) return;
        var active   = (Style)FindResource("RibbonBtnActive");
        var inactive = (Style)FindResource("RibbonBtn");
        ShapeBtnSquare.Style  = vm.Shape == "Square"  ? active : inactive;
        ShapeBtnRounded.Style = vm.Shape == "Rounded" ? active : inactive;
        ShapeBtnStar.Style    = vm.Shape == "Star"    ? active : inactive;
        ShapeBtnHeart.Style   = vm.Shape == "Heart"   ? active : inactive;
        ShapeBtnCloud.Style   = vm.Shape == "Cloud"   ? active : inactive;
    }

    private void UpdatePinButtonText()
    {
        if (DataContext is not PostItViewModel vm) return;
        PinBtnText.Text = vm.IsPinned ? "📌 Unpin" : "📌 Pin";
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Canvas? FindAncestorCanvas()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is Canvas canvas) return canvas;
        }
        return null;
    }
}
