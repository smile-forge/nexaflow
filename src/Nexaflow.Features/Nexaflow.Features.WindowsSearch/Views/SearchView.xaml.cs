using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using Nexaflow.Features.WindowsSearch.Services;

namespace Nexaflow.Features.WindowsSearch.Views;

public partial class SearchView : UserControl, IPageView
{
    private readonly SearchViewModel _vm;

    private GridViewColumnHeader? _lastSortHeader;
    private ListSortDirection     _lastSortDir = ListSortDirection.Ascending;

    private bool _searchStarted;

    public SearchView(SearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // ONCE, not on every Loaded. In a tabbed shell Loaded fires again every time the tab is switched
        // back to, so re-running here wiped the results the user came back to look at, restarted the query,
        // and left any scan already in flight writing into a list that had just been cleared.
        Loaded += async (_, _) =>
        {
            if (_searchStarted) return;
            _searchStarted = true;
            await _vm.RunSearchAsync(CancellationToken.None);
        };
    }

    // ── IPageView ────────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => _vm;

    // ── IPageView (Reinitialize) ──────────────────────────────────────────────

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        var q = pageParams.GetValueOrDefault("query", string.Empty);
        var r = pageParams.GetValueOrDefault("root",  string.Empty);
        if (q == _vm.SearchQuery && r == _vm.SearchRoot) return;
        _vm.SearchQuery = q;
        _vm.SearchRoot  = r;
        _ = _vm.RunSearchAsync(CancellationToken.None);
    }

    // ── Column sort ──────────────────────────────────────────────────────────

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header
            || header.Role == GridViewColumnHeaderRole.Padding)
            return;

        var propName = SearchResultSort.PropertyFor(header.Content?.ToString());
        if (propName is null) return;

        var ascending = SearchResultSort.NextAscending(
            header == _lastSortHeader, _lastSortDir == ListSortDirection.Ascending);
        var dir = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        var view = CollectionViewSource.GetDefaultView(ResultList.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(propName, dir));

        if (_lastSortHeader is not null)
            _lastSortHeader.Content = SearchResultSort.Strip(_lastSortHeader.Content?.ToString());
        header.Content = SearchResultSort.WithArrow(header.Content?.ToString(), ascending);

        _lastSortHeader = header;
        _lastSortDir    = dir;
    }
}
