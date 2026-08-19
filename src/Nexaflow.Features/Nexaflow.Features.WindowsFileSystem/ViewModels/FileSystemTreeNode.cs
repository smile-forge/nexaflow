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

        // Two blocking directory probes per node, and LoadChildren constructs one node per subfolder —
        // so expanding a folder costs 2xN synchronous opens before the UI can draw.
        using (Timing.Measure($"Tree.node-probe {fullPath}"))
            if (!isDummy && DirectoryExistsSafe(fullPath) && HasSubDirectoriesSafe(fullPath))
                Children.Add(Dummy);
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

    private void LoadChildren()
    {
        if (Children.Count == 1 && Children[0] == Dummy)
        {
            using var _t = Timing.Measure($"Tree.LoadChildren {FullPath} (UI thread)");
            var made = 0;
            Children.Clear();
            if (RealDirOf(FullPath) is not { } real) return;
            try
            {
                foreach (var dir in Directory.GetDirectories(real)
                                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    made++;
                    // Child paths extend THIS node's path, which under a mount is the virtual one —
                    // taking `dir` verbatim would leak the real location into the tree.
                    var name = Path.GetFileName(dir);
                    Children.Add(new FileSystemTreeNode(name, Path.Combine(FullPath, name)));
                }
            }
            catch { /* access denied etc. */ }
            finally { Timing.Note($"Tree.LoadChildren {FullPath} children", made.ToString()); }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
