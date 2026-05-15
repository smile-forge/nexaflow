using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nexaflow.Features.WindowsSearch.Views;

public partial class SearchView : UserControl, IPageView, IRefreshable
{
    private readonly SearchViewModel _vm;

    private GridViewColumnHeader? _lastSortHeader;
    private ListSortDirection     _lastSortDir = ListSortDirection.Ascending;

    public SearchView(SearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) => await _vm.RunSearchAsync(CancellationToken.None);
    }

    // ── IPageView ────────────────────────────────────────────────────────────

    object? IPageView.ViewModel => _vm;

    string IPageView.GetContext() =>
        $"Search tab: '{_vm.SearchQuery}' in {_vm.SearchRoot}. {_vm.ResultCount} results.";

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];

    IContext? IPageView.GetContextObject() => null;

    // ── IRefreshable ─────────────────────────────────────────────────────────

    public void Refresh() => _ = _vm.RefreshAsync();

    // ── Column sort ──────────────────────────────────────────────────────────

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header
            || header.Role == GridViewColumnHeaderRole.Padding)
            return;

        var propName = HeaderToSortProperty(header.Content?.ToString());
        if (propName is null) return;

        var dir = (header == _lastSortHeader && _lastSortDir == ListSortDirection.Ascending)
                  ? ListSortDirection.Descending
                  : ListSortDirection.Ascending;

        var view = CollectionViewSource.GetDefaultView(ResultList.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(propName, dir));

        if (_lastSortHeader is not null)
            _lastSortHeader.Content = StripArrow(_lastSortHeader.Content?.ToString());
        header.Content = StripArrow(header.Content?.ToString()) +
                         (dir == ListSortDirection.Ascending ? "  ↑" : "  ↓");

        _lastSortHeader = header;
        _lastSortDir    = dir;
    }

    private static string? HeaderToSortProperty(string? header) =>
        StripArrow(header) switch
        {
            "Name"     => "FileName",
            "Location" => "Directory",
            "Size"     => "SizeBytes",
            "Modified" => "Modified",
            _ => null
        };

    private static string StripArrow(string? s) =>
        s?.TrimEnd(' ', '↑', '↓') ?? string.Empty;
}
