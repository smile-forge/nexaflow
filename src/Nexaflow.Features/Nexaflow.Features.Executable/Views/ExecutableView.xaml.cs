using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Models;
using Nexaflow.Features.Executable.Services;
using Nexaflow.Features.Executable.ViewModels;
using Nexaflow.Markdown.Binding;
using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Visuals.Text.Editor.Highlighting;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Features.Executable.Views;

public partial class ExecutableView : UserControl, IPageView
{
    private readonly ExecutableViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public ExecutableView(ExecutableViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;

        // Two independent regions on a dependency node: the body carries the module's path and opens
        // it as its own tab; the chip opens the module up in place, told straight to the graph the
        // diagram is bound to.
        DependencyDiagram.DataSource    = new ReflectionDataContext(vm);

        // The viewport has no size of its own to reckon against: what it is showing is a graph laid out as the graph
        // it is, so it asks the diagram how big that came out each time it needs to know.
        DependencyViewport.ContentExtent   = Spread;
        DependencyViewport.MiniMapPicture  = Overview;

        // The diagram says it has been laid out from inside the layout pass that laid it, and fitting the viewport
        // moves the overview, which is more layout. So the fit waits for the pass to finish: Loaded outranks it.
        DependencyDiagram.PreRender += (_, _) =>
            Dispatcher.BeginInvoke(Laid, System.Windows.Threading.DispatcherPriority.Loaded);
        // One press on a node picks it out, which fills the detail pane; two open the module in a tab of its own. The
        // diagram says which node and how often it was pressed, and says nothing about what either should do — a node
        // is not a link, and opening a tab is this page's to decide and this page's to do.
        DependencyDiagram.Selected  += (_, e) => _vm.SelectDependency(Pressed(e.Change));
        DependencyDiagram.DoubleClicked += (_, e) => e.Handled = _vm.OpenModule(Pressed(e.Change));

        vm.PropertyChanged              += OnViewModelPropertyChanged;
        vm.ScrollToHitRequested         += OnScrollToHit;
        vm.DependencyViewResetRequested += OnDependencyViewReset;
        Unloaded                        += OnUnloaded;

        // Clicking into a read-only value box focuses it, and WPF then asks every ancestor to bring
        // the caret into view. Any ancestor that can scroll horizontally obliges, which slid the
        // whole page sideways and pushed the tab rail off screen. Selecting text is not a request
        // to navigate, so those requests are swallowed here.
        AddHandler(RequestBringIntoViewEvent,
                   new RequestBringIntoViewEventHandler(OnRequestBringIntoView), handledEventsToo: true);

        SyncManifest();
    }

    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is TextBox { IsReadOnly: true }) e.Handled = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged              -= OnViewModelPropertyChanged;
        _vm.ScrollToHitRequested         -= OnScrollToHit;
        _vm.DependencyViewResetRequested -= OnDependencyViewReset;
        Unloaded                         -= OnUnloaded;
    }

    /// <summary>
    /// "Collapse all" means start over, so the diagram forgets the folds it had opened, the node it
    /// had selected and where it was zoomed to — leaving the reader zoomed into a corner of a graph
    /// that no longer exists would be the one thing the button was meant to undo.
    /// </summary>
    private void OnDependencyViewReset()
    {
        // Starting over means the next laying is a first sight of it again, and so is fitted rather than compared
        // against an extent belonging to a graph that no longer exists.
        _spread = null;
        DependencyDiagram.ResetDiagramViews();
    }

    /// <summary>How much room the diagram came out needing, for the viewport to fit and to draw its overview against.</summary>
    private CanvasBounds? Spread() =>
        DependencyDiagram.Shown.Laid.Size is { Width: > 0, Height: > 0 } size
            ? new CanvasBounds(0, 0, size.Width, size.Height)
            : null;

    /// <summary>
    /// A picture of the import tree for the viewport's overview: the graph itself, shrunk, rather than a box standing
    /// for it — a dependency graph is recognised by its shape, which is the whole of what an overview is for.
    /// </summary>
    private ImageSource? Overview(Size within) => _overview ??= DependencyDiagram.CapturePicture(within);

    /// <summary>
    /// The last picture taken of it. The overview asks for one every time the view moves, and painting the whole graph
    /// on every notch of the wheel would cost more than the zoom does — so it is taken once per laying out, which is
    /// the only thing that changes what the picture would show.
    /// </summary>
    private ImageSource? _overview;

    /// <summary>
    /// The diagram has been laid out again — a module opened up, or the pane resized.
    ///
    /// <para>
    /// Only the first sight of it is fitted. After that the reader's zoom is theirs: opening a module is a question
    /// about one part of the graph, not a request to see the whole of it from further away, and refitting answered a
    /// question nobody asked by shrinking everything. So the view keeps its scale and moves the least that brings what
    /// appeared onto the page — which is the part they asked for.
    /// </para>
    /// </summary>
    private void Laid()
    {
        _overview = null;

        if (Spread() is not { } now) return;

        var before = _spread;
        _spread = now;

        if (before is not { } was) { DependencyViewport.FitToContent(); return; }

        if (Appeared(was, now) is { } appeared) DependencyViewport.Reveal(appeared);
        else                                    DependencyViewport.RefreshOverview();
    }

    /// <summary>
    /// The band the drawing gained, or null where it did not grow. Reckoned from its own extent rather than from which
    /// node was opened: whatever the reader did, what they want to see is the part that was not there before.
    /// </summary>
    private static CanvasBounds? Appeared(CanvasBounds was, CanvasBounds now) =>
        now.MaxY > was.MaxY + 1 ? new CanvasBounds(now.MinX, was.MaxY, now.MaxX, now.MaxY)
      : now.MaxX > was.MaxX + 1 ? new CanvasBounds(was.MaxX, now.MinY, now.MaxX, now.MaxY)
      : null;

    /// <summary>How much room the drawing took when it was last laid out, so the next laying says what it gained.</summary>
    private CanvasBounds? _spread;

    /// <summary>
    /// Brings a search hit into view. A tinted row three thousand entries down a virtualised list is
    /// not a result anyone can see, so the list that owns the hit is scrolled to it.
    /// </summary>
    private void OnScrollToHit(object hit)
    {
        if (hit is not InspectorRow row) return;

        foreach (var list in (ListBox[])[StringList, ExportList])
        {
            if (list.ItemsSource is null) continue;
            if (!list.Items.Contains(row)) continue;

            list.SelectedItem = row;
            list.ScrollIntoView(row);
            return;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExecutableViewModel.ManifestXml))            SyncManifest();
        if (e.PropertyName == nameof(ExecutableViewModel.HasDependencySelection)) SyncDetailPane();
    }

    /// <summary>Narrowest the detail pane may be dragged before it stops being readable.</summary>
    private const double DetailPaneMinWidth = 180;

    /// <summary>Width the detail column had when it was last open — so closing and reopening the
    /// pane does not undo a drag.</summary>
    private double _detailPaneWidth = 300;

    /// <summary>
    /// Swaps the detail column in and out. Both the width and the floor live on the column, not on
    /// the pane inside it: the splitter resizes the column, so an <c>Auto</c> width would ignore the
    /// drag and a <c>MinWidth</c> on the pane would only let it overflow a column that got smaller.
    /// The floor has to come off while the column is collapsed, or it would hold 180px of empty
    /// space open with nothing selected.
    /// </summary>
    private void SyncDetailPane()
    {
        if (_vm.HasDependencySelection)
        {
            DependencyDetailColumn.MinWidth = DetailPaneMinWidth;
            DependencyDetailColumn.Width    = new GridLength(Math.Max(DetailPaneMinWidth, _detailPaneWidth));
            return;
        }

        if (DependencyDetailColumn.Width.IsAbsolute && DependencyDetailColumn.Width.Value > 0)
            _detailPaneWidth = DependencyDetailColumn.Width.Value;

        DependencyDetailColumn.MinWidth = 0;
        DependencyDetailColumn.Width    = new GridLength(0);
    }

    /// <summary>
    /// AvalonEdit's document is not a dependency property, so the raw-XML pane is filled in code.
    /// XML has no tree-sitter grammar in this repo, but AvalonEdit ships a built-in definition for
    /// it — taken through the registry, which retints it to the app's palette. The shipped colours
    /// are tuned for a light background and come out unreadable on a dark theme (purple on cyan).
    /// </summary>
    private void SyncManifest()
    {
        ManifestEditor.Text = _vm.ManifestXml;
        ManifestEditor.SyntaxHighlighting = HighlightingRegistry.Themed("XML");

        // AvalonEdit paints its own selection and current-line colours, which default to a light
        // scheme and are invisible against a dark surface.
        ManifestEditor.TextArea.SelectionBrush =
            TryFindResource("AccentSubtleBrush") as Brush ?? ManifestEditor.TextArea.SelectionBrush;
        ManifestEditor.TextArea.SelectionForeground = null;
        ManifestEditor.Options.HighlightCurrentLine = false;
    }

    /// <summary>The module a press on the diagram landed on, under the name the walk gave it.</summary>
    private static string? Pressed(ContentSelectionChange change) =>
        change.Picked.FirstOrDefault(pick => pick.Id is not null)?.Id;

    /// <summary>
    /// The tree and the diagram are two views of one thing, so picking a row means the same as
    /// picking a node: both feed the detail pane rather than each answering "what is this" its own way.
    /// </summary>
    private void DependencyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _vm.SelectDependency((e.NewValue as InspectorNode)?.Payload is DependencyNode d ? d.Name : null);

    /// <summary>
    /// In the tree the "+" marker is only text, so it needs a gesture of its own: double-clicking a
    /// module that has not been opened up expands it, and double-clicking one that is already open
    /// inspects it. That mirrors what the diagram's chip and node body do.
    /// </summary>
    private void DependencyTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView { SelectedItem: InspectorNode { Payload: DependencyNode dependency } })
            return;

        if (dependency.CanExpand) _vm.ExpandModule(dependency.Name);
        else                      _vm.OpenDependency(dependency.Path);

        e.Handled = true;
    }

    /// <summary>
    /// Selects the row under the cursor before its context menu opens, so the menu acts on what was
    /// right-clicked rather than on whatever happened to be selected before.
    /// </summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
            item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T) source = VisualTreeHelper.GetParent(source);
        return source as T;
    }

    void IPageView.Reinitialize(Dictionary<string, string> pageParams) { }
}
