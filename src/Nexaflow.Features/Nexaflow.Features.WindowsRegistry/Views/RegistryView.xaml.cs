using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsRegistry.ViewModels;

namespace Nexaflow.Features.WindowsRegistry.Views;

public partial class RegistryView : UserControl, IPageView
{
    public RegistryViewModel ViewModel { get; }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    /// <summary>Re-raised from the VM so the page can refresh its breadcrumb/title.</summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    public RegistryView(RegistryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
        ViewModel.NavigationChanged += segs => NavigationChanged?.Invoke(segs);
    }

    // Re-activating an existing tab (re-click / routing) must NOT re-root — rooting happens once, when
    // the page is created (see RegistryPageRegistration.ContentFactory). Preserve the user's position.
    void IPageView.Reinitialize(Dictionary<string, string> pageParams) { }

    private void KeyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is RegistryTreeNode node)
            ViewModel.OnTreeNodeSelected(node);
    }

    // Right-clicking a tree item selects it first so the context menu acts on that key.
    private void KeyTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
            item.IsSelected = true;
    }

    private void ValueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView lv)
            ViewModel.SelectedValue = lv.SelectedItem as RegistryValue;
    }

    private void ValueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedValue is { } v && ViewModel.EditValueCommand.CanExecute(v))
            ViewModel.EditValueCommand.Execute(v);
    }

    private void InputPrompt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.ConfirmInputPromptCommand.CanExecute(null))
            ViewModel.ConfirmInputPromptCommand.Execute(null);
        else if (e.Key == Key.Escape && ViewModel.CancelInputPromptCommand.CanExecute(null))
            ViewModel.CancelInputPromptCommand.Execute(null);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }
}
