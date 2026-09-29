using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Hdf5;
using Nexaflow.IO.Hdf5.Projection;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>
/// Drives the HDF5 viewer tab. Opens the file off the UI thread — from disk, or through the VFS when it sits inside
/// an archive — lists its tree a group and a page at a time, and shows the selected object: a group's members, a
/// dataset as a windowed table sliced for N-D data, or why a link leads nowhere, with its details and attributes
/// in the drawer.
/// </summary>
public sealed partial class Hdf5ViewModel : ObservableObject, IPageViewModel, IDisposable
{
    /// <summary>Members listed per page of a group.</summary>
    internal const int TreePage = 500;

    private readonly IShellServices _shell;
    private readonly Hdf5SourceCache _cache;
    private readonly string? _initialNode;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _selectionCts;
    private Hdf5Lease? _lease;
    private bool _disposed;

    public Hdf5ViewModel(string filePath, string? node, IShellServices shell, Hdf5SourceCache cache)
    {
        FilePath     = filePath;
        _initialNode = node;
        _shell       = shell;
        _cache       = cache;
        _statusText  = Str.Get("Hdf5.Status.Loading");
        Loaded       = LoadAsync();
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Completes once the file is open and the first selection shows, or opening has failed.</summary>
    public Task Loaded { get; }

    internal IHdf5Source? Source => _lease?.Source;

    /// <summary>The showing of the current selection: its details, attributes and first table window.</summary>
    internal Task? ShowTask { get; private set; }

    public ObservableCollection<Hdf5NodeViewModel> Roots { get; } = [];
    public ObservableCollection<Hdf5MemberCount> GroupMembers { get; } = [];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string? _loadProblem;
    [ObservableProperty] private string _treeFilter = string.Empty;
    [ObservableProperty] private Hdf5NodeViewModel? _selectedNode;
    [ObservableProperty] private string? _contentMessage;
    [ObservableProperty] private string? _groupProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentHeader), nameof(CurrentSummary))]
    private Hdf5Object? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGroupContent), nameof(IsTableContent), nameof(IsMessageContent))]
    private Hdf5ContentKind _contentKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private Hdf5TableViewModel? _table;

    public bool IsGroupContent   => ContentKind == Hdf5ContentKind.Group;
    public bool IsTableContent   => ContentKind == Hdf5ContentKind.Table;
    public bool IsMessageContent => ContentKind == Hdf5ContentKind.Message;

    public string CurrentHeader => Current?.Path ?? Str.Get("Hdf5.Content.NothingSelected");

    public string CurrentSummary => Current switch
    {
        { Dataset: { } d } => $"{d.Type.DisplayName} · {Hdf5NodeViewModel.ShapeText(d.Space)}",
        { } o              => KindText(o.Kind),
        _                  => string.Empty,
    };

    // ── Opening and listing ───────────────────────────────────────────────

    private async Task LoadAsync()
    {
        try
        {
            _lease = await Task.Run(Open, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Hdf5Exception or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LoadProblem = ex.Message;
            StatusText  = Str.Format("Hdf5.Status.OpenFailedFormat", ex.Message);
            IsLoading   = false;
            OnPropertyChanged(nameof(IsContextReady));
            return;
        }
        if (_disposed) { _lease.Dispose(); return; }

        var root = NewNode(null, _lease.Source.Root);
        root.Display = FileName;
        root.MarkExpansionStarted();
        Roots.Add(root);
        await ListAsync(root, 0);
        root.IsExpanded = true;

        StatusText = Str.Format("Hdf5.Status.RootFormat", root.ObjectChildren.Count());
        IsLoading  = false;
        OnPropertyChanged(nameof(IsContextReady));
        await NavigateToAsync(_initialNode ?? Hdf5Path.Root);
    }

    private Hdf5Lease Open() => File.Exists(FilePath)
        ? _cache.Acquire(FilePath)
        : _cache.Acquire(VirtualFileSystem.Instance.OpenRead(FilePath));

    private Hdf5NodeViewModel NewNode(Hdf5NodeViewModel? parent, Hdf5Object o) =>
        Hdf5NodeViewModel.For(parent, o, node => ListAsync(node, 0));

    /// <summary>Lists one page of a group's members into its tree row.</summary>
    private async Task ListAsync(Hdf5NodeViewModel node, int skip)
    {
        if (Source is not { } src || node.Object is not { Kind: Hdf5ObjectKind.Group } group) return;
        Hdf5Listing listing;
        try
        {
            listing = await Task.Run(() => src.ListChildren(group.Path, skip, TreePage, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Hdf5Exception or ObjectDisposedException)
        {
            listing = new Hdf5Listing([], false, ex.Message);
        }
        node.Apply(listing, skip, o => NewNode(node, o));
        ApplyTreeFilter();
    }

    /// <summary>
    /// Selects the object at a path, expanding its ancestors and paging through large groups to reach it. A path
    /// may name a dataset's projected file (<c>/g/data.npy</c>), the name it has when the file is browsed as a folder. Stops at the deepest
    /// object that exists.
    /// </summary>
    public async Task<Hdf5NodeViewModel?> NavigateToAsync(string path)
    {
        if (Roots.FirstOrDefault() is not { } root || Source is not { } src) return null;
        string target;
        try { target = await Task.Run(() => ResolveObjectPath(src, path), _cts.Token); }
        catch (OperationCanceledException) { return null; }

        var node = root;
        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.ChildrenLoaded)
            {
                node.MarkExpansionStarted();
                await ListAsync(node, 0);
            }
            node.IsExpanded = true;
            if (await FindChildAsync(node, segment) is not { } next) break;
            node = next;
        }
        node.IsSelected = true;
        SelectedNode    = node;
        return node;
    }

    private async Task<Hdf5NodeViewModel?> FindChildAsync(Hdf5NodeViewModel node, string name)
    {
        while (true)
        {
            if (node.ObjectChildren.FirstOrDefault(c => c.Object!.Name == name) is { } hit) return hit;
            if (node.Children.LastOrDefault() is not { Role: Hdf5NodeRole.More } more) return null;
            await ListAsync(node, more.NextSkip);
        }
    }

    /// <summary>The object a path means: itself when it exists, else the dataset whose projected file it names.</summary>
    internal static string ResolveObjectPath(IHdf5Source source, string path)
    {
        var p = Hdf5Path.Normalize(path);
        if (source.Stat(p) is not null) return p;
        return Hdf5Projection.DatasetPathOf(p, out _) is { } dataset && source.Stat(dataset) is { Kind: Hdf5ObjectKind.Dataset }
            ? dataset
            : p;
    }

    // ── Selection ─────────────────────────────────────────────────────────

    partial void OnSelectedNodeChanged(Hdf5NodeViewModel? value)
    {
        switch (value)
        {
            case { Role: Hdf5NodeRole.More, Parent: { } parent }:
                _ = ListAsync(parent, value.NextSkip);
                break;
            case { Role: Hdf5NodeRole.Object, Object: { } o }:
                ShowTask = ShowAsync(o);
                break;
        }
    }

    partial void OnTableChanged(Hdf5TableViewModel? oldValue, Hdf5TableViewModel? newValue)
    {
        if (oldValue is not null) { oldValue.PropertyChanged -= OnTablePropertyChanged; oldValue.Dispose(); }
        if (newValue is not null) newValue.PropertyChanged += OnTablePropertyChanged;
    }

    private void OnTablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hdf5TableViewModel.IsReading)) OnPropertyChanged(nameof(IsContextReady));
    }

    /// <summary>Shows an object: its details and attributes in the drawer, and what it holds in the content pane.</summary>
    private async Task ShowAsync(Hdf5Object o)
    {
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _selectionCts.Token;

        Current        = o;
        Table          = null;
        ContentMessage = null;
        GroupProblem   = null;
        GroupMembers.Clear();
        ShowDetails(o);
        var attributes = LoadAttributesAsync(o, ct);

        switch (o.Kind)
        {
            case Hdf5ObjectKind.Group:
                ContentKind = Hdf5ContentKind.Group;
                await LoadGroupMembersAsync(o, ct);
                break;
            case Hdf5ObjectKind.Dataset when o.Dataset is { Space.ElementCount: 0 }:
                ShowMessage(Str.Get("Hdf5.Content.Empty"));
                break;
            case Hdf5ObjectKind.Dataset when o.Dataset is { } info && Source is { } src:
                var table = new Hdf5TableViewModel(src, o, new Hdf5SliceViewModel(info));
                Table       = table;
                ContentKind = Hdf5ContentKind.Table;
                await table.RefreshWindowAsync();
                break;
            case Hdf5ObjectKind.NamedDatatype:
                ShowMessage(Str.Get("Hdf5.Content.NamedDatatype"));
                break;
            case Hdf5ObjectKind.UnresolvedLink:
                ShowMessage(Str.Format("Hdf5.Content.UnresolvedFormat", o.Problem));
                break;
            default:
                ShowMessage(Str.Format("Hdf5.Content.UnreadableFormat", o.Problem));
                break;
        }
        await attributes;
    }

    private void ShowMessage(string message)
    {
        ContentMessage = message;
        ContentKind    = Hdf5ContentKind.Message;
    }

    private async Task LoadGroupMembersAsync(Hdf5Object group, CancellationToken ct)
    {
        if (Source is not { } src) return;
        Hdf5Listing listing;
        try
        {
            listing = await Task.Run(() => src.ListChildren(group.Path, 0, int.MaxValue, ct), ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Hdf5Exception or ObjectDisposedException)
        {
            GroupProblem = ex.Message;
            return;
        }
        if (ct.IsCancellationRequested) return;

        foreach (var kind in Enum.GetValues<Hdf5ObjectKind>())
        {
            int n = listing.Items.Count(o => o.Kind == kind);
            if (n > 0) GroupMembers.Add(new Hdf5MemberCount(PluralKindText(kind), n));
        }
        ObjectDetails.Add(new Hdf5DetailRow(Str.Get("Hdf5.Details.Members"),
            listing.Items.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
        GroupProblem = listing.Problem;
    }

    // ── Tree filter ───────────────────────────────────────────────────────

    partial void OnTreeFilterChanged(string value) => ApplyTreeFilter();

    /// <summary>Hides loaded objects whose name does not match, keeping the ancestors of those that do.</summary>
    private void ApplyTreeFilter()
    {
        var filter = TreeFilter.Trim();
        foreach (var root in Roots) Visit(root);

        bool Visit(Hdf5NodeViewModel n)
        {
            bool childVisible = false;
            foreach (var c in n.Children) childVisible |= Visit(c);
            bool self = filter.Length == 0
                        || (n.Role == Hdf5NodeRole.Object && n.Display.Contains(filter, StringComparison.OrdinalIgnoreCase));
            n.IsVisible = n.Parent is null || self || childVisible;
            if (filter.Length > 0 && childVisible && n.ChildrenLoaded) n.IsExpanded = true;
            return n.IsVisible && n.Parent is not null;
        }
    }

    // ── Words ─────────────────────────────────────────────────────────────

    internal static string KindText(Hdf5ObjectKind kind) => kind switch
    {
        Hdf5ObjectKind.Group          => Str.Get("Hdf5.Kind.Group"),
        Hdf5ObjectKind.Dataset        => Str.Get("Hdf5.Kind.Dataset"),
        Hdf5ObjectKind.NamedDatatype  => Str.Get("Hdf5.Kind.NamedDatatype"),
        Hdf5ObjectKind.UnresolvedLink => Str.Get("Hdf5.Kind.UnresolvedLink"),
        _                             => Str.Get("Hdf5.Kind.Unreadable"),
    };

    private static string PluralKindText(Hdf5ObjectKind kind) => kind switch
    {
        Hdf5ObjectKind.Group          => Str.Get("Hdf5.Group.Groups"),
        Hdf5ObjectKind.Dataset        => Str.Get("Hdf5.Group.Datasets"),
        Hdf5ObjectKind.NamedDatatype  => Str.Get("Hdf5.Group.NamedDatatypes"),
        Hdf5ObjectKind.UnresolvedLink => Str.Get("Hdf5.Group.UnresolvedLinks"),
        _                             => Str.Get("Hdf5.Group.Unreadable"),
    };

    internal static string LayoutText(Hdf5Layout layout) => layout switch
    {
        Hdf5Layout.Compact    => Str.Get("Hdf5.Layout.Compact"),
        Hdf5Layout.Contiguous => Str.Get("Hdf5.Layout.Contiguous"),
        Hdf5Layout.Chunked    => Str.Get("Hdf5.Layout.Chunked"),
        _                     => Str.Get("Hdf5.Layout.Virtual"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _selectionCts?.Cancel();
        Table = null;
        _lease?.Dispose();
        _selectionCts?.Dispose();
        _cts.Dispose();
    }
}
