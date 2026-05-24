using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.RibbonHandlers;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.FileSystem;

namespace Nexaflow.Core.Views;

public partial class FileSystemView : UserControl, IPageView, ISelectionProvider
{
    public FileSystemViewModel ViewModel { get; }

    // ── IPageView ─────────────────────────────────────────────────────────────
    IPageViewModel? IPageView.ViewModel => ViewModel;

    // ── ISelectionProvider ────────────────────────────────────────────────────
    IReadOnlyList<string> ISelectionProvider.SelectedFilePaths
        => ViewModel.CurrentSelection.Select(e => e.FullPath).ToList();

    private readonly IKeyboardHandler _keyboardHandler;
    private readonly IDropTarget      _dropTarget;

    // Drag-from-list tracking
    private Point _listDragStartPoint;
    private bool  _listDragPending;

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

    public FileSystemView(string targetDirectory, IKeyboardHandler keyboardHandler, IDropTarget dropTarget)
    {
        InitializeComponent();
        ViewModel        = new FileSystemViewModel(targetDirectory);
        _keyboardHandler = keyboardHandler;
        _dropTarget      = dropTarget;
        DataContext = ViewModel;
        ViewModel.NavigationChanged += OnViewModelNavigationChanged;
        ViewModel.PropertyChanged   += OnViewModelPropertyChanged;
        WireDragDrop();
        WireActionStripDrag();
        Loaded += (_, _) => UpdateListViewVisibility();
    }

    public FileSystemView(FileSystemViewModel viewModel, IKeyboardHandler keyboardHandler, IDropTarget dropTarget)
    {
        InitializeComponent();
        ViewModel        = viewModel;
        _keyboardHandler = keyboardHandler;
        _dropTarget      = dropTarget;
        DataContext = viewModel;
        ViewModel.NavigationChanged += OnViewModelNavigationChanged;
        ViewModel.PropertyChanged   += OnViewModelPropertyChanged;
        WireDragDrop();
        WireActionStripDrag();
        Loaded += (_, _) => UpdateListViewVisibility();
    }

    private void WireDragDrop()
    {
        FileListView.AllowDrop     = true;
        FileListView.PreviewMouseMove += FileListView_PreviewMouseMove;
        FileListView.DragOver  += OnListViewDragOver;
        FileListView.DragLeave += OnDragLeave;
        FileListView.Drop      += OnListViewDrop;

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
        if (_actionDragVm is null || _actionDragVm.IsDestructive) return;
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
        var paths   = ViewModel.CurrentSelection.Select(entry => entry.FullPath).ToList();
        var payload = new FileActionPinPayload(_actionDragVm.Action, paths);
        var data    = new DataObject(PageKinds.FileAction, payload);
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

    private void UpdateListViewVisibility()
    {
        if (ViewModel.IsThisPcMode)
        {
            DriveListView.Visibility = Visibility.Visible;
            FileListView.Visibility  = Visibility.Collapsed;
        }
        else
        {
            DriveListView.Visibility = Visibility.Collapsed;
            FileListView.Visibility  = Visibility.Visible;
        }
    }

    // ── Property change handler ───────────────────────────────────────────
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSystemViewModel.InputPromptVisible)
            && ViewModel.InputPromptVisible)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                InputPromptTextBox.Focus();
                InputPromptTextBox.SelectAll();
            });
        }
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

        if (isThisPcMode || string.IsNullOrEmpty(folderPath)) return;

        var matched = FolderViewletRegistry.GetMatchingViewlets(folderPath);
        foreach (var viewlet in matched)
        {
            var host = new ViewletHost(viewlet, folderPath);
            host.DisplayModeChanged        += OnViewletDisplayModeChanged;
            host.FileViewRequested         += OnFileViewRequested;
            host.ViewletViewRequested      += OnViewletViewRequested;
            host.SwitchFullViewletRequested += OnSwitchFullViewletRequested;
            _activeViewletHosts.Add(host);
        }

        ApplyViewletLayout();
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
                    : new Thickness(2, 6, 2, 2);
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
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Shift tracking (run unconditionally so ShiftHeld stays accurate).
        if (e.Key is Key.LeftShift or Key.RightShift)
            ViewModel.ShiftHeld = true;

        // Don't intercept shortcuts when an overlay or TextBox has focus.
        if (ViewModel.InputPromptVisible || ViewModel.ConfirmationVisible) return;
        if (Keyboard.FocusedElement is TextBox) return;

        // Ctrl+A: pure UI selection — select all list-view entries.
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FileListView.SelectAll();
            e.Handled = true;
            return;
        }

        if (_keyboardHandler.CanProcessKey(e.Key, Keyboard.Modifiers))
            e.Handled = _keyboardHandler.ProcessKey(e.Key, Keyboard.Modifiers);
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

    // ── File list selection ───────────────────────────────────────────────
    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView lv) return;
        var selected = lv.SelectedItems.OfType<FileSystemEntry>().ToList();
        ViewModel.OnSelectionChanged(selected);
    }

    // Clicking an already-selected item deselects it (works for single and multi-selection)
    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView lv) return;

        // Walk up from the clicked element to find the ListViewItem
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListViewItem)
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);

        if (element is not ListViewItem item) return;
        if (item.DataContext is not FileSystemEntry entry) return;

        // Never intercept double-clicks — let them reach the MouseBinding.
        if (e.ClickCount > 1) { _listDragPending = false; return; }

        // Record position so PreviewMouseMove can detect a drag gesture (file list only).
        _listDragStartPoint = e.GetPosition(null);
        _listDragPending    = lv == FileListView;

        // Only deselect when it's the sole selected item and no modifier is held
        bool noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        bool isAlreadySelected = lv.SelectedItems.Count == 1 && lv.SelectedItem == entry;
        if (isAlreadySelected && noModifiers)
        {
            lv.SelectedItem = null;
            e.Handled = true;
        }
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
            IsDrive     = node.Kind == TreeNodeKind.Drive,
        };

        var actions = ViewModel.BuildContextActions([entry]);
        if (actions.Count == 0) return;

        DirectoryTree.ContextMenu = BuildContextMenu(actions);
        DirectoryTree.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Builds a styled <see cref="ContextMenu"/> from a list of action view-models.</summary>
    private static ContextMenu BuildContextMenu(IReadOnlyList<FileActionViewModel> actions)
    {
        var textBrush        = (Brush)Application.Current.Resources["TextBrush"];
        var surfaceBrush     = (Brush)Application.Current.Resources["SurfaceBrush"];
        var surface2Brush    = (Brush)Application.Current.Resources["Surface2Brush"];
        var borderBrush      = (Brush)Application.Current.Resources["BorderBrush"];
        var destructiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));

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

    private void InputPromptTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private void InputPromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.ConfirmInputPromptCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelInputPromptCommand.Execute(null);
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

        _listDragPending = false;

        var paths = FileListView.SelectedItems
            .OfType<FileSystemEntry>()
            .Where(entry => !entry.IsDrive)
            .Select(entry => entry.FullPath)
            .ToArray();

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

        if (!string.IsNullOrEmpty(destination))
            _dropTarget.Drop(e.Data, destination, move: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
        if (!string.IsNullOrEmpty(destination))
            _dropTarget.Drop(e.Data, destination, move: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
