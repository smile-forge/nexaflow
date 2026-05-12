using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.Common;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Views;

public partial class FileSystemView : UserControl, IRefreshable
{
    public FileSystemViewModel ViewModel { get; }

    /// <summary>
    /// Raised whenever the current directory changes.
    /// Subscribers (e.g. ShellViewModel) should update the tab's BreadcrumbSegments.
    /// Each tuple contains (DisplayLabel, FullPath) — FullPath is empty for "This PC".
    /// </summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    public FileSystemView(string targetDirectory)
    {
        InitializeComponent();
        ViewModel   = new FileSystemViewModel(targetDirectory);
        DataContext = ViewModel;
        ViewModel.NavigationChanged += OnViewModelNavigationChanged;
        ViewModel.PropertyChanged   += OnViewModelPropertyChanged;
    }

    public FileSystemView(FileSystemViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
        ViewModel.NavigationChanged += OnViewModelNavigationChanged;
        ViewModel.PropertyChanged   += OnViewModelPropertyChanged;
    }

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ViewModel.Refresh();

    private void OnViewModelNavigationChanged(IReadOnlyList<(string Label, string Path)> segments)
        => NavigationChanged?.Invoke(segments);

    // ── AI summary row height ─────────────────────────────────────────────
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSystemViewModel.AiSummaryVisible))
            UpdateSummaryRowHeight();

        if (e.PropertyName == nameof(FileSystemViewModel.InputPromptVisible)
            && ViewModel.InputPromptVisible)
        {
            // Give WPF a layout pass so the TextBox is visible before focusing it
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                InputPromptTextBox.Focus();
                InputPromptTextBox.SelectAll();
            });
        }
    }

    private void UpdateSummaryRowHeight()
    {
        if (ViewModel.AiSummaryVisible)
        {
            var target = Math.Max(80, RightGrid.ActualHeight * 0.25);
            AiSummaryRow.Height = new GridLength(target, GridUnitType.Pixel);
        }
        else
        {
            AiSummaryRow.Height = new GridLength(0, GridUnitType.Pixel);
        }
    }

    // ── Shift key tracking ────────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.LeftShift or Key.RightShift)
            ViewModel.ShiftHeld = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
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
    }

    // ── File list selection ───────────────────────────────────────────────
    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = FileListView.SelectedItems
            .OfType<FileSystemEntry>()
            .ToList();
        ViewModel.OnSelectionChanged(selected);
    }

    // Clicking an already-selected item deselects it (works for single and multi-selection)
    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Walk up from the clicked element to find the ListViewItem
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListViewItem)
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);

        if (element is not ListViewItem item) return;
        if (item.DataContext is not FileSystemEntry entry) return;

        // Never intercept double-clicks — let them reach the MouseBinding.
        if (e.ClickCount > 1) return;

        // Only deselect when it's the sole selected item and no modifier is held
        bool noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        bool isAlreadySelected = FileListView.SelectedItems.Count == 1
                                 && FileListView.SelectedItem == entry;
        if (isAlreadySelected && noModifiers)
        {
            FileListView.SelectedItem = null;
            e.Handled = true;
        }
    }

    // ── Context menus ─────────────────────────────────────────────────────────

    private void FileListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Identify which entry was right-clicked
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListViewItem)
            element = VisualTreeHelper.GetParent(element);

        List<FileSystemEntry> targets;
        if (element is ListViewItem { DataContext: FileSystemEntry clicked })
        {
            // If the clicked item is not already in the selection, select it first.
            if (!FileListView.SelectedItems.Contains(clicked))
            {
                FileListView.SelectedItem = clicked;
            }

            targets = FileListView.SelectedItems.OfType<FileSystemEntry>().ToList();
        }
        else
        {
            // Right-clicked on empty space — actions for the current folder
            targets = [];
        }

        var actions = ViewModel.BuildContextActions(targets);
        if (actions.Count == 0) return;

        FileListView.ContextMenu = BuildContextMenu(actions);
        FileListView.ContextMenu.IsOpen = true;
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
}
