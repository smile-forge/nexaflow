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
        // it as its own tab, the chip opens the module up in place.
        DependencyDiagram.LinkNavigate  = OnDiagramLink;
        DependencyDiagram.DiagramExpand = OnDiagramExpand;
        DependencyDiagram.DiagramSelect = s => _vm.SelectDependency(s.Key);

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
    private void OnDependencyViewReset() => DependencyDiagram.ResetDiagramViews();

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

    /// <summary>
    /// A dependency node's body was clicked: open that module as its own inspector tab. Returning
    /// true tells the markdown renderer the link was handled in-app, so it does not hand it to the OS.
    /// </summary>
    private bool OnDiagramLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;

        // Tolerate a file: URI in case one is ever produced.
        string path = Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : href;

        return File.Exists(path) && _vm.OpenDependency(path);
    }

    /// <summary>
    /// A node's expand chip was clicked. The key is the module name the generated diagram declared,
    /// so this hands it straight to the walk without keeping a table of mermaid ids. Claiming the
    /// request (returning true) is what makes the diagram wait for the re-walk rather than opening
    /// a subtree it does not have.
    /// </summary>
    private bool OnDiagramExpand(DiagramExpandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key)) return false;

        if (request.Expand) _vm.ExpandModule(request.Key);
        else                _vm.CollapseModule(request.Key);
        return true;
    }

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
