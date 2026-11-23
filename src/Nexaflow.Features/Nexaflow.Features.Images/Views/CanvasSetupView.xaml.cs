using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Visuals.Common.Viewing;
using SkiaSharp;

namespace Nexaflow.Features.Images.Views;

/// <summary>A new canvas's size and background, as the New picker's layered-picture options set them up: bound to their
/// <see cref="CanvasSetup"/>, which it tells the screen's size in pixels once it is shown on one.</summary>
[ViewFor(typeof(NewLayeredPictureOptions))]
public partial class CanvasSetupView : UserControl
{
    public CanvasSetupView(NewLayeredPictureOptions options)
    {
        InitializeComponent();
        DataContext = options.Setup;
        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            options.Setup.UseScreen(new SKSizeI((int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX),
                                                (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY)));
        };
    }
}
