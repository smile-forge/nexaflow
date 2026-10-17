using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.UsbProbe.StorageTools.Views;

/// <summary>
/// What the storage browser does with the pointer that bindings cannot say: which menu a right-click opens (a row's, a
/// folder's, or the open folder's), and where something dragged in from elsewhere lands. What each then does is the
/// tool's.
/// </summary>
public partial class UsbStorageView : UserControl
{
    public UsbStorageView() => InitializeComponent();

    UsbStorageTool? Tool => DataContext as UsbStorageTool;

    // ── Right-click ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>On a row, the selected rows' menu — the row joins the selection first if it was not in it, as in the
    /// file browser; on the blank space, the open folder's.</summary>
    void OnEntriesContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is not DependencyObject hit || PointerMenus.ItemUnder<GridViewColumnHeader>(hit, Entries) is not null) return;
        if (ItemsControl.ContainerFromElement(Entries, hit) is ListViewItem { DataContext: UsbFileRow row })
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
        if (e.OriginalSource is DependencyObject hit && PointerMenus.ItemUnder<TreeViewItem>(hit, Tree) is { DataContext: UsbFolderNode node })
            Open("FolderMenu", Tree, node);
    }

    /// <summary>On a step of the path bar, that folder's menu; elsewhere on the bar, the open folder's.</summary>
    void OnPathBarContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        var crumb = (e.OriginalSource as FrameworkElement)?.DataContext as UsbPathCrumb;
        if (crumb is null && Tool?.Browser.Crumbs.LastOrDefault() is { } open) crumb = open;
        if (crumb is not null) Open("FolderMenu", PathBar, crumb);
    }

    void Open(string key, FrameworkElement target, object? about) => PointerMenus.OpenAt((ContextMenu)FindResource(key), target, about);

    // ── Drag and drop ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Files dragged in from the file browser or Windows copy into the folder under the pointer — a folder
    /// row or tree node — or into the folder open.</summary>
    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Tool is { } tool && e.Data.GetDataPresent(DataFormats.FileDrop) && tool.CanDropInto(DropFolder(sender, e))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Tool is not { } tool || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        _ = tool.DropAsync(paths, DropFolder(sender, e));
    }

    string? DropFolder(object sender, DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject hit) return null;
        if (sender == Tree) return PointerMenus.ItemUnder<TreeViewItem>(hit, Tree)?.DataContext is UsbFolderNode node ? node.FullPath : null;
        return ItemsControl.ContainerFromElement(Entries, hit) is ListViewItem { DataContext: UsbFileRow { IsFolder: true } folder } ? folder.FullPath : null;
    }
}
