using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.Controls;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.RibbonHandlers;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using Nexaflow.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>
/// The WPF file view's view-model over one <see cref="FileSystemModel"/>: what only this view needs — the footer's words,
/// the action strip and its timing, the create picker and the define-new wizard, the operations panel — while where the tab
/// is, what it lists and what the assistant is given are the model's.
/// </summary>
public partial class FileSystemViewModel : ObservableObject, IPageViewModel, ISearchable, IPasteTarget
{
    /// <summary>The tab, named in no UI toolkit.</summary>
    public FileSystemModel Model { get; }

    private readonly IShellServices _shell;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Opens a specific directory as the starting point.</summary>
    public FileSystemViewModel(string targetDirectory, IShellServices shell, IAIService ai,
                               IReadOnlyDictionary<Type, IFeatureConfig> configs, IVirtualFileSystem vfs,
                               FileSystemFeatureRegistry registry, FileOperationQueue operations, FileCompanions companions)
        : this(shell, configs, vfs, registry, operations, companions)
        => Model.Navigation.StartAt(targetDirectory);

    /// <summary>Opens "This PC" showing all connected drives.</summary>
    public static FileSystemViewModel CreateThisPc(IShellServices shell, IAIService ai,
                                                   IReadOnlyDictionary<Type, IFeatureConfig> configs, IVirtualFileSystem vfs,
                                                   FileSystemFeatureRegistry registry, FileOperationQueue operations,
                                                   FileCompanions companions)
    {
        using var _t = Timing.Measure("ThisPC.CreateThisPc (total, UI thread)");
        var vm = new FileSystemViewModel(shell, configs, vfs, registry, operations, companions);
        vm.Model.Navigation.StartAtThisPc();
        return vm;
    }

    private FileSystemViewModel(IShellServices shell, IReadOnlyDictionary<Type, IFeatureConfig> configs, IVirtualFileSystem vfs,
                                FileSystemFeatureRegistry registry, FileOperationQueue operations, FileCompanions companions)
    {
        _shell = shell;
        Model  = new FileSystemModel(shell, configs, vfs, registry, operations, companions);
        // The action strip is rebuilt once the selection settles.
        _actionDebounceTimer = shell.CreateUiDelay(TimeSpan.FromMilliseconds(150), OnActionDebounceTimer);
        FileOperations = new FileOperationsPanelViewModel(Operations, shell);

        Model.Location.PropertyChanged += OnLocationChanged;
        Model.Listing.PropertyChanged  += OnListingChanged;
        Model.Creation.PropertyChanged += OnCreationChanged;
        Model.Listing.Counted          += () => UpdateEntryCountLabel(Model.Listing.FolderCount, Model.Listing.FileCount, 0);
        Model.Navigation.Arrived       += () => OnSelectionChanged([]);
    }

