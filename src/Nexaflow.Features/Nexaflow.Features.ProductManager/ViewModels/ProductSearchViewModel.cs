using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.Services;
using Nexaflow.Search;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// One row of a graph search: a node whose name matched, or a line of source that did.
/// </summary>
/// <param name="NodeId">The graph node id — what every drill-in takes.</param>
/// <param name="Kind">The node's type (<c>product</c> / <c>type</c> / <c>member</c> / <c>file</c> / …).</param>
/// <param name="Detail">Where it is: the node id for a name hit, <c>path:line</c> for a source hit.</param>
/// <param name="Text">The matched source line, or the node id when the name is what matched.</param>
/// <param name="RelativePath">Repo-relative source file, or null for a node with no file behind it.</param>
public sealed record ProductSearchRow(
    string NodeId, string Label, string Kind, string Detail, string Text,
    string? RelativePath, int Line, bool IsSourceHit)
{
    /// <summary>True when this row is a product-tree node — the ones the sunburst can focus.</summary>
    public bool IsProductNode => Kind == NodeType.Product;

    /// <summary>What the "Open" button does, spelled out — the two behaviours are different enough that a
    /// button reading just "Open" on both would be a lie about one of them.</summary>
    public string OpenLabel => IsProductNode ? "Open in tree" : RelativePath is null ? "" : "Open file";

    public bool CanOpen => IsProductNode || RelativePath is not null;
}

/// <summary>
/// The graph search results page: one query, run over the <em>whole</em> knowledge graph — node names and
/// the source behind them — with each result openable where it actually lives.
/// <para>
/// It is a page and not a filtered list on the Product tab because the graph is not what that tab shows.
/// The sunburst shows the product tree; the graph is the tree crossed with the whole repo, so a result may
/// be a feature, a type, a file or a line of code — three of which the sunburst has nowhere to put. A page
/// of results can hold all four and hand each one to whatever does know how to show it.
/// </para>
/// <para>
/// Two matching passes, kept apart on purpose. <b>Name</b> matches come from the graph's own ranked search
/// (exact label, then prefix, then substring), and <b>source</b> matches from a grep across every code node
/// — the second is the expensive half and the reason the whole thing runs off the UI thread.
/// </para>
/// </summary>
public sealed partial class ProductSearchViewModel : ObservableObject, IPageViewModel
{
    /// <summary>Rows kept per pass. A search that matched a common word can hit thousands of lines; the
    /// page says how many it stopped at rather than pretending the cap was the answer.</summary>
    private const int PassCap = 200;

    private readonly IShellServices _shell;
    private readonly string _productRoot;

    public ProductSearchViewModel(string productRoot, string query, IShellServices shell)
    {
        _productRoot = productRoot;
        _shell       = shell;
        Query        = query ?? string.Empty;
    }

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _statusText = string.Empty;

    public ObservableCollection<ProductSearchRow> Results { get; } = [];

    /// <summary>The graph the results came from. Loaded once and reused, so a second query from this page
    /// costs a grep and not a re-read of the whole file.</summary>
    private KnowledgeGraph? _graph;

    public string ProductRoot => _productRoot;

    /// <summary>
    /// Runs whatever is in <see cref="Query"/> — what the page's own box does, and what a query handed in
    /// through the tab parameter arrives as.
    /// <para>
    /// Parsed with the shared syntax rather than taken as literal text, so <c>/pattern/</c> means here what
    /// it means in the AI bar. Without that a regex typed at the Product tab would reach this page as the
    /// four characters of its own delimiters.
    /// </para>
    /// </summary>
    public Task SearchAsync(CancellationToken ct = default) =>
        RunAndShowAsync(SearchSyntax.Parse(Query.Trim()), ct);

    /// <summary>
    /// Runs <paramref name="request"/> and fills <see cref="Results"/>, returning the rows shown. The graph
    /// read and both matching passes happen off the UI thread; only the rows come back.
    /// </summary>
    private async Task<IReadOnlyList<ProductSearchRow>> RunAndShowAsync(
        SearchRequest request, CancellationToken ct)
    {
        if (IsSearching) return Results.ToList();
        IsSearching = true;
        StatusText  = "Searching…";
        try
        {
            if (request.Text.Trim().Length == 0)
            {
                Results.Clear();
                StatusText = "Type something to search for.";
                return [];
            }

            var root = _productRoot;
            var found = await Task.Run(() => Run(root, request, ct), ct).ConfigureAwait(true);

            Results.Clear();
            foreach (var row in found.Rows) Results.Add(row);
            StatusText = found.Status;
            return found.Rows;
        }
        catch (OperationCanceledException) { StatusText = "Search cancelled."; return []; }
        catch (Exception ex)               { StatusText = $"Couldn't search the graph: {ex.Message}"; return []; }
        finally
        {
            IsSearching = false;
            IsLoaded    = true;
        }
    }

    private sealed record Found(IReadOnlyList<ProductSearchRow> Rows, string Status);

