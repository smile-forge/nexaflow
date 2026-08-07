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
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Features.ProductManager.Graph.Converters;
using Nexaflow.Features.ProductManager.Graph.Layout;
using Nexaflow.Features.ProductManager.Graph.Loaders;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Features.ProductManager.Graph.ViewModels;

/// <summary>
/// The Graph viewer page. Loads a <c>graph.json</c> off the UI thread, then shows a <b>focused neighbourhood</b>:
/// only the nodes within <see cref="HopRadius"/> edges of <see cref="FocusNodeId"/> (a whole-repo graph is far
/// too large to show at once). It starts focused on the product root; selecting a node re-centres on it. Layout
/// is radial by hop distance and recomputed each focus change — a pure view concern (positions are never stored).
/// </summary>
public sealed partial class GraphViewModel : ObservableObject, IPageViewModel
{
    private readonly GraphLoader _loader;
    private readonly IShellServices? _shell;

    private readonly Dictionary<string, GraphNodeViewModel> _byId = new(StringComparer.Ordinal);
    private readonly List<GraphNodeViewModel> _allNodes = [];
    private readonly List<GraphEdge> _allEdges = [];
    private readonly List<GraphHyperEdge> _allHyperEdges = [];
    private readonly Dictionary<string, List<string>> _adjacency = new(StringComparer.Ordinal);
    private GraphNodeViewModel? _rootNode;
    private CancellationTokenSource? _layoutCts;

    private readonly HashSet<int> _hiddenCommunities = [];   // communities the user toggled off (survives re-focus)
    private Dictionary<string, int> _dist = new(StringComparer.Ordinal);   // last BFS neighbourhood (focus → hop distance)
    private bool _suppressToggle;   // guards the rail-row callback while we set IsVisible programmatically

    /// <summary>Hard ceiling on realized nodes — a very deep neighbourhood keeps the closest ones (by hop, then
    /// kind) and reports the rest via <see cref="CapNote"/> rather than choking the canvas with thousands.</summary>
    private const int MaxRealized = 700;
    private double _viewScale = 1.0;   // last zoom the view reported, for the LOD pass

    public GraphViewModel(string filePath, GraphLoader? loader = null, IShellServices? shell = null,
                          string? focusNodeId = null)
    {
        FilePath = filePath;
        _loader = loader ?? new GraphLoader();
        _shell = shell;
        _initialFocusId = focusNodeId;
    }

    /// <summary>Node to land on instead of the root — a deep link from the graph search results.</summary>
    private readonly string? _initialFocusId;

    /// <summary>
    /// Selects <paramref name="id"/> if the graph holds it, focusing its neighbourhood. False when it does
    /// not — the caller can then say so rather than leaving the viewer sitting on an unrelated node.
    /// </summary>
    public bool SelectById(string? id)
    {
        if (id is not { Length: > 0 } || !_byId.TryGetValue(id, out var vm)) return false;
        SelectedNode = vm;
        return true;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The currently-visible neighbourhood (the bound collections — never the full graph).</summary>
    public ObservableCollection<GraphNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> Edges { get; } = [];

    /// <summary>The n-ary hyperedges (signature / annotated / calls) whose endpoints are all currently visible.</summary>
    public ObservableCollection<GraphHyperEdgeViewModel> HyperEdges { get; } = [];

    /// <summary>The Segments rail: one row per community present in the current neighbourhood. Toggling a row
    /// hides/shows that community's nodes without disturbing the rest.</summary>
    public ObservableCollection<CommunitySegmentViewModel> Communities { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowNodePlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(OpenInCodeCommand))]
    private GraphNodeViewModel? _selectedNode;

    public bool HasSelection => SelectedNode is not null;
    public bool ShowNodePlaceholder => SelectedNode is null;

    /// <summary>The node the neighbourhood is centred on. Driven by selection.</summary>
    [ObservableProperty] private string? _focusNodeId;

    /// <summary>How many edges out from the focus node to show (1–10). A shallow default keeps the start view
    /// (and each drill-in) readable; slide it up to explore deeper.</summary>
    [ObservableProperty] private int _hopRadius = 2;

