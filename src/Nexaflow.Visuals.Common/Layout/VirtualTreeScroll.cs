using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Layout;

/// <summary>
/// Brings an item of a virtualized <see cref="TreeView"/> into view when its row may not exist yet.
/// <para>
/// A virtualized tree makes rows only for what is on screen, so an item scrolled out of view, or under a folder just
/// opened, has no <see cref="TreeViewItem"/> to call <see cref="FrameworkElement.BringIntoView()"/> on. This walks
/// down the trail of items from a root, asking each level's panel to make the row for the next one before stepping
/// into it, and brings the last row into view.
/// </para>
/// </summary>
public static class VirtualTreeScroll
{
    /// <summary>
    /// Brings the last item of <paramref name="trail"/> into view. The trail starts at one of the tree's own items
    /// and names each item below it in turn; every item but the last must be expanded.
    /// </summary>
    /// <returns>The row for the last item, or null when some item in the trail is not where it says.</returns>
    public static TreeViewItem? BringIntoView(TreeView tree, IReadOnlyList<object> trail)
    {
        ItemsControl level = tree;
        TreeViewItem? row = null;

        foreach (var item in trail)
        {
            var index = level.Items.IndexOf(item);
            if (index < 0) return null;

            row = RowAt(level, index);
            if (row is null) return null;

            level = row;
        }

        row?.BringIntoView();
        return row;
    }

    /// <summary>The row for the item at <paramref name="index"/> of <paramref name="level"/>, made if it is not.</summary>
    private static TreeViewItem? RowAt(ItemsControl level, int index)
    {
        if (level.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem made) return made;

        // A level that has just been expanded has not been laid out, so its panel may not exist yet.
        level.ApplyTemplate();
        if (ItemsPanelOf(level) is null) level.UpdateLayout();

        if (ItemsPanelOf(level) is VirtualizingStackPanel panel)
            panel.BringIndexIntoViewPublic(index);
        else
            level.UpdateLayout();

        return level.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
    }

    /// <summary>
    /// The panel holding <paramref name="level"/>'s own rows. Breadth first, because the rows below hold presenters of
    /// their own, and the nearest one is this level's.
    /// </summary>
    private static Panel? ItemsPanelOf(ItemsControl level)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(level);

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node is ItemsPresenter presenter)
            {
                presenter.ApplyTemplate();
                return VisualTreeHelper.GetChildrenCount(presenter) > 0
                    ? VisualTreeHelper.GetChild(presenter, 0) as Panel
                    : null;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                pending.Enqueue(VisualTreeHelper.GetChild(node, i));
        }

        return null;
    }
}
