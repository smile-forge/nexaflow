using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.ViewModels;

namespace Nexaflow.Features.VirtualDisk.Views;

public partial class VirtualDiskView : UserControl, IPageView
{
    private readonly VirtualDiskViewModel _vm;

    public VirtualDiskView(VirtualDiskViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
    }

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        // Single-image tab — params don't change after creation; nothing to re-init.
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is DiskNode node)
            _vm.ActivateRowCommand.Execute(node);
    }
}