    [ObservableProperty] private string _focusLabel = string.Empty;

    /// <summary>Hide edges below this confidence (0 = show all; raise it to drop weak inferred links).</summary>
    [ObservableProperty] private double _minConfidence;

    /// <summary>True when the neighbourhood has any coloured communities.</summary>
    [ObservableProperty] private bool _hasCommunities;

    /// <summary>True when the graph has any hyperedges at all (the Hyperedges toggle is shown).</summary>
    [ObservableProperty] private bool _hasHyperEdges;

    /// <summary>Whether n-ary hyperedges (signature / annotated / calls) are drawn.</summary>
    [ObservableProperty] private bool _showHyperEdges = true;

    /// <summary>True when the left rail has anything to show (communities and/or a hyperedge toggle).</summary>
    [ObservableProperty] private bool _showSegmentsRail;

    /// <summary>Whether the Segments rail is expanded (the list of communities) or collapsed to its header.</summary>
    [ObservableProperty] private bool _isSegmentsRailExpanded = true;

    /// <summary>How many in-range nodes were dropped by the realized-node cap (0 when everything fits).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapNote))]
    private int _hiddenByCap;

    public string CapNote => HiddenByCap > 0 ? $"+{HiddenByCap:N0} more (lower depth or focus closer)" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>Raised after a neighbourhood rebuild. The bool is <c>true</c> for a fresh view (the viewer fits to
    /// it) and <c>false</c> for an incremental expand (keep the transform; only the new nodes glide in).</summary>
    public event Action<bool>? LayoutChanged;

    private readonly Stack<string> _history = new();
    private bool _navigatingBack;
    private bool _resetView;   // Root/Back force a fresh, re-centred layout even if the target is already visible

    /// <summary>Reads the graph off the UI thread, indexes it, and focuses the product root.</summary>
    public async Task LoadAsync()
    {
        if (IsLoaded) return;
        try
        {
            var graph = await Task.Run(() => _loader.Load(FilePath)).ConfigureAwait(true);

            foreach (var n in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                var vm = new GraphNodeViewModel(n);
                if (_byId.TryAdd(n.Id, vm)) _allNodes.Add(vm);
            }
            foreach (var e in graph.Edges)
                if (_byId.ContainsKey(e.Source) && _byId.ContainsKey(e.Target))
                {
                    _allEdges.Add(e);
                    Connect(e.Source, e.Target);
                }
            foreach (var h in graph.HyperEdges)              // hyperedges contribute to reachability + are drawn
            {
                if (h.Endpoints.All(e => _byId.ContainsKey(e.Node))) _allHyperEdges.Add(h);
                for (var i = 0; i < h.Endpoints.Count; i++)
                    for (var j = i + 1; j < h.Endpoints.Count; j++)
                        Connect(h.Endpoints[i].Node, h.Endpoints[j].Node);
            }
            HasHyperEdges = _allHyperEdges.Count > 0;

            Summary = $"{graph.Nodes.Count:N0} nodes · {graph.Edges.Count:N0} edges · {graph.HyperEdges.Count:N0} hyperedges";

            _rootNode = PickRoot();
            if (_rootNode is null)
            {
                HasError = true;
                ErrorMessage = "This graph is empty. Regenerate it from the Product tab (⋮ → Generate graph).";
            }
            else
            {
                // A deep link lands on the node that was asked for; everything else starts at the root.
                // Selecting drives the focus + neighbourhood (see OnSelectedNodeChanged).
                if (!SelectById(_initialFocusId)) SelectedNode = _rootNode;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Couldn't open this graph: {ex.Message}";
        }
        finally
        {
            IsLoaded = true;
        }
    }

    private void Connect(string a, string b)
    {
        if (!_adjacency.TryGetValue(a, out var la)) _adjacency[a] = la = [];
        la.Add(b);
        if (!_adjacency.TryGetValue(b, out var lb)) _adjacency[b] = lb = [];
        lb.Add(a);
    }

    /// <summary>The product-tree root to open on: a product node no other product node contains, preferring the
    /// one with the most descendants (so a forest of top-level groups opens on its biggest tree / the super-root).</summary>
    private GraphNodeViewModel? PickRoot()
    {
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var contained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _allEdges)
            if (e.Relationship == EdgeRelationship.Contains)
            {
                if (!childrenOf.TryGetValue(e.Source, out var list)) childrenOf[e.Source] = list = [];
                list.Add(e.Target);
                contained.Add(e.Target);
            }

        var roots = _allNodes.Where(n => n.Kind == NodeType.Product && !contained.Contains(n.Id)).ToList();
        if (roots.Count == 0) return _allNodes.FirstOrDefault(n => n.Kind == NodeType.Product) ?? _allNodes.FirstOrDefault();
        if (roots.Count == 1) return roots[0];
        return roots.OrderByDescending(r => Reach(r.Id, childrenOf)).ThenBy(r => r.Id, StringComparer.Ordinal).First();
    }

    private static int Reach(string start, IReadOnlyDictionary<string, List<string>> childrenOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (!childrenOf.TryGetValue(u, out var children)) continue;
            foreach (var c in children) if (seen.Add(c)) queue.Enqueue(c);
        }
        return seen.Count;
    }

