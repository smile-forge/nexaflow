using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.ViewModels;

/// <summary>
/// The Compressed inspector: archive metadata on the left, a flattened directory tree on the right, and
/// an action bar. Reads everything through <see cref="IVirtualFileSystem"/> so it works for any
/// registered format.
/// </summary>
public sealed partial class CompressedViewModel : ObservableObject, IPageViewModel
{
    private readonly IShellServices _shell;
    private readonly IVirtualFileSystem _vfs;

    [ObservableProperty] private string _archivePath = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;

    // ── Metadata pane ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _format = string.Empty;
    [ObservableProperty] private string _entryCountText = string.Empty;
    [ObservableProperty] private string _totalSizeText = string.Empty;
    [ObservableProperty] private string _compressedSizeText = string.Empty;
    [ObservableProperty] private string _ratioText = string.Empty;
    [ObservableProperty] private string _encryptionText = string.Empty;
    [ObservableProperty] private string _signatureText = "Unsigned";
    [ObservableProperty] private string? _comment;
    [ObservableProperty] private bool _hasComment;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    // ── Capability gates (drive the action-bar button enablement) ──────────────
    [ObservableProperty] private bool _canModify;
    [ObservableProperty] private bool _canEncrypt;
    [ObservableProperty] private bool _isRecognised;

    /// <summary>Flattened, expand-aware list of visible rows.</summary>
    public ObservableCollection<ArchiveNode> VisibleRows { get; } = [];

    private ArchiveNode? _root;

    public CompressedViewModel(string path, IShellServices shell, IVirtualFileSystem vfs)
    {
        _shell = shell;
        _vfs = vfs;
        ArchivePath = path;
        FileName = string.IsNullOrEmpty(path) ? "Archive" : Path.GetFileName(path);
        Load();
    }

    // ── Loading ────────────────────────────────────────────────────────────────

    private void Load()
    {
        VisibleRows.Clear();
        _root = null;

        ArchiveSummary? summary = null;
        try { summary = _vfs.DescribeArchive(ArchivePath); }
        catch (Exception ex) { StatusText = $"Could not read archive: {ex.Message}"; }

        if (summary is null)
        {
            IsRecognised = false;
            Format = "Unknown";
            StatusText = string.IsNullOrEmpty(StatusText)
                ? "No installed handler recognises this archive format."
                : StatusText;
            return;
        }

        IsRecognised = true;
        Format = summary.Format;
        CanModify = summary.Capabilities.HasFlag(ArchiveCapabilities.Modify);
        CanEncrypt = summary.Capabilities.HasFlag(ArchiveCapabilities.Encrypt);

        var files = summary.Entries.Where(e => !e.IsDirectory).ToList();
        long total = files.Sum(e => e.Size);
        long comp = files.Sum(e => Math.Max(0, e.CompressedSize));
        EntryCountText = $"{files.Count} file{(files.Count == 1 ? "" : "s")}";
        TotalSizeText = ArchiveNode.FormatBytes(total);
        CompressedSizeText = ArchiveNode.FormatBytes(comp);
        RatioText = total > 0 ? $"{100.0 * (1.0 - (double)comp / total):0.#}% smaller" : "—";
        EncryptionText = summary.IsEncrypted ? "Encrypted" : "Not encrypted";
        Comment = summary.Comment;
        HasComment = !string.IsNullOrWhiteSpace(summary.Comment);

        _root = BuildTree(summary.Entries);
        // Auto-expand the first level for an immediately useful view.
        foreach (var child in _root.Children) child.IsExpanded = child.IsFolder && _root.Children.Count <= 12;
        RebuildVisibleRows();
    }

    /// <summary>Builds a directory tree from the flat entry list, summing folder sizes from descendants.</summary>
    private static ArchiveNode BuildTree(IReadOnlyList<VirtualEntry> entries)
    {
        var root = new ArchiveNode { Name = "", ArchivePath = "", IsFolder = true, Depth = -1 };
        var folders = new Dictionary<string, ArchiveNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        ArchiveNode EnsureFolder(string path, int depth)
        {
            if (folders.TryGetValue(path, out var existing)) return existing;
            var slash = path.LastIndexOf('/');
            var parentPath = slash < 0 ? "" : path[..slash];
            var name = slash < 0 ? path : path[(slash + 1)..];
            var parent = EnsureFolder(parentPath, depth - 1);
            var node = new ArchiveNode { Name = name, ArchivePath = path, IsFolder = true, Depth = parent.Depth + 1 };
            parent.Children.Add(node);
            folders[path] = node;
            return node;
        }

        foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var full = e.Name.Replace('\\', '/').Trim('/');
            if (full.Length == 0) continue;
            var slash = full.LastIndexOf('/');
            var parentPath = slash < 0 ? "" : full[..slash];
            var name = slash < 0 ? full : full[(slash + 1)..];
            var parent = EnsureFolder(parentPath, parentPath.Count(c => c == '/'));

            if (e.IsDirectory)
            {
                EnsureFolder(full, full.Count(c => c == '/'));
            }
            else
            {
                parent.Children.Add(new ArchiveNode
                {
                    Name = name,
                    ArchivePath = full,
                    IsFolder = false,
                    Depth = parent.Depth + 1,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    Modified = e.Modified,
                });
            }
        }

