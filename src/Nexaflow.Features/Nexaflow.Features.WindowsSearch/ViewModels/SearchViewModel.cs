using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsSearch.Services;
using System.Collections.ObjectModel;
using System.Data.OleDb;
using System.Diagnostics;
using System.Text.Json.Nodes;

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

    private readonly IShellServices      _shellServices;
    private readonly IReadOnlyList<string> _drives;
    private string _baseQuery  = string.Empty;
    private ParsedQuery? _lastParsed;
    private CancellationTokenSource? _cts;

    public SearchViewModel(string query, string root, IReadOnlyList<string> drives, IShellServices shellServices)
    {
        _searchQuery   = query;
        _searchRoot    = root;
        _drives        = drives;
        _shellServices = shellServices;
    }

    [RelayCommand]
    private async Task RunSearch(CancellationToken ct) => await RunSearchAsync(ct);

    public async Task RunSearchAsync(CancellationToken externalCt)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) ||
            (string.IsNullOrEmpty(SearchRoot) && _drives.Count == 0))
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
            var rl = string.IsNullOrEmpty(r)
                ? (_drives.Count > 0 ? "This PC" : "Search")
                : Path.GetFileName(r.TrimEnd('\\', '/'));
            Tab.Title = qs;
            Tab.PageParams = new() { ["query"] = q, ["root"] = r, ["drives"] = string.Join(";", _drives) };
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
            var entries = _drives.Count > 0 && string.IsNullOrEmpty(SearchRoot)
                ? await WindowsSearchService.SearchAcrossAsync(parsed, _drives, ct)
                : await WindowsSearchService.SearchAsync(parsed, SearchRoot, ct);
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
        // A This-PC search has an empty SearchRoot but searches every drive — keying "performed" off the
        // query (not the root) so a cross-drive search with results is no longer reported as "no search".
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return "Search tab: no search performed yet.";
        var scope = string.IsNullOrEmpty(SearchRoot)
            ? (_drives.Count > 0 ? "This PC (all drives)" : "no scope set")
            : $"'{SearchRoot}'";
        return $"Search tab: '{SearchQuery}' in {scope}. {ResultCount} result(s).";
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "search",
            "Search files using Windows Search. Supports plain terms (budget report), " +
            "quoted phrases (\"annual report\"), file globs (*.xml, *.doc*, name.*), " +
            "property filters (kind:document, size:>1mb, author:john, date:>2024-01, " +
            "modified:2023, before:2024, after:2023-06), and boolean prefix syntax " +
            "(+required -excluded).",
            [new ClientToolParameter("query", "The search query to run.")],
            ToolSafety.SafeOperation,
            (arguments, ct) =>
            {
                var query = arguments["query"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(query))
                    return Task.FromResult(ToolResult.Error("No query provided."));

                SearchQuery = query;
                _ = RunSearchAsync(CancellationToken.None);
                return Task.FromResult(ToolResult.Ok($"searching for {query}", $"Started search for '{query}'."));
            })
    ];

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
