using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.ViewModels;

namespace Nexaflow.Features.WindowsApps.Views;

public partial class WindowsAppsView : UserControl, IPageView
{
    public WindowsAppsViewModel ViewModel { get; }

    // ── IPageView ─────────────────────────────────────────────────────────────
    // Exposes the ViewModel to the AI pipeline so GetContext()/GetClientTools() are reachable.
    IPageViewModel? IPageView.ViewModel => ViewModel;

    public WindowsAppsView(WindowsAppsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // GridViewColumnHeader swallows the mouse, so a Button in the header template never sees the
        // click — handle the header click at the ListView level (same trick as the file browser).
        AppListView.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Column.Header: SortableHeader header })
            ViewModel.SortByCommand.Execute(header.Key);
    }

    private void AppListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView lv)
            ViewModel.SetSelection(lv.SelectedItems.OfType<InstalledAppItem>().ToList());
    }

    // The "⋯" button opens its own dropdown; pin the row item onto the menu so its items inherit it.
    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } btn)
        {
            menu.PlacementTarget = btn;
            menu.DataContext     = btn.DataContext;
            menu.IsOpen          = true;
            e.Handled            = true;
        }
    }

    private void UninstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.UninstallCommand.Execute(item);
    }

    private void OpenLocationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.OpenLocationCommand.Execute(item);
    }

    private void DeleteRecordMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.DeleteRecordCommand.Execute(item);
    }
}
