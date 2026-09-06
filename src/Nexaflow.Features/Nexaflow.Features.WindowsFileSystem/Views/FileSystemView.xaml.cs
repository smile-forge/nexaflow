using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.WindowsFileSystem.Controls;
using Nexaflow.Features.WindowsFileSystem.RibbonHandlers;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Features.WindowsFileSystem;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.WindowsFileSystem.Views;

public partial class FileSystemView : UserControl, IPageView, ISelectionProvider, IKeyboardHandler
{
    public FileSystemViewModel ViewModel { get; }

    // ── IPageView ─────────────────────────────────────────────────────────────
    IPageViewModel? IPageView.ViewModel => ViewModel;

    // ── ISelectionProvider ────────────────────────────────────────────────────
    IReadOnlyList<string> ISelectionProvider.SelectedFilePaths
        => ViewModel.CurrentSelection.Select(e => e.FullPath).ToList();

    private readonly IKeyboardHandler _actionKeys;   // maps shortcut keys → file actions (this view IS the page handler)
    private readonly IDropTarget      _dropTarget;

    // Drag-from-list tracking. DragArming, not a bare bool: arming that outlives its own gesture is
    // what let a press the list never saw released start a drag nobody asked for.
    private readonly DragArming _listDrag = new();

    // Click-to-deselect is deferred to mouse-up so a mouse-down on the selected item can still begin a
    // drag; holds the list awaiting that deselect, cleared once a drag fires (see the mouse handlers).
    private ListView? _deselectOnMouseUp;

    // Right-drag tracking. Explorer's gesture: hold the RIGHT button, drag, and the destination asks
    // what to do — rather than the modifier having had to be decided before the button came up.
    private readonly DragArming _listRightDrag = new();

    // …which is why the context menu is deferred to mouse-up too. Opening it on mouse-down swallowed
    // the press a right-drag needs, exactly as deselecting on mouse-down used to make a selected file
    // impossible to drag. Holds the list the menu belongs to and the entries it would be about.
    private ListView? _rightMenuOnMouseUp;
    private List<FileSystemEntry> _rightMenuTargets = [];

    // Which button is dragging, remembered from the hover — the drop cannot be asked. See DropChoiceLatch.
    private readonly DropChoiceLatch _dropChoice = new();

    // The wash on the row a drop would land in — the destination, shown rather than spelled out.
    private readonly DropTargetHighlight _dropHighlight = new();

    // A question a drop has asked but that has not been put to the user yet, and whether our own drag
    // loop is still on the stack. See AskPendingChoice: a menu must not open inside DoDragDrop.
    private (IDropChoiceTarget Target, DropPlan Plan)? _pendingChoice;
    private bool _localDragInFlight;

    // Drag-from-ActionStrip tracking
    private readonly DragArming _actionDrag = new();
    private FileActionViewModel? _actionDragVm;

    // ── Viewlet state ─────────────────────────────────────────────────────────
    private readonly List<ViewletHost> _activeViewletHosts = [];
    private bool _fileViewActive;
    private int  _activeFullIndex;

