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
    internal static readonly FileSystemTreeNode Dummy = new("…", string.Empty, isDummy: true);
    private readonly bool _isDummy;

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
            if (value) LoadChildren();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Regular folder node.</summary>
    public FileSystemTreeNode(string name, string fullPath, bool isDummy = false)
    {
        _name    = name;
        FullPath = fullPath;
        Kind     = TreeNodeKind.Folder;
        _isDummy = isDummy;

        // Deliberately no I/O here. Deciding whether this node gets an expander costs two directory
        // opens, and a parent builds one child per subfolder — so probing in the constructor put 2xN
        // blocking opens on the dispatcher before the tree could draw. ProbeExpandersAsync does the whole
        // batch off-thread instead; see LoadChildren.
    }

    /// <summary>Drive or This PC node — readiness is checked asynchronously; no blocking I/O here.</summary>
    public FileSystemTreeNode(string name, string fullPath, TreeNodeKind kind)
    {
        _name    = name;
        FullPath = fullPath;
        Kind     = kind;

        if (kind == TreeNodeKind.Drive)
            DriveStatus = DriveStatus.Loading;
        // ThisPc children are added externally (one per drive)
        // Drive children (Dummy) are added by CheckDriveAsync once IsReady is confirmed
    }

    /// <summary>The VFS the tree resolves through, so a mounted node expands like any other folder.
    /// Settable for tests; defaults to the process singleton.</summary>
    internal static IVirtualFileSystem Vfs { get; set; } = VirtualFileSystem.Instance;

    /// <summary>The real directory behind a (possibly mounted) node path, or null if there isn't one.</summary>
    internal static string? RealDirOf(string path)
        => Vfs.TryResolveReal(path) is { } real && Directory.Exists(real) ? real : null;

    internal static bool DirectoryExistsSafe(string path) => RealDirOf(path) is not null;

    internal static bool HasSubDirectoriesSafe(string path)
    {
        if (RealDirOf(path) is not { } real) return false;
        try { return Directory.EnumerateDirectories(real).Any(); }
        catch { return false; }
    }

    /// <summary>Whether this node's real children have been read. Explicit rather than inferred from the
    /// Dummy child: now that the expander is settled asynchronously, "no children" is ambiguous between
    /// "not probed yet" and "genuinely empty", and expanding on the first reading skipped the load.</summary>
    internal bool ChildrenLoaded { get; private set; }

    private void LoadChildren()
    {
        if (ChildrenLoaded) return;
        ChildrenLoaded = true;

        // The child list itself stays synchronous: it is ONE directory read, and TryExpandTo walks
        // Children the instant it sets IsExpanded, so an empty collection here would break expand-to-path.
        var made = new List<FileSystemTreeNode>();
        using (Timing.Measure($"Tree.LoadChildren {FullPath} (UI thread)"))
        {
            Children.Clear();
            if (RealDirOf(FullPath) is not { } real) return;
            try
            {
                foreach (var dir in Directory.GetDirectories(real)
                                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    // Child paths extend THIS node's path, which under a mount is the virtual one —
                    // taking `dir` verbatim would leak the real location into the tree.
                    var name = Path.GetFileName(dir);
                    var child = new FileSystemTreeNode(name, Path.Combine(FullPath, name));
                    Children.Add(child);
                    made.Add(child);
                }
            }
            catch { /* access denied etc. */ }
        }
        Timing.Note($"Tree.LoadChildren {FullPath} children", made.Count.ToString());
        _ = ProbeExpandersAsync(made);
    }

    /// <summary>
    /// Settles the expander on a freshly built batch of sibling nodes, off the dispatcher.
    /// <para>
    /// Whether a folder is expandable is two directory opens — cheap warm, tens of milliseconds cold on a
    /// spindle, and unbounded behind a mount that resolves somewhere slow. Done inline that cost is paid
    /// N times before the tree draws; done here the children appear immediately and each twisty arrives
    /// when its answer does. Nothing is claimed before it is known: a node shows no expander until the
    /// probe says it has one, so the tree never offers to open something empty.
    /// </para>
    /// <para>
    /// Awaited from the dispatcher, so the continuation returns there and the collection is mutated on the
    /// thread that owns it — the same shape as the drive probe in <c>CheckDriveAsync</c>.
    /// </para>
    /// </summary>
    internal static async Task ProbeExpandersAsync(IReadOnlyList<FileSystemTreeNode> nodes)
    {
        if (nodes.Count == 0) return;

        var paths = new string[nodes.Count];
        for (var i = 0; i < nodes.Count; i++) paths[i] = nodes[i].FullPath;

        bool[] expandable;
        using (Timing.Measure($"Tree.probeExpanders n={paths.Length} (background)"))
            expandable = await Task.Run(
                () => Array.ConvertAll(paths, p => DirectoryExistsSafe(p) && HasSubDirectoriesSafe(p)));

        using var _t = Timing.Measure($"Tree.applyExpanders n={paths.Length} (UI thread)");
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
