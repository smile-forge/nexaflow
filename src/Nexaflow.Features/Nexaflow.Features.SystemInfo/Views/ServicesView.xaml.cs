using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Features.SystemInfo.Views;

public partial class ServicesView : UserControl, IPageView
{
    public ServicesViewModel ViewModel { get; }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    public ServicesView(ServicesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // The startup-mode ComboBox is OneWay-bound to StartMode; a genuine user pick routes here, the VM
    // guards against the programmatic snap-back so this never loops.
    private void StartMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: ServiceRow row, SelectedItem: string mode })
            ViewModel.OnStartModeSelected(row, mode);
    }
}
