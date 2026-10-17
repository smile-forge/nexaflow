using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Right-click menus picked by what is under the pointer — a row's, a folder's, the blank space's — as a file browser
/// offers them. A view handles <c>ContextMenuOpening</c>, finds what was clicked with <see cref="ItemUnder{T}"/> and opens
/// the menu for it with <see cref="OpenAt"/>.
/// </summary>
/// <remarks>
/// The menu keeps the view's data context, so its items bind to the view model's commands as the view's own controls
/// do; what it was opened on is its <see cref="FrameworkElement.Tag"/>, which an item passes on as
/// <c>CommandParameter="{Binding Tag, RelativeSource={RelativeSource AncestorType=ContextMenu}}"</c>.
/// </remarks>
public static class PointerMenus
{
    /// <summary>Opens <paramref name="menu"/> at the pointer over <paramref name="target"/>, about <paramref name="about"/>.</summary>
    public static void OpenAt(ContextMenu menu, FrameworkElement target, object? about)
    {
        menu.DataContext = target.DataContext;
        menu.Tag = about;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>The nearest <typeparamref name="T"/> at or above <paramref name="hit"/>, short of <paramref name="stop"/>.</summary>
    public static T? ItemUnder<T>(DependencyObject hit, DependencyObject stop) where T : DependencyObject
    {
        for (var at = hit; at is not null && at != stop; at = (at is Visual ? VisualTreeHelper.GetParent(at) : null) ?? LogicalTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }
}
