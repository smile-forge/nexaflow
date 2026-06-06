using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Features.SystemInfo.Views;

public partial class EnvironmentVariablesView : UserControl, IPageView
{
    public EnvironmentVariablesViewModel ViewModel { get; }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    public EnvironmentVariablesView(EnvironmentVariablesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }
}
