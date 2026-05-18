using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nexaflow.Features.WindowsSearch.Views;

public partial class SearchView : UserControl, IPageView
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

    string IPageView.GetContext()
    {
        if (string.IsNullOrWhiteSpace(_vm.SearchQuery) || string.IsNullOrEmpty(_vm.SearchRoot))
            return $"Search tab: no search performed yet. Root: {(string.IsNullOrEmpty(_vm.SearchRoot) ? "not set" : $"'{_vm.SearchRoot}'")}";
        return $"Search tab: '{_vm.SearchQuery}' in '{_vm.SearchRoot}'. {_vm.ResultCount} result(s).";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions()
    {
        IReadOnlyDictionary<string, string> searchParams =
            new Dictionary<string, string> { { "Query", string.Empty } };

        return
        [
            new ActionDescriptor(
                "Search",
                "Search files using Windows Search. Supports plain terms (budget report), " +
                "quoted phrases (\"annual report\"), file globs (*.xml, *.doc*, name.*), " +
                "property filters (kind:document, size:>1mb, author:john, date:>2024-01, " +
                "modified:2023, before:2024, after:2023-06), and boolean prefix syntax " +
                "(+required -excluded).",
                searchParams)
        ];
    }

    void IPageView.Execute(ActionDescriptor action)
    {
        if (action.Name == "Search"
            && action.Parameters?.TryGetValue("Query", out var query) == true
            && !string.IsNullOrWhiteSpace(query))
        {
            _vm.SearchQuery = query;
            _ = _vm.RunSearchAsync(CancellationToken.None);
        }
    }

    IContext? IPageView.GetContextObject()
    {
        if (_vm.SelectedEntry is not { } entry) return null;

        if (entry.IsFolder)
            return new FileSystemContext
            {
                RootPath      = entry.FilePath,
                CurrentPath   = entry.FilePath,
                SelectedItems = []
            };

        var dir = Path.GetDirectoryName(entry.FilePath);
        if (string.IsNullOrEmpty(dir)) return null;

        return new FileSystemContext
        {
            RootPath      = dir,
            CurrentPath   = dir,
            SelectedItems = [entry.FilePath]
        };
    }

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
