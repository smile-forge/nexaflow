using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.ViewModels;

namespace Nexaflow.Features.WindowsApps.Views;

public partial class WindowsAppsView : UserControl, IPageView
{
    /// <summary>The pane's width, remembered across close/reopen (and after the user drags the splitter).</summary>
    private double _lastPaneWidth = 400;

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

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyAdvancedPaneState();
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

    // ── Advanced options pane ─────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowsAppsViewModel.IsAdvancedOpen))
            ApplyAdvancedPaneState();
    }

    /// <summary>
    /// Shows or hides the right-hand pane. Collapsing it zeroes the column so the list reclaims the
    /// width outright, and the width the user dragged to is kept for the next time it opens.
    /// </summary>
    private void ApplyAdvancedPaneState()
    {
        if (AdvancedHost.Visibility == Visibility.Visible &&
            AdvancedColumn.Width.IsAbsolute && AdvancedColumn.Width.Value > 1)
            _lastPaneWidth = AdvancedColumn.Width.Value;

        var open = ViewModel.IsAdvancedOpen;
        AdvancedHost.Visibility     = open ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdvancedColumn.Width        = new GridLength(open ? _lastPaneWidth : 0);
        AdvancedColumn.MinWidth     = open ? 280 : 0;
    }

    // ── Row actions ───────────────────────────────────────────────────────────

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

    private void ModifyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.ModifyCommand.Execute(item);
    }

    private void MoveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.ShowMoveCommand.Execute(item);
    }

    private void AdvancedOptionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: InstalledAppItem item })
            ViewModel.ShowAdvancedOptionsCommand.Execute(item);
    }
}