    /// <summary>
    /// Raised whenever the current directory changes.
    /// Subscribers (e.g. ShellViewModel) should update the tab's BreadcrumbSegments.
    /// Each tuple contains (DisplayLabel, FullPath) — FullPath is empty for "This PC".
    /// </summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    public FileSystemView(FileSystemViewModel viewModel, IKeyboardHandler keyboardHandler, IDropTarget dropTarget)
    {
        InitializeComponent();
        ViewModel        = viewModel;
        _actionKeys      = keyboardHandler;
        _dropTarget      = dropTarget;
        DataContext = viewModel;
        ViewModel.NavigationChanged += OnViewModelNavigationChanged;
        ViewModel.PropertyChanged   += OnViewModelPropertyChanged;
        ViewModel.FolderBusyCleared += ReEvaluateCurrentFolder;   // a mutation on this folder finished
        WireDragDrop();
        WireActionStripDrag();

        // The VM subscribes to the shell's folder-busy signal only while this view is loaded (the shell
        // outlives the tab, so a permanent subscription would leak the VM); re-sync on each load.
        Loaded   += (_, _) => ViewModel.AttachBusyTracking();
        Unloaded += (_, _) => ViewModel.DetachBusyTracking();

        // Same rule for the This PC contributors and the mount table, which the shell also outlives.
        Loaded   += (_, _) => ViewModel.AttachThisPcProviderTracking();
        Unloaded += (_, _) => ViewModel.DetachThisPcProviderTracking();

        // Column-header sorting. GridViewColumnHeader is a ButtonBase that captures the
        // mouse on press, so a Button inside the header template never sees the click —
        // handle its Click at the ListView level instead. Whole header is clickable.
        FileListView.AddHandler(ButtonBase.ClickEvent,  new RoutedEventHandler(OnColumnHeaderClick));
        DriveListView.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));

        Loaded += (_, _) => UpdateListViewVisibility();

        // Catch up on the navigation that already happened, because subscribing above was too late to hear
        // it. A tab opened AT a path — a ribbon folder button, a restored session tab, open-in-new-tab —
        // gets a FileSystemViewModel constructed with that path, and its constructor calls NavigateTo. That
        // raises NavigationChanged before this view exists, so the only announcement of the folder is made
        // to nobody and RefreshViewlets never runs: the tab shows the right files and no viewlets at all.
        //
        // It used to work by accident. Selecting the folder's tree node echoed back into OnTreeNodeSelected,
        // which called NavigateTo a second time — by then this view was listening, so the event arrived and
        // the viewlets appeared. The tree-probe perf work stopped that duplicate navigation (rightly: it
        // enumerated the folder twice), and took the accident with it.
        //
        // Once, here, rather than on Loaded: this is a full teardown and rebuild that constructs each
        // viewlet's view — git and dotnet run real tools — so doing it on every tab switch would repeat
        // that work for a folder whose viewlets are already on screen.
        if (!ViewModel.IsThisPcMode && !string.IsNullOrEmpty(ViewModel.CurrentPath))
            RefreshViewlets(ViewModel.CurrentPath, ViewModel.IsThisPcMode);
    }

    private void WireDragDrop()
    {
        FileListView.AllowDrop     = true;
        FileListView.PreviewMouseMove += FileListView_PreviewMouseMove;
        FileListView.DragOver  += OnListViewDragOver;
        FileListView.DragLeave += OnDragLeave;
        FileListView.Drop      += OnListViewDrop;

        // This PC mode had no drop wiring at all, so a drag over a drive row did nothing and said
        // nothing. It resolves the same way the file list does: the row under the cursor, or nowhere.
        DriveListView.AllowDrop  = true;
        DriveListView.DragOver  += OnListViewDragOver;
        DriveListView.DragLeave += OnDragLeave;
        DriveListView.Drop      += OnListViewDrop;

        DirectoryTree.AllowDrop  = true;
        DirectoryTree.DragOver  += OnTreeDragOver;
        DirectoryTree.DragLeave += OnDragLeave;
        DirectoryTree.Drop      += OnTreeDrop;

        // Popup must be anchored to this UserControl so relative offsets work.
        Loaded += (_, _) => DropTooltipPopup.PlacementTarget = this;
    }

    private void WireActionStripDrag()
    {
        ActionStrip.PreviewMouseLeftButtonDown += ActionStrip_PreviewMouseLeftButtonDown;
        ActionStrip.PreviewMouseMove           += ActionStrip_PreviewMouseMove;
        ActionStrip.MouseLeftButtonUp          += ActionStrip_MouseLeftButtonUp;
    }

    private void ActionStrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _actionDragVm = FindActionViewModelAt(e.OriginalSource as DependencyObject);
        if (_actionDragVm is null || _actionDragVm.IsDestructive || !_actionDragVm.IsRibbonPinnable) return;
        _actionDrag.Arm(e.GetPosition(null));
    }

    private void ActionStrip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        _actionDrag.ObserveButton(e.LeftButton);

        if (_actionDragVm is null) return;
        if (!_actionDrag.ShouldStart(e.GetPosition(null))) return;

        // Folder actions need the current directory, not the selected file paths.
        IReadOnlyList<string> paths = _actionDragVm.Action is FileActions.FolderActionAdapter
            ? (string.IsNullOrEmpty(ViewModel.CurrentPath) ? [] : [ViewModel.CurrentPath])
            : ViewModel.CurrentSelection.Select(entry => entry.FullPath).ToList();
        var payload = new FileActionPinPayload(_actionDragVm.Action, paths);
        var data    = new DataObject(FileSystemPageRegistration.FileActionKind, payload);
        DragDrop.DoDragDrop(ActionStrip, data, DragDropEffects.Copy);
    }

    private void ActionStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _actionDrag.Disarm();
        _actionDragVm      = null;
    }

    private static FileActionViewModel? FindActionViewModelAt(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: FileActionViewModel vm }) return vm;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("path", out var path) && !string.IsNullOrEmpty(path))
        {
            if (string.Equals(path, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase))
                ViewModel.Refresh();
            else
                ViewModel.NavigateTo(path);
        }
        else
        {
            ViewModel.GoToThisPc(rebuildTree: true);
        }
    }

    private void OnViewModelNavigationChanged(IReadOnlyList<(string Label, string Path)> segments)
    {
        NavigationChanged?.Invoke(segments);
        UpdateListViewVisibility();
        RefreshViewlets(ViewModel.CurrentPath, ViewModel.IsThisPcMode);
    }

    /// <summary>
    /// Shows the list the current mode calls for — and gives only that one the entries.
    /// <para>
    /// Both lists render the same <see cref="FileSystemViewModel.Entries"/> in different columns, and both
    /// used to bind it in XAML, so every row a folder load produced was taken up twice: once by the list on
    /// screen and once by a list nobody could see. The hidden one went on raising SelectionChanged as the
    /// collection was rebuilt, too — see the guard in <see cref="FileListView_SelectionChanged"/>.
    /// </para>
    /// </summary>
    private void UpdateListViewVisibility()
    {
        var (shown, hidden) = ViewModel.IsThisPcMode
            ? (DriveListView, FileListView)
            : (FileListView, DriveListView);

        hidden.Visibility  = Visibility.Collapsed;
        hidden.ItemsSource = null;

        shown.Visibility  = Visibility.Visible;
        shown.ItemsSource = ViewModel.Entries;   // same reference on a re-show: WPF makes that a no-op
    }

    // ── Property change handler ───────────────────────────────────────────
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSystemViewModel.CreateOverlayVisible)
            && ViewModel.CreateOverlayVisible)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                CreateFileNameBox.Focus();
                SelectBaseName(CreateFileNameBox);
            });
        }
    }

    /// <summary>Selects the name without its extension (Explorer-style), so the user types over it.</summary>
    private static void SelectBaseName(TextBox tb)
    {
        var text = tb.Text ?? string.Empty;
        int dot  = text.LastIndexOf('.');
        if (dot > 0) tb.Select(0, dot);
        else tb.SelectAll();
    }

    // ── Viewlet management ────────────────────────────────────────────────

    private void RefreshViewlets(string folderPath, bool isThisPcMode)
    {
        // Unsubscribe events from old hosts
        foreach (var host in _activeViewletHosts)
        {
            host.DisplayModeChanged       -= OnViewletDisplayModeChanged;
            host.FileViewRequested        -= OnFileViewRequested;
            host.ViewletViewRequested     -= OnViewletViewRequested;
            host.SwitchFullViewletRequested -= OnSwitchFullViewletRequested;
        }
        _activeViewletHosts.Clear();
        _fileViewActive   = false;
        _activeFullIndex  = 0;

        ViewletStackPanel.Children.Clear();
        FullViewletContent.Content = null;

        if (isThisPcMode || string.IsNullOrEmpty(folderPath))
        {
            ViewModel.SetActiveViewletSurfaces([]);
            return;
        }

        var matched = FolderViewletRegistry.GetMatchingViewlets(folderPath, ViewModel.Registry);
        foreach (var viewlet in matched)
        {
            var host = new ViewletHost(viewlet, folderPath);
            host.DisplayModeChanged        += OnViewletDisplayModeChanged;
            host.FileViewRequested         += OnFileViewRequested;
            host.ViewletViewRequested      += OnViewletViewRequested;
            host.SwitchFullViewletRequested += OnSwitchFullViewletRequested;
            host.QuiesceFolderHandler       = QuiesceActiveFolderAsync;
            _activeViewletHosts.Add(host);
        }

        // Expose every active viewlet's AI surface to the page VM so its context line and tools merge
        // into what the file browser offers the agent (see IViewletAiSurface).
        ViewModel.SetActiveViewletSurfaces(
            _activeViewletHosts.Select(h => h.AiSurface).OfType<IViewletAiSurface>().ToList());

        ApplyViewletLayout();
    }

    /// <summary>
    /// Quiesces the current folder before a viewlet mutates it (e.g. the Git viewlet removing a worktree):
    /// stops the browser's own in-flight enumeration, then asks each active viewlet that holds handles /
    /// runs child processes against the folder to release them (see <see cref="IViewletQuiescible"/>).
    /// Best-effort and awaited, so on return nothing here is locking the folder.
    /// </summary>
    private async Task QuiesceActiveFolderAsync(CancellationToken ct)
    {
        ViewModel.CancelEntryLoad();   // drop any directory handle held by a streaming load

        foreach (var quiescible in _activeViewletHosts.Select(h => h.Quiescible).OfType<IViewletQuiescible>())
        {
            try { await quiescible.QuiesceAsync(ct); }
            catch { /* best-effort — a viewlet's own failure must not block the mutation */ }
        }
    }

    /// <summary>
    /// Re-evaluates the current folder after a mutation finished (the shell cleared its busy mark — e.g. a
    /// worktree deletion completed). If the folder is now gone, walk up to the nearest surviving ancestor and
    /// navigate there in this same tab (falling back to This PC) so the user isn't stranded. If it still
    /// exists, refresh its contents and rebuild the viewlets in place — the mutation may have changed which
    /// viewlets apply.
    /// </summary>
    private void ReEvaluateCurrentFolder()
    {
        var path = ViewModel.CurrentPath;

        if (!string.IsNullOrEmpty(path) && !System.IO.Directory.Exists(path))
        {
            if (FileSystemViewModel.NearestExistingAncestor(path) is { } ancestor)
                ViewModel.NavigateTo(ancestor);            // navigation rebuilds contents + viewlets
            else
                ViewModel.GoToThisPc(rebuildTree: true);   // even the drive is gone — fall back to This PC
            return;
        }

        // Still there: refresh contents, then tear down + reinit the viewlets for the (possibly changed) folder.
        ViewModel.Refresh();
        RefreshViewlets(path, ViewModel.IsThisPcMode);
    }

    private void ApplyViewletLayout()
    {
        var fullHosts    = _activeViewletHosts.Where(h => h.CurrentMode == ViewletDisplayMode.Full).ToList();
        var nonFullHosts = _activeViewletHosts.Where(h => h.CurrentMode != ViewletDisplayMode.Full).ToList();

        // Keep active full index in range
        if (_activeFullIndex >= fullHosts.Count) _activeFullIndex = 0;

        ViewletStackPanel.Children.Clear();
        FullViewletContent.Content = null;

        bool showingFullViewlet = fullHosts.Count > 0 && !_fileViewActive;

        if (showingFullViewlet)
        {
            // Full viewlet takes over the main area
            var activeFullHost = fullHosts[_activeFullIndex];

            // Update the cycle-button label (name of the next full viewlet)
            if (fullHosts.Count > 1)
            {
                var nextIndex = (_activeFullIndex + 1) % fullHosts.Count;
                activeFullHost.SetSwitchButtonLabel(fullHosts[nextIndex].CurrentMode.ToString());
            }
            else
            {
                activeFullHost.SetSwitchButtonLabel(null);
            }
            activeFullHost.SetPageToggleState(viewletViewActive: true);

            FullViewletContent.Content  = activeFullHost;
            FullViewletContent.Visibility = Visibility.Visible;

            // Hide file lists and action strip
            FileListView.Visibility  = Visibility.Collapsed;
            DriveListView.Visibility = Visibility.Collapsed;
            ActionStrip.Visibility   = Visibility.Collapsed;
        }
        else
        {
            FullViewletContent.Visibility = Visibility.Collapsed;

            // If we were in full mode and switched to file view, still show any active full-host in banner mode
            // For file view: put non-full viewlets in the stack, and if there's a full host, update its toggle state
            if (fullHosts.Count > 0 && _fileViewActive)
            {
                fullHosts[_activeFullIndex].SetPageToggleState(viewletViewActive: false);
            }

            // Populate the stack with non-full viewlets
            for (int i = 0; i < nonFullHosts.Count; i++)
            {
                nonFullHosts[i].Margin = i == 0
                    ? new Thickness(2)
                    : new Thickness(2, 3, 2, 2);   // halved inter-viewlet gap (was 6)
                ViewletStackPanel.Children.Add(nonFullHosts[i]);
            }

            // Restore file list and action strip
            ActionStrip.Visibility = Visibility.Visible;
            UpdateListViewVisibility();
        }
    }

    private void OnViewletDisplayModeChanged(object? sender, EventArgs e) => ApplyViewletLayout();

    private void OnFileViewRequested(object? sender, EventArgs e)
    {
        _fileViewActive = true;
        ApplyViewletLayout();
    }

    private void OnViewletViewRequested(object? sender, EventArgs e)
    {
        _fileViewActive = false;
        ApplyViewletLayout();
    }

    private void OnSwitchFullViewletRequested(object? sender, EventArgs e)
    {
        var fullHosts = _activeViewletHosts.Where(h => h.CurrentMode == ViewletDisplayMode.Full).ToList();
        if (fullHosts.Count > 1)
            _activeFullIndex = (_activeFullIndex + 1) % fullHosts.Count;
        ApplyViewletLayout();
    }

    // ── Keyboard handling ─────────────────────────────────────────────────
    // Shortcut dispatch is centralised: the shell calls this view's IKeyboardHandler (below) for the active
    // page, so the shortcuts fire whichever shell control has focus. This override only tracks Shift (used
    // by force-delete and the drop-hint visual) so it stays accurate regardless of what's focused.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.LeftShift or Key.RightShift)
            ViewModel.ShiftHeld = true;
    }

    // ── IKeyboardHandler (dispatched by the shell for the active page) ─────
    // Ctrl+A is a view-level selection; the rest delegate to the shared FileSystemKeyboardHandler so they
    // take the same path as clicking the action strip. The shell already skips this while a TextBox is
    // focused, so an inline rename keeps its native editing keys.
    public bool CanProcessKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.A && modifiers == ModifierKeys.Control) return true;
        return _actionKeys.CanProcessKey(key, modifiers);
    }

    public bool ProcessKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.A && modifiers == ModifierKeys.Control) { FileListView.SelectAll(); return true; }
        return _actionKeys.ProcessKey(key, modifiers);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key is Key.LeftShift or Key.RightShift)
            ViewModel.ShiftHeld = false;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ViewModel.ShiftHeld = false;
    }

    // ── Tree selection ────────────────────────────────────────────────────
    private void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileSystemTreeNode node)
            ViewModel.OnTreeNodeSelected(node);

        // Defer scroll so TreeViewItem containers are realized after the layout pass
        if (e.NewValue is not null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                () => ScrollTreeItemIntoView(DirectoryTree, e.NewValue));
    }

    private static void ScrollTreeItemIntoView(ItemsControl container, object item)
    {
        if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
        {
            tvi.BringIntoView();
            return;
        }
        foreach (var child in container.Items)
        {
            if (container.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childTvi)
                ScrollTreeItemIntoView(childTvi, item);
        }
    }

    // Keep a tree item's ICON in view when it scrolls into focus — never scroll right to
    // chase a long file name. Intercepts every bring-into-view request (manual selection
    // scrolls AND framework-initiated ones) and constrains the target rect to the item's
    // left edge: disclosure arrow + icon at the row's own indent. Reaching the end of a
    // long name would otherwise push the icon off the left of the narrow tree pane.
    private bool _suppressTreeBringIntoView;
    private const double TreeIconAnchorWidth = 44;

    private void DirectoryTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_suppressTreeBringIntoView) return;
        if (e.TargetObject is not TreeViewItem tvi) return;

        e.Handled = true;
        _suppressTreeBringIntoView = true;
        try
        {
            // Anchor on the header row (RowBorder) so vertical scroll targets just the row —
            // not the whole expanded subtree — and horizontal scroll stops at the icon.
            if (tvi.Template?.FindName("RowBorder", tvi) is FrameworkElement { ActualHeight: > 0 } row)
                row.BringIntoView(new Rect(0, 0, Math.Min(TreeIconAnchorWidth, row.ActualWidth), row.ActualHeight));
            else
                tvi.BringIntoView(new Rect(0, 0, TreeIconAnchorWidth, Math.Min(tvi.RenderSize.Height, 28)));
        }
        finally { _suppressTreeBringIntoView = false; }
    }

    // ── Column header sorting ─────────────────────────────────────────────
    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column.Header: SortableHeader header }) return;
        if (sender is not ListView lv) return;

        // ResortEntries() rebuilds the Entries collection, which drops the ListView selection.
        // Capture and restore it (same entry instances, reordered) so sorting keeps the selection.
        var selected = lv.SelectedItems.OfType<FileSystemEntry>().ToList();

        ViewModel.SortByCommand.Execute(header.Key);

        if (selected.Count > 0)
        {
            lv.SelectedItems.Clear();
            foreach (var entry in selected) lv.SelectedItems.Add(entry);
        }
    }

    // ── File list selection ───────────────────────────────────────────────
    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only the list on screen speaks for the page. The other one raises this as its items are taken
        // away (see UpdateListViewVisibility), and an empty selection from it would clear the action strip.
        if (sender is not ListView { Visibility: Visibility.Visible } lv) return;
        var selected = lv.SelectedItems.OfType<FileSystemEntry>().ToList();
        ViewModel.OnSelectionChanged(selected);
    }

    // Clicking the sole-selected item deselects it (deferred to mouse-up so it can still be dragged);
    // clicking the empty area below the rows clears the selection.
    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView lv) return;
        _deselectOnMouseUp = null;

        // Walk up from the clicked element, noting whether we hit a row, a column header, or chrome.
        var element = e.OriginalSource as DependencyObject;
        ListViewItem? item = null;
        bool onHeaderOrChrome = false;
        while (element is not null)
        {
            if (element is ListViewItem lvi) { item = lvi; break; }
            if (element is GridViewColumnHeader or ScrollBar or Thumb) { onHeaderOrChrome = true; break; }
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        // Empty area below the rows: clear the selection (mirrors clicking a selected row).
        // Header/scrollbar clicks are left alone so sorting keeps the selection.
        if (item is null)
        {
            _listDrag.Disarm();
            if (!onHeaderOrChrome && Keyboard.Modifiers == ModifierKeys.None && lv.SelectedItems.Count > 0)
                lv.UnselectAll();
            return;
        }

        if (item.DataContext is not FileSystemEntry entry) return;

        // Never intercept double-clicks — let them reach the MouseBinding.
        if (e.ClickCount > 1) { _listDrag.Disarm(); return; }

        // Record position so PreviewMouseMove can detect a drag gesture (file list only).
        if (lv == FileListView) _listDrag.Arm(e.GetPosition(null));

        // Deselect an already-selected sole item — but on mouse-UP, not here. Deselecting on mouse-down
        // clears the selection before PreviewMouseMove can start a drag, making a selected file
        // impossible to drag. Leave it selected so the drag can arm; if the click turns out to be a
        // plain click (no drag), FileListView_PreviewMouseLeftButtonUp clears it.
        bool noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        bool isAlreadySelected = lv.SelectedItems.Count == 1 && lv.SelectedItem == entry;
        if (isAlreadySelected && noModifiers)
            _deselectOnMouseUp = lv;
    }

    // Deferred deselect: a plain click (no drag) on the already-selected sole item clears it here, on
    // mouse-up. A real drag clears _deselectOnMouseUp in PreviewMouseMove, so this no-ops after a drag.
    private void FileListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _listDrag.Disarm();   // the press is over, whatever else it did or did not become

        if (_deselectOnMouseUp is not { } lv) return;
        _deselectOnMouseUp = null;

        // Selection is a mouse-DOWN gesture in WPF, so nothing re-selects on up and clearing here
        // sticks without handling the event — handling PreviewMouseUp would swallow the item's own up
        // and leave the mouse captured. Re-check the guard in case selection shifted between down/up.
        if (Keyboard.Modifiers == ModifierKeys.None && lv.SelectedItems.Count == 1)
            lv.SelectedItem = null;
    }

    // ── Context menus ─────────────────────────────────────────────────────────

    /// <summary>
    /// Notes what a right-press is about and arms a possible right-drag — but opens nothing. The menu
    /// belongs to <see cref="FileListView_PreviewMouseRightButtonUp"/>: opening it here would swallow
    /// the press a drag needs, the same trap the deferred left-button deselect above exists to avoid,
    /// and it is where Explorer opens its own. The event is still handled so WPF's automatic
    /// <c>ContextMenuOpening</c> never fires and a menu built for a previous click cannot reappear.
    /// </summary>
    private void FileListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView lv) return;

        _rightMenuOnMouseUp   = null;
        _listRightDrag.Disarm();

        // Identify which entry was right-clicked
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListViewItem)
            element = VisualTreeHelper.GetParent(element);

        List<FileSystemEntry> targets;
        if (element is ListViewItem { DataContext: FileSystemEntry clicked })
        {
            // If the clicked item is not already in the selection, select it first.
            if (!lv.SelectedItems.Contains(clicked))
                lv.SelectedItem = clicked;

            targets = lv.SelectedItems.OfType<FileSystemEntry>().ToList();

            // Only the file list can be dragged out of: a This PC row is a device, not a path.
            if (lv == FileListView && targets.Count > 0) _listRightDrag.Arm(e.GetPosition(null));
        }
        else
        {
            // Right-clicked on empty space — actions for the current folder
            targets = [];
        }

        _rightMenuOnMouseUp = lv;
        _rightMenuTargets   = targets;
        e.Handled = true;
    }

    /// <summary>Opens the menu the press armed — unless the press turned into a drag, which clears it.</summary>
    private void FileListView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _listRightDrag.Disarm();

        if (_rightMenuOnMouseUp is not { } lv) return;
        _rightMenuOnMouseUp = null;

        var actions = ViewModel.BuildContextActions(_rightMenuTargets);
        if (actions.Count == 0) return;

        OpenMenuOn(lv, BuildContextMenu(actions));
        e.Handled = true;
    }

    private void DirectoryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Find the tree node that was right-clicked
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not TreeViewItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is not TreeViewItem { DataContext: FileSystemTreeNode node }) return;
        if (string.IsNullOrEmpty(node.FullPath)) return; // "This PC" virtual root

        // Select the node under the cursor
        (element as TreeViewItem)!.IsSelected = true;

        // Represent as a directory FileSystemEntry
        var entry = new FileSystemEntry
        {
            Name        = node.Name,
            FullPath    = node.FullPath,
            IsDirectory = true,
            IsThisPcItem     = node.Kind == TreeNodeKind.Drive,
        };

        var actions = ViewModel.BuildContextActions([entry]);

        var menu = BuildContextMenu(actions);

        // Shell-level pane action — not a file action, so it's appended here after a separator.
        if (actions.Count > 0) menu.Items.Add(BuildMenuSeparator());
        var paneItem = BuildMenuItem("◨", "Open in right pane", ViewModel.OpenInRightPaneCommand,
            (Brush)Application.Current.Resources["TextBrush"],
            (Brush)Application.Current.Resources["Surface2Brush"]);
        paneItem.CommandParameter = node.FullPath;
        menu.Items.Add(paneItem);

        OpenMenuOn(DirectoryTree, menu);
        e.Handled = true;
    }

    private void ActionStrip_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border strip) return;

        // Right-clicking an action button keeps that button's own menu (Modify / Add to Ribbon);
        // only the empty strip space offers "Define New…". Walk up to the strip, returning if a
        // Button is in the ancestry (the button template's own Border must not count as "empty").
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element != strip)
        {
            if (element is Button) return;
            element = VisualTreeHelper.GetParent(element);
        }

        var cmd = ViewModel.OpenDefineNewWizardCommand;
        if (!cmd.CanExecute(null)) return;

        // Plain ContextMenu + MenuItem so this matches the action-button menu (and the rest of the
        // app), which use the global MenuItem style rather than the file-list's custom template.
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Define New…", Command = cmd });
        OpenMenuOn(strip, menu);
        e.Handled = true;
    }

    /// <summary>Inserts a template token (e.g. <c>#filepath</c>) into the wizard field the
    /// token menu was opened on, at the caret.</summary>
    private void WzInsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string token } mi) return;
        if (mi.Parent is not ContextMenu { PlacementTarget: TextBox tb }) return;

        int at = tb.SelectionStart;
        tb.SelectedText = token;          // replaces any selection, else inserts at the caret
        tb.CaretIndex   = at + token.Length;
        tb.Focus();
    }

    /// <summary>
    /// Shows <paramref name="menu"/> for <paramref name="owner"/>, and takes it back off the owner once
    /// it closes.
    /// <para>
    /// The taking-back is the point. WPF opens whatever sits in <c>ContextMenu</c> on <em>any</em>
    /// right-button up over the element, so a menu left assigned by an earlier click reappears on the
    /// release that ends a right-drag — which is how dropping onto the folder tree produced the tree's
    /// ordinary menu instead of the drop one.
    /// </para>
    /// </summary>
    private static void OpenMenuOn(FrameworkElement owner, ContextMenu menu)
    {
        owner.ContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(owner.ContextMenu, menu)) owner.ContextMenu = null;
        };
        menu.IsOpen = true;
    }

    /// <summary>Builds a styled <see cref="ContextMenu"/> from a list of action view-models.</summary>
    private static ContextMenu BuildContextMenu(IReadOnlyList<FileActionViewModel> actions)
    {
        var textBrush        = (Brush)Application.Current.Resources["TextBrush"];
        var surface2Brush    = (Brush)Application.Current.Resources["Surface2Brush"];
        var destructiveBrush = (Brush)Application.Current.Resources["DangerBrush"];

        var menu = BuildStyledContextMenu();

        foreach (var action in actions)
        {
            var foreground = action.IsDestructive ? destructiveBrush : textBrush;
            menu.Items.Add(BuildMenuItem(action.Icon, action.DisplayName, action.ExecuteCommand, foreground, surface2Brush));
        }

        return menu;
    }

    /// <summary>The empty, themed menu the file list's menus are all built on.</summary>
    private static ContextMenu BuildStyledContextMenu()
    {
        var surfaceBrush = (Brush)Application.Current.Resources["SurfaceBrush"];
        var borderBrush  = (Brush)Application.Current.Resources["BorderBrush"];

        return new ContextMenu
        {
            Background      = surfaceBrush,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(3),
            Template        = BuildContextMenuTemplate(surfaceBrush, borderBrush),
        };
    }

    /// <summary>
    /// Replaces the default ContextMenu template which draws a white left-gutter
    /// icon strip. This template is just a themed border wrapping an ItemsPresenter.
    /// </summary>
    private static ControlTemplate BuildContextMenuTemplate(Brush background, Brush borderBrush)
    {
        var outerBorder = new FrameworkElementFactory(typeof(Border));
        outerBorder.SetValue(Border.BackgroundProperty,      background);
        outerBorder.SetValue(Border.BorderBrushProperty,     borderBrush);
        outerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        outerBorder.SetValue(Border.CornerRadiusProperty,    new CornerRadius(4));
        outerBorder.SetValue(Border.PaddingProperty,         new Thickness(3));
        outerBorder.SetValue(UIElement.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius   = 8,
            ShadowDepth  = 2,
            Opacity      = 0.4,
            Color        = Colors.Black,
        });

        var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        outerBorder.AppendChild(itemsPresenter);

        var template = new ControlTemplate(typeof(ContextMenu));
        template.VisualTree = outerBorder;
        return template;
    }

    private static MenuItem BuildMenuItem(string icon, string displayName, ICommand command, Brush foreground, Brush hoverBrush)
    {
        var item = new MenuItem
        {
            Command         = command,
            Foreground      = foreground,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        // The label lives in the code-built template below, not in Header, so the item would otherwise
        // reach automation (and a screen reader) unnamed.
        AutomationProperties.SetName(item, displayName);

        // Build the ControlTemplate entirely in code so there is no icon-presenter
        // column at all — just a Border containing a horizontal StackPanel.
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty,   Brushes.Transparent);
        borderFactory.SetValue(Border.PaddingProperty,      new Thickness(6, 4, 16, 4));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
        stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
        iconFactory.SetValue(TextBlock.TextProperty,              icon);
        iconFactory.SetValue(TextBlock.FontSizeProperty,          14d);
        iconFactory.SetValue(TextBlock.WidthProperty,             22d);
        iconFactory.SetValue(TextBlock.TextAlignmentProperty,     TextAlignment.Center);
        iconFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconFactory.SetValue(TextBlock.MarginProperty,            new Thickness(0, 0, 6, 0));
        iconFactory.SetValue(TextBlock.ForegroundProperty,        foreground);

        var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameFactory.SetValue(TextBlock.TextProperty,              displayName);
        nameFactory.SetValue(TextBlock.FontSizeProperty,          13d);
        nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        nameFactory.SetValue(TextBlock.ForegroundProperty,        foreground);

        stackFactory.AppendChild(iconFactory);
        stackFactory.AppendChild(nameFactory);
        borderFactory.AppendChild(stackFactory);

        var template = new ControlTemplate(typeof(MenuItem));
        template.VisualTree = borderFactory;

        // Hover only while the item can actually be chosen: IsMouseOver stays true over a disabled
        // MenuItem, so a plain hover trigger lights up an item that will not respond.
        var hoverTrigger = new MultiTrigger();
        hoverTrigger.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true));
        hoverTrigger.Conditions.Add(new System.Windows.Condition(UIElement.IsEnabledProperty,   true));
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "Bd"));
        template.Triggers.Add(hoverTrigger);

        // …and an item that cannot be chosen has to look it. This template sets its own foregrounds, so
        // nothing else would dim them, and a command whose CanExecute is false would otherwise render as a
        // perfectly ordinary item that swallows the click.
        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Bd"));
        template.Triggers.Add(disabledTrigger);

        item.Template = template;
        return item;
    }

    /// <summary>A themed 1px divider for the code-built context menus (the default Separator draws a
    /// system-coloured line that clashes with the dark menu).</summary>
    private static Separator BuildMenuSeparator()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.HeightProperty,     1d);
        line.SetValue(Border.BackgroundProperty, (Brush)Application.Current.Resources["BorderBrush"]);
        line.SetValue(Border.MarginProperty,     new Thickness(6, 4, 6, 4));

        var template = new ControlTemplate(typeof(Separator)) { VisualTree = line };
        return new Separator { Template = template };
    }

    private void CreateFileNameBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) SelectBaseName(tb);
    }

    private void CreateFileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel.CreateCommand.CanExecute(null))
                ViewModel.CreateCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelCreateCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Drag-from-list: initiate WPF drag when the mouse moves far enough ──
    private void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // A move that reports a button up says what that button's release would have said, had this list
        // been the one to see it. It often is not: a menu holding mouse capture swallows the press and
        // the release both, and arming left standing from some earlier click was then instantly "far
        // enough" from an origin the cursor had long since left — a drag nobody started, landing a copy
        // wherever the button happened to come up. Disarming here is what closes that.
        _listDrag.ObserveButton(e.LeftButton);
        _listRightDrag.ObserveButton(e.RightButton);

        // Right button first: it is the same drag with a different question waiting at the other end.
        if (_listRightDrag.ShouldStart(e.GetPosition(null)))
        {
            _rightMenuOnMouseUp = null;   // this press became a drag, so no menu is owed on the way up
            BeginListDrag(rightButton: true);
            return;
        }

        if (!_listDrag.ShouldStart(e.GetPosition(null))) return;

        _deselectOnMouseUp = null;  // a drag is not a click — keep the dragged item selected
        BeginListDrag(rightButton: false);
    }

    /// <summary>
    /// Starts the OLE drag for the current selection. <paramref name="rightButton"/> changes nothing
    /// about the payload — the destination reads the button state itself and offers a choice — but it
    /// does change when the drag ends, which WPF will not work out on its own.
    /// </summary>
    private void BeginListDrag(bool rightButton)
    {
        // Explorer (or whatever receives the drop) understands only real paths, so resolve a mount and
        // materialise an in-archive entry on the way out.
        var paths = Services.ShellPath.Realize(
            FileListView.SelectedItems
                .OfType<FileSystemEntry>()
                .Where(entry => !entry.IsThisPcItem)
                .Select(entry => entry.FullPath));

        if (paths.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, paths);

        // WPF's built-in continue-drag rule ends the drag when the LEFT button comes up, so a right-drag
        // would follow the cursor forever and never drop. Saying so explicitly also keeps this from
        // depending on a framework internal that is not part of any contract.
        QueryContinueDragEventHandler? untilTheRightButtonComesUp = null;
        if (rightButton)
        {
            untilTheRightButtonComesUp = (_, args) =>
            {
                args.Action = args.EscapePressed                                          ? DragAction.Cancel
                            : args.KeyStates.HasFlag(DragDropKeyStates.RightMouseButton)   ? DragAction.Continue
                            : DragAction.Drop;
                args.Handled = true;
            };
            FileListView.QueryContinueDrag += untilTheRightButtonComesUp;
        }

        _localDragInFlight = true;
        try
        {
            DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            _localDragInFlight = false;
            if (untilTheRightButtonComesUp is not null)
                FileListView.QueryContinueDrag -= untilTheRightButtonComesUp;
        }

        // The loop has unwound, so a question the drop left behind can safely be put to the user now.
        AskPendingChoice();
    }

    // ── Drag-drop: ListView (drops into current directory) ────────────────
    private void OnListViewDragOver(object sender, DragEventArgs e)
    {
        var folder = FolderEntryUnderMouse(e);

        // Refused during the hover, not complained about after the drop: dropping something onto itself
        // means nothing, and four pixels of drift during an ordinary click is enough to produce one.
        if (!_dropTarget.CanAcceptDrop(e.Data) ||
            _dropTarget.IsSelfDrop(e.Data, folder?.FullPath ?? ViewModel.CurrentPath))
        {
            e.Effects = DragDropEffects.None;
            EndDropFeedback();
            e.Handled = true;
            return;
        }

        string? folderName = folder?.Name;
        HighlightDropRow(e, fromTree: false);

        // A right-drag has not decided anything yet, so the tooltip cannot promise a direction — but the
        // effect still has to be something, or the drop is never delivered and there is nothing to ask about.
        _dropChoice.Observe(e.KeyStates);
        if (OfferedChoice() is { } choices)
        {
            e.Effects = DragDropEffects.Copy;
            ShowDropTooltip(choices.GetChoicePrompt(folderName), e);
            e.Handled = true;
            return;
        }

        bool isMove  = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Effects    = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowDropTooltip(_dropTarget.GetDropDescription(e.Data, folderName, isMove), e);
        e.Handled = true;
    }

    private void OnListViewDrop(object sender, DragEventArgs e)
    {
        // Only the tooltip goes here — the destination wash is CompleteDrop's to keep or clear, since a
        // right-drag is about to ask a question about the very row it is on.
        HideDropTooltip();
        if (!_dropTarget.CanAcceptDrop(e.Data)) { _dropHighlight.Clear(); return; }

        // Resolve destination: a directory entry under the cursor, or current path.
        string destination = FolderEntryUnderMouse(e)?.FullPath ?? ViewModel.CurrentPath;

        // Belt and braces: nothing may escape an IDropTarget::Drop callback. A fault here is an unhandled
        // UI-thread exception that abandons the rest of the drop with only a generic toast to show for it,
        // which is one of the ways a folder drop used to disappear without explanation.
        try
        {
            if (string.IsNullOrEmpty(destination)) _dropHighlight.Clear();
            else                                   CompleteDrop(e, destination);
        }
        catch (Exception ex)
        {
            _dropHighlight.Clear();
            ViewModel.ReportError(ex.Message);
        }

        e.Handled = true;
    }

    // ── Drag-drop: TreeView (drops into hovered folder node) ─────────────
    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        var node = ResolveTreeNodeUnderMouse(e);

        if (!_dropTarget.CanAcceptDrop(e.Data) ||
            _dropTarget.IsSelfDrop(e.Data, node?.FullPath ?? ViewModel.CurrentPath))
        {
            e.Effects = DragDropEffects.None;
            EndDropFeedback();
            e.Handled = true;
            return;
        }

        string? folderName = node?.Name;
        HighlightDropRow(e, fromTree: true);
        ScheduleTreeReveal(e);

        _dropChoice.Observe(e.KeyStates);
        if (OfferedChoice() is { } choices)
        {
            e.Effects = DragDropEffects.Copy;
            ShowDropTooltip(choices.GetChoicePrompt(folderName), e);
            e.Handled = true;
            return;
        }

        bool isMove  = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Effects    = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowDropTooltip(_dropTarget.GetDropDescription(e.Data, folderName, isMove), e);
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        HideDropTooltip();
        StopTreeRevealTimer();
        if (!_dropTarget.CanAcceptDrop(e.Data)) { _dropHighlight.Clear(); return; }

        var node = ResolveTreeNodeUnderMouse(e);
        var destination = node?.FullPath ?? ViewModel.CurrentPath;

        // Belt and braces: nothing may escape an IDropTarget::Drop callback. A fault here is an unhandled
        // UI-thread exception that abandons the rest of the drop with only a generic toast to show for it,
        // which is one of the ways a folder drop used to disappear without explanation.
        try
        {
            if (string.IsNullOrEmpty(destination)) _dropHighlight.Clear();
            else                                   CompleteDrop(e, destination);
        }
        catch (Exception ex)
        {
            _dropHighlight.Clear();
            ViewModel.ReportError(ex.Message);
        }

        e.Handled = true;
    }

    private FileSystemTreeNode? ResolveTreeNodeUnderMouse(DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return null;
        var element = src;
        while (element is not null && element is not TreeViewItem)
            element = VisualTreeHelper.GetParent(element);
        return (element as TreeViewItem)?.DataContext as FileSystemTreeNode;
    }

    // ── Tree: reveal a node whose left edge is scrolled out of sight ──────────

    /// <summary>Which node the reveal timer is waiting on, so a cursor that moves on cancels it.</summary>
    private TreeViewItem? _treeRevealCandidate;
    private System.Windows.Threading.DispatcherTimer? _treeRevealTimer;

    /// <summary>
    /// A folder deep in the tree can sit with its name scrolled off to the left, so the row a drag is
    /// hovering says nothing about which folder it is. After a pause — long enough that crossing rows on
    /// the way somewhere else does not move the view — scroll it back into sight.
    /// <para>
    /// Horizontally only, and only when the left edge is actually off: a drag is a poor moment to move
    /// the ground under the pointer, so it does the least that makes the row readable.
    /// </para>
    /// </summary>
    private void ScheduleTreeReveal(DragEventArgs e)
    {
        var node = ContainerUnderMouse<TreeViewItem>(e);
        if (ReferenceEquals(node, _treeRevealCandidate)) return;   // still the same row — let it run on

        _treeRevealCandidate = node;
        _treeRevealTimer?.Stop();
        if (node is null) return;

        _treeRevealTimer ??= new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };

        _treeRevealTimer.Tick -= OnTreeRevealTick;
        _treeRevealTimer.Tick += OnTreeRevealTick;
        _treeRevealTimer.Start();
    }

    private void OnTreeRevealTick(object? sender, EventArgs e)
    {
        _treeRevealTimer?.Stop();
        if (_treeRevealCandidate is not { } node) return;

        var scroll = FindDescendant<ScrollViewer>(DirectoryTree);
        if (scroll is null || scroll.HorizontalOffset <= 0) return;

        // The header row, not the item: the item spans its whole expanded subtree.
        if (node.Template?.FindName("RowBorder", node) is not FrameworkElement { ActualHeight: > 0 } row) return;

        double left = row.TransformToAncestor(scroll).Transform(new Point(0, 0)).X;
        if (left >= 0) return;   // already readable

        scroll.ScrollToHorizontalOffset(Math.Max(0, scroll.HorizontalOffset + left - TreeRevealMargin));
    }

    private void StopTreeRevealTimer()
    {
        _treeRevealTimer?.Stop();
        _treeRevealCandidate = null;
    }

    private const double TreeRevealMargin = 8;

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => EndDropFeedback();

    // ── Drop tooltip Popup ────────────────────────────────────────────────
    private void ShowDropTooltip(string text, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        DropTooltipPopup.HorizontalOffset = pos.X + 14;
        DropTooltipPopup.VerticalOffset   = pos.Y + 18;
        DropTooltipText.Text              = text;
        DropTooltipPopup.IsOpen           = true;
    }

    private void HideDropTooltip() => DropTooltipPopup.IsOpen = false;

    /// <summary>
    /// Stops saying what a drop would do. The tooltip always goes; the destination wash goes with it
    /// unless a choice menu is about to be shown on that very destination, which is why the drop path
    /// clears the two separately (see <see cref="CompleteDrop"/>).
    /// </summary>
    private void EndDropFeedback()
    {
        HideDropTooltip();
        _dropHighlight.Clear();
        StopTreeRevealTimer();
    }

    // ── Drop: right-drag asks, left-drag is told ──────────────────────────────

    /// <summary>
    /// Whether the drag in flight wants to be asked, and the target that can answer.
    /// <para>
    /// The answer comes from <see cref="_dropChoice"/> rather than from the drop's own key state,
    /// because the drop is the moment the button was released and so reports no button at all.
    /// </para>
    /// </summary>
    private IDropChoiceTarget? OfferedChoice()
        => _dropChoice.OffersChoice ? _dropTarget as IDropChoiceTarget : null;

    /// <summary>The row container under the cursor, whatever kind of list it belongs to.</summary>
    private static T? ContainerUnderMouse<T>(DragEventArgs e) where T : DependencyObject
    {
        if (e.OriginalSource is not DependencyObject src) return null;

        var element = src;
        while (element is not null and not T)
            element = VisualTreeHelper.GetParent(element);

        return element as T;
    }

    /// <summary>The directory row under the cursor, or null over a file row or the list background.</summary>
    private static FileSystemEntry? FolderEntryUnderMouse(DragEventArgs e)
        => ContainerUnderMouse<ListViewItem>(e) is { DataContext: FileSystemEntry { IsDirectory: true } entry }
            ? entry
            : null;

    /// <summary>
    /// Washes whichever row a drop would land in, so the destination is shown rather than spelled out.
    /// A tree node is adorned on its header alone — adorning the item would cover its whole subtree.
    /// </summary>
    private void HighlightDropRow(DragEventArgs e, bool fromTree)
    {
        if (!fromTree)
        {
            var row = ContainerUnderMouse<ListViewItem>(e);
            _dropHighlight.Show(row?.DataContext is FileSystemEntry { IsDirectory: true } ? row : null);
            return;
        }

        var node = ContainerUnderMouse<TreeViewItem>(e);
        _dropHighlight.Show(node is null ? null : node.Template?.FindName("RowBorder", node) as UIElement ?? node);
    }

    private void CompleteDrop(DragEventArgs e, string destination)
    {
        if (OfferedChoice() is { } choices)
        {
            // Captured now, asked later. The data object dies with this callback, and a menu opened
            // inside it would block the OLE drop on a thread with no message pump — the very failure the
            // drop path was restructured to avoid. The destination wash is deliberately left up: with the
            // folder name out of the menu's labels it is the only thing saying where "here" is, so it
            // comes down when the menu is answered, not when the menu appears.
            if (choices.Capture(e.Data, destination) is { } plan)
            {
                _pendingChoice = (choices, plan);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, AskPendingChoice);
            }
            else
            {
                _dropHighlight.Clear();
            }

            return;
        }

        _dropHighlight.Clear();
        _dropTarget.Drop(e.Data, destination, move: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>
    /// Puts the captured question to the user — but never while our own drag is still on the stack.
    /// <para>
    /// <see cref="DragDrop.DoDragDrop"/> runs a modal loop that pumps this dispatcher, so a menu posted
    /// from inside the drop callback can open <em>within</em> that loop, with the drag still live. Every
    /// click the menu appears to receive is then still the drag's, and the next one lands as another
    /// drop wherever the cursor happens to be — which is how ignoring the menu and clicking a folder
    /// started a copy onto that folder. <see cref="BeginListDrag"/> calls back once the loop has
    /// returned; a drag from outside the app has no loop of ours and is asked by the post above.
    /// </para>
    /// </summary>
    private void AskPendingChoice()
    {
        if (_localDragInFlight) return;
        if (_pendingChoice is not { } pending) return;

        _pendingChoice = null;
        ShowDropChoiceMenu(pending.Target, pending.Plan);
    }

    /// <summary>
    /// Explorer's right-drag menu, in this app's clothes. Flat by design: the global MenuItem style
    /// replaces the default template, so a submenu would need template work, and there is nothing here
    /// to nest. Dismissing it — Escape, or a click elsewhere — is the cancel.
    /// <para>
    /// The destination is not in the labels; it is the row washed accent underneath, which stays washed
    /// until this menu is answered. A folder name in a menu item stretches the menu across the window to
    /// say what the highlight already says, and says it worse.
    /// </para>
    /// </summary>
    private void ShowDropChoiceMenu(IDropChoiceTarget choices, DropPlan plan)
    {
        var textBrush     = (Brush)Application.Current.Resources["TextBrush"];
        var surface2Brush = (Brush)Application.Current.Resources["Surface2Brush"];

        var menu = BuildStyledContextMenu();
        menu.PlacementTarget = this;

        // A drop is one event, so it can be answered once and only once. Choosing Copy here used to leave
        // a live command holding this plan, and something later — the next context menu opened over the
        // list, reliably — reached it and ran the whole copy again. Whatever does the reaching, a second
        // call now finds the plan spent and does nothing.
        bool answered = false;

        void Answer(DropChoice? choice)
        {
            if (answered) return;
            answered = true;
            _dropHighlight.Clear();
            if (choice is { } made) choices.Execute(plan, made);
        }

        menu.Items.Add(BuildMenuItem("📋", "Copy here",
            new RelayCommand(() => Answer(DropChoice.Copy), () => choices.CanExecute(plan, DropChoice.Copy)),
            textBrush, surface2Brush));
        menu.Items.Add(BuildMenuItem("➡", "Move here",
            new RelayCommand(() => Answer(DropChoice.Move), () => choices.CanExecute(plan, DropChoice.Move)),
            textBrush, surface2Brush));
        menu.Items.Add(BuildMenuSeparator());
        menu.Items.Add(BuildMenuItem("✕", "Cancel",
            new RelayCommand(() => Answer(null)), textBrush, surface2Brush));

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(DropChoiceMenu, menu)) DropChoiceMenu = null;

            // Taken apart on the way out — at Background, which is below the Input priority a clicked
            // MenuItem posts its own command at, so a choice already on its way still lands. After this
            // there is no item left for anything to invoke.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                Answer(null);          // dismissed without choosing is a cancel
                menu.Items.Clear();
            });
        };

        // Opened through the same owner-managed path as every other menu here, rather than by setting
        // IsOpen on a menu nothing owns. An unowned ContextMenu is one WPF never fully takes down, and a
        // menu still holding mouse capture is a menu a later click can still be delivered to.
        DropChoiceMenu = menu;   // held so the menu is not collected while it is open
        OpenMenuOn(this, menu);
    }

    /// <summary>Keeps the live drop-choice menu rooted; a ContextMenu with no owner can be collected mid-show.</summary>
    private ContextMenu? DropChoiceMenu { get; set; }
}
