using System.Windows.Controls;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Video.Views;

/// <summary>Gen Video's options in the New picker: bound to a <see cref="ViewModels.GenerateVideoOptions"/>.</summary>
[ViewFor(typeof(GenerateVideoOptions))]
public partial class GenerateVideoOptionsView : UserControl
{
    public GenerateVideoOptionsView(GenerateVideoOptions options)
    {
        InitializeComponent();
        DataContext = options;
    }
}
