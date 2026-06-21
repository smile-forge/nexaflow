using Nexaflow.Features.Images.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// Borderless full-screen presentation of the current image. Navigation mirrors a photo viewer:
/// Esc leaves (and stops auto-advance, keeping the current image); Space / → / left-click advance;
/// ← / right-click go back. Binds to the same <see cref="ImageViewModel"/> as the tab, so auto-advance
/// and rotation stay in sync.
/// </summary>
public partial class FullScreenImageWindow : Window
{
    private readonly ImageViewModel _vm;

    public FullScreenImageWindow(ImageViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;

        Loaded += (_, _) => Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm.ExitFullScreen();
                e.Handled = true;
                break;
            case Key.Space or Key.Right or Key.Down:
                _vm.StepNext();
                e.Handled = true;
                break;
            case Key.Left or Key.Up:
                _vm.StepPrevious();
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _vm.StepNext();
        else if (e.ChangedButton == MouseButton.Right) _vm.StepPrevious();
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Delta > 0) _vm.StepPrevious(); else _vm.StepNext();
        e.Handled = true;
        base.OnMouseWheel(e);
    }
}
