using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.VirtualDisk.Views;

[ViewFor(typeof(VirtualDiskViewModel))]
public partial class VirtualDiskView : UserControl
{
    private readonly VirtualDiskViewModel _vm;

    public VirtualDiskView(VirtualDiskViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is DiskNode node)
            _vm.ActivateRowCommand.Execute(node);
    }
}