    partial void OnSelectedNodeChanged(GraphNodeViewModel? value)
    {
        if (value is null) return;
        if (!_navigatingBack && FocusNodeId is not null && FocusNodeId != value.Id)
        {
            _history.Push(FocusNodeId);
            BackCommand.NotifyCanExecuteChanged();
        }
        FocusNodeId = value.Id;
    }

    partial void OnFocusNodeIdChanged(string? value) => RebuildVisible();
    partial void OnHopRadiusChanged(int value) => RebuildVisible();
    partial void OnMinConfidenceChanged(double value) => RebuildVisible();

    /// <summary>Recomputes the visible neighbourhood: BFS from the focus out to <see cref="HopRadius"/>, refreshes
    /// the Segments rail, then realises it. A <b>fresh</b> view (the focus wasn't showing) is laid out from the centre
    /// and fit; an <b>incremental</b> expand keeps every already-visible node pinned and grows the new ones out of the focus.</summary>
    private void RebuildVisible()
    {
        if (FocusNodeId is null || !_byId.ContainsKey(FocusNodeId))
        {
            _layoutCts?.Cancel();
            Nodes.Clear(); Edges.Clear(); Communities.Clear(); HasCommunities = false;
            return;
        }

        _dist = Bfs(FocusNodeId, HopRadius);
        SyncCommunityRail(_dist);
        RealizeNeighbourhood();
    }

    /// <summary>Realises <see cref="_dist"/> into the bound collections, gated by the Segments rail: a node in a
    /// hidden community drops out (the focus is always kept). Fresh → laid out from the centre and fit; otherwise
    /// survivors stay pinned and only the delta (revealed / re-shown nodes) glides in.</summary>
    private void RealizeNeighbourhood()
    {
        _layoutCts?.Cancel();
        if (FocusNodeId is null || !_byId.ContainsKey(FocusNodeId)) { Nodes.Clear(); Edges.Clear(); return; }

        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _dist.Keys)
        {
            if (id != FocusNodeId && _byId.TryGetValue(id, out var vc) && vc.Community is int c && _hiddenCommunities.Contains(c))
                continue;   // community toggled off (but never hide the focus itself)
            visible.Add(id);
        }

        // Cap: a very deep neighbourhood keeps the closest MaxRealized (by hop, then kind importance) so the canvas
        // never realizes thousands of containers. The rest are reported via CapNote — never silently dropped.
        HiddenByCap = 0;
        if (visible.Count > MaxRealized)
        {
            var kept = visible
                .OrderBy(id => _dist.TryGetValue(id, out var d) ? d : int.MaxValue)
                .ThenBy(id => _byId.TryGetValue(id, out var vm) ? KindRank(vm.Kind) : 9)
                .ThenBy(id => id, StringComparer.Ordinal)
                .Take(MaxRealized)
                .ToHashSet(StringComparer.Ordinal);
            kept.Add(FocusNodeId);   // the focus is never capped out
            HiddenByCap = visible.Count - kept.Count;
            visible = kept;
        }

