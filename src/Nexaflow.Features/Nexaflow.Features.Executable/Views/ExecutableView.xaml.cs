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

        // A clicked node in the dependency diagram carries the module's path as its href.
        DependencyDiagram.LinkNavigate = OnDiagramLink;

        vm.PropertyChanged      += OnViewModelPropertyChanged;
        vm.ScrollToHitRequested += OnScrollToHit;
        Unloaded                += OnUnloaded;

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
        _vm.PropertyChanged      -= OnViewModelPropertyChanged;
        _vm.ScrollToHitRequested -= OnScrollToHit;
        Unloaded                 -= OnUnloaded;
    }

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
        if (e.PropertyName == nameof(ExecutableViewModel.ManifestXml)) SyncManifest();
    }

    /// <summary>
    /// AvalonEdit's document is not a dependency property, so the raw-XML pane is filled in code.
    /// XML has no tree-sitter grammar in this repo, but AvalonEdit ships a built-in highlighting
    /// definition for it.
    /// </summary>
    private void SyncManifest()
    {
        ManifestEditor.Text = _vm.ManifestXml;
        ManifestEditor.SyntaxHighlighting =
            ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("XML");

        // AvalonEdit paints its own selection and current-line colours, which default to a light
        // scheme and are invisible against a dark surface.
        ManifestEditor.TextArea.SelectionBrush =
            TryFindResource("AccentSubtleBrush") as Brush ?? ManifestEditor.TextArea.SelectionBrush;
        ManifestEditor.TextArea.SelectionForeground = null;
        ManifestEditor.Options.HighlightCurrentLine = false;
    }

    /// <summary>
    /// A dependency node was clicked. The href carries a scheme saying which of the two actions the
    /// node offers — expand it in place, or open it as its own tab — because a graph node has both
    /// and only one gesture to spend. Returning true tells the markdown renderer the link was
    /// handled in-app, so it does not hand it to the OS.
    /// </summary>
    private bool OnDiagramLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;

        if (href.StartsWith(DependencyMermaid.ExpandScheme, StringComparison.Ordinal))
        {
            _vm.ExpandModule(href[DependencyMermaid.ExpandScheme.Length..]);
            return true;
        }

        string target = href.StartsWith(DependencyMermaid.OpenScheme, StringComparison.Ordinal)
            ? href[DependencyMermaid.OpenScheme.Length..]
            : href;

        // Tolerate a file: URI in case one is ever produced.
        string path = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : target;

        return File.Exists(path) && _vm.OpenDependency(path);
    }

    /// <summary>
    /// In the tree the leading "+" is only text, so it needs a gesture of its own: double-clicking a
    /// module that has not been opened up expands it, and double-clicking one that is already open
    /// inspects it. That mirrors what a click on the same node does in the diagram.
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
