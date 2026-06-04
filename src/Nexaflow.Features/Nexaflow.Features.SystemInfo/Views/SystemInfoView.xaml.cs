using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Features.SystemInfo.Views;

public partial class SystemInfoView : UserControl, IPageView
{
    public SystemInfoViewModel ViewModel { get; }

    // Exposes the ViewModel to the AI pipeline so GetContext() is reachable.
    IPageViewModel? IPageView.ViewModel => ViewModel;

    public SystemInfoView(SystemInfoViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }
}
