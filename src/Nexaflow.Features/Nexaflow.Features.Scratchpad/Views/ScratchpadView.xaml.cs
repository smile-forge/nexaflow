using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Scratchpad.Views;

public partial class ScratchpadView : System.Windows.Controls.UserControl, IKeyboardHandler
{
    private ScratchpadViewModel Vm => (ScratchpadViewModel)DataContext;

    /// <summary>Exposed to PostItControl so drag calculations can compensate for canvas zoom.</summary>
    public double CanvasScale => CanvasScaleTransform.ScaleX;

    // ── Pan state ─────────────────────────────────────────────────────────
    private bool  _isPanning;
    private Point _panStart;
    private double _panStartOffX;
    private double _panStartOffY;

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
    }

    // ── IKeyboardHandler ─────────────────────────────────────────────────

    public bool CanProcessKey(Key key, ModifierKeys modifiers)
        => (key == Key.V && modifiers == ModifierKeys.Control)
        || (key == Key.Add && modifiers == ModifierKeys.Control)
        || (key == Key.Subtract && modifiers == ModifierKeys.Control)
        || (key == Key.D0 && modifiers == ModifierKeys.Control);

    public bool ProcessKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.V && modifiers == ModifierKeys.Control) { Vm.PasteAsNoteCommand.Execute(null); return true; }
        if (key == Key.Add && modifiers == ModifierKeys.Control)      { ZoomBy(1.15); return true; }
        if (key == Key.Subtract && modifiers == ModifierKeys.Control) { ZoomBy(1 / 1.15); return true; }
        if (key == Key.D0 && modifiers == ModifierKeys.Control)       { ResetZoom(); return true; }
        return false;
    }

    // ── Keyboard (Ctrl+V creates note unless a RichTextBox is focused) ────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.V && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
        {
            var focused = FocusManager.GetFocusedElement(this);
            if (focused is not RichTextBox)
            {
                Vm.PasteAsNoteCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // ── Canvas mouse events ───────────────────────────────────────────────

    private void CanvasHost_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            // Double-click on canvas background → add a note at that position
            var screenPt = e.GetPosition(CanvasHost);
            var canvasPt = ScreenToCanvas(screenPt);
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
        var mouse  = e.GetPosition(CanvasHost);
        ZoomAt(factor, mouse);
        e.Handled = true;
    }

    private void CanvasHost_KeyDown(object sender, KeyEventArgs e)
    {
        if (ProcessKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    // ── Drag+drop from external sources ──────────────────────────────────

    private void CanvasHost_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.Text) ||
            e.Data.GetDataPresent(DataFormats.UnicodeText) ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
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

        var canvasPt = ScreenToCanvas(e.GetPosition(CanvasHost));
        Vm.AddNoteWithContent(content, canvasPt);
        e.Handled = true;
    }

    // ── Toolbar button handlers ───────────────────────────────────────────

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var center = ScreenToCanvas(new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2));
        Vm.AddNote(center);
    }

    private void ZoomToFit_Click(object sender, RoutedEventArgs e)
    {
        Vm.ZoomToFitWithViewport(CanvasHost.ActualWidth, CanvasHost.ActualHeight);
        ApplyTransform();
    }

    private void ToggleBin_Click(object sender, RoutedEventArgs e)
        => Vm.ToggleRecycleBinCommand.Execute(null);

    private void BinDrop_Click(object sender, RoutedEventArgs e)
        => BinPopup.IsOpen = !BinPopup.IsOpen;

    // ── Zoom helpers ─────────────────────────────────────────────────────

    private void ZoomBy(double factor)
    {
        var center = new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2);
        ZoomAt(factor, center);
    }

    private void ResetZoom()
    {
        Vm.Scale   = 1.0;
        Vm.OffsetX = 0;
        Vm.OffsetY = 0;
        ApplyTransform();
    }

    private void ZoomAt(double factor, Point mouseOnHost)
    {
        var newScale = Math.Clamp(CanvasScaleTransform.ScaleX * factor, MinScale, MaxScale);
        var actualFactor = newScale / CanvasScaleTransform.ScaleX;

        CanvasTranslateTransform.X = mouseOnHost.X - (mouseOnHost.X - CanvasTranslateTransform.X) * actualFactor;
        CanvasTranslateTransform.Y = mouseOnHost.Y - (mouseOnHost.Y - CanvasTranslateTransform.Y) * actualFactor;
        CanvasScaleTransform.ScaleX = newScale;
        CanvasScaleTransform.ScaleY = newScale;

        UpdateVmOffset();
        UpdateZoomLabel();
    }

    private void ApplyTransform()
    {
        CanvasScaleTransform.ScaleX     = Vm.Scale;
        CanvasScaleTransform.ScaleY     = Vm.Scale;
        CanvasTranslateTransform.X      = Vm.OffsetX;
        CanvasTranslateTransform.Y      = Vm.OffsetY;
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

    private Point ScreenToCanvas(Point screenPt)
        => new((screenPt.X - CanvasTranslateTransform.X) / CanvasScaleTransform.ScaleX,
               (screenPt.Y - CanvasTranslateTransform.Y) / CanvasScaleTransform.ScaleY);
}
