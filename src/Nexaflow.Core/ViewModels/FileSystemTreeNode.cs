using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Nexaflow.Core.ViewModels;

public enum DriveStatus { Ready, Loading, Unavailable }

// ── Tree node ─────────────────────────────────────────────────────────────────
public enum TreeNodeKind { Folder, Drive, ThisPc }

public class FileSystemTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private string _name;
    private DriveStatus _driveStatus;

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

    /// <summary>Emoji glyph used in the tree item template.</summary>
    public string Icon => Kind switch
    {
        TreeNodeKind.ThisPc => "🖥",
        TreeNodeKind.Drive  => DriveIcon(FullPath),
        _                   => "📁"
    };

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

        if (!isDummy && Directory.Exists(fullPath) && HasSubDirectoriesSafe(fullPath))
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

    internal static bool HasSubDirectoriesSafe(string path)
    {
        try { return Directory.EnumerateDirectories(path).Any(); }
        catch { return false; }
    }

    private void LoadChildren()
    {
        if (Children.Count == 1 && Children[0] == Dummy)
        {
            Children.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(FullPath)
                                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    Children.Add(new FileSystemTreeNode(Path.GetFileName(dir), dir));
                }
            }
            catch { /* access denied etc. */ }
        }
    }

    private static string DriveIcon(string root)
    {
        try
        {
            var info = new DriveInfo(root);
            return info.DriveType switch
            {
                DriveType.CDRom    => "💿",
                DriveType.Removable => "🔌",
                DriveType.Network  => "🌐",
                DriveType.Fixed    => NativeMethods.IsNoSeekPenalty(root) ? "💾" : "💽",
                _                  => "💽"
            };
        }
        catch { return "💽"; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
