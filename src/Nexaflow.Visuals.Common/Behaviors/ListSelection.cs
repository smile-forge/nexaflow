using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Clicking a list's blank space — below the last row, or beside a row's cells — clears its selection, as a file list
/// does. <c>b:ListSelection.ClearOnBlankClick="True"</c> on a <see cref="ListBox"/> (and so a <see cref="ListView"/>).
/// </summary>
/// <remarks>
/// A click with Ctrl or Shift held is left alone, since it is adding to the selection; so is one on a column header, a
/// scroll bar or anything inside a row.
/// </remarks>
public static class ListSelection
{
    public static readonly DependencyProperty ClearOnBlankClickProperty = DependencyProperty.RegisterAttached(
        "ClearOnBlankClick", typeof(bool), typeof(ListSelection), new PropertyMetadata(false, OnClearOnBlankClickChanged));

    public static bool GetClearOnBlankClick(DependencyObject element) => (bool)element.GetValue(ClearOnBlankClickProperty);
    public static void SetClearOnBlankClick(DependencyObject element, bool value) => element.SetValue(ClearOnBlankClickProperty, value);

    static void OnClearOnBlankClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;
        list.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
        if ((bool)e.NewValue) list.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
    }

    static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is not DependencyObject hit || !IsBlank(list, hit)) return;
        if (list.SelectionMode == SelectionMode.Single) list.SelectedItem = null;
        else list.UnselectAll();
        list.Focus();
    }

    /// <summary>Whether <paramref name="hit"/> is the list's own empty space rather than a row or the list's chrome.</summary>
    public static bool IsBlank(ListBox list, DependencyObject hit)
    {
        for (var at = hit; at is not null && at != list; at = Parent(at))
            if (at is ListBoxItem or GridViewColumnHeader or ScrollBar or Thumb) return false;
        return true;
    }

    static DependencyObject? Parent(DependencyObject child) =>
        (child is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(child) : null) ?? LogicalTreeHelper.GetParent(child);
}
