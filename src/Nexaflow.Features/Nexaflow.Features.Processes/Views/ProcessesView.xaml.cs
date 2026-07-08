using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Visuals.Common;

namespace Nexaflow.Features.Processes.Views;

public partial class ProcessesView : UserControl, IPageView
{
    public ProcessesViewModel ViewModel { get; }
    private WindowMinimizeWatcher? _minimizeWatcher;

    IPageViewModel? IPageView.ViewModel => ViewModel;

    public ProcessesView(ProcessesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // GridViewColumnHeader swallows the click, so handle header clicks at the ListView level.
        // Row buttons (the expander) aren't headers, so they're ignored here.
        ProcList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));

        // The view's Loaded/Unloaded is the active-tab signal (the shell hosts the active page in one
        // ContentPresenter, so an inactive tab's content leaves the visual tree). Minimize doesn't
        // unload the tree, so the watcher pauses the 1 s poll while the window is minimized.
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

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Column.Header: SortableHeader header })
            ViewModel.SortByCommand.Execute(header.Key);
    }

    private void ProcList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Resolve the row under the cursor directly, so it works regardless of selection state.
        var row = e.OriginalSource is DependencyObject src
                  && ItemsControl.ContainerFromElement(ProcList, src) is ListViewItem { Content: ProcessRowViewModel r }
            ? r
            : ViewModel.SelectedRow;
        if (row is not null)
            ViewModel.ViewDetailsCommand.Execute(row);
    }
}
