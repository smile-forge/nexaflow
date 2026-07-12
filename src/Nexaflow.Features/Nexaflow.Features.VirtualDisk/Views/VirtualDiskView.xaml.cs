using System.Windows.Controls;
using Nexaflow.Features.VirtualDisk.ViewModels;

namespace Nexaflow.Features.VirtualDisk.Views;

public partial class VirtualDiskView : UserControl
{
    public VirtualDiskView(VirtualDiskViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
