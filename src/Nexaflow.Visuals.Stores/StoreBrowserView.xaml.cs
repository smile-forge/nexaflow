using System.Windows;
using System.Windows.Controls;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Dropping;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Stores;

/// <summary>
/// What a store browser does with the pointer that bindings cannot say: which menu a right-click opens (a row's, a
/// folder's, or the open folder's), and where something dragged in from this PC lands. What each then does is the
/// <see cref="StoreBrowser"/>'s.
/// </summary>
public partial class StoreBrowserView : UserControl, IDropTarget
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
    static readonly string[] DroppableKinds = [DragKinds.Files, DragKinds.FileContents];

    public IReadOnlyList<string> AcceptedKinds => DroppableKinds;

    /// <summary>Files dragged in from the file browser or Windows copy into the folder under the pointer — a folder
    /// row or tree node — or into the folder open.</summary>
    public DropOffer? OfferDrop(DropRequest request)
    {
        if (Browser is not { } browser) return null;

        var hit = this.ElementUnder(request.At);
        if (!DropHitTesting.IsInside(hit, Tree) && !DropHitTesting.IsInside(hit, Entries)) return null;

        return browser.CanDropInto(FolderUnder(hit))
            ? new DropOffer(DropEffect.Copy, Str.Get("Stores.Drop.CopyHere"))
            : DropOffer.Refused;
    }

    public void AcceptDrop(DropRequest request)
    {
        if (Browser is not { } browser) return;

        var folder = FolderUnder(this.ElementUnder(request.At));
        if (request.Payload.FilesOnDisk() is { Count: > 0 } paths) _ = browser.DropAsync(paths, folder);
    }

    object? FolderUnder(DependencyObject? hit)
        => DropHitTesting.IsInside(hit, Tree)
            ? DropHitTesting.Holding<TreeViewItem>(hit)?.DataContext as StoreFolderNode
            : DropHitTesting.Holding<ListViewItem>(hit) is { DataContext: StoreRow { IsFolder: true } folder } ? folder : null;
}