        SumFolderSizes(root);
        SortChildren(root);
        return root;
    }

    private static (long size, long comp) SumFolderSizes(ArchiveNode node)
    {
        if (!node.IsFolder) return (node.Size, Math.Max(0, node.CompressedSize));
        long s = 0, c = 0;
        foreach (var child in node.Children) { var (cs, cc) = SumFolderSizes(child); s += cs; c += cc; }
        node.Size = s; node.CompressedSize = c;
        return (s, c);
    }

    private static void SortChildren(ArchiveNode node)
    {
        node.Children.Sort((a, b) =>
            a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var c in node.Children) SortChildren(c);
    }

    private void RebuildVisibleRows()
    {
        VisibleRows.Clear();
        if (_root is null) return;
        void Walk(ArchiveNode node)
        {
            foreach (var child in node.Children)
            {
                VisibleRows.Add(child);
                if (child.IsFolder && child.IsExpanded) Walk(child);
            }
        }
        Walk(_root);
    }

    // ── Row interaction ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateRow(ArchiveNode? node)
    {
        if (node is null) return;
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            RebuildVisibleRows();
        }
        // File activation (open inner file in its viewer) arrives with the VFS-aware open routing.
    }

    // ── Action bar ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsRecognised))]
    private async Task Extract()
    {
        var dest = await _shell.PickFolderAsync(Path.GetDirectoryName(ArchivePath));
        if (string.IsNullOrEmpty(dest)) return;
        var path = ArchivePath;
        await RunBusy("Extracting…", () => _vfs.ExtractAll(path, dest));
        StatusText = $"Extracted to {dest}";
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task AddFile()
    {
        var source = await _shell.PickOpenFileAsync();
        if (string.IsNullOrEmpty(source)) return;
        await AddSourcesAsync([source]);
    }

    [RelayCommand(CanExecute = nameof(IsRecognised))]
    private async Task Test()
    {
        var summary = _vfs.DescribeArchive(ArchivePath);
        if (summary is null) { StatusText = "Nothing to test."; return; }
        var path = ArchivePath;
        int ok = 0, bad = 0;
        await RunBusy("Testing…", () =>
        {
            foreach (var e in summary.Entries.Where(e => !e.IsDirectory))
            {
                try
                {
                    using var s = _vfs.OpenRead(Path.Combine(path, e.Name.Replace('/', Path.DirectorySeparatorChar)));
                    s.CopyTo(Stream.Null);
                    ok++;
                }
                catch { bad++; }
            }
        });
        StatusText = bad == 0 ? $"OK — {ok} entr{(ok == 1 ? "y" : "ies")} verified." : $"{bad} of {ok + bad} entries failed.";
    }

    /// <summary>Adds dropped files to the archive (drag-and-drop onto the page).</summary>
    public async Task AddSourcesAsync(IReadOnlyList<string> sources)
    {
        if (!CanModify) { StatusText = "This format is read-only."; return; }
        var files = sources.Where(File.Exists)
            .Select(s => (SourcePath: s, EntryName: Path.GetFileName(s)))
            .ToList();
        if (files.Count == 0) return;
        var path = ArchivePath;
        await RunBusy("Adding…", () => _vfs.AddFiles(path, files));
        Load();
        StatusText = $"Added {files.Count} file{(files.Count == 1 ? "" : "s")}.";
    }

    private async Task RunBusy(string status, Action work)
    {
        IsBusy = true;
        StatusText = status;
        try { await Task.Run(work); }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsBusy = false; }
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────────

    public string GetContext()
        => IsRecognised
            ? $"Compressed archive '{FileName}' ({Format}) — {EntryCountText}, {TotalSizeText} uncompressed."
            : $"Compressed archive '{FileName}' — unrecognised format.";
}