    /// <summary>Pure: loads (or reuses) the graph and runs both passes. No UI state is touched here, which
    /// is what lets the whole thing sit inside a <c>Task.Run</c>.</summary>
    private Found Run(string root, SearchRequest request, CancellationToken ct)
    {
        _graph ??= new ProductStore(root).LoadGraph();
        if (_graph is not { } graph)
            return new Found([], "No knowledge graph has been built yet — generate it from the Product tab "
                               + "(⋮ → Generate graph).");

        if (GraphTextSearch.TryCompile(request) is not { } regex)
            return new Found([], $"'{request.Text}' isn't a valid regular expression.");

        ct.ThrowIfCancellationRequested();

        var byName = GraphTextSearch.Names(graph, request, regex);
        var rows = new List<ProductSearchRow>();
        foreach (var node in byName.Take(PassCap))
            rows.Add(NameRow(node));

        ct.ThrowIfCancellationRequested();

        var inSource = GraphTextSearch.InSource(graph, request, regex, Reader(root), PassCap);
        foreach (var hit in inSource)
            rows.Add(SourceRow(hit));

        return new Found(rows, Status(byName.Count, inSource.Count));
    }

    private static string Status(int names, int lines)
    {
        var sb = new StringBuilder();
        sb.Append(names == 0 ? "No node names matched" : $"{names:N0} node name(s)");
        sb.Append(names > PassCap ? $" (showing the first {PassCap})" : "");
        sb.Append(" · ");
        sb.Append(lines == 0 ? "no source lines matched" : $"{lines:N0} source line(s)");
        sb.Append(lines >= PassCap ? $" (stopped at {PassCap} — there may be more)" : "");
        return sb.ToString();
    }

    private static ProductSearchRow NameRow(GraphNode node) => new(
        node.Id,
        node.Label ?? node.Id,
        node.Type,
        node.Id,
        node.Id,
        node.FilePath,
        Line(node),
        IsSourceHit: false);

    private static ProductSearchRow SourceRow(GraphQuery.GrepHit hit) => new(
        hit.Node.Id,
        hit.Node.Label ?? hit.Node.Id,
        hit.Node.Type,
        $"{hit.Node.FilePath}:{hit.Line}",
        hit.Text.Trim(),
        hit.Node.FilePath,
        hit.Line,
        IsSourceHit: true);

    private static int Line(GraphNode node) =>
        node.Metadata?.GetValueOrDefault("line") is { } t && int.TryParse(t, out var n) ? n : 0;

    // ── Drill-in ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the result where it lives: a product node focuses the sunburst on it, anything with a file
    /// behind it goes to whatever normally opens that file.
    /// <para>
    /// Two behaviours behind one button because there is exactly one obvious destination per row, and asking
    /// the user to pick between "tree" and "file" for a row that only has one of them would be a menu with a
    /// single live entry. The button says which it is.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Open(ProductSearchRow? row)
    {
        if (row is null) return;

        if (row.IsProductNode)
        {
            _shell.OpenTab(ProductManagerTabRegistration.StaticPageKind, new Dictionary<string, string>
            {
                ["path"] = _productRoot,
                ["node"] = row.NodeId,
            });
            return;
        }

        if (row.RelativePath is not { Length: > 0 } rel) return;
        _shell.HandleObject(Path.Combine(_productRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Opens the graph viewer with this node selected — the separate button, because "show me
    /// where this sits in the graph" is a different question from "take me to it".</summary>
    [RelayCommand]
    private void ShowInGraph(ProductSearchRow? row)
    {
        if (row is null) return;
        _shell.OpenTab(Graph.GraphViewerTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["path"] = new ProductStore(_productRoot).GraphFilePath,
            ["node"] = row.NodeId,
        });
    }

    /// <summary>Re-runs the query as edited in the page's own box.</summary>
    [RelayCommand]
    private Task Refresh() => SearchAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Source is read from the product root — the same reader the assistant's graph tools use, so
    /// a line shown here is the line they would quote.</summary>
    private static GraphQuery.ReadLines Reader(string root) => rel =>
    {
        try
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full)
                ? File.ReadAllText(full).Replace("\r\n", "\n").Split('\n')
                : null;
        }
        catch { return null; }
    };

    // ── IPageViewModel ────────────────────────────────────────────────────────

    /// <summary>Held until the search finishes, so the page is never pinned as "0 results" mid-run.</summary>
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded) return $"Graph search for \"{Query}\" — still running…";
        if (Results.Count == 0) return $"Graph search for \"{Query}\": no matches. {StatusText}";

        var sb = new StringBuilder();
        sb.Append($"Graph search for \"{Query}\" — {Results.Count} result(s). {StatusText}\n");
        foreach (var row in Results.Take(30))
            sb.Append($"  [{row.Kind}] {row.Label} — {row.Detail}\n");
        if (Results.Count > 30) sb.Append($"  … (+{Results.Count - 30} more)\n");
        sb.Append("(Use graph_context on a node id to read what it is and what owns it.)");
        return sb.ToString();
    }

    /// <summary>The product root is the scope these results came from.</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(_productRoot) ? null : _productRoot;
}
