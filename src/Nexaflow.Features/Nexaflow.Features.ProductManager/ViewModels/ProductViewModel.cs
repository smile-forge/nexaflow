using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Features.ProductManager.Model;
using Nexaflow.Features.ProductManager.Services;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Dialogs;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// The single Product surface: a zoomable sunburst (the tree of component truth) + a derived details pane.
/// The live <b>Current</b> version (gitignored <c>.product/</c>) is editable; <b>snapshots</b> (committed
/// in the export dir) are frozen and read-only. Take Snapshot exports + git-tags; Settings sets the export dir.
/// </summary>
public partial class ProductViewModel : ObservableObject, IPageViewModel, IDisposable
{
    public const string ProductRootSentinel = "__product__";

    private readonly ProductStore   _store;
    private readonly ProductGit     _git;
    private readonly IShellServices _shell;
    private IFileWatch? _watch;
    private ProductState _state = new();
    private ProductSnapshot? _snapshot;     // non-null when a read-only snapshot is selected
    private string _exportDir = "docs/product";
    private bool _suppress;
    private IReadOnlyList<IClientTool>? _tools;

    public string ProductRoot { get; }

    [ObservableProperty] private string _productName = "Product";
    [ObservableProperty] private string? _focusedNodeId;
    [ObservableProperty] private SunburstNode? _sunburstRoot;
    [ObservableProperty] private bool _hasNodes;

    // ── Details: summary (derived) ──────────────────────────────────────────
    [ObservableProperty] private string _headerTitle = string.Empty;
    [ObservableProperty] private string _caption = string.Empty;
    [ObservableProperty] private string _versionInfoLine = string.Empty;
    [ObservableProperty] private bool _showVersionInfo;        // only for a (read-only) snapshot
    [ObservableProperty] private bool _hasNeedsAttention;      // collapse the section when nothing is faulted
    [ObservableProperty] private GridLength _doneStar     = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _shouldStar   = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _faultedStar  = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _shouldntStar = new(0, GridUnitType.Star);
    public ObservableCollection<ProductAggregator.AttentionNode> NeedsAttention { get; } = [];

