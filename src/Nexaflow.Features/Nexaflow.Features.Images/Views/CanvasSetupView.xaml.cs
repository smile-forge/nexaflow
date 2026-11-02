using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Features.Images.ViewModels;
using SkiaSharp;

namespace Nexaflow.Features.Images.Views;

/// <summary>A new canvas's size and background: bound to a <see cref="CanvasSetup"/>, which it tells the screen's size in
/// pixels once it is shown on one.</summary>
public partial class CanvasSetupView : UserControl
{
    public CanvasSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is not CanvasSetup setup) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            setup.UseScreen(new SKSizeI((int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX),
                                        (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY)));
        };
    }
}
