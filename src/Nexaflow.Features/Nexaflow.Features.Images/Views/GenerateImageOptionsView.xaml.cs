using System.Windows.Controls;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Images.Views;

/// <summary>Gen Image's options in the New picker: bound to a <see cref="ViewModels.GenerateImageOptions"/>.</summary>
[ViewFor(typeof(GenerateImageOptions))]
public partial class GenerateImageOptionsView : UserControl
{
    public GenerateImageOptionsView(GenerateImageOptions options)
    {
        InitializeComponent();
        DataContext = options;
    }
}
