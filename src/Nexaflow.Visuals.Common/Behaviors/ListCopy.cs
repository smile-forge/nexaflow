using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>What a row puts on the clipboard when it is copied out of a list: the row as a line of text.</summary>
public interface IClipboardText
{
    string ClipboardText { get; }
}

/// <summary>
/// Lets a list or tree be copied: Ctrl+C and a right-click Copy put the selected rows on the clipboard, one line each,
/// in the order the list shows them. A row says what it copies as through <see cref="IClipboardText"/>; anything else
/// copies as its <c>ToString()</c>.
/// </summary>
/// <remarks>
/// A list's rows are drawn by templates, so their text cannot be selected the way a text box's can — this is the way
/// a reader gets it out instead. <c>local:ListCopy.Enabled="True"</c> on a <see cref="ListBox"/> (and so a
/// <see cref="ListView"/>) or a <see cref="TreeView"/>.
/// </remarks>
public static class ListCopy
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ListCopy), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    static readonly CommandBinding Binding = new(ApplicationCommands.Copy, OnCopy, OnCanCopy);

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control || d is not (ListBox or TreeView)) return;
        if ((bool)e.NewValue)
        {
            control.CommandBindings.Add(Binding);
            control.ContextMenu ??= Menu();
        }
        else
        {
            control.CommandBindings.Remove(Binding);
        }
    }

    static ContextMenu Menu() => new()
    {
        Items = { new MenuItem { Header = Str.Get("Common.List.Copy"), Command = ApplicationCommands.Copy, InputGestureText = "Ctrl+C" } },
    };

    static void OnCanCopy(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Selected(sender).Count > 0;
        e.Handled = true;
    }

    static void OnCopy(object sender, ExecutedRoutedEventArgs e)
    {
        if (SelectionText(sender) is not { } text) return;
        Clipboard.SetText(text);
        e.Handled = true;
    }

    /// <summary>What a copy of <paramref name="list"/> puts on the clipboard — its selected rows, a line each, in the order
    /// the list shows them — or null when nothing is selected.</summary>
    public static string? SelectionText(object list)
    {
        var rows = Selected(list);
        return rows.Count == 0 ? null : string.Join(Environment.NewLine, rows.Select(TextOf));
    }

    /// <summary>The selected rows in the order the list shows them, not the order they were picked.</summary>
    static IReadOnlyList<object> Selected(object sender) => sender switch
    {
        ListBox list when list.SelectionMode == SelectionMode.Single => list.SelectedItem is { } one ? [one] : [],
        ListBox list => [.. Ordered(list.Items, list.SelectedItems)],
        TreeView tree => tree.SelectedItem is { } item ? [item] : [],
        _ => [],
    };

    static IEnumerable<object> Ordered(ItemCollection items, IList selected)
    {
        var picked = new HashSet<object>(selected.Cast<object>());
        return items.Cast<object>().Where(picked.Contains);
    }

    /// <summary>What one row copies as.</summary>
    public static string TextOf(object item) => item is IClipboardText text ? text.ClipboardText : item.ToString() ?? "";
}
