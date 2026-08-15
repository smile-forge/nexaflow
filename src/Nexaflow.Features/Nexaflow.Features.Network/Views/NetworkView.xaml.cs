using System.Windows.Controls;
using Nexaflow.Features.Network.ViewModels;

namespace Nexaflow.Features.Network.Views;

public partial class NetworkView : UserControl
{
    public NetworkView(NetworkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
