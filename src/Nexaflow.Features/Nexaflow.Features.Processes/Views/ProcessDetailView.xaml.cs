using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;

namespace Nexaflow.Features.Processes.Views;

public partial class ProcessDetailView : UserControl, IPageView
{
    public ProcessDetailViewModel ViewModel { get; }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    public ProcessDetailView(ProcessDetailViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // Click a Modules column header to sort (GridViewColumnHeader is a ButtonBase).
        ModulesList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnModuleHeaderClick));

        Loaded   += (_, _) => ViewModel.OnActivated();
        Unloaded += (_, _) => ViewModel.OnDeactivated();
    }

    private void OnModuleHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Column.Header: SortableHeader header })
            ViewModel.SortModulesCommand.Execute(header.Key);
    }

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("pid", out var s) && int.TryParse(s, out var pid))
            ViewModel.Reinitialize(pid);
    }
}
