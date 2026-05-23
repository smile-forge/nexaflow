using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.Services;
using System.Collections.ObjectModel;
using System.Data.OleDb;
using System.Diagnostics;

namespace Nexaflow.Features.WindowsSearch.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IPageViewModel
{
    [ObservableProperty] private string             _searchQuery  = string.Empty;
    [ObservableProperty] private string             _searchRoot   = string.Empty;
    [ObservableProperty] private bool               _isSearching;
    [ObservableProperty] private string             _statusText   = string.Empty;
    [ObservableProperty] private int                _resultCount;
    [ObservableProperty] private SearchResultEntry? _selectedEntry;
    [ObservableProperty] private bool               _hasSelection;

    partial void OnSelectedEntryChanged(SearchResultEntry? value)
    {
        HasSelection = value is not null;
        OpenLocationCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<SearchResultEntry> Results { get; } = [];

    /// <summary>Set by <see cref="SearchTabRegistration"/> so this VM can keep tab meta in sync.</summary>
    public Page? Tab { get; set; }

    private readonly IShellServices _shellServices;
    private string _baseQuery  = string.Empty;
    private ParsedQuery? _lastParsed;
    private CancellationTokenSource? _cts;

    public SearchViewModel(string query, string root, IShellServices shellServices)
    {
        _searchQuery   = query;
        _searchRoot    = root;
        _shellServices = shellServices;
    }

    [RelayCommand]
    private async Task RunSearch(CancellationToken ct) => await RunSearchAsync(ct);

    public async Task RunSearchAsync(CancellationToken externalCt)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || string.IsNullOrEmpty(SearchRoot))
        {
            StatusText = "Enter a search term.";
            return;
        }

        _baseQuery  = SearchQuery;
        _lastParsed = SearchQueryParser.Parse(SearchQuery);
        await ExecuteSearch(_lastParsed, externalCt);
    }

    /// <summary>Re-runs the last query (including any merged refinements) without re-parsing.</summary>
    public async Task RefreshAsync()
    {
        if (_lastParsed is not null)
            await ExecuteSearch(_lastParsed, CancellationToken.None);
        else
            await RunSearchAsync(CancellationToken.None);
    }

    /// <summary>
    /// Merges <paramref name="refinement"/> with the original query using AND and
    /// re-queries Windows Search. Does not filter client-side.
    /// </summary>
    public async Task MergeAndSearchAsync(string refinement)
    {
        var merged = SearchQueryParser.Merge(
            SearchQueryParser.Parse(_baseQuery),
            SearchQueryParser.Parse(refinement));
        SearchQuery = merged.RawInput;
        _lastParsed = merged;
        await ExecuteSearch(merged, CancellationToken.None);

        if (Tab is not null)
        {
            var q  = SearchQuery;
            var r  = SearchRoot;
            var qs = q.Length > 12 ? q[..12] + "…" : q;
            var rl = string.IsNullOrEmpty(r) ? "Search" : Path.GetFileName(r.TrimEnd('\\', '/'));
            Tab.Title = qs;
            Tab.PageParams = new() { ["query"] = q, ["root"] = r };
            Tab.Breadcrumbs.Clear();
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = rl });
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = $"Query : {qs}" });
        }
    }

    private async Task ExecuteSearch(ParsedQuery parsed, CancellationToken externalCt)
    {
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        IsSearching = true;
        StatusText  = "Searching…";
        Results.Clear();
        ResultCount = 0;

        try
        {
            var entries = await WindowsSearchService.SearchAsync(parsed, SearchRoot, ct);
            ct.ThrowIfCancellationRequested();

            foreach (var e in entries) Results.Add(e);
            ResultCount = Results.Count;
            StatusText  = ResultCount == 0
                ? "No results."
                : $"{ResultCount} result{(ResultCount == 1 ? "" : "s")}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (OleDbException ex)
        {
            StatusText  = "Windows Search service unavailable.";
            ResultCount = 0;
            Debug.WriteLine($"[WindowsSearch] OleDbException: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText  = $"Search error: {ex.Message}";
            ResultCount = 0;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenLocation()
    {
        if (SelectedEntry is null) return;
        var dir = Path.GetDirectoryName(SelectedEntry.FilePath);
        if (string.IsNullOrEmpty(dir)) return;
        _shellServices.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = dir,
            ["label"] = Path.GetFileName(dir.TrimEnd('\\', '/'))
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFile()
    {
        if (SelectedEntry is null) return;
        try { Process.Start(new ProcessStartInfo(SelectedEntry.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[SearchView] Open file: {ex.Message}"); }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || string.IsNullOrEmpty(SearchRoot))
            return $"Search tab: no search performed yet. Root: {(string.IsNullOrEmpty(SearchRoot) ? "not set" : $"'{SearchRoot}'")}";
        return $"Search tab: '{SearchQuery}' in '{SearchRoot}'. {ResultCount} result(s).";
    }

    public IReadOnlyList<ActionDescriptor> GetAvailableActions()
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

    public void Execute(ActionDescriptor action)
    {
        if (action.Name == "Search"
            && action.Parameters?.TryGetValue("Query", out var query) == true
            && !string.IsNullOrWhiteSpace(query))
        {
            SearchQuery = query;
            _ = RunSearchAsync(CancellationToken.None);
        }
    }

    public IContext? GetContextObject()
    {
        if (SelectedEntry is not { } entry) return null;

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
}
