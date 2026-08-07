using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
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
    private readonly Services.ArchiveSignatureService _signer = new();
    private readonly IArchiveEncryptor? _encryptor;

    // ── Overlays (choice picker + password) ────────────────────────────────────
    [ObservableProperty] private bool _choiceVisible;
    [ObservableProperty] private string _choiceTitle = string.Empty;
    [ObservableProperty] private bool _choiceAsGrid;   // Convert: icon-badge grid. Recompress: plain list.
    [ObservableProperty] private bool _choiceAsList = true;
    public ObservableCollection<ChoiceOption> ChoiceOptions { get; } = [];
    private Func<string, Task>? _choiceAction;

    [ObservableProperty] private bool _passwordVisible;
    [ObservableProperty] private string _passwordTitle = string.Empty;
    private Func<string, Task>? _passwordAction;

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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFileCommand))]
    private bool _canModify;
    [ObservableProperty] private bool _canEncrypt;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecompressCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(EncryptCommand))]
    private bool _isRecognised;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _hasSignature;

    /// <summary>Flattened, expand-aware list of visible rows.</summary>
    public ObservableCollection<ArchiveNode> VisibleRows { get; } = [];

    private ArchiveNode? _root;
    private int _fileCount;

    public CompressedViewModel(string path, IShellServices shell, IVirtualFileSystem vfs)
    {
        _shell = shell;
        _vfs = vfs;
        _encryptor = shell.DiscoverImplementations<IArchiveEncryptor>()
            .Select(t => { try { return Activator.CreateInstance(t) as IArchiveEncryptor; } catch { return null; } })
            .FirstOrDefault(e => e is not null);
        ArchivePath = path;
        FileName = string.IsNullOrEmpty(path) ? "Archive" : Path.GetFileName(path);
        Load();
    }

    // ── Loading ────────────────────────────────────────────────────────────────

    private void Load()
    {
        // Every action that reaches here rewrites the archive (add, recompress), so the tree is rebuilt
        // from scratch — the search's node references would point at entries that no longer exist.
        ClearSearch();

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
        _fileCount = files.Count;
        long total = files.Sum(e => e.Size);
        long comp = files.Sum(e => Math.Max(0, e.CompressedSize));
        EntryCountText = $"{files.Count} file{(files.Count == 1 ? "" : "s")}";
        TotalSizeText = ArchiveNode.FormatBytes(total);
        CompressedSizeText = ArchiveNode.FormatBytes(comp);
        RatioText = total > 0 ? $"{100.0 * (1.0 - (double)comp / total):0.#}% smaller" : "—";
        EncryptionText = summary.IsEncrypted ? "Encrypted" : "Not encrypted";
        HasSignature = _signer.HasSignature(ArchivePath);
        SignatureText = HasSignature ? "Signed (Sig Check to confirm)" : "Unsigned";
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
                // An active "?" search narrows this to the hits and the folders on the way to them.
                if (_searchVisible is not null && !_searchVisible.Contains(child)) continue;
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
            return;
        }
        // Open the inner file in its normal viewer — the shell routes it through the VFS-aware opener.
        var full = Path.Combine(ArchivePath, node.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
        _shell.HandleObject(full);
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
        var source = await _shell.PickOpenFileAsync(null, Path.GetDirectoryName(ArchivePath));
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

    [RelayCommand(CanExecute = nameof(IsRecognised))]
    private async Task Sign()
    {
        var path = ArchivePath;
        string fingerprint = string.Empty;
        await RunBusy("Signing…", () => fingerprint = _signer.Sign(path));
        HasSignature = true;
        SignatureText = $"Signed · {fingerprint}";
        StatusText = "Wrote a detached signature (.sig).";
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task Verify()
    {
        var path = ArchivePath;
        if (!_signer.HasSignature(path)) { SignatureText = "Unsigned"; StatusText = "No .sig signature alongside this archive."; return; }

        (bool Valid, string? Fingerprint) result = default;
        await RunBusy("Verifying…", () => result = _signer.Verify(path));
        SignatureText = result.Valid ? $"Verified ✓ · {result.Fingerprint}" : "Signature INVALID";
        StatusText = result.Valid ? "Signature matches the archive." : "Signature does not match — the archive may have changed.";
    }

    // ── Recompress / Convert / Encrypt / Decrypt ──────────────────────────────────

    private bool CanConvert => IsRecognised && ConvertTargets().Count > 0;
    private bool CanEncryptArchive => IsRecognised && _encryptor is { } e && e.CanEncrypt(ArchivePath);
    private bool CanDecryptArchive => _encryptor is { } e && e.CanEncrypt(ArchivePath);
    private bool CanVerify => IsRecognised && HasSignature;

    // Recompress applies a compression level — meaningless for an uncompressed tar.
    private bool CanRecompress => IsRecognised && _vfs.CanCreate(Path.GetFileName(ArchivePath))
        && !Path.GetExtension(ArchivePath).Equals(".tar", StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanRecompress))]
    private void Recompress() => ShowChoice("Recompress — choose a level",
        [new ChoiceOption("Store (no compression)", "Store (no compression)", "0"), new ChoiceOption("Fast", "Fast", "3"),
         new ChoiceOption("Optimal", "Optimal", "6"), new ChoiceOption("Smallest", "Smallest", "9")],
        async choice =>
    {
        int level = choice.StartsWith("Store") ? 0 : choice.StartsWith("Fast") ? 3 : choice.StartsWith("Smallest") ? 9 : 6;
        var path = ArchivePath;
        await RunBusy("Recompressing…", () => _vfs.Repackage(path, path, new ArchiveWriteOptions { CompressionLevel = level }));
        Load();
        StatusText = $"Recompressed ({choice}).";
    });

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private void Convert() => ShowChoice("Convert — target format",
        ConvertTargets().Select(f => new ChoiceOption(f, f, IconForFormat(f))), async fmt =>
    {
        var dest = SiblingPath($" (as {fmt.Replace('.', '_')})", "." + fmt);
        var path = ArchivePath;
        await RunBusy($"Converting to {fmt}…", () => _vfs.Repackage(path, dest, null));
        StatusText = $"Converted → {Path.GetFileName(dest)}";
    }, asGrid: true);

    [RelayCommand(CanExecute = nameof(CanEncryptArchive))]
    private void Encrypt() => ShowPassword("Encrypt — set a password", async pwd =>
    {
        var dest = SiblingPath(" (encrypted)", ".zip");
        var path = ArchivePath;
        await RunBusy("Encrypting…", () => _encryptor!.Encrypt(path, dest, pwd));
        StatusText = $"Encrypted copy written: {Path.GetFileName(dest)}";
    });

    [RelayCommand(CanExecute = nameof(CanDecryptArchive))]
    private void Decrypt() => ShowPassword("Decrypt — enter the password", async pwd =>
    {
        var dest = SiblingPath(" (decrypted)", ".zip");
        var path = ArchivePath;
        try
        {
            await RunBusy("Decrypting…", () => _encryptor!.Decrypt(path, dest, pwd));
            StatusText = $"Decrypted copy written: {Path.GetFileName(dest)}";
        }
        catch { StatusText = "Decrypt failed — wrong password or not an encrypted zip."; }
    });

    private IReadOnlyList<string> ConvertTargets()
    {
        var current = Path.GetFileName(ArchivePath).ToLowerInvariant();

        // Multi-entry containers are always valid targets; single-stream codecs only when the archive
        // holds exactly one file (a .gz/.zst/.br stream can't carry a directory of files).
        string[] multi  = ["zip", "tar", "tar.gz", "tar.bz2", "tar.zst", "tar.lz4", "tar.br"];
        string[] single = _fileCount == 1 ? ["gz", "bz2", "zst", "lz4", "br"] : [];

        return multi.Concat(single)
            .Where(f => !current.EndsWith("." + f, StringComparison.Ordinal) && _vfs.CanCreate("x." + f))
            .ToList();
    }

    /// <summary>The short type badge shown on a format button (the codec after the last dot): tar.zst → ZST.</summary>
    private static string IconForFormat(string fmt) => fmt.Split('.')[^1].ToUpperInvariant();

    private string SiblingPath(string suffix, string ext)
    {
        var dir = Path.GetDirectoryName(ArchivePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(ArchivePath);
        var dest = Path.Combine(dir, baseName + suffix + ext);
        for (int n = 1; File.Exists(dest); n++) dest = Path.Combine(dir, $"{baseName}{suffix} ({n}){ext}");
        return dest;
    }

    // ── Overlay plumbing ───────────────────────────────────────────────────────

    private void ShowChoice(string title, IEnumerable<ChoiceOption> options, Func<string, Task> action,
                            bool asGrid = false)
    {
        ChoiceTitle = title;
        ChoiceOptions.Clear();
        foreach (var o in options) ChoiceOptions.Add(o);
        if (ChoiceOptions.Count == 0) { StatusText = "No applicable options."; return; }
        ChoiceAsGrid = asGrid;
        ChoiceAsList = !asGrid;
        _choiceAction = action;
        ChoiceVisible = true;
    }

    [RelayCommand]
    private async Task PickChoice(string option)
    {
        ChoiceVisible = false;
        var action = _choiceAction; _choiceAction = null;
        if (action is not null) await action(option);
    }

    [RelayCommand]
    private void CancelChoice() { ChoiceVisible = false; _choiceAction = null; }

    private void ShowPassword(string title, Func<string, Task> action)
    {
        PasswordTitle = title;
        _passwordAction = action;
        PasswordVisible = true;
    }

    /// <summary>Invoked from the view's password overlay (a PasswordBox can't be data-bound).</summary>
    public async Task SubmitPasswordAsync(string password)
    {
        PasswordVisible = false;
        var action = _passwordAction; _passwordAction = null;
        if (action is not null && !string.IsNullOrEmpty(password)) await action(password);
    }

    [RelayCommand]
    private void CancelPassword() { PasswordVisible = false; _passwordAction = null; }

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
    {
        if (!IsRecognised)
            return $"Compressed archive '{FileName}' — unrecognised format"
                 + (string.IsNullOrEmpty(StatusText) ? "." : $" ({StatusText}).");

        var sb = new StringBuilder();
        sb.Append($"Compressed archive '{FileName}' ({Format}) — {EntryCountText}, {TotalSizeText} uncompressed");
        if (!string.IsNullOrEmpty(CompressedSizeText)) sb.Append($" / {CompressedSizeText} compressed");
        if (!string.IsNullOrEmpty(RatioText) && RatioText != "—") sb.Append($" ({RatioText})");
        sb.Append($". {EncryptionText}; {SignatureText}.");
        if (HasComment) sb.Append($" Comment: \"{Comment}\".");

        var outline = TopEntriesOutline();
        if (outline.Length > 0) sb.Append($"\nTop-level entries:\n{outline}");
        sb.Append("\n(Use list_entries for the full manifest, read_entry to read one entry's text, "
                + "or test_archive to check integrity.)");
        return sb.ToString();
    }

    /// <summary>The archive file path — the scope this page's tools act within (aspect-4 disambiguation).</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(ArchivePath) ? null : ArchivePath;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) — the manifest + entry content the model couldn't reach before ──
        new DelegateClientTool(
            "list_entries",
            "List the archive's entries — path, uncompressed size, compressed size — the full manifest the "
          + "one-line summary only counts. Directories are marked <dir>. Capped for very large archives.",
            [new ClientToolParameter("prefix", "Optional in-archive path prefix to filter by (e.g. \"docs/\").", Required: false)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(
                IsRecognised ? ListEntries(ToolArgs.Str(args, "prefix", "path", "filter"))
                             : ToolResult.Error("No recognised archive is loaded."))),

        new DelegateClientTool(
            "read_entry",
            "Read the text content of one file entry inside the archive by its path (e.g. \"docs/readme.md\"). "
          + "Reads through the archive without extracting to disk; binary, oversized, or encrypted entries are refused.",
            [new ClientToolParameter("path", "The entry's full path inside the archive, forward-slash separated.")],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (!IsRecognised) return Task.FromResult(ToolResult.Error("No recognised archive is loaded."));
                var entryPath = ToolArgs.Str(args, "path", "entry", "name");
                return Task.FromResult(string.IsNullOrEmpty(entryPath)
                    ? ToolResult.Error("No 'path' provided.")
                    : ReadEntryText(entryPath));
            }),

        new DelegateClientTool(
            "test_archive",
            "Verify the archive's integrity by reading every entry back — reports how many are readable and "
          + "how many failed. Read-only; nothing is written or extracted.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(
                IsRecognised ? TestArchive() : ToolResult.Error("No recognised archive is loaded."))),
    ];

    // ── AI read-tool helpers ─────────────────────────────────────────────────────

    private const int MaxListedEntries = 1000;
    private const long MaxEntryReadBytes = 256 * 1024;

    /// <summary>Top-level entry outline for <see cref="GetContext"/> — folders (as sorted) first, size per
    /// node, capped so a huge archive doesn't flood the prompt.</summary>
    private string TopEntriesOutline()
    {
        if (_root is null || _root.Children.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        const int cap = 20;
        var shown = 0;
        foreach (var node in _root.Children)
        {
            if (shown == cap) { sb.Append($"  … ({_root.Children.Count - cap} more)\n"); break; }
            shown++;
            sb.Append(node.IsFolder
                ? $"  {node.Name}/  ({ArchiveNode.FormatBytes(node.Size)})\n"
                : $"  {node.Name}  {ArchiveNode.FormatBytes(node.Size)}\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    private ToolResult ListEntries(string? prefix)
    {
        ArchiveSummary? summary;
        try { summary = _vfs.DescribeArchive(ArchivePath); }
        catch (Exception ex) { return ToolResult.Error($"Could not read archive: {ex.Message}"); }
        if (summary is null) return ToolResult.Error("No recognised archive is loaded.");

        var norm = prefix?.Replace('\\', '/').TrimStart('/');
        var entries = summary.Entries
            .Where(e => string.IsNullOrEmpty(norm)
                     || e.Name.Replace('\\', '/').StartsWith(norm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
            return ToolResult.Ok("no matching entries",
                string.IsNullOrEmpty(norm) ? "The archive has no entries." : $"No entries under '{prefix}'.");

        var fileCount = summary.Entries.Count(e => !e.IsDirectory);
        var sb = new StringBuilder();
        sb.Append($"{summary.Format} archive — {fileCount} file(s)");
        if (summary.IsEncrypted) sb.Append(" (encrypted)");
        sb.Append(". path | size | compressed\n");

        foreach (var e in entries.Take(MaxListedEntries))
        {
            var path = e.Name.Replace('\\', '/');
            sb.Append(e.IsDirectory
                ? $"  {path.TrimEnd('/')}/  <dir>\n"
                : $"  {path}  {ArchiveNode.FormatBytes(e.Size)}  {ArchiveNode.FormatBytes(e.CompressedSize)}\n");
        }
        if (entries.Count > MaxListedEntries) sb.Append($"  … ({entries.Count - MaxListedEntries} more)\n");

        var listed = Math.Min(entries.Count, MaxListedEntries);
        return ToolResult.Ok($"{listed} entr{(listed == 1 ? "y" : "ies")} listed", sb.ToString().TrimEnd('\n'));
    }

    private ToolResult ReadEntryText(string entryPath)
    {
        var norm = entryPath.Replace('\\', '/').Trim('/');
        ArchiveSummary? summary;
        try { summary = _vfs.DescribeArchive(ArchivePath); }
        catch (Exception ex) { return ToolResult.Error($"Could not read archive: {ex.Message}"); }
        if (summary is null) return ToolResult.Error("No recognised archive is loaded.");
        if (summary.IsEncrypted)
            return ToolResult.Error("The archive is encrypted — entries can't be read without decrypting it first.");

        var match = summary.Entries.FirstOrDefault(e => !e.IsDirectory
            && e.Name.Replace('\\', '/').Trim('/').Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (match is null) return ToolResult.Error($"No file entry '{entryPath}' in the archive.");
        if (match.Size > MaxEntryReadBytes)
            return ToolResult.Error(
                $"Entry '{norm}' is {ArchiveNode.FormatBytes(match.Size)} — too large to read inline "
              + $"(limit {ArchiveNode.FormatBytes(MaxEntryReadBytes)}). Extract it instead.");

        byte[] bytes;
        try
        {
            var full = Path.Combine(ArchivePath, norm.Replace('/', Path.DirectorySeparatorChar));
            using var stream = _vfs.OpenRead(full);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch (Exception ex) { return ToolResult.Error($"Could not read '{norm}': {ex.Message}"); }

        if (LooksBinary(bytes))
            return ToolResult.Error(
                $"Entry '{norm}' looks binary ({ArchiveNode.FormatBytes(bytes.LongLength)}) — not shown as text.");

        var text = DecodeText(bytes);
        return ToolResult.Ok($"read {norm} ({ArchiveNode.FormatBytes(bytes.LongLength)})",
            text.Length == 0 ? "(empty entry)" : text);
    }

    private ToolResult TestArchive()
    {
        ArchiveSummary? summary;
        try { summary = _vfs.DescribeArchive(ArchivePath); }
        catch (Exception ex) { return ToolResult.Error($"Could not read archive: {ex.Message}"); }
        if (summary is null) return ToolResult.Error("No recognised archive is loaded.");

        int ok = 0, bad = 0;
        foreach (var e in summary.Entries.Where(e => !e.IsDirectory))
        {
            try
            {
                using var s = _vfs.OpenRead(Path.Combine(ArchivePath, e.Name.Replace('/', Path.DirectorySeparatorChar)));
                s.CopyTo(Stream.Null);
                ok++;
            }
            catch { bad++; }
        }
        var msg = bad == 0
            ? $"OK — all {ok} entr{(ok == 1 ? "y" : "ies")} read back cleanly."
            : $"{bad} of {ok + bad} entries failed to read — the archive may be corrupt.";
        return ToolResult.Ok(bad == 0 ? $"{ok} entries verified" : $"{bad} failed", msg);
    }

    /// <summary>NUL-byte heuristic: an archived text file has none in its first several KB.</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 8000);
        for (var i = 0; i < n; i++) if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>UTF-8 decode, dropping a leading BOM so it doesn't surface as a stray character.</summary>
    private static string DecodeText(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}

/// <summary>One option in the choice overlay: an icon badge over a label; <see cref="Value"/> is passed to
/// the picked action.</summary>
public sealed record ChoiceOption(string Label, string Value, string Icon);
