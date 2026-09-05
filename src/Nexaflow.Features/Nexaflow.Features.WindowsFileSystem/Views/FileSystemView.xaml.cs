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

    // Drag-from-list tracking
    private Point _listDragStartPoint;
    private bool  _listDragPending;

    // Click-to-deselect is deferred to mouse-up so a mouse-down on the selected item can still begin a
    // drag; holds the list awaiting that deselect, cleared once a drag fires (see the mouse handlers).
    private ListView? _deselectOnMouseUp;

    // Drag-from-ActionStrip tracking
    private Point _actionDragStartPoint;
    private bool  _actionDragPending;
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
        _actionDragStartPoint = e.GetPosition(null);
        _actionDragPending    = true;
    }

    private void ActionStrip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_actionDragPending || e.LeftButton != MouseButtonState.Pressed || _actionDragVm is null) return;

        var delta = e.GetPosition(null) - _actionDragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _actionDragPending = false;
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
        _actionDragPending = false;
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
            _listDragPending = false;
            if (!onHeaderOrChrome && Keyboard.Modifiers == ModifierKeys.None && lv.SelectedItems.Count > 0)
                lv.UnselectAll();
            return;
        }

        if (item.DataContext is not FileSystemEntry entry) return;

        // Never intercept double-clicks — let them reach the MouseBinding.
        if (e.ClickCount > 1) { _listDragPending = false; return; }

        // Record position so PreviewMouseMove can detect a drag gesture (file list only).
        _listDragStartPoint = e.GetPosition(null);
        _listDragPending    = lv == FileListView;

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
        if (_deselectOnMouseUp is not { } lv) return;
        _deselectOnMouseUp = null;

        // Selection is a mouse-DOWN gesture in WPF, so nothing re-selects on up and clearing here
        // sticks without handling the event — handling PreviewMouseUp would swallow the item's own up
        // and leave the mouse captured. Re-check the guard in case selection shifted between down/up.
        if (Keyboard.Modifiers == ModifierKeys.None && lv.SelectedItems.Count == 1)
            lv.SelectedItem = null;
    }

    // ── Context menus ─────────────────────────────────────────────────────────

    private void FileListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView lv) return;

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
        }
        else
        {
            // Right-clicked on empty space — actions for the current folder
            targets = [];
        }

        var actions = ViewModel.BuildContextActions(targets);
        if (actions.Count == 0) return;

        lv.ContextMenu = BuildContextMenu(actions);
        lv.ContextMenu.IsOpen = true;
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

        DirectoryTree.ContextMenu = menu;
        DirectoryTree.ContextMenu.IsOpen = true;
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
        strip.ContextMenu = menu;
        menu.IsOpen = true;
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

    /// <summary>Builds a styled <see cref="ContextMenu"/> from a list of action view-models.</summary>
    private static ContextMenu BuildContextMenu(IReadOnlyList<FileActionViewModel> actions)
    {
        var textBrush        = (Brush)Application.Current.Resources["TextBrush"];
        var surfaceBrush     = (Brush)Application.Current.Resources["SurfaceBrush"];
        var surface2Brush    = (Brush)Application.Current.Resources["Surface2Brush"];
        var borderBrush      = (Brush)Application.Current.Resources["BorderBrush"];
        var destructiveBrush = (Brush)Application.Current.Resources["DangerBrush"];

        var menu = new ContextMenu
        {
            Background      = surfaceBrush,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(3),
            Template        = BuildContextMenuTemplate(surfaceBrush, borderBrush),
        };

        foreach (var action in actions)
        {
            var foreground = action.IsDestructive ? destructiveBrush : textBrush;
            menu.Items.Add(BuildMenuItem(action.Icon, action.DisplayName, action.ExecuteCommand, foreground, surface2Brush));
        }

        return menu;
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

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "Bd"));
        template.Triggers.Add(hoverTrigger);

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
        if (!_listDragPending || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _listDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _listDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _listDragPending   = false;
        _deselectOnMouseUp = null;  // a drag is not a click — keep the dragged item selected

        // Explorer (or whatever receives the drop) understands only real paths, so resolve a mount and
        // materialise an in-archive entry on the way out.
        var paths = Services.ShellPath.Realize(
            FileListView.SelectedItems
                .OfType<FileSystemEntry>()
                .Where(entry => !entry.IsThisPcItem)
                .Select(entry => entry.FullPath));

        if (paths.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    // ── Drag-drop: ListView (drops into current directory) ────────────────
    private void OnListViewDragOver(object sender, DragEventArgs e)
    {
        if (!_dropTarget.CanAcceptDrop(e.Data))
        {
            e.Effects = DragDropEffects.None;
            HideDropTooltip();
            e.Handled = true;
            return;
        }

        // Check if hovering over a directory entry in the list.
        string? folderName = null;
        if (e.OriginalSource is DependencyObject src)
        {
            var element = src;
            while (element is not null && element is not ListViewItem)
                element = VisualTreeHelper.GetParent(element);
            if (element is ListViewItem { DataContext: FileSystemEntry { IsDirectory: true } entry })
                folderName = entry.Name;
        }

        bool isMove  = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Effects    = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowDropTooltip(_dropTarget.GetDropDescription(e.Data, folderName, isMove), e);
        e.Handled = true;
    }

    private void OnListViewDrop(object sender, DragEventArgs e)
    {
        HideDropTooltip();
        if (!_dropTarget.CanAcceptDrop(e.Data)) return;

        // Resolve destination: a directory entry under the cursor, or current path.
        string destination = ViewModel.CurrentPath;
        if (e.OriginalSource is DependencyObject src)
        {
            var element = src;
            while (element is not null && element is not ListViewItem)
                element = VisualTreeHelper.GetParent(element);
            if (element is ListViewItem { DataContext: FileSystemEntry { IsDirectory: true } entry })
                destination = entry.FullPath;
        }

        // Belt and braces: nothing may escape an IDropTarget::Drop callback. A fault here is an unhandled
        // UI-thread exception that abandons the rest of the drop with only a generic toast to show for it,
        // which is one of the ways a folder drop used to disappear without explanation.
        try
        {
            if (!string.IsNullOrEmpty(destination))
                _dropTarget.Drop(e.Data, destination, move: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        catch (Exception ex)
        {
            ViewModel.ReportError(ex.Message);
        }

        e.Handled = true;
    }

    // ── Drag-drop: TreeView (drops into hovered folder node) ─────────────
    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (!_dropTarget.CanAcceptDrop(e.Data))
        {
            e.Effects = DragDropEffects.None;
            HideDropTooltip();
            e.Handled = true;
            return;
        }

        bool isMove  = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Effects    = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        string? folderName = ResolveTreeNodeUnderMouse(e)?.Name;
        ShowDropTooltip(_dropTarget.GetDropDescription(e.Data, folderName, isMove), e);
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        HideDropTooltip();
        if (!_dropTarget.CanAcceptDrop(e.Data)) return;

        var node = ResolveTreeNodeUnderMouse(e);
        var destination = node?.FullPath ?? ViewModel.CurrentPath;

        // Belt and braces: nothing may escape an IDropTarget::Drop callback. A fault here is an unhandled
        // UI-thread exception that abandons the rest of the drop with only a generic toast to show for it,
        // which is one of the ways a folder drop used to disappear without explanation.
        try
        {
            if (!string.IsNullOrEmpty(destination))
                _dropTarget.Drop(e.Data, destination, move: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        catch (Exception ex)
        {
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

    private void OnDragLeave(object sender, DragEventArgs e) => HideDropTooltip();

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
}
