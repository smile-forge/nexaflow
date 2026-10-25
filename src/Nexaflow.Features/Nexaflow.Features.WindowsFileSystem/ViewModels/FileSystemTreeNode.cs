using Nexaflow.IO.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

public enum DriveStatus   { Ready, Loading, Unavailable }

/// <summary>Which icon a This PC row draws. <c>Cloud</c> is for rows contributed by an
/// <c>IThisPcItemProvider</c>; the rest map from <see cref="System.IO.DriveType"/>.</summary>
public enum DriveIconType { HDD, SSD, Network, Removable, CDDVD, Cloud }

// ── Tree node ─────────────────────────────────────────────────────────────────
public enum TreeNodeKind { Folder, Drive, ThisPc }

public class FileSystemTreeNode : INotifyPropertyChanged
{
    private bool          _isExpanded;
    private bool          _isSelected;
    private string        _name;
    private DriveStatus   _driveStatus;
    private DriveIconType _driveIconType = DriveIconType.HDD;

    public string       FullPath { get; }
    public TreeNodeKind Kind     { get; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    /// <summary>Drive readiness state — only meaningful for Drive nodes.</summary>
    public DriveStatus DriveStatus
    {
        get => _driveStatus;
        set { _driveStatus = value; OnPropertyChanged(); }
    }

    /// <summary>Visual icon variant — only meaningful for Drive nodes.</summary>
    public DriveIconType DriveIconType
    {
        get => _driveIconType;
        set { _driveIconType = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FileSystemTreeNode> Children { get; } = [];

    // Dummy child keeps the expand arrow visible before real load
    internal static readonly FileSystemTreeNode Dummy = new("…");

    /// <summary>The runtime's file system the node reads its children through; null only for <see cref="Dummy"/>,
    /// which has none to read.</summary>
    private readonly IVirtualFileSystem? _vfs;

    /// <summary>The runtime's file system the node reads its children through.</summary>
    internal IVirtualFileSystem Vfs => _vfs ?? throw new InvalidOperationException("The placeholder row reads nothing.");

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            // Don't allow expansion of drive nodes that haven't finished loading or are unavailable
            if (Kind == TreeNodeKind.Drive &&
                (DriveStatus == DriveStatus.Loading || DriveStatus == DriveStatus.Unavailable))
                return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value) _ = LoadChildrenAsync();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Regular folder node, reading its children through <paramref name="vfs"/>.</summary>
    public FileSystemTreeNode(string name, string fullPath, IVirtualFileSystem vfs)
    {
        _name    = name;
        FullPath = fullPath;
        Kind     = TreeNodeKind.Folder;
        _vfs     = vfs;

        // Deliberately no I/O here. Deciding whether this node gets an expander costs a directory read, and a
        // parent builds one child per subfolder — so probing in the constructor put N blocking reads on the
        // dispatcher before the tree could draw. ProbeExpandersAsync does the whole batch off-thread instead;
        // see ReadChildrenAsync.
    }

    /// <summary>The placeholder row under a node not yet read.</summary>
    private FileSystemTreeNode(string name)
    {
        _name    = name;
        FullPath = string.Empty;
        Kind     = TreeNodeKind.Folder;
    }

    /// <summary>Drive or This PC node — readiness is checked asynchronously; no blocking I/O here.</summary>
    public FileSystemTreeNode(string name, string fullPath, TreeNodeKind kind, IVirtualFileSystem vfs)
    {
        _name    = name;
        FullPath = fullPath;
        Kind     = kind;
        _vfs     = vfs;

        if (kind == TreeNodeKind.Drive)
            DriveStatus = DriveStatus.Loading;
        // ThisPc children are added externally (one per row)
        // A row's children (Dummy) are added once its provider has described it
    }

    /// <summary>The folders directly inside <paramref name="path"/>, sorted, as the tree shows them: a folder on disk,
    /// under a mount or on a location — never an archive's insides, which browse in the list. A folder that cannot be
    /// read (gone, access denied, a dropped connection) has none.</summary>
    internal static async Task<IReadOnlyList<string>> ChildFoldersAsync(IVirtualFileSystem vfs, string path,
                                                                        CancellationToken ct = default)
    {
        if (vfs.GetBacking(path) is VirtualBacking.Materialized) return [];
        var names = new List<string>();
        try
        {
            await foreach (var entry in vfs.EnumerateAsync(path, ct))
                if (entry.IsDirectory) names.Add(entry.Name);
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>The read of this node's children, once started; every expand after the first awaits the same one.</summary>
    private Task? _childrenLoad;

    /// <summary>Whether this node's children have been asked for. Explicit rather than inferred from the Dummy child:
    /// the expander is settled asynchronously, so "no children" is ambiguous between "not probed yet" and "genuinely
    /// empty", and expanding on the first reading skipped the load.</summary>
    internal bool ChildrenLoaded => _childrenLoad is not null;

    /// <summary>
    /// Reads this node's children — once; a second expand awaits the first read. Expand-to-path awaits it before it
    /// walks <see cref="Children"/>, so the walk never sees a list still being read, and the read itself is a round
    /// trip on a location that the dispatcher does not wait for.
    /// </summary>
    internal Task LoadChildrenAsync() => _childrenLoad ??= ReadChildrenAsync();

    private async Task ReadChildrenAsync()
    {
        // This PC's rows are its places, added by the browser; there is no folder behind it to read.
        if (_vfs is null || string.IsNullOrEmpty(FullPath)) return;

        IReadOnlyList<string> names;
        using (Timing.Measure($"Tree.LoadChildren {FullPath} (read)"))
            names = await ChildFoldersAsync(_vfs, FullPath);

        var made = new List<FileSystemTreeNode>(names.Count);
        Children.Clear();
        foreach (var name in names)
        {
            // Child paths extend THIS node's path, which under a mount is the virtual one — the listing never
            // surfaces the real location the mount maps onto.
            var child = new FileSystemTreeNode(name, Path.Combine(FullPath, name), _vfs);
            Children.Add(child);
            made.Add(child);
        }
        Timing.Note($"Tree.LoadChildren {FullPath} children", made.Count.ToString());
        _ = ProbeExpandersAsync(made);
    }

    /// <summary>How many folders the expander probe reads at once: enough to hide a location's round trips, few
    /// enough not to queue the thread pool behind a wide folder.</summary>
    private const int ProbeParallelism = 8;

    /// <summary>
    /// Settles the expander on a freshly built batch of sibling nodes, off the dispatcher.
    /// <para>
    /// Whether a folder is expandable is a directory read — cheap warm, tens of milliseconds cold on a spindle, a
    /// round trip on a location, and unbounded behind a mount that resolves somewhere slow. Done inline that cost is
    /// paid N times before the tree draws; done here the children appear immediately and each twisty arrives when
    /// its answer does, <see cref="ProbeParallelism"/> folders at a time. Nothing is claimed before it is known: a
    /// node shows no expander until the probe says it has one, so the tree never offers to open something empty.
    /// </para>
    /// <para>
    /// Awaited from the dispatcher, so the continuation returns there and the collection is mutated on the
    /// thread that owns it — the same shape as This PC's row probe in <c>CheckThisPcItemAsync</c>.
    /// </para>
    /// </summary>
    internal static async Task ProbeExpandersAsync(IReadOnlyList<FileSystemTreeNode> nodes)
    {
        if (nodes.Count == 0) return;

        var expandable = new bool[nodes.Count];
        using (Timing.Measure($"Tree.probeExpanders n={nodes.Count} (background)"))
            await Task.Run(() => Parallel.ForAsync(0, nodes.Count, new ParallelOptions { MaxDegreeOfParallelism = ProbeParallelism },
                async (i, ct) => expandable[i] = await nodes[i].Vfs.HasChildFoldersAsync(nodes[i].FullPath, ct)));

        using var _t = Timing.Measure($"Tree.applyExpanders n={nodes.Count} (UI thread)");
        for (var i = 0; i < nodes.Count; i++)
        {
            // ChildrenLoaded is the load-bearing half of this test. A node can be expanded while its probe
            // is still out — expand-to-path does exactly that on the way to the target — and dropping a
            // Dummy in afterwards leaves a "…" row under a node that has already loaded, with nothing left
            // to clear it because IsExpanded never changes again. Count == 0 alone would also mis-mark a
            // folder that really is empty.
            if (expandable[i] && !nodes[i].ChildrenLoaded && nodes[i].Children.Count == 0)
                nodes[i].Children.Add(Dummy);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