    // ── Details: editable node (only when a real node is focused, on Current) ──
    [ObservableProperty] private bool _isNodeSelected;
    [ObservableProperty] private string _detailTitle = string.Empty;
    [ObservableProperty] private string _detailDescription = string.Empty;
    [ObservableProperty] private Status _detailStatus = Status.Should;
    [ObservableProperty] private string _detailNote = string.Empty;
    [ObservableProperty] private int _nodeSnaplinkCount;   // for the property panel's 🔗 affordance + count
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCycleStatus))]
    private bool _isStatusDerived;   // true when the focused node has children → status is rolled-up, read-only
    public IReadOnlyList<Status> AllStatuses => StatusInfo.All;

    /// <summary>The focused node's status colour (for the click-to-cycle status pill next to the title).</summary>
    public Brush DetailStatusBrush => StatusInfo.Brush(DetailStatus);
    public bool HasNodeSnaplinks => NodeSnaplinkCount > 0;

    /// <summary>The status pill is editable only on a leaf in the editable version (a parent's status is derived).</summary>
    public bool CanCycleStatus => IsEditable && !IsStatusDerived;

    public ObservableCollection<ConcernBox> ConcernBoxes { get; } = [];
    public ObservableCollection<SnaplinkRow> Snaplinks { get; } = [];
    public ObservableCollection<string> AddableConcerns { get; } = [];

    // Snaplinks overlay — shared by the concern boxes and the node properties panel (see SnaplinkEditTarget).
    private SnaplinkEditTarget? _editTarget;
    [ObservableProperty] private bool _concernSnaplinksVisible;
    [ObservableProperty] private string _concernSnaplinksTitle = string.Empty;
    public ObservableCollection<SnaplinkRow> ConcernSnaplinkRows { get; } = [];
    [ObservableProperty] private SnaplinkPickerViewModel? _picker;   // inline file/heading/class picker for adding
    [ObservableProperty] private string _newSnaplinkUrl = string.Empty;   // the "Link URL" tab's field

    // Restructure overlay — drag-drop the whole tree (⋮ menu).
    [ObservableProperty] private bool _restructureVisible;
    public ObservableCollection<RestructureNode> RestructureRoots { get; } = [];

    // ── Versions (Current = live/editable; others = committed read-only snapshots) ──
    public ObservableCollection<string> Versions { get; } = [];
    [ObservableProperty] private string _selectedVersion = ProductStore.CurrentVersion;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCycleStatus))]
    private bool _isEditable = true;

    // Take-snapshot overlay
    [ObservableProperty] private bool _takeSnapshotVisible;
    [ObservableProperty] private string _newSnapshotVersion = string.Empty;

    // Settings overlay (export dir + concern vocabulary)
    [ObservableProperty] private bool _settingsVisible;
    [ObservableProperty] private string _settingsExportDir = string.Empty;
    [ObservableProperty] private string _newSettingsConcern = string.Empty;
    public ObservableCollection<ConcernDefRow> SettingsConcerns { get; } = [];

    // ── Confirmation overlay (shared Visuals.Common dialog) ────────────────
    [ObservableProperty] private ConfirmationRequest? _confirmation;

    private void ShowConfirmation(string title, string prompt, Action onConfirm)
        => Confirmation = new ConfirmationRequest(title, prompt, onConfirm, confirmLabel: "Delete");

    public ProductViewModel(ProductStore store, ProductGit git, string root, IShellServices shell)
    {
        _store = store;
        _git   = git;
        ProductRoot = root;
        _shell = shell;
        Load();
        // watch only the live tree; snapshots are static, so ignore changes while one is selected
        _watch = shell.WatchFile(store.TreeFilePath, () => { if (SelectedVersion == ProductStore.CurrentVersion) Load(); });
    }

    [RelayCommand] private void Refresh() => Load();

    private void Load()
    {
        var live = _store.Load();
        _exportDir = string.IsNullOrWhiteSpace(live.Product.ExportDir) ? "docs/product" : live.Product.ExportDir;

        if (SelectedVersion == ProductStore.CurrentVersion)
        {
            _state = live;
            _snapshot = null;
        }
        else
        {
            _snapshot = _store.LoadSnapshot(_exportDir, SelectedVersion);
            if (_snapshot is null)   // snapshot vanished (deleted) → fall back to Current
            {
                _suppress = true; SelectedVersion = ProductStore.CurrentVersion; _suppress = false;
                _state = live;
            }
            else _state = new ProductState { Product = _snapshot.Product, Nodes = _snapshot.Nodes };
        }

        IsEditable  = SelectedVersion == ProductStore.CurrentVersion;
        ProductName = string.IsNullOrWhiteSpace(_state.Product.Product)
            ? new DirectoryInfo(ProductRoot).Name : _state.Product.Product;
        SyncVersions();
        if (FocusedNodeId is not null && !_state.Nodes.ContainsKey(FocusedNodeId)) FocusedNodeId = null;
        RebuildAll();
    }

    // ["Current", …snapshots] — rebuilt only when the set changes, preserving a valid selection.
    private void SyncVersions()
    {
        var desired = new List<string> { ProductStore.CurrentVersion };
        desired.AddRange(_store.ListSnapshots(_exportDir).Select(s => s.Version));
        if (Versions.SequenceEqual(desired)) return;

        _suppress = true;
        Versions.Clear();
        foreach (var v in desired) Versions.Add(v);
        if (!Versions.Contains(SelectedVersion)) SelectedVersion = ProductStore.CurrentVersion;
        _suppress = false;
    }

    partial void OnSelectedVersionChanged(string value)
    {
        if (_suppress) return;
        FocusedNodeId = null;   // a different tree → start at its product root
        Load();
    }

    // ── Snapshot / export / settings ─────────────────────────────────────────

    [RelayCommand]
    private void ShowTakeSnapshot()
    {
        NewSnapshotVersion = string.Empty;
        TakeSnapshotVisible = true;
    }

    [RelayCommand] private void CancelTakeSnapshot() => TakeSnapshotVisible = false;

    [RelayCommand]
    private void ConfirmTakeSnapshot()
    {
        var version = NewSnapshotVersion.Trim();
        if (version.Length == 0 || string.Equals(version, ProductStore.CurrentVersion, StringComparison.OrdinalIgnoreCase))
        {
            _shell.ShowError("Enter a version number.");
            return;
        }
        if (_git.IsRepo && _git.HasTag(version))
        {
            _shell.ShowError($"Git tag '{version}' already exists.");
            return;
        }

        var live = _store.Load();   // a snapshot always captures the live Current state
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var snap = new ProductSnapshot { Version = version, Date = date, Product = live.Product, Nodes = live.Nodes };
        if (_git.IsRepo) snap.Tag = version;

        var jsonPath = _store.WriteSnapshot(_exportDir, snap);
        var mdPath   = _store.WriteExportText(_exportDir, "PRODUCT.md", ProductMarkdown.Render(live, version, date));

        TakeSnapshotVisible = false;

        if (_git.IsRepo)
        {
            var (ok, err) = _git.CommitAndTag([jsonPath, mdPath], $"Product snapshot {version}", version);
            _shell.ShowNotification(ok
                ? $"Snapshot {version} exported, committed and tagged."
                : $"Snapshot {version} exported to {_exportDir}; git step failed: {err}");
        }
        else _shell.ShowNotification($"Snapshot {version} exported to {_exportDir}.");

        SyncVersions();
        SelectedVersion = version;   // open the new (read-only) snapshot
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var live = _store.Load();
        SettingsExportDir = _exportDir;
        NewSettingsConcern = string.Empty;
        SettingsConcerns.Clear();
        foreach (var c in live.Product.Concerns)
            SettingsConcerns.Add(new ConcernDefRow(c.Name, c.IsDefault, RemoveSettingsConcern));
        SettingsVisible = true;
    }

    [RelayCommand] private void CancelSettings() => SettingsVisible = false;

    [RelayCommand]
    private void AddSettingsConcern()
    {
        var name = NewSettingsConcern.Trim();
        if (name.Length == 0 || SettingsConcerns.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        SettingsConcerns.Add(new ConcernDefRow(name, false, RemoveSettingsConcern));
        NewSettingsConcern = string.Empty;
    }

    private void RemoveSettingsConcern(ConcernDefRow row) => SettingsConcerns.Remove(row);

    [RelayCommand]
    private void ConfirmSettings()
    {
        var dir = SettingsExportDir.Trim().Replace('\\', '/').Trim('/');
        if (dir.Length == 0) { _shell.ShowError("Enter an export folder (relative to the product root)."); return; }
        var live = _store.Load();
        live.Product.ExportDir = dir;
        live.Product.Concerns = SettingsConcerns.Select(r => new ConcernDef { Name = r.Name, IsDefault = r.IsDefault }).ToList();
        _store.SaveProduct(live.Product);
        _exportDir = dir;
        SettingsVisible = false;
        _suppress = true; SelectedVersion = ProductStore.CurrentVersion; _suppress = false;
        Load();
    }

    [RelayCommand]
    private void DeleteSnapshot()
    {
        var version = SelectedVersion;
        if (version == ProductStore.CurrentVersion) { _shell.ShowError("Current isn't a snapshot."); return; }
        ShowConfirmation("Delete snapshot",
            $"Delete snapshot \"{version}\" from {_exportDir}? The git tag, if any, is left in place.",
            () =>
            {
                _store.DeleteSnapshot(_exportDir, version);
                _suppress = true; SelectedVersion = ProductStore.CurrentVersion; _suppress = false;
                Load();
            });
    }

    // ── Navigation (called by the view from the sunburst) ────────────────────

    public void OnArcClicked(string id)
    {
        if (id == ProductRootSentinel) return;
        SetFocus(id);
    }

    public void OnCentreClicked()
    {
        if (FocusedNodeId is null) return;                 // already at product root
        SetFocus(_state.Nodes.TryGetValue(FocusedNodeId, out var n) ? n.Parent : null);
    }

    private void SetFocus(string? id)
    {
        FocusedNodeId = id;
        RebuildAll();
    }

    /// <summary>Raised when the focus path changes so the page registration can rebuild the tab's breadcrumb
    /// bar (FileSystemView pattern). Each segment is (Label, NodeId); NodeId is null for the product root.</summary>
    public event Action<IReadOnlyList<(string Label, string? NodeId)>>? NavigationChanged;

    /// <summary>The current breadcrumb path (root → focused), for the registration's initial apply.</summary>
    public IReadOnlyList<(string Label, string? NodeId)> CurrentPath { get; private set; } = [];

    /// <summary>Focus a node (null = product root) — the breadcrumb bar's navigation target.</summary>
    public void NavigateTo(string? nodeId) => SetFocus(nodeId);

    private void BuildBreadcrumb()
    {
        var path = new List<(string Label, string? NodeId)> { (ProductName, null) };
        if (FocusedNodeId is not null)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>();
            for (var cur = FocusedNodeId; cur is not null && _state.Nodes.TryGetValue(cur, out var n) && seen.Add(cur); cur = n.Parent)
                chain.Insert(0, cur);   // root → focused
            foreach (var id in chain) path.Add((_state.Nodes[id].Title, id));
        }
        CurrentPath = path;
        NavigationChanged?.Invoke(path);
    }

    // ── Rebuild ──────────────────────────────────────────────────────────────

    private void RebuildAll()
    {
        HasNodes = _state.Nodes.Count > 0;
        BuildSunburst();
        BuildDetail();
    }

    private void BuildSunburst()
    {
        // arc fills come from an accent ramp (Accent → Accent2 across siblings); a node WITH children keeps
        // the darker tone, a LEAF gets a lighter tint. Status is a separate marker bar. Centre is neutral.
        var accent  = ColorOf("AccentBrush");
        var accent2 = ColorOf("Accent2Brush");
        var neutral = StatusInfo.Res("Surface2Brush");

        if (FocusedNodeId is null)
        {
            var roots = ProductAggregator.Roots(_state).ToList();
            var children = new List<SunburstNode>();
            for (var i = 0; i < roots.Count; i++)
            {
                var baseCol = Ramp(accent, accent2, i, roots.Count);
                children.Add(ToSun(roots[i], 1, accent, accent2, FillFor(roots[i], baseCol), baseCol));
            }
            SunburstRoot = new SunburstNode
            {
                Id = ProductRootSentinel, Label = ProductName, Weight = 1,
                Fill = neutral, HasChildren = children.Count > 0, Children = children
            };
        }
        else if (_state.Nodes.ContainsKey(FocusedNodeId))
        {
            SunburstRoot = ToSun(FocusedNodeId, 0, accent, accent2, neutral, default);
        }
        else SunburstRoot = null;
    }

    private SunburstNode ToSun(string id, int depth, Color accent, Color accent2, Brush fill, Color baseColor)
    {
        var node = _state.Nodes[id];
        var kids = node.Children.Where(_state.Nodes.ContainsKey).ToList();
        var childSun = new List<SunburstNode>();
        if (depth < 2)
            for (var i = 0; i < kids.Count; i++)
            {
                var kidBase = depth == 0 ? Ramp(accent, accent2, i, kids.Count) : baseColor;
                childSun.Add(ToSun(kids[i], depth + 1, accent, accent2, FillFor(kids[i], kidBase), kidBase));
            }

        return new SunburstNode
        {
            Id = id,
            Label = node.Title,
            Weight = Math.Max(1, ProductAggregator.Leaves(_state, id).Count),
            Fill = fill,
            StatusFill = StatusInfo.Brush(ProductAggregator.EffectiveStatus(_state, id)),
            HasChildren = kids.Count > 0,
            Children = childSun
        };
    }

    private Brush FillFor(string id, Color baseCol)
    {
        var hasKids = _state.Nodes[id].Children.Any(_state.Nodes.ContainsKey);
        return hasKids ? Brush(baseCol) : Brush(Mix(baseCol, Colors.White, 0.42));   // leaf → lighter
    }

    private static Color Ramp(Color a, Color b, int i, int n) => Mix(a, b, n <= 1 ? 0.5 : (double)i / (n - 1));
    private static Color ColorOf(string key) => (StatusInfo.Res(key) as SolidColorBrush)?.Color ?? Colors.Gray;
    private static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void BuildDetail()
    {
        _suppress = true;
        try
        {
            var node = FocusedNodeId is not null && _state.Nodes.TryGetValue(FocusedNodeId, out var n) ? n : null;
            IsNodeSelected = node is not null;

            HeaderTitle = node?.Title ?? ProductName;
            BuildBreadcrumb();

            var (sCount, _) = ScopeBreakdown();
            DoneStar     = new GridLength(sCount.Done,     GridUnitType.Star);
            ShouldStar   = new GridLength(sCount.Should,   GridUnitType.Star);
            FaultedStar  = new GridLength(sCount.Faulted,  GridUnitType.Star);
            ShouldntStar = new GridLength(sCount.Shouldnt, GridUnitType.Star);
            Caption = $"{sCount.Percent}% shipping — {sCount.Done} done, {sCount.Should} should, " +
                      $"{sCount.Faulted} faulted, {sCount.Shouldnt} shouldn't";

            NeedsAttention.Clear();
            foreach (var a in ScopeNeedsAttention()) NeedsAttention.Add(a);
            HasNeedsAttention = NeedsAttention.Count > 0;

            ShowVersionInfo = _snapshot is not null;
            VersionInfoLine = _snapshot is null
                ? string.Empty
                : $"Snapshot · {_snapshot.Date}" + (_snapshot.Tag is null ? "" : $" · tag {_snapshot.Tag}") + " · read-only";

            DetailTitle       = node?.Title       ?? string.Empty;
            DetailDescription = node?.Description  ?? string.Empty;
            DetailNote        = node?.Note         ?? string.Empty;

            // A parent's status is derived from its children (read-only); a leaf carries its own editable status.
            // Either way the pill EXCLUDES this node's own concerns — they're shown separately as concern boxes
            // (whereas the sunburst arc bundles them in).
            var hasKids = node is not null && node.Children.Any(_state.Nodes.ContainsKey);
            IsStatusDerived = hasKids;
            DetailStatus = node is null ? Status.Should
                : ProductAggregator.DerivedStatusExcludingConcerns(_state, FocusedNodeId!);

            ConcernBoxes.Clear();
            foreach (var link in node?.Concerns ?? [])
                ConcernBoxes.Add(new ConcernBox(link, IsEditable, SaveAndRollup, RemoveConcernBox, EditConcernSnaplinks));

            // These rows feed the property panel's 🔗 dropdown (open only) — add/remove go through the
            // shared overlay (EditNodeSnaplinks), so the per-row remove callback is a no-op here.
            Snaplinks.Clear();
            foreach (var link in node?.Snaplinks ?? [])
                Snaplinks.Add(new SnaplinkRow(link, SaveTree, _ => { }, r => OpenSnaplink(r.Model)));
            NodeSnaplinkCount = Snaplinks.Count;

            AddableConcerns.Clear();
            if (node is not null)
                foreach (var name in _state.Product.Concerns.Select(c => c.Name)
                             .Where(name => node.Concerns?.Any(l => l.Tag == name) != true))
                    AddableConcerns.Add(name);
        }
        finally { _suppress = false; }
    }

    // ── Scope helpers (a real node's subtree, or the whole product at root) ──

    private IReadOnlyList<string> ScopeLeaves() =>
        FocusedNodeId is null
            ? _state.Nodes.Where(kv => kv.Value.Children.All(c => !_state.Nodes.ContainsKey(c))).Select(kv => kv.Key).ToList()
            : ProductAggregator.Leaves(_state, FocusedNodeId);

    private (ProductAggregator.StatusCount Count, int Total) ScopeBreakdown()
    {
        if (FocusedNodeId is not null) { var c = ProductAggregator.StatusBreakdown(_state, FocusedNodeId); return (c, c.Total); }
        int should = 0, shouldnt = 0, done = 0, faulted = 0;
        foreach (var id in ScopeLeaves())
            switch (ProductAggregator.EffectiveStatus(_state, id))
            {
                case Status.Should: should++; break;
                case Status.Shouldnt: shouldnt++; break;
                case Status.Done: done++; break;
                case Status.Faulted: faulted++; break;
            }
        var sc = new ProductAggregator.StatusCount(should, shouldnt, done, faulted);
        return (sc, sc.Total);
    }

    private IReadOnlyList<ProductAggregator.AttentionNode> ScopeNeedsAttention() =>
        FocusedNodeId is null
            ? _state.Nodes.Keys.Where(id => ProductAggregator.IsLocallyFaulted(_state, id))
                  .Select(id => { var n = _state.Nodes[id]; return new ProductAggregator.AttentionNode(id, n.Title, Status.Faulted, n.Note); }).ToList()
            : ProductAggregator.NeedsAttention(_state, FocusedNodeId);

    // ── Editing the focused node (Current only) ──────────────────────────────

    private ProductNode? Focused => FocusedNodeId is not null && _state.Nodes.TryGetValue(FocusedNodeId, out var n) ? n : null;

    partial void OnDetailTitleChanged(string value)       { if (Apply(n => n.Title = value)) { HeaderTitle = value; BuildSunburst(); BuildBreadcrumb(); } }
    partial void OnDetailDescriptionChanged(string value) => Apply(n => n.Description = NullIfBlank(value));
    partial void OnDetailNoteChanged(string value)        { if (Apply(n => n.Note = NullIfBlank(value))) RefreshAttentionNote(); }
    partial void OnDetailStatusChanged(Status value)      { OnPropertyChanged(nameof(DetailStatusBrush)); if (Apply(n => n.Status = value)) RebuildAll(); }
    partial void OnNodeSnaplinkCountChanged(int value)    => OnPropertyChanged(nameof(HasNodeSnaplinks));

    /// <summary>Click-to-cycle the focused node's status (should → shouldn't → done → faulted).</summary>
    [RelayCommand]
    private void CycleDetailStatus()
    {
        if (!CanCycleStatus) return;   // parents are derived/read-only
        DetailStatus = DetailStatus switch
        {
            Status.Should   => Status.Shouldnt,
            Status.Shouldnt => Status.Done,
            Status.Done     => Status.Faulted,
            _               => Status.Should
        };
    }

    private bool Apply(Action<ProductNode> edit)
    {
        if (_suppress || !IsEditable || Focused is not { } node) return false;
        edit(node);
        SaveTree();
        return true;
    }

    private void RefreshAttentionNote()
    {
        var was = _suppress; _suppress = true;
        NeedsAttention.Clear();
        foreach (var a in ScopeNeedsAttention()) NeedsAttention.Add(a);
        HasNeedsAttention = NeedsAttention.Count > 0;
        _suppress = was;
    }

    // ── Concerns ──────────────────────────────────────────────────────────────

    /// <summary>Persist + refresh the sunburst (rolled-up status depends on concern statuses at every level).</summary>
    private void SaveAndRollup() { SaveTree(); BuildSunburst(); }

    [RelayCommand]
    private void AddConcern(string? name)
    {
        var tag = name?.Trim();
        if (!IsEditable || Focused is not { } node || string.IsNullOrWhiteSpace(tag)) return;
        node.Concerns ??= [];
        if (node.Concerns.All(c => c.Tag != tag))
        {
            node.Concerns.Add(new ConcernLink { Tag = tag, Status = Status.Should });
            SaveTree();
        }
        RebuildAll();   // concern set changed → refresh the rollup too
    }

    private void RemoveConcernBox(ConcernBox box)
    {
        if (!IsEditable || Focused is not { } node) return;
        node.Concerns?.Remove(box.Model);
        if (node.Concerns is { Count: 0 }) node.Concerns = null;
        SaveTree();
        RebuildAll();   // concern set changed → refresh the rollup too
    }

    // ── Snaplinks overlay (shared: concern boxes + node properties panel) ───

    /// <summary>What the snaplinks overlay is editing — a concern↔node link or a node — abstracted to a
    /// get/set of the snaplink list plus a refresh of whatever surfaced the editor.</summary>
    private sealed class SnaplinkEditTarget(Func<List<Snaplink>?> get, Action<List<Snaplink>?> set, Action refresh)
    {
        public IReadOnlyList<Snaplink> List => get() ?? [];
        public void Add(Snaplink link) { var l = get() ?? []; l.Add(link); set(l); }
        public void Remove(Snaplink link) { var l = get(); l?.Remove(link); set(l is { Count: > 0 } ? l : null); }
        public void Refresh() => refresh();
    }

    private void OpenSnaplinkEditor(string title, SnaplinkEditTarget target)
    {
        _editTarget = target;
        ConcernSnaplinksTitle = title;
        Picker = new SnaplinkPickerViewModel(ProductRoot);
        NewSnaplinkUrl = string.Empty;
        RebuildConcernSnaplinkRows();
        ConcernSnaplinksVisible = true;
    }

    private void EditConcernSnaplinks(ConcernBox box)
        => OpenSnaplinkEditor($"Snaplinks · {box.Tag}",
            new SnaplinkEditTarget(() => box.Model.Snaplinks, v => box.Model.Snaplinks = v, box.RefreshSnaplinks));

    /// <summary>Opens the snaplink editor for the focused node's attachments (the property panel's 🔗).</summary>
    public void EditNodeSnaplinks()
    {
        if (!IsEditable || Focused is not { } node) return;
        OpenSnaplinkEditor($"Attachments · {node.Title}",
            new SnaplinkEditTarget(() => node.Snaplinks, v => node.Snaplinks = v, BuildDetail));
    }

    [RelayCommand] private void CloseConcernSnaplinks() => ConcernSnaplinksVisible = false;

    private void RebuildConcernSnaplinkRows()
    {
        ConcernSnaplinkRows.Clear();
        foreach (var l in _editTarget?.List ?? [])
            ConcernSnaplinkRows.Add(new SnaplinkRow(l, SaveTree, RemoveConcernSnaplink, r => OpenSnaplink(r.Model)));
    }

    /// <summary>Adds the snaplink currently selected in the inline picker to the target being edited.</summary>
    [RelayCommand]
    private void AddPickedSnaplink()
    {
        if (!IsEditable || _editTarget is null || Picker?.BuildSelectedLink() is not { } link) return;
        AddConcernSnaplink(link);
    }

    /// <summary>Opens a snaplink's target in a new tab (markdown / code), or hands a URL to the shell.</summary>
    public void OpenSnaplink(Snaplink link)
    {
        if (link.Type == "url")
        {
            if (!string.IsNullOrWhiteSpace(link.Target)) _shell.HandleObject(link.Target!);
            return;
        }
        if (string.IsNullOrWhiteSpace(link.Doc)) return;
        var full = Path.IsPathRooted(link.Doc) ? link.Doc : Path.Combine(ProductRoot, link.Doc);
        if (link.Type == "code")
        {
            var p = new Dictionary<string, string> { ["path"] = full };
            if (!string.IsNullOrWhiteSpace(link.Ast)) p["ast"] = link.Ast!;
            _shell.OpenTab("Code", p);
        }
        else
        {
            var p = new Dictionary<string, string> { ["path"] = full };
            if (link.TitlePath is { Count: > 0 } tp) p["heading"] = string.Join(">", tp);
            _shell.OpenTab("Markdown", p);
        }
    }

    private void RemoveConcernSnaplink(SnaplinkRow row)
    {
        if (!IsEditable || _editTarget is null) return;
        _editTarget.Remove(row.Model);
        SaveTree();
        RebuildConcernSnaplinkRows();
        _editTarget.Refresh();
    }

    /// <summary>Adds the URL typed (or dropped) into the "Link URL" tab as a snaplink, then clears the field.</summary>
    [RelayCommand]
    private void AddConcernUrl()
    {
        if (!IsEditable || _editTarget is null || string.IsNullOrWhiteSpace(NewSnaplinkUrl)) return;
        AddConcernSnaplink(new Snaplink { Type = "url", Target = NewSnaplinkUrl.Trim(), Status = Status.Should });
        NewSnaplinkUrl = string.Empty;
    }

    /// <summary>Called from the "Drop URL here" target in the URL tab — fills the field with the dropped text.</summary>
    public void SetDroppedUrl(string url)
    {
        if (!string.IsNullOrWhiteSpace(url)) NewSnaplinkUrl = url.Trim();
    }

    private void AddConcernSnaplink(Snaplink link)
    {
        if (_editTarget is null) return;
        _editTarget.Add(link);
        SaveTree();
        RebuildConcernSnaplinkRows();
        _editTarget.Refresh();
    }

    // ── Node tree mutation (Current only; from the sunburst menu / empty state) ──

    public void AddChild(string? parentId)
    {
        if (!IsEditable) return;
        var parent = parentId is null or ProductRootSentinel ? null : parentId;
        _shell.ShowPrompt("New node", "Title", string.Empty,
            title =>
            {
                if (string.IsNullOrWhiteSpace(title)) return;
                var id = UniqueId(Slug(title));
                var node = new ProductNode { Title = title.Trim(), Status = Status.Should, Parent = parent };
                var defaults = _state.Product.Concerns.Where(c => c.IsDefault).Select(c => c.Name).ToList();
                if (defaults.Count > 0)
                    node.Concerns = defaults.Select(n => new ConcernLink { Tag = n, Status = Status.Should }).ToList();
                _state.Nodes[id] = node;
                if (parent is not null && _state.Nodes.TryGetValue(parent, out var p)) p.Children.Add(id);
                SaveTree();
                RebuildAll();
            },
            () => { });
    }

    public void RenameNode(string id)
    {
        if (!IsEditable || !_state.Nodes.TryGetValue(id, out var node)) return;
        _shell.ShowPrompt("Rename node", "Title", node.Title,
            title => { if (!string.IsNullOrWhiteSpace(title)) { node.Title = title.Trim(); SaveTree(); RebuildAll(); } },
            () => { });
    }

    /// <summary>Sets a status from the sunburst menu, cascading to the subtree (should-only) — see
    /// <see cref="ProductTreeOps.CascadeStatus"/>.</summary>
    public void SetNodeStatus(string id, Status status)
    {
        if (!IsEditable || !_state.Nodes.ContainsKey(id)) return;
        ProductTreeOps.CascadeStatus(_state, id, status);
        SaveTree();
        RebuildAll();
    }

    /// <summary>Navigates the view to a node (used by the "needs attention" list and breadcrumb).</summary>
    [RelayCommand] private void NavigateToNode(string? id) { if (id is not null) NavigateTo(id); }

    public void DeleteNode(string id)
    {
        if (!IsEditable || id == ProductRootSentinel || !_state.Nodes.TryGetValue(id, out var node)) return;
        ShowConfirmation("Delete node", $"Delete \"{node.Title}\" and all its descendants? This cannot be undone.",
            () =>
            {
                var parent = node.Parent;
                RemoveRecursive(id);
                if (FocusedNodeId == id || (FocusedNodeId is not null && !_state.Nodes.ContainsKey(FocusedNodeId)))
                    FocusedNodeId = parent is not null && _state.Nodes.ContainsKey(parent) ? parent : null;
                SaveTree();
                RebuildAll();
                if (RestructureVisible) BuildRestructure();   // keep the restructure tree in sync
            });
    }

    private void RemoveRecursive(string id)
    {
        if (!_state.Nodes.TryGetValue(id, out var node)) return;
        foreach (var child in node.Children.ToList()) RemoveRecursive(child);
        _state.Nodes.Remove(id);
        if (node.Parent is not null && _state.Nodes.TryGetValue(node.Parent, out var parent))
            parent.Children.Remove(id);
    }

    // ── Restructure (re-parenting; Current only) ─────────────────────────────

    public bool CanPromoteNode(string id) => ProductTreeOps.CanPromote(_state, id);
    public bool CanDemoteNode(string id)  => ProductTreeOps.CanDemote(_state, id);

    /// <summary>Outdent: the node becomes a sibling of its parent.</summary>
    public void PromoteNode(string id) { if (IsEditable && ProductTreeOps.Promote(_state, id)) { SaveTree(); RebuildAll(); } }

    /// <summary>Indent: the node moves under its previous sibling.</summary>
    public void DemoteNode(string id)  { if (IsEditable && ProductTreeOps.Demote(_state, id))  { SaveTree(); RebuildAll(); } }

    /// <summary>Wraps the node in a new parent inserted between it and its current parent.</summary>
    public void InsertParentAbove(string id)
    {
        if (!IsEditable || !_state.Nodes.TryGetValue(id, out var node)) return;
        _shell.ShowPrompt("Insert parent", "New parent title", string.Empty,
            title =>
            {
                if (string.IsNullOrWhiteSpace(title)) return;
                var parentId = node.Parent;
                var newId = UniqueId(Slug(title));
                var newNode = new ProductNode { Title = title.Trim(), Status = Status.Should, Parent = parentId, Children = [id] };
                var defaults = _state.Product.Concerns.Where(c => c.IsDefault).Select(c => c.Name).ToList();
                if (defaults.Count > 0)
                    newNode.Concerns = defaults.Select(n => new ConcernLink { Tag = n, Status = Status.Should }).ToList();
                _state.Nodes[newId] = newNode;
                if (parentId is not null && _state.Nodes.TryGetValue(parentId, out var parent))
                {
                    var at = parent.Children.IndexOf(id);
                    if (at >= 0) parent.Children[at] = newId; else parent.Children.Add(newId);
                }
                node.Parent = newId;
                SaveTree();
                RebuildAll();
            },
            () => { });
    }

    /// <summary>Re-parents <paramref name="id"/> under <paramref name="newParentId"/> (the product-root sentinel
    /// or null makes it top-level). Rejects cycles. Used by the restructure tree's drag-drop.</summary>
    public bool ReparentNode(string id, string? newParentId)
    {
        if (newParentId == ProductRootSentinel) newParentId = null;
        if (!IsEditable || !ProductTreeOps.Reparent(_state, id, newParentId)) return false;
        SaveTree();
        RebuildAll();
        if (RestructureVisible) BuildRestructure();
        return true;
    }

    [RelayCommand]
    private void ShowRestructure()
    {
        if (!IsEditable) return;
        BuildRestructure();
        RestructureVisible = true;
    }

    [RelayCommand] private void CloseRestructure() => RestructureVisible = false;

    private void BuildRestructure()
    {
        RestructureRoots.Clear();
        var root = new RestructureNode(ProductRootSentinel, ProductName, null);
        foreach (var id in ProductAggregator.Roots(_state)) root.Children.Add(BuildRestructureNode(id));
        RestructureRoots.Add(root);
    }

    private RestructureNode BuildRestructureNode(string id)
    {
        var node = _state.Nodes[id];
        var rn = new RestructureNode(id, node.Title, StatusInfo.Brush(node.Status));
        foreach (var c in node.Children.Where(_state.Nodes.ContainsKey)) rn.Children.Add(BuildRestructureNode(c));
        return rn;
    }

    // ── Persistence (Current only — snapshots are frozen) ─────────────────────

    private void SaveTree()
    {
        if (!IsEditable) return;
        _store.SaveTree(_state.Nodes);
    }

    // ── IPageViewModel ─────────────────────────────────────────────────────────

    public string GetContext() => ProductTools.Describe(_state, FocusedNodeId);
    public IReadOnlyList<IClientTool> GetClientTools() => _tools ??= ProductTools.ForRoot(ProductRoot);
    public string? GetAiSystemPromptGuidance() =>
        "A product tracker: a tree of component nodes, each with a status (should/shouldn't/done/faulted), " +
        "cross-cutting concerns, and snaplinks. Faulted items are leaks that must not vanish. Explore with " +
        "product_zoom (a ring at a time); record progress by marking nodes/concerns done or faulted.";

    public void Dispose() => _watch?.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "node" : slug;
    }

    private string UniqueId(string baseId)
    {
        if (!_state.Nodes.ContainsKey(baseId)) return baseId;
        for (var n = 2; ; n++)
            if (!_state.Nodes.ContainsKey($"{baseId}-{n}")) return $"{baseId}-{n}";
    }
}
