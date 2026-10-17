using System.Windows;
using System.Windows.Controls;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Visuals.Stores;

/// <summary>
/// What a store browser does with the pointer that bindings cannot say: which menu a right-click opens (a row's, a
/// folder's, or the open folder's), and where something dragged in from this PC lands. What each then does is the
/// <see cref="StoreBrowser"/>'s.
/// </summary>
public partial class StoreBrowserView : UserControl
{
    public StoreBrowserView() => InitializeComponent();

    StoreBrowser? Browser => DataContext as StoreBrowser;

    // ── Right-click ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>On a row, the selected rows' menu — the row joins the selection first if it was not in it, as in the
    /// file browser; on the blank space, the open folder's.</summary>
    void OnEntriesContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is not DependencyObject hit || PointerMenus.ItemUnder<GridViewColumnHeader>(hit, Entries) is not null) return;
        if (ItemsControl.ContainerFromElement(Entries, hit) is ListViewItem { DataContext: StoreRow row })
        {
            if (!row.IsSelected)
            {
                Entries.UnselectAll();
                row.IsSelected = true;
            }
            Open("RowMenu", Entries, null);
        }
        else
        {
            Entries.UnselectAll();
            Open("BlankMenu", Entries, null);
        }
    }

    void OnTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is DependencyObject hit && PointerMenus.ItemUnder<TreeViewItem>(hit, Tree) is { DataContext: StoreFolderNode node })
            Open("FolderMenu", Tree, node);
    }

    /// <summary>On a step of the path bar, that folder's menu; elsewhere on the bar, the open folder's.</summary>
    void OnPathBarContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        var crumb = (e.OriginalSource as FrameworkElement)?.DataContext as StoreCrumb;
        if (crumb is null && Browser?.Crumbs.LastOrDefault() is { } open) crumb = open;
        if (crumb is not null) Open("FolderMenu", PathBar, crumb);
    }

    void Open(string key, FrameworkElement target, object? about) => PointerMenus.OpenAt((ContextMenu)FindResource(key), target, about);

    // ── Drag and drop ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Files dragged in from the file browser or Windows copy into the folder under the pointer — a folder
    /// row or tree node — or into the folder open.</summary>
    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Browser is { } browser && e.Data.GetDataPresent(DataFormats.FileDrop) && browser.CanDropInto(DropTarget(sender, e))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Browser is not { } browser || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        _ = browser.DropAsync(paths, DropTarget(sender, e));
    }

    object? DropTarget(object sender, DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject hit) return null;
        if (sender == Tree) return PointerMenus.ItemUnder<TreeViewItem>(hit, Tree)?.DataContext as StoreFolderNode;
        return ItemsControl.ContainerFromElement(Entries, hit) is ListViewItem { DataContext: StoreRow { IsFolder: true } folder } ? folder : null;
    }
}
