using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Visuals.Common;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Processes.Views;

[ViewFor(typeof(ProcessDetailViewModel))]
public partial class ProcessDetailView : UserControl
{
    public ProcessDetailViewModel ViewModel { get; }
    private WindowMinimizeWatcher? _minimizeWatcher;

    public ProcessDetailView(ProcessDetailViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // Click a Modules column header to sort (GridViewColumnHeader is a ButtonBase).
        ModulesList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnModuleHeaderClick));

        // Minimize doesn't unload the tree — pause the 1 s sampling while the window is minimized.
        Loaded += (_, _) =>
        {
            ViewModel.OnActivated();
            _minimizeWatcher = WindowMinimizeWatcher.Attach(this,
                minimized => { if (minimized) ViewModel.OnDeactivated(); else ViewModel.OnActivated(); });
        };
        Unloaded += (_, _) =>
        {
            ViewModel.OnDeactivated();
            _minimizeWatcher?.Dispose();
            _minimizeWatcher = null;
        };
    }

    private void OnModuleHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Column.Header: SortableHeader header })
            ViewModel.SortModulesCommand.Execute(header.Key);
    }
}
