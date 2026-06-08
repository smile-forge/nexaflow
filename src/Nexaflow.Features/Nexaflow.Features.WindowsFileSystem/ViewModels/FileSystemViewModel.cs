using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ClientTools;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.RibbonHandlers;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>Footer quick-filter for the entry list.</summary>
public enum EntryFilter { None, FoldersOnly, FilesOnly }


// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class FileSystemViewModel : ObservableObject, IPageViewModel
{
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private FileSystemEntry? _selectedEntry;

    // ── Footer: clickable folder/file counts + quick-filter ────────────────────
    [ObservableProperty] private string _selectionSummary   = string.Empty; // "N selected · " or ""
    [ObservableProperty] private string _folderCountText    = string.Empty; // "38 folders"
    [ObservableProperty] private string _fileCountText      = string.Empty; // "2 files"
    [ObservableProperty] private bool   _hasFolders;
    [ObservableProperty] private bool   _hasFiles;
    [ObservableProperty] private bool   _showCountSeparator;
    [ObservableProperty] private bool   _isEmpty = true;

    /// <summary>Active footer filter; drives which entries the list view shows.</summary>
    [ObservableProperty] private EntryFilter _activeFilter = EntryFilter.None;

    partial void OnActiveFilterChanged(EntryFilter value) => ApplyEntryFilter();

    private void ApplyEntryFilter()
    {
        var view = CollectionViewSource.GetDefaultView(Entries);
        if (view is null) return;
        view.Filter = ActiveFilter switch
        {
            EntryFilter.FoldersOnly => static o => o is FileSystemEntry { IsDirectory: true },
            EntryFilter.FilesOnly   => static o => o is FileSystemEntry { IsDirectory: false },
            _                       => null
        };
    }

    [RelayCommand]
    private void ToggleFolderFilter() =>
        ActiveFilter = ActiveFilter == EntryFilter.FoldersOnly ? EntryFilter.None : EntryFilter.FoldersOnly;

    [RelayCommand]
    private void ToggleFileFilter() =>
        ActiveFilter = ActiveFilter == EntryFilter.FilesOnly ? EntryFilter.None : EntryFilter.FilesOnly;

    // ── File action strip ─────────────────────────────────────────────────────
    private readonly FileActionManager _actionRegistry;
    private readonly DefaultFileOpener _opener;
    public ObservableCollection<FileActionViewModel> FileActions { get; } = [];

    // ── Ribbon pinning ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPinFileActionToRibbon))]
    private void PinFileActionToRibbon(FileActionViewModel vm)
    {
        // Folder actions (wrapped in FolderActionAdapter) operate on the current directory,
        // not the selected files. Capture the folder path so the pinned button re-opens
        // the action at the same location.
        IReadOnlyList<string> paths = vm.Action is FolderActionAdapter
            ? (string.IsNullOrEmpty(CurrentPath) ? [] : [CurrentPath])
            : CurrentSelection.Select(e => e.FullPath).ToList();
        var payload = new FileActionPinPayload(vm.Action, paths);
        _shell.PinToRibbon(FileSystemPageRegistration.FileActionKind, payload);
    }

    private bool CanPinFileActionToRibbon(FileActionViewModel? vm)
        => vm is not null && !vm.IsDestructive && vm.IsRibbonPinnable;

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

    // ── Create overlay (the "New" button) ──────────────────────────────────────
    // A single pane: pick a file type from the icon list, name it below. The name is
    // prefilled per type, its extension follows the selected type, and a live warning
    // shows when the resolved name already exists in the current folder.

    [ObservableProperty] private bool _createOverlayVisible;
    [ObservableProperty] private CreateActionViewModel? _selectedCreateAction;
    [ObservableProperty] private string _createFileName = string.Empty;
    [ObservableProperty] private bool _createNameExists;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _canCreate;

    public ObservableCollection<CreateActionViewModel> CreateActions { get; } = [];

    /// <summary>Opens the create overlay, populated live from every create-action source.</summary>
    public void OpenCreateOverlay()
    {
        if (_isThisPcMode || string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath))
            return;
        OpenCreateOverlayWith(_actionRegistry.GetCreateActions());
    }

    /// <summary>Populates and shows the overlay for a given action set (also the unit-test seam).</summary>
    internal void OpenCreateOverlayWith(IReadOnlyList<IFileCreateAction> actions)
    {
        CreateActions.Clear();
        foreach (var action in actions)
            CreateActions.Add(new CreateActionViewModel(action, SelectCreateAction));

        SelectedCreateAction = CreateActions.FirstOrDefault();
        RecomputeCreateState();          // also covers the empty-list case
        CreateOverlayVisible = true;
    }

    private void SelectCreateAction(CreateActionViewModel a) => SelectedCreateAction = a;

    partial void OnSelectedCreateActionChanged(CreateActionViewModel? value)
    {
        foreach (var a in CreateActions)
            a.IsSelected = ReferenceEquals(a, value);
        CreateFileName = DefaultNameFor(value);   // triggers RecomputeCreateState via OnCreateFileNameChanged
    }

    partial void OnCreateFileNameChanged(string value) => RecomputeCreateState();

    private static string DefaultNameFor(CreateActionViewModel? a)
    {
        if (a is null) return string.Empty;
        return string.IsNullOrEmpty(a.FileExtension) ? "New Folder" : "New File" + a.FileExtension;
    }

    /// <summary>Applies the host extension rule: keep the user's extension, else append the type's.</summary>
    private string ResolveCreateName()
    {
        var name = (CreateFileName ?? string.Empty).Trim();
        if (name.Length == 0 || SelectedCreateAction is null) return name;
        return Path.HasExtension(name) ? name : name + SelectedCreateAction.FileExtension;
    }

    private void RecomputeCreateState()
    {
        var name = ResolveCreateName();
        bool valid = SelectedCreateAction is not null
                     && name.Length > 0
                     && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        bool exists = false;
        if (valid && !string.IsNullOrEmpty(CurrentPath))
        {
            var full = Path.Combine(CurrentPath, name);
            exists = File.Exists(full) || Directory.Exists(full);
        }

        CreateNameExists = valid && exists;
        CanCreate        = valid && !exists;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        var action = SelectedCreateAction?.Action;
        if (action is null) return;

        var name = ResolveCreateName();
        if (name.Length == 0) return;

        CreateOverlayVisible = false;
        try
        {
            action.Create(CurrentPath, name);
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Could not create '{name}': {ex.Message}");
        }
        Refresh();
    }

    [RelayCommand]
    private void CancelCreate() => CreateOverlayVisible = false;

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

        // Filter built-in actions + resolve shell verbs + custom external apps —
        // all pure background work.
        var (applicable, shellVerbs, customActions) = await Task.Run(() =>
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

            // Only look up shell verbs for a single-file selection, and only when the
            // user has enabled registry-based file type mapping.
            List<ShellVerbAction> verbs = [];
            if (ExternalAppRegistry.Instance.UseRegistryMapping &&
                !useFolderActions && selected.Count == 1 && !selected[0].IsDirectory)
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

            // User-defined external apps — apply to file selections regardless of count
            // (the registry filters out single-file-only definitions on multi-select).
            IReadOnlyList<CustomAction> customs = !useFolderActions
                ? ExternalAppRegistry.Instance.Resolve(selected)
                : Array.Empty<CustomAction>();

            return (builtIn, verbs, customs);
        });

        // Check the selection hasn't changed while we were on the background thread.
        if (!ReferenceEquals(selected, _pendingSelection)) return;

        var paths = selected.Count > 0
            ? selected.Select(e => e.FullPath).ToList()
            : (!string.IsNullOrEmpty(CurrentPath) ? [CurrentPath] : new List<string>());

        FileActions.Clear();

        FileActionViewModel BuildActionVm(IFileAction action)
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
            return vm;
        }

        void AddAction(IFileAction action) => FileActions.Add(BuildActionVm(action));

        foreach (var action in applicable)
            AddAction(action);

        foreach (var verb in shellVerbs)
            AddAction(verb);

        foreach (var custom in customActions)
            AddAction(custom);

        // "New" — the first item when an empty folder area is in focus (no file selected). It is a
        // folder action (operates on the current folder) whose PerformAction opens the create overlay.
        if (useFolderActions && selected.Count == 0 && !_isThisPcMode
            && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
        {
            var newAction = new FolderActionAdapter(new NewMenuAction(OpenCreateOverlay));
            FileActions.Insert(0, BuildActionVm(newAction));
        }
    }

    private bool   _isThisPcMode;
    public  bool   IsThisPcMode => _isThisPcMode;
    private string _rootPath      = string.Empty;
    public  string RootPath       => _rootPath;

    // Active sort column (a FileSystemEntry property name) + direction. Observable so the
    // GridView headers can render the sort-direction glyph on the active column.
    [ObservableProperty] private string _sortColumn    = nameof(FileSystemEntry.Name);
    [ObservableProperty] private bool   _sortAscending = true;

    private bool   _navigating;
    private bool   _refreshing;

    // ── Entry count ──────────────────────────────────────────────────────────

    private int _selectedCount;

    private void UpdateEntryCountLabel(int selectedCount = 0)
    {
        _selectedCount = selectedCount;
        var folders = Entries.Count(e => e.IsDirectory);
        var files   = Entries.Count(e => !e.IsDirectory);

        HasFolders         = folders > 0;
        HasFiles           = files   > 0;
        ShowCountSeparator = folders > 0 && files > 0;
        IsEmpty            = folders == 0 && files == 0;

        FolderCountText  = $"{folders} {(folders == 1 ? "folder" : "folders")}";
        FileCountText    = $"{files} {(files == 1 ? "file" : "files")}";
        SelectionSummary = selectedCount > 0 ? $"{selectedCount} selected  ·  " : string.Empty;
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
    public FileSystemViewModel(string targetDirectory, IShellServices shell, IAIService ai,
                               IReadOnlyDictionary<Type, IFeatureConfig> configs)
        : this(shell, ai, configs)
    {
        InitDebounceTimer();
        _rootPath = targetDirectory;
        BuildDirectoryTree(targetDirectory);
        NavigateTo(targetDirectory);
    }

    /// <summary>Opens "This PC" showing all connected drives.</summary>
    public static FileSystemViewModel CreateThisPc(IShellServices shell, IAIService ai,
                                                   IReadOnlyDictionary<Type, IFeatureConfig> configs)
    {
        var vm = new FileSystemViewModel(shell, ai, configs);
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

    private readonly IShellServices _shell;
    private readonly IAIService _ai;

    internal FileSystemFeatureRegistry Registry { get; }

    private FileSystemViewModel(IShellServices shell, IAIService ai,
                                IReadOnlyDictionary<Type, IFeatureConfig> configs)
    {
        _shell          = shell;
        _ai             = ai;
        Registry        = FileSystemFeatureRegistry.For(shell, ai, configs);
        _actionRegistry = new FileActionManager(Registry);
        _opener         = new DefaultFileOpener(Registry);
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
            ResetSortAndFilter();

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
                ResetSortAndFilter();
                RefreshEntries();
                SelectAndExpandPath(path);  // sync tree
                FireNavigationChanged(path); // sync breadcrumb
        }
        finally
        {
            _navigating = false;
        }

        // Build the empty-selection action strip so the "New" button is available
        // immediately on entering a folder (no click required).
        OnSelectionChanged([]);
    }

    [RelayCommand]
    private async Task OpenEntry(FileSystemEntry entry)
    {
        if (entry.IsDirectory) { NavigateTo(entry.FullPath); return; }
        await OpenFileDefaultAsync(entry);
    }

    /// <summary>
    /// Resolves and executes the default "open" action for a file via <see cref="DefaultFileOpener"/>
    /// (FileExtension &gt; MagicNumber &gt; PerceivedType &gt; ContentType; internal beats shell at the
    /// same specificity), refreshing the view when the action mutated the file system.
    /// </summary>
    private async Task OpenFileDefaultAsync(FileSystemEntry entry)
    {
        if (await _opener.OpenAsync(entry.FullPath)) Refresh();
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

    // ── IPageViewModel ────────────────────────────────────────────────────────

    /// <summary>AI surfaces of the viewlets active for the current folder. The view re-registers these
    /// on every navigation (see <c>FileSystemView.RefreshViewlets</c>); empty when none apply.</summary>
    private IReadOnlyList<IViewletAiSurface> _viewletSurfaces = [];

    /// <summary>Called by the view when the active viewlet set changes so their context + tools
    /// merge into <see cref="GetContext"/> / <see cref="GetClientTools"/>.</summary>
    public void SetActiveViewletSurfaces(IReadOnlyList<IViewletAiSurface> surfaces)
        => _viewletSurfaces = surfaces;

    public string GetContext()
    {
        if (_isThisPcMode)
        {
            var drives = Entries.Count;
            return $"File browser — This PC ({drives} drive{(drives == 1 ? "" : "s")}).";
        }
        if (string.IsNullOrEmpty(CurrentPath))
            return "File browser — no location selected.";

        var folders = Entries.Count(e => e.IsDirectory);
        var files   = Entries.Count - folders;
        var sb = new StringBuilder(
            $"File browser at '{CurrentPath}' — {folders} folder{(folders == 1 ? "" : "s")}, " +
            $"{files} file{(files == 1 ? "" : "s")}");

        var selection = CurrentSelection;
        if (selection.Count > 0)
        {
            const int cap = 5;
            sb.Append(". Selected: ").Append(string.Join(", ", selection.Take(cap).Select(e => e.Name)));
            if (selection.Count > cap) sb.Append($" (+{selection.Count - cap} more)");
        }
        sb.Append('.');

        // Append each active viewlet's context line (a .NET solution's build state, a git repo's status…).
        foreach (var surface in _viewletSurfaces)
        {
            var line = surface.GetContext();
            if (!string.IsNullOrWhiteSpace(line))
                sb.Append('\n').Append(line);
        }

        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools()
    {
        var tools = new List<IClientTool>
        {
            new GetFileListTool(this),
            new FindFilesByNameTool(this),
            new GetFileContentsTool(this),
            new GetLineCountTool(this),
            new GetFileStatsTool(this),
            new CreateTextFileTool(this),
            new CreateDirectoryTool(this),
            new CopyTool(this),
            new MoveTool(this),
            new RenameTool(this),
            new DeleteTool(this),
        };

        // Tools contributed by the active viewlets (dotnet build/test, git status/diff…).
        foreach (var surface in _viewletSurfaces)
            tools.AddRange(surface.GetClientTools());

        return tools;
    }

    /// <summary>The folder these tools resolve relative names against (their confinement boundary).
    /// Distinguishes one file-system tab from another when both are pinned as AI context.</summary>
    public string? GetSecurityContext()
        => IsThisPcMode                     ? "This PC"
         : string.IsNullOrEmpty(CurrentPath) ? null
         : CurrentPath;

    /// <summary>
    /// Risk of letting the AI act here: High when unconfined or system-critical (This PC, the system
    /// drive root, or inside the Windows folder), Medium for a whole non-system drive root, else Low.
    /// </summary>
    public ContextSecurityRisk GetContextSecurityRisk()
    {
        if (IsThisPcMode) return ContextSecurityRisk.High;             // unconfined — AI can go anywhere
        if (string.IsNullOrEmpty(CurrentPath)) return ContextSecurityRisk.High;

        string full;
        try { full = Path.GetFullPath(CurrentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return ContextSecurityRisk.Low; }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(windowsDir) &&
            (full.Equals(windowsDir, StringComparison.OrdinalIgnoreCase) ||
             full.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            return ContextSecurityRisk.High;                          // the Windows system folder

        var driveRoot  = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(driveRoot) && full.Equals(driveRoot, StringComparison.OrdinalIgnoreCase))
            return string.Equals(driveRoot, systemRoot, StringComparison.OrdinalIgnoreCase)
                ? ContextSecurityRisk.High                            // system drive root (e.g. C:\)
                : ContextSecurityRisk.Medium;                         // other drive root (e.g. D:\)

        return ContextSecurityRisk.Low;
    }

    public IContext? GetContextObject()
    {
        if (IsThisPcMode)
            return new FileSystemContext
            {
                RootPath        = string.Empty,
                CurrentPath     = string.Empty,
                AvailableDrives = DriveInfo.GetDrives()
                                           .Select(d => d.RootDirectory.FullName)
                                           .ToList()
            };
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
        Func<FileSystemEntry, object> key = SortColumn switch
        {
            nameof(FileSystemEntry.Modified)  => e => e.Modified,
            // Drives report capacity via DriveTotalBytes; files via SizeBytes.
            nameof(FileSystemEntry.SizeBytes) => e => (object)(e.IsDrive ? e.DriveTotalBytes : e.SizeBytes),
            nameof(FileSystemEntry.TypeLabel) => e => e.TypeLabel,
            _ => e => e.Name
        };

        // Folders always first, then sort within group
        return SortAscending
            ? source.OrderBy(e => !e.IsDirectory).ThenBy(key)
            : source.OrderBy(e => !e.IsDirectory).ThenByDescending(key);
    }

    /// <summary>Reorders the current <see cref="Entries"/> in place using the active sort —
    /// without re-reading the directory, so it works in "This PC" (drive list) mode too.</summary>
    private void ResortEntries()
    {
        var sorted = ApplySort(Entries.ToList()).ToList();
        Entries.Clear();
        foreach (var e in sorted) Entries.Add(e);
    }

    /// <summary>Resets sort to Name-ascending and clears the footer filter — called on navigation
    /// so each folder starts from a clean view.</summary>
    private void ResetSortAndFilter()
    {
        SortColumn    = nameof(FileSystemEntry.Name);
        SortAscending = true;
        ActiveFilter  = EntryFilter.None;
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn    = column;
            SortAscending = true;
        }
        ResortEntries();
    }

}
