using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.RibbonHandlers;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Core.FileActions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Core.ViewModels;


// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class FileSystemViewModel : ObservableObject, IQueryHandler, IPageViewModel, IActionExecutor
{
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private FileSystemEntry? _selectedEntry;
    [ObservableProperty] private string _entryCountLabel = string.Empty;

    // ── File action strip ─────────────────────────────────────────────────────
    private readonly FileActionManager _actionRegistry;
    public ObservableCollection<FileActionViewModel> FileActions { get; } = [];

    // ── Ribbon pinning ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPinFileActionToRibbon))]
    private void PinFileActionToRibbon(FileActionViewModel vm)
    {
        // Folder actions (wrapped in FolderActionAdapter) operate on the current directory,
        // not the selected files. Capture the folder path so the pinned button re-opens
        // the action at the same location.
        IReadOnlyList<string> paths = vm.Action is FileActions.FolderActionAdapter
            ? (string.IsNullOrEmpty(CurrentPath) ? [] : [CurrentPath])
            : CurrentSelection.Select(e => e.FullPath).ToList();
        var payload = new FileActionPinPayload(vm.Action, paths);
        ((IShellServices?)WorkContext.ShellServices)?.PinToRibbon(PageKinds.FileAction, payload);
    }

    private bool CanPinFileActionToRibbon(FileActionViewModel? vm)
        => vm is not null && !vm.IsDestructive;

    // Debounce timer — action strip is only rebuilt after input has been idle
    // for a short interval, so rapid selection changes (including double-clicks)
    // never stall the UI thread.
    private readonly DispatcherTimer _actionDebounceTimer = new(DispatcherPriority.Background);
    private IReadOnlyList<FileSystemEntry> _pendingSelection = [];

    // ── Multi-selection (forwarded from view) ─────────────────────────────────
    public IReadOnlyList<FileSystemEntry> CurrentSelection { get; private set; } = [];

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
        _refreshing = true;
        try
        {
            if (_isThisPcMode)
            {
                GoToThisPc(rebuildTree: false);
            }
            else if (!string.IsNullOrEmpty(CurrentPath))
            {
                foreach (var root in TreeRoots)
                    RefreshExpandedNode(root);
                RefreshEntries();
                SelectAndExpandPath(CurrentPath);
            }
            // Clear selection after a refresh so the action strip re-evaluates
            OnSelectionChanged([]);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Returns a list of applicable <see cref="FileActionViewModel"/>s for the
    /// given entries, suitable for use in a context menu.
    /// Does <em>not</em> update the main action strip.
    /// </summary>
    /// <summary>
    /// Finds the action of type <typeparamref name="T"/> in the current action strip and
    /// executes it exactly as if the user clicked the button, including flash animation and
    /// shift-force detection. Returns false when no such action is currently visible.
    /// </summary>
    public bool TryExecuteAction<T>() where T : class
    {
        var vm = FileActions.FirstOrDefault(a => a.Action is T ||
                     (a.Action is FolderActionAdapter adapter && adapter.Inner is T));
        if (vm?.ExecuteCommand.CanExecute(null) != true) return false;
        vm.ExecuteCommand.Execute(null);
        return true;
    }

    public IReadOnlyList<FileActionViewModel> BuildContextActions(IReadOnlyList<FileSystemEntry> entries)
    {
        if (_isThisPcMode) return [];

        var canPerform = _actionRegistry.SnapshotCanPerform();

        bool onlyFolders     = entries.Count > 0 && entries.All(e => e.IsDirectory);
        bool anyDrives       = entries.Any(e => e.IsDrive);
        bool useFolderActions = entries.Count == 0 || onlyFolders || anyDrives;

        IReadOnlyList<IFileAction> applicable = useFolderActions
            ? _actionRegistry.FilterFolderActions(entries, canPerform.Folder)
            : _actionRegistry.FilterActions(entries, canPerform.File);

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
        CurrentSelection  = selected;
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

        bool onlyFolders = selected.Count > 0 && selected.All(e => e.IsDirectory);
        bool anyDrives   = selected.Any(e => e.IsDrive);
        bool useFolderActions = selected.Count == 0 || onlyFolders || anyDrives;

        // Filter built-in actions + resolve shell verbs — both are pure background work.
        var (applicable, shellVerbs) = await Task.Run(() =>
        {
            IReadOnlyList<IFileAction> builtIn;
            if (useFolderActions)
            {
                builtIn = _actionRegistry.FilterFolderActions(selected, canPerform.Folder);
            }
            else
            {
                builtIn = _actionRegistry.FilterActions(selected, canPerform.File);
            }

            // Only look up shell verbs for a single-file selection
            List<ShellVerbAction> verbs = [];
            if (!useFolderActions && selected.Count == 1 && !selected[0].IsDirectory)
            {
                var entry      = selected[0];
                var ext        = Path.GetExtension(entry.Name);
                var info       = ShellTypeResolver.Resolve(ext);
                if (info is not null)
                {
                    string experienceId = $"/shell/{ext.TrimStart('.').ToLowerInvariant()}";
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
                            experienceId,
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
    public  bool   IsThisPcMode => _isThisPcMode;
    private string _rootPath      = string.Empty;
    public  string RootPath       => _rootPath;
    private string _sortColumn    = nameof(FileSystemEntry.Name);
    private bool   _sortAscending = true;
    private bool   _navigating;
    private bool   _refreshing;

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


    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Opens a specific directory as the starting point.</summary>
    public FileSystemViewModel(string targetDirectory, WorkContext workContext) : this(workContext)
    {
        InitDebounceTimer();
        _rootPath = targetDirectory;
        BuildDirectoryTree(targetDirectory);
        NavigateTo(targetDirectory);
    }

    /// <summary>Opens "This PC" showing all connected drives.</summary>
    public static FileSystemViewModel CreateThisPc(WorkContext workContext)
    {
        var vm = new FileSystemViewModel(workContext);
        vm.InitDebounceTimer();
        vm._rootPath     = string.Empty;
        vm._isThisPcMode = true;
        vm.CurrentPath   = string.Empty;

        var thisPc = new FileSystemTreeNode("This PC", string.Empty, TreeNodeKind.ThisPc)
        {
            IsExpanded = true
        };
        vm.TreeRoots.Add(thisPc);

        // DriveInfo.GetDrives() is fast — just reads the drive table without I/O per drive.
        // Each drive is shown immediately in a Loading state; readiness is checked per-drive
        // on a background thread so slow/network drives never block the UI.
        foreach (var d in DriveInfo.GetDrives())
        {
            var node  = new FileSystemTreeNode(d.Name, d.RootDirectory.FullName, TreeNodeKind.Drive);
            var entry = new FileSystemEntry
            {
                Name          = d.Name,
                FullPath      = d.RootDirectory.FullName,
                IsDirectory   = true,
                IsDrive       = true,
                DriveStatus   = DriveStatus.Loading,
                DriveIconType = DriveIconType.HDD
            };
            thisPc.Children.Add(node);
            vm.Entries.Add(entry);
            _ = CheckDriveAsync(d, node, entry);
        }

        vm.UpdateEntryCountLabel();
        vm.NavigationChanged?.Invoke([("This PC", string.Empty)]);
        return vm;
    }

    private static async Task CheckDriveAsync(DriveInfo drive, FileSystemTreeNode node, FileSystemEntry entry)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                if (!drive.IsReady)
                    return (IsReady: false, Label: drive.Name, HasChildren: false,
                            UsedBytes: 0L, TotalBytes: 0L, FileSystem: string.Empty,
                            KindLabel: string.Empty, IconType: DriveIconType.HDD);

                var lbl = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                var hasSub = FileSystemTreeNode.HasSubDirectoriesSafe(drive.RootDirectory.FullName);

                long total  = drive.TotalSize;
                long used   = total - drive.TotalFreeSpace;
                string fs   = drive.DriveFormat;

                string kind = drive.DriveType switch
                {
                    DriveType.CDRom    => "CD/DVD Drive",
                    DriveType.Removable => "Removable Disk",
                    DriveType.Network  => "Network Drive",
                    DriveType.Ram      => "RAM Disk",
                    _                  => "Local Disk"
                };

                var iconType = drive.DriveType switch
                {
                    DriveType.CDRom    => DriveIconType.CDDVD,
                    DriveType.Removable => DriveIconType.Removable,
                    DriveType.Network  => DriveIconType.Network,
                    DriveType.Fixed    => NativeMethods.IsNoSeekPenalty(drive.RootDirectory.FullName)
                                             ? DriveIconType.SSD : DriveIconType.HDD,
                    _                  => DriveIconType.HDD
                };

                return (IsReady: true, Label: lbl, HasChildren: hasSub,
                        UsedBytes: used, TotalBytes: total, FileSystem: fs,
                        KindLabel: kind, IconType: iconType);
            });

            // Resume on the WPF dispatcher — safe to touch UI objects directly.
            node.Name  = result.Label;
            entry.Name = result.Label;

            if (result.IsReady)
            {
                entry.DriveUsedBytes  = result.UsedBytes;
                entry.DriveTotalBytes = result.TotalBytes;
                entry.FileSystem      = result.FileSystem;
                entry.DriveKindLabel  = result.KindLabel;
                entry.DriveIconType   = result.IconType;
                node.DriveIconType    = result.IconType;

                if (result.HasChildren && node.Children.Count == 0) node.Children.Add(FileSystemTreeNode.Dummy);
                node.DriveStatus  = DriveStatus.Ready;
                entry.DriveStatus = DriveStatus.Ready;
            }
            else
            {
                node.DriveStatus  = DriveStatus.Unavailable;
                entry.DriveStatus = DriveStatus.Unavailable;
            }
        }
        catch
        {
            node.DriveStatus  = DriveStatus.Unavailable;
            entry.DriveStatus = DriveStatus.Unavailable;
        }
    }

    internal WorkContext WorkContext { get; }

    private FileSystemViewModel(WorkContext workContext)
    {
        WorkContext     = workContext;
        _actionRegistry = new FileActionManager(workContext);
        FileMapManager.Instance.RegisterKnownExperiences(_actionRegistry.AllExperiences);
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
        // Guard: if we're already inside NavigateTo or Refresh (which calls SelectAndExpandPath
        // which triggers SelectedItemChanged), skip to avoid re-entry.
        if (_navigating || _refreshing) return;

        if (node.Kind == TreeNodeKind.ThisPc)
            GoToThisPc(rebuildTree: false);  // tree already has this node — don't rebuild
        else if (!string.IsNullOrEmpty(node.FullPath))
            NavigateTo(node.FullPath);
    }

    /// <summary>
    /// Switches to "This PC" mode: refreshes the right-panel drive list and
    /// updates the breadcrumb, exactly as NavigateTo does for a real path.
    /// If the tree is currently a directory tree (not already ThisPc) it is
    /// rebuilt via the <paramref name="rebuildTree"/> flag.
    /// Drive readiness is checked per-drive on a background thread to avoid
    /// blocking the UI for slow/network drives.
    /// </summary>
    public async void GoToThisPc(bool rebuildTree = false)
    {
        if (_navigating) return;
        _navigating = true;
        try
        {
            _isThisPcMode = true;
            CurrentPath   = string.Empty;

            _actionDebounceTimer.Stop();
            FileActions.Clear();

            FileSystemTreeNode? thisPcNode;
            if (rebuildTree)
            {
                TreeRoots.Clear();
                thisPcNode = new FileSystemTreeNode("This PC", string.Empty, TreeNodeKind.ThisPc)
                {
                    IsExpanded = true
                };
                TreeRoots.Add(thisPcNode);
            }
            else
            {
                thisPcNode = TreeRoots.FirstOrDefault(n => n.Kind == TreeNodeKind.ThisPc);
            }

            // Populate entries immediately with all drives in Loading state,
            // then resolve each drive individually on a background thread.
            Entries.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                var entry = new FileSystemEntry
                {
                    Name          = d.Name,
                    FullPath      = d.RootDirectory.FullName,
                    IsDirectory   = true,
                    IsDrive       = true,
                    DriveStatus   = DriveStatus.Loading,
                    DriveIconType = DriveIconType.HDD
                };
                Entries.Add(entry);

                if (rebuildTree && thisPcNode is not null)
                {
                    var node = new FileSystemTreeNode(d.Name, d.RootDirectory.FullName, TreeNodeKind.Drive);
                    thisPcNode.Children.Add(node);
                    _ = CheckDriveAsync(d, node, entry);
                }
                else
                {
                    // Tree already has drive nodes — find the matching one and update its entry
                    var existingNode = thisPcNode?.Children
                        .FirstOrDefault(c => string.Equals(c.FullPath, d.RootDirectory.FullName,
                                             StringComparison.OrdinalIgnoreCase));
                    _ = CheckDriveAsync(d, existingNode ?? new FileSystemTreeNode(d.Name, d.RootDirectory.FullName, TreeNodeKind.Drive), entry);
                }
            }

            UpdateEntryCountLabel();
            NavigationChanged?.Invoke([("This PC", string.Empty)]);

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
                SelectAndExpandPath(path);  // sync tree
                FireNavigationChanged(path); // sync breadcrumb
        }
        finally
        {
            _navigating = false;
        }
    }

    [RelayCommand]
    private async Task OpenEntry(FileSystemEntry entry)
    {
        if (entry.IsDirectory) { NavigateTo(entry.FullPath); return; }
        await OpenFileDefaultAsync(entry);
    }

    /// <summary>
    /// Resolves and executes the default "open" action for a file, applying the rule:
    /// FileExtension > MagicNumber > PerceivedType > ContentType, and at the same
    /// specificity level an internal action beats a shell "open" verb.
    /// </summary>
    private async Task OpenFileDefaultAsync(FileSystemEntry entry)
    {
        var fileInfo = new FileInfo(entry.FullPath);
        var ext      = Path.GetExtension(entry.Name);

        var canPerform = _actionRegistry.SnapshotCanPerform();

        var (internalActions, shellOpenVerb) = await Task.Run(() =>
        {
            var actions  = _actionRegistry.FilterActions([entry], canPerform.File);
            var typeInfo = ShellTypeResolver.Resolve(ext);
            var openVerb = typeInfo?.Verbs.FirstOrDefault(v =>
                string.Equals(v.Verb, "open", StringComparison.OrdinalIgnoreCase));
            return (actions, openVerb);
        });

        // Find highest-specificity internal action
        IFileAction? bestInternal    = null;
        int          bestInternalSpec = -1;
        foreach (var action in internalActions)
        {
            int spec = FileMapManager.Instance.GetMatchSpecificity(fileInfo, action.ExperienceId);
            if (spec > bestInternalSpec) { bestInternal = action; bestInternalSpec = spec; }
        }

        // Shell "open" verb is Extension-level (4); encode priority as spec*2 + (internal?1:0)
        // so that internal wins over shell at the same specificity level.
        int internalPriority = bestInternal  is not null ? bestInternalSpec * 2 + 1 : -1;
        int shellPriority    = shellOpenVerb is not null ? 4 * 2 + 0               : -1;

        if (internalPriority >= shellPriority && bestInternal is not null)
        {
            bestInternal.PerformAction(entry.FullPath);
            if (bestInternal.RequiresRefresh) Refresh();
        }
        else if (shellOpenVerb is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.FullPath)
                {
                    Verb            = shellOpenVerb.Verb,
                    UseShellExecute = true
                });
            }
            catch { }
        }
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

    public float CanProcess(string input, IPageViewModel? pageVm = null)
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

    public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
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

    // ── IPageViewModel ────────────────────────────────────────────────────────

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

    public IContext? GetContextObject()
    {
        if (IsThisPcMode) return null;
        if (string.IsNullOrEmpty(RootPath))
        {
            return new FileSystemContext
            {
                RootPath = CurrentPath,
                CurrentPath = "",
                SelectedItems = CurrentSelection.Select(e => e.FullPath).ToList()
            };
        }
        return new FileSystemContext
        {
            RootPath      = RootPath,
            CurrentPath   = CurrentPath,
            SelectedItems = CurrentSelection.Select(e => e.FullPath).ToList()
        };
    }

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

    /// <summary>
    /// Diffs an expanded node's children against disk: removes entries that no longer exist,
    /// inserts new ones in sorted order. Recurses depth-first so deeper expansions are updated
    /// before their parents, preserving IsExpanded state on all surviving nodes.
    /// </summary>
    private static void RefreshExpandedNode(FileSystemTreeNode node)
    {
        // Recurse first — virtual roots like "This PC" (FullPath="") must propagate into children
        foreach (var child in node.Children.ToList())
            RefreshExpandedNode(child);

        // Only diff nodes with a real filesystem path and loaded children
        if (string.IsNullOrEmpty(node.FullPath)) return;
        if (node.Children.Count == 0 || node.Children[0] == FileSystemTreeNode.Dummy) return;

        HashSet<string> diskDirs;
        try { diskDirs = new HashSet<string>(Directory.GetDirectories(node.FullPath), StringComparer.OrdinalIgnoreCase); }
        catch { diskDirs = []; }

        // Remove children no longer on disk
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (!diskDirs.Contains(node.Children[i].FullPath))
                node.Children.RemoveAt(i);
        }

        // Insert children new on disk (maintains alphabetical order)
        var existing = new HashSet<string>(node.Children.Select(c => c.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var dir in diskDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(dir)) continue;
            var newNode = new FileSystemTreeNode(Path.GetFileName(dir), dir);
            InsertSorted(node.Children, newNode);
        }
    }

    private static void InsertSorted(ObservableCollection<FileSystemTreeNode> children, FileSystemTreeNode newNode)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (string.Compare(newNode.FullPath, children[i].FullPath, StringComparison.OrdinalIgnoreCase) < 0)
            {
                children.Insert(i, newNode);
                return;
            }
        }
        children.Add(newNode);
    }

    private static bool TryExpandTo(FileSystemTreeNode node, string target)
    {
        if (string.Equals(node.FullPath, target, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            node.IsExpanded = true;
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

}
