using System.Windows.Controls;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Images.Views;

/// <summary>Draws the New picker's layered-picture options: the canvas setup they make the picture with.</summary>
[ViewFor(typeof(NewLayeredPictureOptions))]
public sealed class NewLayeredPictureOptionsView : ContentControl
{
    public NewLayeredPictureOptionsView(NewLayeredPictureOptions options)
        => Content = new CanvasSetupView { DataContext = options.Setup };
}