        var focusVm = _byId[FocusNodeId];
        var fresh = _resetView || Nodes.All(v => v.Id != FocusNodeId);
        _resetView = false;
        double fpx = fresh ? 0 : focusVm.X, fpy = fresh ? 0 : focusVm.Y;

        if (fresh) Nodes.Clear();
        else for (var i = Nodes.Count - 1; i >= 0; i--) if (!visible.Contains(Nodes[i].Id)) Nodes.RemoveAt(i);

        foreach (var v in Nodes) v.IsNew = false;   // survivors are not new
        var present = new HashSet<string>(Nodes.Select(v => v.Id), StringComparer.Ordinal);
        foreach (var id in visible.OrderBy(x => x, StringComparer.Ordinal))
            if (!present.Contains(id) && _byId.TryGetValue(id, out var vm))
            {
                vm.IsNew = true;
                vm.SnapTo(fpx, fpy);   // new nodes start at the focus, then grow out to their target
                Nodes.Add(vm);
            }

        Edges.Clear();
        foreach (var e in _allEdges)
            if (e.Confidence >= MinConfidence
                && visible.Contains(e.Source) && visible.Contains(e.Target)
                && _byId.TryGetValue(e.Source, out var a) && _byId.TryGetValue(e.Target, out var b))
                Edges.Add(new GraphEdgeViewModel(e, a, b));

        HyperEdges.Clear();
        if (ShowHyperEdges)
            foreach (var h in _allHyperEdges)
                if (h.Confidence >= MinConfidence && h.Endpoints.All(ep => visible.Contains(ep.Node)))
                    HyperEdges.Add(new GraphHyperEdgeViewModel(h,
                        h.Endpoints.Select(ep => new HyperSpokeViewModel(_byId[ep.Node], ep.Role)).ToList()));

        RefreshLod();   // apply the current zoom's LOD to the freshly-realized set

