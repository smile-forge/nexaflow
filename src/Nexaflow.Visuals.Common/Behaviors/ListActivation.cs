using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Opens a row the way a file list does: double-clicking it, or pressing Enter on the selected one, runs
/// <c>ListActivation.Command</c> with that row as the parameter.
/// </summary>
/// <remarks>
/// For lists drawn from a template with no code-behind of their own — a tool's tile, a resource dictionary. A
/// <c>MouseBinding</c> on the list cannot do it: the row handles the mouse-down first, so the gesture never reaches the
/// list, whereas the list's own <c>MouseDoubleClick</c> is raised regardless. Only a double-click on a row counts, not on
/// a header or the empty space beneath the rows.
/// </remarks>
public static class ListActivation
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(ListActivation), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;
        list.MouseDoubleClick -= OnDoubleClick;
        list.KeyDown -= OnKeyDown;
        if (e.NewValue is null) return;
        list.MouseDoubleClick += OnDoubleClick;
        list.KeyDown += OnKeyDown;
    }

    static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is not DependencyObject hit || ItemsControl.ContainerFromElement(list, hit) is not ListBoxItem row) return;
        e.Handled = Run(list, row.DataContext);
    }

    static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: { } item } list) e.Handled = Run(list, item);
    }

    static bool Run(ListBox list, object? item)
    {
        if (GetCommand(list) is not { } command || item is null || !command.CanExecute(item)) return false;
        command.Execute(item);
        return true;
    }
}
