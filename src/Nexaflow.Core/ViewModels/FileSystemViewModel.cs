using Nexaflow.Features.WinFileSystem.FileActions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Features.Web.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageView   = Nexaflow.Features.Images.Views.ImageView;
using HtmlView    = Nexaflow.Features.Web.Views.HtmlView;
using MarkdownView = Nexaflow.Features.Markdown.Views.MarkdownView;

namespace Nexaflow.Core.ViewModels;

// ── Tree node ─────────────────────────────────────────────────────────────────

public enum TreeNodeKind { Folder, Drive, ThisPc }

public class FileSystemTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string       Name     { get; }
    public string       FullPath { get; }
    public TreeNodeKind Kind     { get; }

    /// <summary>Emoji glyph used in the tree item template.</summary>
    public string Icon => Kind switch
    {
        TreeNodeKind.ThisPc => "🖥",
        TreeNodeKind.Drive  => DriveIcon(FullPath),
        _                   => "📁"
    };

    public ObservableCollection<FileSystemTreeNode> Children { get; } = [];

    // Dummy child keeps the expand arrow visible before real load
    private static readonly FileSystemTreeNode Dummy = new("…", string.Empty, isDummy: true);
    private readonly bool _isDummy;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
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
        Name     = name;
        FullPath = fullPath;
        Kind     = TreeNodeKind.Folder;
        _isDummy = isDummy;

        if (!isDummy && Directory.Exists(fullPath) && HasSubDirectories(fullPath))
            Children.Add(Dummy);
    }

    /// <summary>Drive or This PC node.</summary>
    public FileSystemTreeNode(string name, string fullPath, TreeNodeKind kind)
    {
        Name     = name;
        FullPath = fullPath;
        Kind     = kind;

        if (kind == TreeNodeKind.Drive && Directory.Exists(fullPath) && HasSubDirectories(fullPath))
            Children.Add(Dummy);
        // ThisPc children are added externally (one per drive)
    }

    private static bool HasSubDirectories(string path)
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
                _                  => "💾"
            };
        }
        catch { return "💾"; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── File / folder entry shown in the right panel ──────────────────────────────

public class FileSystemEntry
{
    public string   Name        { get; init; } = string.Empty;
    public string   FullPath    { get; init; } = string.Empty;
    public bool     IsDirectory { get; init; }
    public bool     IsDrive     { get; init; }
    public long     SizeBytes   { get; init; }
    public DateTime Modified    { get; init; }

    public string TypeLabel => IsDirectory ? "Folder" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public string SizeLabel => IsDirectory ? string.Empty : FormatSize(SizeBytes);
    public string ModifiedLabel => Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024          => $"{bytes} B",
        < 1024 * 1024   => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _               => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class FileSystemViewModel : ObservableObject, IQueryHandler, IContextProvider, IActionExecutor
{
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private FileSystemEntry? _selectedEntry;
    [ObservableProperty] private bool   _aiSummaryVisible;
    [ObservableProperty] private bool   _aiSummaryIsDirty;
    [ObservableProperty] private string _entryCountLabel = string.Empty;

    // ── File action strip ─────────────────────────────────────────────────────
    private readonly FileActionRegistry _actionRegistry;
    public ObservableCollection<FileActionViewModel> FileActions { get; } = [];

    // Debounce timer — action strip is only rebuilt after input has been idle
    // for a short interval, so rapid selection changes (including double-clicks)
    // never stall the UI thread.
    private readonly DispatcherTimer _actionDebounceTimer = new(DispatcherPriority.Background);
    private IReadOnlyList<FileSystemEntry> _pendingSelection = [];

    // ── Shift state (forwarded from view key events) ──────────────────────────
    [ObservableProperty] private bool _shiftHeld;

    partial void OnShiftHeldChanged(bool value)
    {
        foreach (var fa in FileActions)
            fa.ShiftHeld = value;
    }

    // ── Confirmation overlay ──────────────────────────────────────────────────
    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = "Are you sure?";
    [ObservableProperty] private string _confirmationPrompt = string.Empty;

    private Action? _pendingConfirm;
    private Action? _pendingCancel;

    public void ShowConfirmation(string prompt, Action onConfirm, Action onCancel)
        => ShowConfirmation("Are you sure?", prompt, onConfirm, onCancel);

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action onCancel)
    {
        _pendingConfirm     = onConfirm;
        _pendingCancel      = onCancel;
        ConfirmationTitle   = title;
        ConfirmationPrompt  = prompt;
        ConfirmationVisible = true;
    }

    [RelayCommand]
    private void ConfirmAction()
    {
        ConfirmationVisible = false;
        var action = _pendingConfirm;
        _pendingConfirm = null;
        _pendingCancel  = null;
        action?.Invoke();
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        ConfirmationVisible = false;
        var cancel = _pendingCancel;
        _pendingConfirm = null;
        _pendingCancel  = null;
        cancel?.Invoke();
    }

    // ── Input prompt overlay ──────────────────────────────────────────────────
    [ObservableProperty] private bool   _inputPromptVisible;
    [ObservableProperty] private string _inputPromptTitle  = string.Empty;
    [ObservableProperty] private string _inputPromptLabel  = string.Empty;
    [ObservableProperty] private string _inputPromptValue  = string.Empty;

    private Action<string>? _pendingInputConfirm;
    private Action?         _pendingInputCancel;

    public void ShowInputPrompt(string title, string label, string initialValue,
                                Action<string> onConfirm, Action onCancel)
    {
        _pendingInputConfirm = onConfirm;
        _pendingInputCancel  = onCancel;
        InputPromptTitle     = title;
        InputPromptLabel     = label;
        InputPromptValue     = initialValue;
        InputPromptVisible   = true;
    }

    [RelayCommand]
    private void ConfirmInputPrompt()
    {
        InputPromptVisible = false;
        var confirm = _pendingInputConfirm;
        var value   = InputPromptValue;
        _pendingInputConfirm = null;
        _pendingInputCancel  = null;
        confirm?.Invoke(value);
    }

    [RelayCommand]
    private void CancelInputPrompt()
    {
        InputPromptVisible = false;
        var cancel = _pendingInputCancel;
        _pendingInputConfirm = null;
        _pendingInputCancel  = null;
        cancel?.Invoke();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Refreshes the file list and tree at the current path, keeping the
    /// expanded state and selection intact.
    /// </summary>
    public void Refresh()
    {
        if (_isThisPcMode)
        {
            PopulateThisPcEntries();
        }
        else if (!string.IsNullOrEmpty(CurrentPath))
        {
            RefreshEntries();
            SelectAndExpandPath(CurrentPath);
        }
        // Clear selection after a refresh so the action strip re-evaluates
        OnSelectionChanged([]);
    }

    /// <summary>
    /// Returns a list of applicable <see cref="FileActionViewModel"/>s for the
    /// given entries, suitable for use in a context menu.
    /// Does <em>not</em> update the main action strip.
    /// </summary>
    public IReadOnlyList<FileActionViewModel> BuildContextActions(IReadOnlyList<FileSystemEntry> entries)
    {
        if (_isThisPcMode) return [];

        var applicable = _actionRegistry.GetActionsFor(entries);
        var paths = entries.Count > 0
            ? entries.Select(e => e.FullPath).ToList()
            : (!string.IsNullOrEmpty(CurrentPath) ? [CurrentPath] : new List<string>());

        var result = new List<FileActionViewModel>();
        foreach (var action in applicable)
        {
            var vm = new FileActionViewModel(action);
            vm.ShiftHeld = ShiftHeld;
            vm.UpdatePaths(paths);
            vm.ActionExecuted += Refresh;
            result.Add(vm);
        }
        return result;
    }

    /// <summary>
    /// Called from the view whenever the list-view selection changes.
    /// Does the absolute minimum work on the UI thread — just stores the new
    /// selection and resets the debounce timer.  The strip is rebuilt by
    /// <see cref="OnActionDebounceTimer"/> after input has been idle for 150 ms,
    /// which means any double-click sequence completes before any work is done.
    /// </summary>
    public void OnSelectionChanged(IReadOnlyList<FileSystemEntry> selected)
    {
        
        _pendingSelection = selected;

        // Update the visible count immediately — this is a cheap label update.
        SelectedEntry = selected.Count == 1 ? selected[0] : null;
        UpdateEntryCountLabel(selected.Count);

        // Restart the debounce window; the strip rebuild is deferred.
        _actionDebounceTimer.Stop();

        if (_isThisPcMode)
        {
            FileActions.Clear();
            return;
        }

        _actionDebounceTimer.Start();
        
    }

    /// <summary>
    /// Fires on the UI (STA) thread after input has been idle for 150 ms.
    /// Does the clipboard check and rebuilds the action strip.
    /// </summary>
    private async void OnActionDebounceTimer(object? sender, EventArgs e)
    {
        _actionDebounceTimer.Stop();

        var selected = _pendingSelection;

        // Snapshot CanPerformAction here on the STA UI thread (OLE clipboard).
        var canPerform = _actionRegistry.SnapshotCanPerform();

        // Filter built-in actions + resolve shell verbs — both are pure background work.
        var (applicable, shellVerbs) = await Task.Run(() =>
        {
            var builtIn = _actionRegistry.FilterActions(selected, canPerform);

            // Only look up shell verbs for a single-file selection
            List<ShellVerbAction> verbs = [];
            if (selected.Count == 1 && !selected[0].IsDirectory)
            {
                var entry = selected[0];
                var ext   = Path.GetExtension(entry.Name);
                var info  = ShellTypeResolver.Resolve(ext);
                if (info is not null)
                {
                    string filePattern = string.IsNullOrEmpty(ext) ? "*.*" : $"*{ext}";
                    foreach (var verb in info.Verbs)
                    {
                        // Deduplicate: skip if a built-in already handles the same verb display name
                        if (builtIn.Any(a => string.Equals(a.DisplayName, verb.FriendlyName,
                                                            StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var icon = string.IsNullOrEmpty(verb.DefaultIconSpec)
                            ? null
                            : ShellIconLoader.Load(verb.DefaultIconSpec);

                        // Tooltip: prefer the verb's own resolved description, then the type description
                        string? tooltip = verb.Tooltip
                            ?? (string.IsNullOrEmpty(info.ProgIdDescription) ? null : info.ProgIdDescription);

                        verbs.Add(new ShellVerbAction(
                            verb.Verb,
                            verb.FriendlyName,
                            verb.Command,
                            filePattern,
                            info.ContentType,
                            icon,
                            tooltip));
                    }
                }
            }
            return (builtIn, verbs);
        });

        // Check the selection hasn't changed while we were on the background thread.
        if (!ReferenceEquals(selected, _pendingSelection)) return;

        var paths = selected.Count > 0
            ? selected.Select(e => e.FullPath).ToList()
            : (!string.IsNullOrEmpty(CurrentPath) ? [CurrentPath] : new List<string>());

        FileActions.Clear();

        void AddAction(IFileAction action)
        {
            var vm = new FileActionViewModel(action);
            vm.ShiftHeld = ShiftHeld;
            vm.UpdatePaths(paths);
            vm.ActionExecuted += Refresh;
            vm.FlashBegan += fa =>
            {
                foreach (var other in FileActions)
                    if (other != fa) other.IsDimmed = true;
            };
            vm.FlashEnded += _ =>
            {
                foreach (var other in FileActions)
                    other.IsDimmed = false;
            };
            FileActions.Add(vm);
        }

        foreach (var action in applicable)
            AddAction(action);

        foreach (var verb in shellVerbs)
            AddAction(verb);
    }

    private bool   _isThisPcMode;
    private string _rootPath      = string.Empty;
    private string _sortColumn    = nameof(FileSystemEntry.Name);
    private bool   _sortAscending = true;
    private bool   _navigating;

    // ── AI Summary ───────────────────────────────────────────────────────────
    private const string AiSummaryFileName = ".aisummary";
    private string _aiSummaryOriginal    = string.Empty;
    private string _aiSummaryTextBacking = string.Empty;

    public string AiSummaryText
    {
        get => _aiSummaryTextBacking;
        set
        {
            if (SetProperty(ref _aiSummaryTextBacking, value))
            {
                AiSummaryIsDirty = value != _aiSummaryOriginal;
                SaveAiSummaryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveAiSummary))]
    private void SaveAiSummary()
    {
        if (!AiSummaryVisible || string.IsNullOrEmpty(CurrentPath)) return;
        try
        {
            File.WriteAllText(Path.Combine(CurrentPath, AiSummaryFileName), _aiSummaryTextBacking);
            _aiSummaryOriginal = _aiSummaryTextBacking;
            AiSummaryIsDirty   = false;
            SaveAiSummaryCommand.NotifyCanExecuteChanged();
        }
        catch { /* access denied etc. */ }
    }

    private bool CanSaveAiSummary() => AiSummaryIsDirty;

    private void LoadAiSummary(string path)
    {
        var filePath = Path.Combine(path, AiSummaryFileName);
        if (File.Exists(filePath))
        {
            try
            {
                _aiSummaryOriginal    = File.ReadAllText(filePath);
                _aiSummaryTextBacking = _aiSummaryOriginal;
                OnPropertyChanged(nameof(AiSummaryText));
                AiSummaryIsDirty = false;
                AiSummaryVisible = true;
                SaveAiSummaryCommand.NotifyCanExecuteChanged();
                return;
            }
            catch { /* fall through */ }
        }
        _aiSummaryOriginal    = string.Empty;
        _aiSummaryTextBacking = string.Empty;
        OnPropertyChanged(nameof(AiSummaryText));
        AiSummaryIsDirty = false;
        AiSummaryVisible = false;
        SaveAiSummaryCommand.NotifyCanExecuteChanged();
    }

    // ── Entry count ──────────────────────────────────────────────────────────

    private int _selectedCount;

    private void UpdateEntryCountLabel(int selectedCount = 0)
    {
        _selectedCount = selectedCount;
        var folders = Entries.Count(e => e.IsDirectory);
        var files   = Entries.Count(e => !e.IsDirectory);
        int total   = folders + files;

        string totals = (folders, files) switch
        {
            (0, 0) => "0 items",
            (0, _) => $"{files} {(files == 1 ? "file" : "files")}",
            (_, 0) => $"{folders} {(folders == 1 ? "folder" : "folders")}",
            _      => $"{folders} {(folders == 1 ? "folder" : "folders")}, {files} {(files == 1 ? "file" : "files")}"
        };

        EntryCountLabel = selectedCount > 0
            ? $"{selectedCount} selected  ·  {totals}"
            : totals;
    }

    public ObservableCollection<FileSystemTreeNode> TreeRoots { get; } = [];
    public ObservableCollection<FileSystemEntry>    Entries   { get; } = [];

    /// <summary>
    /// Raised whenever the current directory changes.
    /// The argument is the ordered list of path segments starting from the root label.
    /// Each segment carries a Navigate action so the breadcrumb bar can navigate on click.
    /// </summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    /// <summary>
    /// Raised when a file action requests opening a new tab.
    /// The caller (e.g. ShellViewModel) should open the tab.
    /// </summary>
    public event Action<TabEntry>? TabOpenRequested;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Opens a specific directory as the starting point.</summary>
    public FileSystemViewModel(string targetDirectory) : this()
    {
        InitDebounceTimer();
        _rootPath = targetDirectory;
        BuildDirectoryTree(targetDirectory);
        NavigateTo(targetDirectory);
    }

    /// <summary>Opens "This PC" showing all connected drives.</summary>
    public static FileSystemViewModel CreateThisPc()
    {
        var vm = new FileSystemViewModel();
        vm.InitDebounceTimer();
        vm._rootPath    = string.Empty;
        vm.BuildThisPcTree();
        vm.PopulateThisPcEntries();
        return vm;
    }

    private FileSystemViewModel()
    {
        _actionRegistry = new FileActionRegistry(new Dictionary<Type, object>
        {
            [typeof(IInputPromptService)] = new InputPromptServiceBridge(this),
            [typeof(ITabOpener)]          = new TabOpenerBridge(this)
        });
    }

    private void InitDebounceTimer()
    {
        _actionDebounceTimer.Tick    += OnActionDebounceTimer;
        _actionDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
    }

    // ── Tree ─────────────────────────────────────────────────────────────────

    private void BuildDirectoryTree(string rootPath)
    {
        _isThisPcMode = false;
        TreeRoots.Clear();
        var root = new FileSystemTreeNode(Path.GetFileName(rootPath) is { Length: > 0 } n ? n : rootPath, rootPath)
        {
            IsExpanded = true
        };
        TreeRoots.Add(root);
    }

    private void BuildThisPcTree()
    {
        _isThisPcMode = true;
        CurrentPath   = string.Empty;
        TreeRoots.Clear();

        var thisPc = new FileSystemTreeNode("This PC", string.Empty, TreeNodeKind.ThisPc)
        {
            IsExpanded = true
        };

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            thisPc.Children.Add(new FileSystemTreeNode(label, drive.RootDirectory.FullName, TreeNodeKind.Drive));
        }

        TreeRoots.Add(thisPc);
    }

    /// <summary>
    /// Re-roots the tree at the current path so the view starts fresh from
    /// the folder the user has navigated to. Used when pinning a tab to the ribbon.
    /// </summary>
    public void ResetRootToCurrentPath()
    {
        if (_isThisPcMode || string.IsNullOrEmpty(CurrentPath)) return;
        _rootPath = CurrentPath;
        BuildDirectoryTree(CurrentPath);
        // Select the new root node
        var root = TreeRoots.FirstOrDefault();
        if (root is not null) root.IsSelected = true;
    }

    public void OnTreeNodeSelected(FileSystemTreeNode node)
    {
        // Guard: if we're already inside NavigateTo (which called SelectAndExpandPath
        // which triggered SelectedItemChanged), skip to avoid re-entry.
        if (_navigating) return;

        if (node.Kind == TreeNodeKind.ThisPc)
            GoToThisPc(rebuildTree: false);  // tree already has this node — don't rebuild
        else if (!string.IsNullOrEmpty(node.FullPath))
            NavigateTo(node.FullPath);
    }

    /// <summary>
    /// Switches to "This PC" mode: refreshes the right-panel drive list and
    /// updates the breadcrumb, exactly as NavigateTo does for a real path.
    /// If the tree is currently a directory tree (not already ThisPc) it is
    /// rebuilt, but that is done via the <paramref name="rebuildTree"/> flag
    /// which callers can suppress when they know the tree is already correct
    /// (e.g. the user clicked the ThisPc root node that already exists).
    /// </summary>
    public void GoToThisPc(bool rebuildTree = false)
    {
        if (_navigating) return;
        _navigating = true;
        try
        {
            _isThisPcMode = true;
            CurrentPath   = string.Empty;

            // Clear the action strip immediately — no actions apply to This PC
            _actionDebounceTimer.Stop();
            FileActions.Clear();
            // When the user clicked the existing "This PC" tree root we must NOT
            // clear TreeRoots — that node is the one whose SelectedItemChanged
            // event is currently on the call stack.
            if (rebuildTree)
                BuildThisPcTree();

            PopulateThisPcEntries();   // refreshes Entries, fires NavigationChanged

            // Sync tree selection to the "This PC" root without rebuilding
            var thisPcNode = TreeRoots.FirstOrDefault(n => n.Kind == TreeNodeKind.ThisPc);
            if (thisPcNode is not null)
            {
                ClearSelection(TreeRoots);
                thisPcNode.IsSelected = true;
            }
        }
        finally
        {
            _navigating = false;
        }
    }

    // ── Right panel ──────────────────────────────────────────────────────────

    public void NavigateTo(string path)
    {
        if (_navigating) return;
        if (!Directory.Exists(path)) return;
        _navigating = true;
        try
        {
            _isThisPcMode = false;
                CurrentPath   = path;
                RefreshEntries();
                LoadAiSummary(path);
                SelectAndExpandPath(path);  // sync tree
                FireNavigationChanged(path); // sync breadcrumb
        }
        finally
        {
            _navigating = false;
        }
    }

    [RelayCommand]
    private void OpenEntry(FileSystemEntry entry)
    {
        if (entry.IsDirectory) NavigateTo(entry.FullPath);
    }

    public void NavigateUp()
    {
        if (_isThisPcMode) return;
        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
            NavigateTo(parent.FullName);
        else
            GoToThisPc(rebuildTree: true); // at drive root, folder tree has no ThisPc node
    }

    // ── IQueryHandler ─────────────────────────────────────────────────────────

    public string Description =>
        "Navigates the file browser to a directory. Use when the user types a folder path.";

    public float CanProcess(string input)
    {
        var trimmed = input.Trim();
        if (Path.IsPathRooted(trimmed))
            return (_isThisPcMode || IsRootedOnDriveOrThisPc()) ? 0.9f : 0f;

        // Relative path only meaningful when in folder mode
        if (!_isThisPcMode && !string.IsNullOrEmpty(_rootPath) && trimmed.Length > 0
            && !trimmed.Contains(' '))
            return 0.6f;

        return 0f;
    }

    public Task<string?> ProcessAsync(string input)
    {
        var trimmed = input.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            if (Directory.Exists(trimmed))
            {
                NavigateTo(trimmed);
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>($"Path not found: {trimmed}");
        }

        if (!_isThisPcMode && !string.IsNullOrEmpty(_rootPath))
        {
            var candidates = new[]
            {
                string.IsNullOrEmpty(CurrentPath) ? null : Path.GetFullPath(Path.Combine(CurrentPath, trimmed)),
                Path.GetFullPath(Path.Combine(_rootPath, trimmed))
            };
            foreach (var candidate in candidates)
            {
                if (candidate is not null && Directory.Exists(candidate))
                {
                    NavigateTo(candidate);
                    return Task.FromResult<string?>(null);
                }
            }
        }

        return Task.FromResult<string?>($"Path not found: {trimmed}");
    }

    // ── IContextProvider ──────────────────────────────────────────────────────

    public string GetContext()
    {
        if (_isThisPcMode)
            return "File browser - This PC view (showing available drives)";
        if (string.IsNullOrEmpty(CurrentPath))
            return "File browser - no location selected";
        var selected = SelectedEntry is not null ? $" — selected: '{SelectedEntry.Name}'" : string.Empty;
        return $"File browser at '{CurrentPath}'{selected}";
    }

    public IReadOnlyList<ActionDescriptor> GetAvailableActions() =>
    [
        new("navigate", "Navigate the file browser to a directory",
            new Dictionary<string, string> { ["path"] = "absolute folder path" }),
        new("gotoRoot", "Go back to the This PC drive list"),
    ];

    // ── IActionExecutor ───────────────────────────────────────────────────────

    public Task<bool> TryExecuteActionAsync(string actionJson)
    {
        try
        {
            var doc  = JsonDocument.Parse(actionJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("action", out var actionEl)) return Task.FromResult(false);

            switch (actionEl.GetString()?.ToLowerInvariant())
            {
                case "navigate" when root.TryGetProperty("path", out var pathEl):
                {
                    var path = pathEl.GetString() ?? string.Empty;
                    if (!Directory.Exists(path)) return Task.FromResult(false);
                    NavigateTo(path);
                    return Task.FromResult(true);
                }
                case "gotoroot":
                    GoToThisPc(rebuildTree: true);
                    return Task.FromResult(true);
            }
        }
        catch { /* malformed JSON — return false below */ }
        return Task.FromResult(false);
    }

    private bool IsRootedOnDriveOrThisPc()
    {
        if (_isThisPcMode) return true;
        if (string.IsNullOrEmpty(_rootPath)) return true; // navigated from This PC
        // Root is a drive letter root like "C:\" with no further subpath
        var root = Path.GetPathRoot(_rootPath);
        return string.Equals(root, _rootPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(root?.TrimEnd('\\'), _rootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the ordered breadcrumb segments for the given path relative to _rootPath,
    /// then raises NavigationChanged.
    /// </summary>
    private void FireNavigationChanged(string path)
    {
        var segments = new List<(string Label, string Path)>();

        if (_isThisPcMode)
        {
            // Showing the "This PC" drive list — single root crumb only
            segments.Add(("This PC", string.Empty));
        }
        else if (string.IsNullOrEmpty(_rootPath))
        {
            // Navigated from "This PC" into a drive or one of its subdirectories.
            // Breadcrumb: This PC > C:\ > Folder > ...
            segments.Add(("This PC", string.Empty));

            var driveRoot = Path.GetPathRoot(path) ?? path;
            var driveLabel = driveRoot.TrimEnd(Path.DirectorySeparatorChar);
            segments.Add((driveLabel, driveRoot));

            var relative = Path.GetRelativePath(driveRoot, path);
            if (relative != ".")
            {
                var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var built = driveRoot;
                foreach (var part in parts)
                {
                    built = Path.Combine(built, part);
                    segments.Add((part, built));
                }
            }
        }
        else
        {
            // Standard directory mode rooted at _rootPath.
            var rootLabel = Path.GetFileName(_rootPath) is { Length: > 0 } n ? n : _rootPath;
            segments.Add((rootLabel, _rootPath));

            if (!string.Equals(path, _rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(_rootPath, path);
                var parts    = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var built    = _rootPath;
                foreach (var part in parts)
                {
                    built = Path.Combine(built, part);
                    segments.Add((part, built));
                }
            }
        }

        NavigationChanged?.Invoke(segments);
    }

    /// <summary>
    /// Expands the tree to the given path and marks that node as selected,
    /// without refreshing entries (the caller is responsible for that).
    /// </summary>
    public void SelectAndExpandPath(string path)
    {
        // Clear any existing selection first
        ClearSelection(TreeRoots);
        // Walk the tree roots and expand each level to reach `path`
        foreach (var root in TreeRoots)
            if (TryExpandTo(root, path)) break;
    }

    private static void ClearSelection(IEnumerable<FileSystemTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected) node.IsSelected = false;
            if (node.Children.Count > 0) ClearSelection(node.Children);
        }
    }

    private static bool TryExpandTo(FileSystemTreeNode node, string target)
    {
        if (string.Equals(node.FullPath, target, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            return true;
        }

        // Only descend if the target is under this node.
        // Empty FullPath means a virtual root (e.g. "This PC") — always descend into its children.
        if (!string.IsNullOrEmpty(node.FullPath) &&
            !target.StartsWith(node.FullPath.TrimEnd(Path.DirectorySeparatorChar),
                               StringComparison.OrdinalIgnoreCase))
            return false;

        node.IsExpanded = true; // triggers lazy load

        foreach (var child in node.Children)
        {
            if (TryExpandTo(child, target))
                return true;
        }
        return false;
    }

    private void PopulateThisPcEntries()
    {
        _isThisPcMode = true;
        CurrentPath   = "This PC";
        Entries.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            Entries.Add(new FileSystemEntry
            {
                Name        = label,
                FullPath    = drive.RootDirectory.FullName,
                IsDirectory = true,
                IsDrive     = true,
                Modified    = default
            });
        }
        AiSummaryVisible = false;
        UpdateEntryCountLabel();
        NavigationChanged?.Invoke([("This PC", string.Empty)]);
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        try
        {
            var dirs = Directory.GetDirectories(CurrentPath)
                .Select(d => new FileSystemEntry
                {
                    Name        = Path.GetFileName(d),
                    FullPath    = d,
                    IsDirectory = true,
                    Modified    = Directory.GetLastWriteTime(d)
                });

            var files = Directory.GetFiles(CurrentPath)
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    return new FileSystemEntry
                    {
                        Name        = info.Name,
                        FullPath    = f,
                        IsDirectory = false,
                        SizeBytes   = info.Length,
                        Modified    = info.LastWriteTime
                    };
                });

                var sorted = ApplySort(dirs.Concat(files));
                    foreach (var e in sorted) Entries.Add(e);
                }
                catch { /* access denied */ }
                UpdateEntryCountLabel();
            }

    private IEnumerable<FileSystemEntry> ApplySort(IEnumerable<FileSystemEntry> source)
    {
        Func<FileSystemEntry, object> key = _sortColumn switch
        {
            nameof(FileSystemEntry.Modified) => e => e.Modified,
            nameof(FileSystemEntry.SizeBytes) => e => (object)e.SizeBytes,
            nameof(FileSystemEntry.TypeLabel) => e => e.TypeLabel,
            _ => e => e.Name
        };

        // Folders always first, then sort within group
        return _sortAscending
            ? source.OrderBy(e => !e.IsDirectory).ThenBy(key)
            : source.OrderBy(e => !e.IsDirectory).ThenByDescending(key);
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn    = column;
            _sortAscending = true;
        }
        RefreshEntries();
    }

    // ── InputPromptServiceBridge ──────────────────────────────────────────────

    /// <summary>
    /// Implements <see cref="IInputPromptService"/> by forwarding calls to the
    /// owning <see cref="FileSystemViewModel"/>'s overlay state.
    /// Registered in the DI dictionary so <see cref="RenameFile"/> receives it.
    /// </summary>
    private sealed class InputPromptServiceBridge : IInputPromptService
    {
        private readonly FileSystemViewModel _vm;
        public InputPromptServiceBridge(FileSystemViewModel vm) => _vm = vm;

        public void Show(string title, string label, string initialValue,
                         Action<string> onConfirm, Action onCancel)
            => _vm.ShowInputPrompt(title, label, initialValue, onConfirm, onCancel);

        public void ShowConfirmation(string title, string message, Action onConfirm, Action onCancel)
            => _vm.ShowConfirmation(message, onConfirm, onCancel);

        public void RequestRefresh() => _vm.Refresh();
    }

    // ── TabOpenerBridge ───────────────────────────────────────────────────────

    /// <summary>
    /// Implements <see cref="FileActions.ITabOpener"/> by raising
    /// <see cref="FileSystemViewModel.TabOpenRequested"/> so the shell can
    /// create and activate the appropriate tab without any direct coupling.
    /// </summary>
    private sealed class TabOpenerBridge : ITabOpener
    {
        private readonly FileSystemViewModel _vm;
        public TabOpenerBridge(FileSystemViewModel vm) => _vm = vm;

        public void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null)
            => FeatureManager.Instance.RequestTab(pageKind, pageParams);

        public void OpenImageViewer(IReadOnlyList<string> imagePaths)
        {
            if (imagePaths.Count == 0) return;

            var tabTitle = imagePaths.Count == 1
                ? Path.GetFileName(imagePaths[0])
                : $"Images ({imagePaths.Count})";

            var capturedPaths = imagePaths.ToList();

            var tab = new TabEntry
            {
                Title       = tabTitle,
                Icon        = "🖼",
                Breadcrumbs = [new BreadcrumbSegment { Label = tabTitle }]
            };
            tab.PageFactory = () =>
            {
                var imageVm = new ImageViewModel(capturedPaths);
                return new ImageView(imageVm);
            };

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _vm.TabOpenRequested?.Invoke(tab));
        }

        public void OpenHtmlViewer(string filePath)
        {
            var tabTitle = Path.GetFileName(filePath);

            var tab = new TabEntry
            {
                Title       = tabTitle,
                Icon        = "🌐",
                Breadcrumbs = [new BreadcrumbSegment { Label = tabTitle }]
            };
            tab.PageFactory = () =>
            {
                var htmlVm = new HtmlViewModel(filePath);
                return new HtmlView(htmlVm);
            };

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _vm.TabOpenRequested?.Invoke(tab));
        }

        public void OpenMarkdownViewer(string filePath)
        {
            var tabTitle = Path.GetFileName(filePath);

            var tab = new TabEntry
            {
                Title       = tabTitle,
                Icon        = "📝",
                Breadcrumbs = [new BreadcrumbSegment { Label = tabTitle }]
            };
            tab.PageFactory = () =>
            {
                var mdVm = new MarkdownViewModel(filePath);
                return new MarkdownView(mdVm);
            };

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _vm.TabOpenRequested?.Invoke(tab));
        }
    }
}