        FocusLabel = focusVm.Label;
        if (fresh)
        {
            FocusLayout.Apply(Nodes, _dist);   // immediate radial spread so the fit frames the destination
            LayoutChanged?.Invoke(true);
        }
        _ = RunLayoutAsync(_dist, fresh);
    }

    /// <summary>The view reports its zoom here (throttled to actual changes); the 3-tier LOD hides node kinds below
    /// their <see cref="GraphNodeViewModel.MinScale"/> and any edge/hyperedge that would dangle from a hidden node.</summary>
    public void ApplyLod(double scale)
    {
        _viewScale = scale;
        RefreshLod();
    }

    private void RefreshLod()
    {
        foreach (var n in Nodes) n.LodVisible = _viewScale >= n.MinScale;
        foreach (var e in Edges) e.LodVisible = e.From.LodVisible && e.To.LodVisible;
        foreach (var h in HyperEdges) h.LodVisible = h.Spokes.All(s => s.Node.LodVisible);
    }

    /// <summary>Rebuilds the Segments rail from the neighbourhood: one row per community present, labelled by its most
    /// representative node (nearest the focus; product &gt; file &gt; type &gt; member on ties), counted, and coloured to
    /// match the node fill. Hidden-state lives in <see cref="_hiddenCommunities"/> (the source of truth) so a rebuild
    /// never fires a toggle — a community the user hid stays hidden as they explore.</summary>
    private void SyncCommunityRail(IReadOnlyDictionary<string, int> dist)
    {
        var groups = new Dictionary<int, (int Count, string Label, int Rank, int Dist)>();
        foreach (var (id, d) in dist)
        {
            if (!_byId.TryGetValue(id, out var vm) || vm.Community is not int c) continue;
            var rank = KindRank(vm.Kind);
            if (!groups.TryGetValue(c, out var g)) { groups[c] = (1, vm.Label, rank, d); continue; }
            var better = d < g.Dist || (d == g.Dist && rank < g.Rank);
            groups[c] = (g.Count + 1, better ? vm.Label : g.Label, better ? rank : g.Rank, better ? d : g.Dist);
        }

        Communities.Clear();
        foreach (var kv in groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key))
            Communities.Add(new CommunitySegmentViewModel(
                kv.Key, kv.Value.Label, kv.Value.Count, CommunityBrushConverter.ForCommunity(kv.Key),
                isVisible: !_hiddenCommunities.Contains(kv.Key), onToggle: OnCommunityToggled));

        HasCommunities = Communities.Count > 0;
        ShowSegmentsRail = HasCommunities || HasHyperEdges;
    }

    partial void OnShowHyperEdgesChanged(bool value) => RealizeNeighbourhood();

    private static int KindRank(string kind) => kind switch
    {
        NodeType.Product => 0,
        NodeType.File => 1,
        NodeType.Type => 2,
        _ => 3,
    };

    /// <summary>A rail row was toggled → update the hidden set and re-realise incrementally (survivors stay put; the
    /// community's nodes fade out / grow back). The rail is not rebuilt, so the clicked row survives.</summary>
    private void OnCommunityToggled(CommunitySegmentViewModel seg)
    {
        if (_suppressToggle) return;
        if (seg.IsVisible) _hiddenCommunities.Remove(seg.Id); else _hiddenCommunities.Add(seg.Id);
        RealizeNeighbourhood();
    }

    /// <summary>Show every community again (clears all hides).</summary>
    [RelayCommand]
    private void ShowAllCommunities()
    {
        if (_hiddenCommunities.Count == 0) return;
        _hiddenCommunities.Clear();
        _suppressToggle = true;
        foreach (var c in Communities) c.IsVisible = true;
        _suppressToggle = false;
        RealizeNeighbourhood();
    }

    private Dictionary<string, int> Bfs(string start, int radius)
    {
        var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (dist[u] >= radius) continue;
            if (!_adjacency.TryGetValue(u, out var neighbours)) continue;
            foreach (var v in neighbours)
                if (!dist.ContainsKey(v)) { dist[v] = dist[u] + 1; queue.Enqueue(v); }
        }
        return dist;
    }

    /// <summary>Runs the hybrid force layout off the UI thread (fresh → only the focus is pinned; incremental →
    /// every already-visible node is pinned in place), then publishes the target positions for the view to ease into.</summary>
    private async Task RunLayoutAsync(Dictionary<string, int> dist, bool fresh)
    {
        var cts = new CancellationTokenSource();
        _layoutCts = cts;
        var focus = FocusNodeId;
        if (focus is null) return;

        var layoutNodes = new List<LayoutNode>(Nodes.Count);
        foreach (var v in Nodes)
        {
            var product = v.Kind == NodeType.Product;
            if (fresh)
                layoutNodes.Add(v.Id == focus ? new LayoutNode(v.Id, product, v.X, v.Y) : new LayoutNode(v.Id, product));
            else
                layoutNodes.Add(v.IsNew ? new LayoutNode(v.Id, product) : new LayoutNode(v.Id, product, v.X, v.Y));
        }
        var layoutEdges = Edges.Select(e => (e.From.Id, e.To.Id)).ToList();

        try
        {
            var positions = await Task.Run(
                () => HybridLayout.Compute(layoutNodes, layoutEdges, dist, cts.Token), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;

            foreach (var v in Nodes)
                if (positions.TryGetValue(v.Id, out var p)) { v.Tx = p.X; v.Ty = p.Y; }
            LayoutChanged?.Invoke(fresh);
        }
        catch (OperationCanceledException) { /* superseded by a newer focus */ }
    }

    /// <summary>Re-centre on the product-tree root (a fresh, centred layout).</summary>
    [RelayCommand]
    private void ResetFocus()
    {
        if (_rootNode is null) return;
        _resetView = true;
        if (SelectedNode?.Id == _rootNode.Id) RebuildVisible();   // already selected → re-centre directly
        else SelectedNode = _rootNode;
    }

    /// <summary>Navigate back to the previously-focused node.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (_history.Count == 0) return;
        var prev = _history.Pop();
        _navigatingBack = true;
        _resetView = true;   // re-centre on the node we're returning to
        if (_byId.TryGetValue(prev, out var vm)) SelectedNode = vm;
        else FocusNodeId = prev;
        _navigatingBack = false;
        BackCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoBack => _history.Count > 0;

    /// <summary>Opens the selected node's backing source in the Code (or Markdown) viewer, deep-linking to its
    /// AST path when known — reusing the shell's existing code-navigation route.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInCode()
    {
        if (_shell is null || SelectedNode is not { HasFile: true } node) return;
        var root = RepoRoot();
        if (root is null) return;

        var abs = Path.Combine(root, node.FilePath!.Replace('/', Path.DirectorySeparatorChar));
        var isMarkdown = abs.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                         || abs.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        var p = new Dictionary<string, string> { ["path"] = abs };
        if (!isMarkdown && node.Ast is { Length: > 0 } ast) p["ast"] = ast;
        _shell.OpenTab(isMarkdown ? "Markdown" : "Code", p);
    }

    /// <summary>The product root — <c>graph.json</c> lives at <c>&lt;root&gt;/.product/graph.json</c>.</summary>
    private string? RepoRoot()
    {
        var dotProduct = Path.GetDirectoryName(FilePath);   // <root>/.product
        return dotProduct is null ? null : Path.GetDirectoryName(dotProduct);
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────────────────────────
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded) return $"Knowledge graph \"{FileName}\": still loading…";
        if (HasError) return $"Knowledge graph \"{FileName}\": failed to load — {ErrorMessage}";
        return $"Knowledge graph \"{FileName}\": {Summary}. Showing the {HopRadius}-hop neighbourhood of \"{FocusLabel}\". " +
               "Product nodes, code files, types and members with their relationships.";
    }

    /// <summary>
    /// The product folder this graph was built from — <c>&lt;root&gt;/.product/graph.json</c>, so two levels
    /// up. Deliberately *not* the graph.json path: the sunburst, the integrity page and this canvas are three
    /// views of one tree, and scoping them together is what lets a conversation pin two of them without the
    /// shared tools collapsing first-wins across what would look like different datasets.
    /// </summary>
    public string ProductRoot =>
        Path.GetDirectoryName(Path.GetDirectoryName(FilePath)) ?? Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string? GetSecurityContext() => string.IsNullOrEmpty(ProductRoot) ? FilePath : ProductRoot;

    /// <summary>
    /// The whole product surface, plus the two tools that are about *this view* rather than the model.
    /// <para>
    /// The split is the useful one: <see cref="ProductTools.ForRoot"/> acts on the tree on disk and is
    /// identical on every Product view, because all three are views of the same data. <c>read_graph</c> and
    /// <c>focus_node</c> act on what is rendered here — which node is centred and what is visible — which is
    /// the part that genuinely differs per view.
    /// </para>
    /// <para>
    /// Both view tools are <see cref="ToolSafety.SafeOperation"/>: <c>read_graph</c> is a pure observer of the
    /// in-memory neighbourhood, and <c>focus_node</c> only re-centres the view (a camera/selection move,
    /// nothing committed to disk). Focusing mutates the UI-bound node/edge collections, so it is marshalled to
    /// the UI thread via <see cref="IShellServices.RunOnUiAsync(Action)"/> — a feature never touches the dispatcher.
    /// </para>
    /// </summary>
    public IReadOnlyList<IClientTool> GetClientTools() =>
        [.. ProductTools.ForRoot(ProductRoot), .. ViewTools()];

    private IReadOnlyList<IClientTool> ViewTools() =>
    [
        new DelegateClientTool(
            "read_graph",
            "Read the currently-visible graph neighbourhood: the focused node, the whole-graph summary, and the "
          + "visible neighbour nodes with their kind and relationship to the focus. Pure read of what is on screen.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (!IsLoaded) return Task.FromResult(ToolResult.Error("The graph is still loading."));
                if (HasError) return Task.FromResult(ToolResult.Error($"The graph failed to load: {ErrorMessage}"));
                if (FocusNodeId is null || !_byId.TryGetValue(FocusNodeId, out var focus))
                    return Task.FromResult(ToolResult.Ok(
                        "no focus", $"Knowledge graph '{FileName}': {Summary}. No node is focused."));

                var sb = new StringBuilder();
                sb.Append($"Knowledge graph '{FileName}' — {Summary}.\n");
                sb.Append($"Focused on [{focus.Kind}] \"{focus.Label}\" ({focus.Id}), showing the {HopRadius}-hop neighbourhood.\n");
                sb.Append($"{Nodes.Count} node(s) and {Edges.Count} edge(s) visible");
                if (ShowHyperEdges && HyperEdges.Count > 0) sb.Append($", {HyperEdges.Count} hyperedge(s)");
                sb.Append('.');
                if (HiddenByCap > 0) sb.Append($" ({HiddenByCap:N0} more beyond the display cap.)");

                var neighbours = Nodes
                    .Where(n => n.Id != FocusNodeId)
                    .OrderBy(n => _dist.TryGetValue(n.Id, out var d) ? d : int.MaxValue)
                    .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (neighbours.Count == 0)
                {
                    sb.Append("\nNo neighbour nodes are visible.");
                    return Task.FromResult(ToolResult.Ok($"focused on {focus.Label}, no neighbours", sb.ToString()));
                }

                sb.Append($"\nNeighbours ({neighbours.Count}):");
                foreach (var n in neighbours)
                {
                    var hop = _dist.TryGetValue(n.Id, out var d) ? d : -1;
                    sb.Append($"\n  [{n.Kind}] {n.Label} ({n.Id}) — {DescribeRelationToFocus(FocusNodeId, n.Id, hop)}");
                }

                return Task.FromResult(ToolResult.Ok(
                    $"{neighbours.Count} neighbour(s) of {focus.Label}", sb.ToString()));
            },
            parallelizable: true),

        new DelegateClientTool(
            "focus_node",
            "Re-centre the graph on a node, matched by exact id first then by a case-insensitive label substring. "
          + "The neighbourhood is recomputed around the new focus.",
            [
                new ClientToolParameter("node", "Node id (e.g. 'product:root', 'code:src/A.cs#Foo') or a label substring."),
            ],
            ToolSafety.SafeOperation,
            async (arguments, ct) =>
            {
                if (!IsLoaded) return ToolResult.Error("The graph is still loading.");
                if (HasError) return ToolResult.Error($"The graph failed to load: {ErrorMessage}");

                var query = ToolArgs.Str(arguments, "node");
                if (string.IsNullOrWhiteSpace(query))
                    return ToolResult.Error("Provide a 'node' — a node id or a label substring to focus on.");

                var match = _byId.TryGetValue(query, out var byId) ? byId : null;
                match ??= _allNodes.FirstOrDefault(n => n.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
                match ??= _allNodes.FirstOrDefault(n => n.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return ToolResult.Error($"No node matches \"{query}\".");

                if (FocusNodeId == match.Id)
                    return ToolResult.Ok(
                        $"already focused on {match.Label}",
                        $"The graph is already focused on [{match.Kind}] \"{match.Label}\" ({match.Id}).");

                if (_shell is not null) await _shell.RunOnUiAsync(() => SelectedNode = match);
                else SelectedNode = match;

                return ToolResult.Ok(
                    $"focused on {FocusLabel}",
                    $"Re-centred on [{match.Kind}] \"{match.Label}\" ({match.Id}). "
                  + $"Showing its {HopRadius}-hop neighbourhood ({Nodes.Count} node(s)).");
            }),
    ];

    /// <summary>Describes a neighbour's relation to the focus for <c>read_graph</c>: names the direct binary edge
    /// (with direction) when one is visible, otherwise falls back to its BFS hop distance.</summary>
    private string DescribeRelationToFocus(string focusId, string neighbourId, int hop)
    {
        foreach (var e in Edges)
        {
            if (e.From.Id == focusId && e.To.Id == neighbourId) return $"focus {e.Relationship} it";
            if (e.From.Id == neighbourId && e.To.Id == focusId) return $"it {e.Relationship} focus";
        }
        return hop >= 0 ? $"{hop} hop(s) from focus" : "reachable from focus";
    }

    public string? GetAiSystemPromptGuidance() =>
        "A knowledge-graph viewer: the product tree linked into code files, types and members. It shows the " +
        "neighbourhood around a focused node; selecting a node re-centres on it.";
}