    /// <summary>The model's changes, under the names this view binds.</summary>
    private void OnLocationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderLocation.CurrentPath)) OnPropertyChanged(nameof(CurrentPath));
    }

    private void OnListingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FolderListing.ListingFailure):
                OnPropertyChanged(nameof(ListingFailure));
                OnPropertyChanged(nameof(HasListingFailure));
                OnPropertyChanged(nameof(ListingFailureText));
                break;
            case nameof(FolderListing.ActiveFilter):
                OnPropertyChanged(nameof(ActiveFilter));
                ApplyEntryFilter();
                break;
            case nameof(FolderListing.IsLoadingEntries):
            case nameof(FolderListing.ShowsCloudStatus):
            case nameof(FolderListing.ShowsPosixColumns):
            case nameof(FolderListing.IsFolderBusy):
            case nameof(FolderListing.FolderBusyMessage):
            case nameof(FolderListing.SortColumn):
            case nameof(FolderListing.SortAscending):
                OnPropertyChanged(e.PropertyName);
                break;
        }
    }

    private void OnCreationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileCreation.FileName):   OnPropertyChanged(nameof(CreateFileName));        break;
            case nameof(FileCreation.NameExists): OnPropertyChanged(nameof(CreateNameExists));      break;
            case nameof(FileCreation.Options):    OnPropertyChanged(nameof(SelectedCreateOptions)); break;
            case nameof(FileCreation.CanCreate):
                OnPropertyChanged(nameof(CanCreate));
                CreateCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    // ── Where the tab is ──────────────────────────────────────────────────────

    public string CurrentPath  => Model.Location.CurrentPath;
    public bool   IsThisPcMode => Model.Location.IsThisPc;
    public string RootPath     => Model.Location.RootPath;

    public ObservableCollection<FileSystemTreeNode>   TreeRoots => Model.Navigation.TreeRoots;
    public RangeObservableCollection<FileSystemEntry> Entries   => Model.Listing.Entries;

    /// <summary>
    /// Raised whenever the current directory changes, with the ordered trail from the root label — each segment the path
    /// a breadcrumb click browses to.
    /// </summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged
    {
        add    => Model.Navigation.NavigationChanged += value;
        remove => Model.Navigation.NavigationChanged -= value;
    }

    /// <summary>The runtime's file system, which the browser reads through.</summary>
    internal IVirtualFileSystem Vfs => Model.Vfs;

    internal FileSystemFeatureRegistry Registry => Model.Registry;

    /// <summary>The companion files the features have declared — hidden in the list, carried by every operation.</summary>
    internal FileCompanions Companions => Model.Companions;

    /// <summary>Copy, move and delete work in flight, shared by every file-browser tab of the runtime.</summary>
    public FileOperationQueue Operations => Model.Operations;

    /// <summary>The panel above the tree that shows those operations while they run.</summary>
    public FileOperationsPanelViewModel FileOperations { get; }

    /// <summary>Surfaces a file-operation error (e.g. a failed drag-drop move) as a toast.</summary>
    internal void ReportError(string message) => Model.ReportError(message);

    /// <summary>The newest browse, done once the folder is open and its listing on the way.</summary>
    internal Task Browsing => Model.Navigation.Browsing;

    /// <summary>The newest read of the open folder, done once its every row is in <see cref="Entries"/>.</summary>
    internal Task EntriesLoaded => Model.Listing.EntriesLoaded;

    public Task BrowseToAsync(string path) => Model.Navigation.BrowseToAsync(path);
    public void Reinitialize(Dictionary<string, string> pageParams) => Model.Navigation.Reinitialize(pageParams);
    public void NavigateUp() => Model.Navigation.NavigateUp();
    public void GoToThisPc(bool rebuildTree = false) => Model.Navigation.GoToThisPc(rebuildTree);
    public void OnTreeNodeSelected(FileSystemTreeNode node) => Model.Navigation.OnTreeNodeSelected(node);
    public void ResetRootToCurrentPath() => Model.Navigation.ResetRootToCurrentPath();
    public Task ExpandToPathAsync(string path) => Model.Navigation.ExpandToPathAsync(path);
    public Task<string?> NearestExistingAncestorAsync(string path) => Model.Navigation.NearestExistingAncestorAsync(path);

    /// <summary>Reads the open folder and the tree again, keeping the tree's expanded state; the selection is cleared so the
    /// action strip is worked out again.</summary>
    public void Refresh() => Model.Navigation.Refresh();

    /// <summary>Cancels any in-flight directory enumeration so no folder handle is held open — called by the
    /// host before a viewlet mutates/deletes the current folder (see the folder-quiesce fan-out).</summary>
    public void CancelEntryLoad() => Model.Listing.CancelRead();

    [RelayCommand]
    private Task OpenEntry(FileSystemEntry entry) => Model.Navigation.OpenAsync(entry);

    /// <summary>Opens <paramref name="folderPath"/> as a new File Explorer tab in the window's right pane. Backs the tree's
    /// "Open in right pane" context-menu item.</summary>
    [RelayCommand]
    private void OpenInRightPane(string? folderPath) => Model.Navigation.OpenInRightPane(folderPath);

    // ── What the list shows ───────────────────────────────────────────────────

    public bool    IsLoadingEntries  => Model.Listing.IsLoadingEntries;
    public bool    ShowsCloudStatus  => Model.Listing.ShowsCloudStatus;
    public bool    ShowsPosixColumns => Model.Listing.ShowsPosixColumns;
    public string? ListingFailure    => Model.Listing.ListingFailure;

    public bool HasListingFailure => ListingFailure is not null;

    public string ListingFailureText => ListingFailure is null ? string.Empty : Str.Format("WindowsFileSystem.List.FailedFormat", ListingFailure);

    /// <summary>Reads the folder again after a listing failed.</summary>
    [RelayCommand]
    private void RetryListing() => Refresh();

    /// <summary>The column the rows are sorted by, so the headers draw the sort-direction glyph on it.</summary>
    public string SortColumn    => Model.Listing.SortColumn;
    public bool   SortAscending => Model.Listing.SortAscending;

    [RelayCommand]
    private void SortBy(string column) => Model.Listing.SortBy(column);

    /// <summary>Active footer filter; drives which entries the list view shows.</summary>
    public EntryFilter ActiveFilter
    {
        get => Model.Listing.ActiveFilter;
        set => Model.Listing.ActiveFilter = value;
    }

    private void ApplyEntryFilter()
    {
        var view = CollectionViewSource.GetDefaultView(Entries);
        if (view is null) return;
        var shows = FolderListing.FilterFor(ActiveFilter);
        view.Filter = shows is null ? null : o => o is FileSystemEntry entry && shows(entry);
    }

    [RelayCommand]
    private void ToggleFolderFilter() => Model.Listing.ToggleFilter(EntryFilter.FoldersOnly);

    [RelayCommand]
    private void ToggleFileFilter() => Model.Listing.ToggleFilter(EntryFilter.FilesOnly);

    // ── Footer: clickable folder/file counts + quick-filter ────────────────────
    [ObservableProperty] private string _selectionSummary   = string.Empty; // "N selected · " or ""
    [ObservableProperty] private string _folderCountText    = string.Empty; // "38 folders"
    [ObservableProperty] private string _fileCountText      = string.Empty; // "2 files"
    [ObservableProperty] private bool   _hasFolders;
    [ObservableProperty] private bool   _hasFiles;
    [ObservableProperty] private bool   _showCountSeparator;
    [ObservableProperty] private bool   _isEmpty = true;

    /// <summary>Updates only the selection part of the footer, reusing the totals the listing last published — a selection
    /// change never alters them, and counting the live collection would race the streaming loader.</summary>
    private void UpdateSelectionCount(int selectedCount)
        => UpdateEntryCountLabel(Model.Listing.FolderCount, Model.Listing.FileCount, selectedCount);

    /// <summary>Sets the footer labels from already-known counts.</summary>
    private void UpdateEntryCountLabel(int folders, int files, int selectedCount)
    {
        HasFolders         = folders > 0;
        HasFiles           = files   > 0;
        ShowCountSeparator = folders > 0 && files > 0;
        IsEmpty            = folders == 0 && files == 0;

        FolderCountText  = folders == 1
            ? Str.Format("WindowsFileSystem.Footer.FolderCountOne", folders)
            : Str.Format("WindowsFileSystem.Footer.FolderCountMany", folders);
        FileCountText    = files == 1
            ? Str.Format("WindowsFileSystem.Footer.FileCountOne", files)
            : Str.Format("WindowsFileSystem.Footer.FileCountMany", files);
        SelectionSummary = selectedCount > 0 ? Str.Format("WindowsFileSystem.Footer.SelectedFormat", selectedCount) : string.Empty;
    }

    // ── Watching, while the view is shown ─────────────────────────────────────

    /// <summary>True while a long mutation is in flight on the current folder — the view shows a wait panel.</summary>
    public bool   IsFolderBusy      => Model.Listing.IsFolderBusy;
    public string FolderBusyMessage => Model.Listing.FolderBusyMessage;

    /// <summary>Raised when a mutation on the current folder finishes (its busy mark cleared), so the view can
    /// refresh or re-home. Runs on the UI thread.</summary>
    public event Action? FolderBusyCleared
    {
        add    => Model.Listing.FolderBusyCleared += value;
        remove => Model.Listing.FolderBusyCleared -= value;
    }

    /// <summary>Begins observing the shell's folder-busy signal and syncs the current state. Called by the view on load.</summary>
    public void AttachBusyTracking()
    {
        Model.Listing.AttachBusyTracking();

        // The operations panel starts collapsed, so it cannot rely on its own Loaded to start watching
        // the queue — a collapsed control that only wakes when it loads can never become visible.
        FileOperations.Attach();
    }

    /// <summary>Stops observing the shell's folder-busy signal. Called by the view on unload so the long-lived
    /// shell doesn't retain this VM.</summary>
    public void DetachBusyTracking()
    {
        Model.Listing.DetachBusyTracking();
        FileOperations.Detach();
    }

    /// <summary>Begins observing the This PC contributors, so editing them in Options updates an open tab. Called by the
    /// view on load.</summary>
    public void AttachThisPcProviderTracking() => Model.Navigation.AttachThisPcProviderTracking();

    /// <summary>Stops observing the This PC contributors. Called by the view on unload.</summary>
    public void DetachThisPcProviderTracking() => Model.Navigation.DetachThisPcProviderTracking();

    /// <summary>
    /// Watches the open folder while the view is loaded, so the list keeps up with what changes there. The clipboard is
    /// listened to as long, so the strip's Paste keeps up with what is copied, here or anywhere else.
    /// </summary>
    public void AttachFolderWatching()
    {
        _shell.Clipboard.KindsChanged -= OnClipboardChanged;
        _shell.Clipboard.KindsChanged += OnClipboardChanged;
        Model.Listing.AttachFolderWatching();
    }

    public void DetachFolderWatching()
    {
        _shell.Clipboard.KindsChanged -= OnClipboardChanged;
        Model.Listing.DetachFolderWatching();
    }

    // ── Selection and the action strip ────────────────────────────────────────

    [ObservableProperty] private FileSystemEntry? _selectedEntry;

    public ObservableCollection<FileActionViewModel> FileActions { get; } = [];

    /// <summary>What is selected in the list (forwarded from the view).</summary>
    public IReadOnlyList<FileSystemEntry> CurrentSelection
    {
        get => Model.Selection;
        private set => Model.Selection = value;
    }

    // Debounce timer — action strip is only rebuilt after input has been idle
    // for a short interval, so rapid selection changes (including double-clicks)
    // never stall the UI thread.
    private readonly IUiTimer _actionDebounceTimer;
    private IReadOnlyList<FileSystemEntry> _pendingSelection = [];

    // ── Shift state (forwarded from view key events) ──────────────────────────
    [ObservableProperty] private bool _shiftHeld;

    partial void OnShiftHeldChanged(bool value)
    {
        foreach (var fa in FileActions)
            fa.ShiftHeld = value;
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
        OpenDefineNewWizardCommand.NotifyCanExecuteChanged();

        // Update the visible count immediately — this is a cheap label update.
        SelectedEntry = selected.Count == 1 ? selected[0] : null;
        UpdateSelectionCount(selected.Count);

        // Restart the debounce window; the strip rebuild is deferred.
        _actionDebounceTimer.Stop();

        // On This PC, a selected drive still gets folder actions. Only an empty selection there yields an empty strip —
        // which is correct.
        if (IsThisPcMode && !selected.Any(e => e.IsThisPcItem))
        {
            FileActions.Clear();
            return;
        }

        _actionDebounceTimer.Start();
    }

    /// <summary>Fires on the UI thread after input has been idle for 150 ms, and rebuilds the action strip.</summary>
    private async void OnActionDebounceTimer()
    {
        var selected = _pendingSelection;
        var offered  = await Model.Actions.ForStripAsync(selected);

        // Check the selection hasn't changed while the actions were worked out.
        if (!ReferenceEquals(selected, _pendingSelection)) return;

        FileActions.Clear();
        foreach (var action in offered.Actions)
            FileActions.Add(BuildActionVm(action, offered.Targets));

        // "New" — the first item when an empty folder area is in focus (no file selected). It is a
        // folder action (operates on the current folder) whose PerformAction opens the create overlay.
        if (offered.OffersNew)
            FileActions.Insert(0, BuildActionVm(new FolderActionAdapter(new NewMenuAction(OpenCreateOverlay)), offered.Targets));
    }

    private FileActionViewModel BuildActionVm(IFileAction action, IReadOnlyList<string> paths)
    {
        var vm = new FileActionViewModel(action);
        vm.ShiftHeld = ShiftHeld;
        vm.UpdatePaths(paths);
        vm.ActionExecuted += Refresh;
        vm.ActionFailed   += _shell.ShowError;
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

    /// <summary>
    /// The actions a context menu offers for <paramref name="entries"/>. Does <em>not</em> update the action strip.
    /// </summary>
    public IReadOnlyList<FileActionViewModel> BuildContextActions(IReadOnlyList<FileSystemEntry> entries)
    {
        var paths  = Model.Actions.TargetsOf(entries);
        var result = new List<FileActionViewModel>();
        foreach (var action in Model.Actions.ForMenu(entries))
        {
            var vm = new FileActionViewModel(action);
            vm.ShiftHeld = ShiftHeld;
            vm.UpdatePaths(paths);
            vm.ActionExecuted += Refresh;
            vm.ActionFailed   += _shell.ShowError;
            result.Add(vm);
        }
        return result;
    }

    /// <summary>
    /// Finds the action of type <typeparamref name="T"/> in the current action strip and
    /// executes it exactly as if the user clicked the button, including flash animation and
    /// shift-force detection. Returns false when no such action is currently visible.
    /// </summary>
    public bool TryExecuteAction<T>() where T : class
    {
        if (ActionOf<T>() is not { } vm || !vm.ExecuteCommand.CanExecute(null)) return false;
        vm.ExecuteCommand.Execute(null);
        return true;
    }

    /// <summary>The strip's action of type <typeparamref name="T"/>, where the strip offers one now.</summary>
    private FileActionViewModel? ActionOf<T>() where T : class
        => FileActions.FirstOrDefault(a => a.Action is T || (a.Action is FolderActionAdapter adapter && adapter.Inner is T));

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

    // ── Create overlay (the "New" button) ──────────────────────────────────────
    // A single pane: pick a file type from the icon list, name it below. The name is
    // prefilled per type, its extension follows the selected type, and a live warning
    // shows when the resolved name already exists in the current folder. A type that asks
    // for options first (IFileCreateWithOptions) shows them between the two while chosen.

    [ObservableProperty] private bool _createOverlayVisible;
    [ObservableProperty] private CreateActionViewModel? _selectedCreateAction;

    public ObservableCollection<CreateActionViewModel> CreateActions { get; } = [];

    /// <summary>The name typed for the new item.</summary>
    public string CreateFileName
    {
        get => Model.Creation.FileName;
        set => Model.Creation.FileName = value;
    }

    public bool CreateNameExists => Model.Creation.NameExists;

    /// <summary>The options the chosen type asks for, opened on the current folder while it is chosen; null for a type a
    /// name is enough for.</summary>
    public IFileCreateOptions? SelectedCreateOptions => Model.Creation.Options;

    public bool CanCreate => Model.Creation.CanCreate;

    /// <summary>The newest name-taken question, answered once <see cref="CanCreate"/> says what it found.</summary>
    internal Task CreateNameChecked => Model.Creation.NameChecked;

    /// <summary>Opens the create overlay, populated live from every create-action source.</summary>
    public void OpenCreateOverlay()
    {
        if (!Model.Location.CanCreateHere) return;
        OpenCreateOverlayWith(Model.ActionManager.GetCreateActions());
    }

    /// <summary>Populates and shows the overlay for a given action set (also the unit-test seam).</summary>
    internal void OpenCreateOverlayWith(IReadOnlyList<IFileCreateEntry> actions)
    {
        CreateActions.Clear();
        foreach (var action in actions)
            CreateActions.Add(new CreateActionViewModel(action, SelectCreateAction));

        SelectedCreateAction = CreateActions.FirstOrDefault();
        Model.Creation.Recheck();          // also covers the empty-list case
        CreateOverlayVisible = true;
    }

    private void SelectCreateAction(CreateActionViewModel a) => SelectedCreateAction = a;

    partial void OnSelectedCreateActionChanged(CreateActionViewModel? value)
    {
        foreach (var a in CreateActions)
            a.IsSelected = ReferenceEquals(a, value);
        Model.Creation.Choose(value?.Action);
    }

    /// <summary>The picker closed: the options it showed are closed with it.</summary>
    partial void OnCreateOverlayVisibleChanged(bool value)
    {
        if (!value) Model.Creation.Close();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        // Made before the picker closes: closing it closes the options the item is made through.
        if (!await Model.Creation.CreateAsync()) return;
        CreateOverlayVisible = false;
        Refresh();
    }

    [RelayCommand]
    private void CancelCreate() => CreateOverlayVisible = false;

    // ── "Define New" association wizard ─────────────────────────────────────────
    [ObservableProperty] private bool _wizardOverlayVisible;
    [ObservableProperty] private DefineNewWizardViewModel? _wizard;

    /// <summary>
    /// Opens the wizard that associates a file/extension/glob with an internal viewer or an
    /// external app. Only available for a single selected file — folders, drives, "This PC" and
    /// empty selections have no viewer/association concept.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenDefineNewWizard))]
    private void OpenDefineNewWizard()
    {
        if (CurrentSelection is not [{ IsDirectory: false, IsThisPcItem: false } file]) return;

        Wizard = new DefineNewWizardViewModel(
            _shell, Model.ExternalApps, Registry.FileActions,
            CurrentPath, file.FullPath,
            onClose: () =>
            {
                WizardOverlayVisible = false;
                Wizard = null;
                // Rebuild the action strip so the just-defined action is usable immediately,
                // without re-enumerating the folder (which would clear the list selection).
                OnSelectionChanged(CurrentSelection);
            });
        WizardOverlayVisible = true;
    }

    private bool CanOpenDefineNewWizard()
        => !IsThisPcMode && CurrentSelection is [{ IsDirectory: false, IsThisPcItem: false }];

    /// <summary>
    /// Opens the relevant Options editor for an existing action so the user can tweak it: a
    /// user-defined external app jumps to "External Apps" with that app selected; an internal
    /// viewer jumps to "File Type Actions" with its experience selected.
    /// </summary>
    [RelayCommand]
    private void ModifyAction(FileActionViewModel? vm)
    {
        switch (vm?.Action)
        {
            case CustomAction ca:
                ExternalAppsEditorControl.PendingSelect = ca.Definition;
                _shell.OpenOptions(Model.ExternalApps.ConfigName);   // "externalapps"
                break;
            case { OpensViewer: true } viewer:
                FileMapEditorControl.PendingExperienceId = viewer.ExperienceId;
                _shell.OpenOptions("filemap");                        // FileMapConfig.ConfigName
                break;
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    /// <summary>Called by the view when the active viewlet set changes so their context + tools
    /// merge into <see cref="GetContext"/> / <see cref="GetClientTools"/>.</summary>
    public void SetActiveViewletSurfaces(IReadOnlyList<IViewletAiSurface> surfaces)
        => Model.Assistance.SetActiveViewletSurfaces(surfaces);

    public string GetContext() => Model.Assistance.GetContext();
    public IReadOnlyList<IClientTool> GetClientTools() => Model.Assistance.GetClientTools();
    public string? GetSecurityContext() => Model.Assistance.GetSecurityContext();
    public ContextGrant GetContextGrant() => Model.Assistance.GetContextGrant();
    public ContextSecurityRisk GetContextSecurityRisk() => Model.Assistance.GetContextSecurityRisk();
    public IContext? GetContextObject() => Model.Assistance.GetContextObject();

    // ── ISearchable ───────────────────────────────────────────────────────────

    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers => Model.Assistance.TermRecognizers;
    public string SearchTargetDescription => Model.Assistance.SearchTargetDescription;
    public float ScoreQuery(string input) => Model.Assistance.ScoreQuery(input);

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        => Model.Assistance.SearchAsync(request, display, ct);

    // ── IPasteTarget ──────────────────────────────────────────────────────────

    /// <summary>Files pasted here land in the open folder, as the strip's Paste puts them there — while the strip offers it.</summary>
    public IReadOnlyList<string> PasteKinds => ActionOf<PasteFiles>() is not null ? PasteFiles.PastedKinds : [];

    public Task PasteAsync(IDragPayload pasted)
    {
        PasteFiles.Paste(pasted, CurrentPath, Operations, _shell.Clipboard);
        return Task.CompletedTask;
    }

    /// <summary>What the clipboard holds changed: the strip is read again, so Paste comes and goes with files there.</summary>
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        _actionDebounceTimer.Stop();
        _actionDebounceTimer.Start();
    }
}
