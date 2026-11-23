using System.Windows.Controls;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.SystemInfo.Views;

[ViewFor(typeof(EnvironmentVariablesViewModel))]
public partial class EnvironmentVariablesView : UserControl
{
    public EnvironmentVariablesView(EnvironmentVariablesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
