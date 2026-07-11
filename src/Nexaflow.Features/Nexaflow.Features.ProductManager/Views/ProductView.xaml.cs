using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Features.ProductManager.Views;

public partial class ProductView : UserControl, IPageView
{
    public ProductViewModel ViewModel { get; }

    public ProductView(ProductViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        Sunburst.ArcClicked      += (_, id) => ViewModel.OnArcClicked(id);
        Sunburst.CentreClicked   += (_, _)  => ViewModel.OnCentreClicked();
        Sunburst.ArcRightClicked += (_, e)  => ShowNodeMenu(e.Id);
    }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    /// <summary>Deep-link: the Integrity page asks this tab to focus the node owning a broken snaplink.
    /// (An existing Product tab, whose params are just <c>path</c>, is adopted and re-initialised.)</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("node", out var nodeId) && !string.IsNullOrWhiteSpace(nodeId))
            ViewModel.NavigateTo(nodeId);
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e) => ViewModel.AddChild(null);

    // Snaplink picker: bridge the read-only TreeView.SelectedItem to the picker VM.
    private void PickerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => ViewModel.Picker?.SelectFile(e.NewValue as FileNode);

    // "Drop URL here" target in the Link URL tab.
    private void UrlDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void UrlDrop_Drop(object sender, DragEventArgs e)
    {
        var url = e.Data.GetData(DataFormats.UnicodeText) as string
                  ?? e.Data.GetData(DataFormats.Text) as string;
        if (url is not null) ViewModel.SetDroppedUrl(url);
        e.Handled = true;
    }

    // Version actions (⋮). Flat menu, per the global MenuItem-style submenu caveat.
    private void VersionMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = VersionMenuButton };
        menu.Items.Add(Item("📸  Take snapshot", () => ViewModel.ShowTakeSnapshotCommand.Execute(null)));
        var del = Item("🗑  Delete this snapshot", () => ViewModel.DeleteSnapshotCommand.Execute(null));
        del.IsEnabled = ViewModel.SelectedVersion != ProductStore.CurrentVersion;
        menu.Items.Add(del);
        menu.Items.Add(new Separator());
        var restructure = Item("🧩  Restructure…", () => ViewModel.ShowRestructureCommand.Execute(null));
        restructure.IsEnabled = ViewModel.IsEditable;
        menu.Items.Add(restructure);
        menu.Items.Add(Item("🔗  Validate snaplinks", () => ViewModel.OpenIntegrityCommand.Execute(null)));
        menu.Items.Add(Item("🕸  Open graph", () => ViewModel.OpenGraphCommand.Execute(null)));
        menu.Items.Add(Item("🔄  Regenerate graph", () => ViewModel.GenerateGraphCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("⚙  Settings…", () => ViewModel.ShowSettingsCommand.Execute(null)));
        menu.IsOpen = true;
    }

    // Flat context menu (the global MenuItem style doesn't reliably render submenus — see Styles.xaml note).
    private void ShowNodeMenu(string id)
    {
        if (!ViewModel.IsEditable) return;   // snapshots are read-only

        var menu = new ContextMenu { PlacementTarget = Sunburst };

        if (id == ProductViewModel.ProductRootSentinel)
        {
            menu.Items.Add(Item("Add root node", () => ViewModel.AddChild(null)));
            menu.IsOpen = true;
            return;
        }

        menu.Items.Add(Item("Add child node", () => ViewModel.AddChild(id)));
        menu.Items.Add(Item("Rename…",        () => ViewModel.RenameNode(id)));
        menu.Items.Add(Item("Delete…",        () => ViewModel.DeleteNode(id)));
        menu.Items.Add(new Separator());
        var promote = Item("Promote (out a level)",  () => ViewModel.PromoteNode(id));
        promote.IsEnabled = ViewModel.CanPromoteNode(id);
        menu.Items.Add(promote);
        var demote = Item("Demote (under previous)",  () => ViewModel.DemoteNode(id));
        demote.IsEnabled = ViewModel.CanDemoteNode(id);
        menu.Items.Add(demote);
        menu.Items.Add(Item("Insert parent above…",   () => ViewModel.InsertParentAbove(id)));
        menu.Items.Add(new Separator());
        foreach (var s in StatusInfo.All)
        {
            var captured = s;
            menu.Items.Add(Item($"Status: {StatusInfo.Display(s)}", () => ViewModel.SetNodeStatus(id, captured)));
        }
        menu.IsOpen = true;
    }

    // The "+ concern" button → a dropdown of defined-but-not-present concerns.
    private void AddConcern_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        foreach (var name in ViewModel.AddableConcerns)
        {
            var captured = name;
            menu.Items.Add(Item(name, () => ViewModel.AddConcernCommand.Execute(captured)));
        }
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    // The 🔗 on a concern box → a dropdown listing its snaplinks (click → open in a new tab) + "Modify…".
    private void ConcernAttach_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConcernBox box) return;
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        foreach (var link in box.Snaplinks)
        {
            var captured = link;
            menu.Items.Add(Item(SnaplinkFileLabel(link), () => ViewModel.OpenSnaplink(captured)));
        }
        if (box.Snaplinks.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item("Modify…", () => box.EditSnaplinksCommand.Execute(null)));
        menu.IsOpen = true;
    }

    // The 🔗 on the node property panel → same affordance as the concern boxes: list the node's snaplinks
    // (click → open in a new tab) + "Modify…" (opens the shared snaplinks overlay for the node).
    private void NodeAttach_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        foreach (var row in ViewModel.Snaplinks)
        {
            var link = row.Model;
            menu.Items.Add(Item(SnaplinkFileLabel(link), () => ViewModel.OpenSnaplink(link)));
        }
        if (ViewModel.Snaplinks.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item("Modify…", () => ViewModel.EditNodeSnaplinks()));
        menu.IsOpen = true;
    }

    // The dropdown shows just the file (or the URL) — not the heading / class.method subpath.
    private static string SnaplinkFileLabel(Snaplink link) => link.Type == "url"
        ? link.Target ?? "(url)"
        : string.IsNullOrEmpty(link.Doc) ? link.Display : System.IO.Path.GetFileName(link.Doc);

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ── Restructure tree drag-drop ───────────────────────────────────────────
    private Point _restructureDragStart;
    private RestructureNode? _restructureDrag;

    private void RestructureTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _restructureDragStart = e.GetPosition(null);
        _restructureDrag = NodeUnder(e.OriginalSource as DependencyObject);
    }

    private void RestructureTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Don't drag the synthetic product-root row; everything else can be re-parented.
        if (e.LeftButton != MouseButtonState.Pressed || _restructureDrag is null || _restructureDrag.IsProductRoot) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _restructureDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _restructureDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        try { DragDrop.DoDragDrop((DependencyObject)sender, _restructureDrag, DragDropEffects.Move); }
        catch { /* an aborted drag must never take the app down */ }
        _restructureDrag = null;
    }

    private void RestructureTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RestructureNode)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void RestructureTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(RestructureNode)) is not RestructureNode dragged) return;
        // Dropping on a node makes the dragged node its child; dropping in empty space makes it top-level.
        var target = NodeUnder(e.OriginalSource as DependencyObject);
        ViewModel.ReparentNode(dragged.Id, target?.Id);
        e.Handled = true;
    }

    private void RestructureTree_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var node = NodeUnder(e.OriginalSource as DependencyObject);
        if (node is null || node.IsProductRoot) return;
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        menu.Items.Add(Item("Delete…", () => ViewModel.DeleteNode(node.Id)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static RestructureNode? NodeUnder(DependencyObject? src)
    {
        while (src is not null and not TreeViewItem) src = VisualTreeHelper.GetParent(src);
        return (src as TreeViewItem)?.DataContext as RestructureNode;
    }
}
