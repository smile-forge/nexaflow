using ICSharpCode.AvalonEdit;
using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.ViewModels;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Features.Json.Views;

public partial class JsonView : UserControl, IPageView
{
    private readonly JsonViewModel _vm;

    private JsonNodeModel? _dragSource;
    private Point          _dragStart;
    private const double   DragThreshold = 5.0;

    IPageViewModel? IPageView.ViewModel => _vm;

    internal JsonView(JsonViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;

        vm.ScrollToItemRequested += OnScrollToItem;

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync(CancellationToken.None);
            // Wire scroll-triggered pre-loading after the list is populated
            DisplayList.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(DisplayList_ScrollChanged),
                handledEventsToo: true);
        };

        Unloaded += (_, _) =>
        {
            vm.ScrollToItemRequested -= OnScrollToItem;
            vm.Dispose();
        };
    }

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        var path = pageParams.GetValueOrDefault("path") ?? string.Empty;
        if (path == _vm.FilePath) return;
        _vm.FilePath = path;
        _ = _vm.LoadAsync(CancellationToken.None);
    }

    private void OnScrollToItem(object? sender, JsonDisplayItem item)
        => Dispatcher.InvokeAsync(() => DisplayList.ScrollIntoView(item), DispatcherPriority.Loaded);

    // ── Scroll-triggered virtual loading ─────────────────────────────────────

    // Pre-load the next batch when the user is within ~one viewport of the end
    // of loaded content, OR when all content fits without needing to scroll (so
    // the viewport auto-fills on initial load).
    // NOTE: ScrollChangedEventArgs carries all needed metrics — do NOT check
    // `sender is ScrollViewer`: sender is the ListBox, not the inner ScrollViewer.
    private void DisplayList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_vm.HasVirtualItems) return;
        if (e.ExtentHeight <= 0 || e.ViewportHeight <= 0) return;

        // Fire when within one viewport of the bottom, or when no scroll is needed at all
        var nearBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - e.ViewportHeight;
        var allVisible = e.ExtentHeight <= e.ViewportHeight;

        if (nearBottom || allVisible)
            _vm.TriggerVirtualLoads();
    }

    // ── Inline AvalonEdit (Text mode) ────────────────────────────────────────

    private void InlineEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor) return;
        if (editor.DataContext is not JsonInlineContentDisplayItem item) return;

        editor.Text = item.RawJson;
        editor.LostFocus += (_, _) => _vm.CommitRawJson(item.Node, editor.Text);
    }

    // ── Inline DataGrid (Table mode) ─────────────────────────────────────────

    private void InlineTable_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid dg) return;
        if (dg.DataContext is not JsonInlineContentDisplayItem item) return;
        if (item.Node is not JsonArrayNodeModel arr) return;

        var objects = arr.Children.OfType<JsonObjectNodeModel>().ToList();
        if (objects.Count == 0) return;

        // Collect all distinct keys in order of first appearance
        var keys = new List<string>();
        var seen = new HashSet<string>();
        foreach (var obj in objects)
            foreach (var child in obj.Children)
                if (child.Key is not null && seen.Add(child.Key))
                    keys.Add(child.Key);

        var table = new DataTable();
        foreach (var key in keys)
            table.Columns.Add(key, typeof(string));

        foreach (var obj in objects)
        {
            var row  = table.NewRow();
            var dict = obj.Children
                .Where(c => c.Key is not null)
                .ToDictionary(
                    c => c.Key!,
                    c => c is JsonValueNodeModel v ? v.DisplayValue : "…");
            foreach (var key in keys)
                row[key] = dict.GetValueOrDefault(key, string.Empty);
            table.Rows.Add(row);
        }

        dg.Columns.Clear();
        foreach (DataColumn col in table.Columns)
        {
            dg.Columns.Add(new DataGridTextColumn
            {
                Header  = col.ColumnName,
                Binding = new System.Windows.Data.Binding($"[{col.ColumnName}]"),
            });
        }

        dg.ItemsSource = table.DefaultView;
    }

    // ── Drag and drop ────────────────────────────────────────────────────────

    private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem lbi) return;
        if (lbi.DataContext is not JsonTreeDisplayItem) return;

        _dragSource = (lbi.DataContext as JsonTreeDisplayItem)!.Node;
        _dragStart  = e.GetPosition(DisplayList);
    }

    private void DisplayList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos  = e.GetPosition(DisplayList);
        var diff = pos - _dragStart;

        if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold) return;

        var source = _dragSource;
        _dragSource = null;

        var data = new DataObject(typeof(JsonNodeModel), source);
        DragDrop.DoDragDrop(DisplayList, data, DragDropEffects.Move);
    }

    private void DisplayList_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(JsonNodeModel)) as JsonNodeModel;
        var target  = GetNodeAtPoint(e.GetPosition(DisplayList));

        if (dragged is null || target is null || dragged == target ||
            dragged.Parent is null || dragged.Parent != target.Parent)
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }

        e.Handled = true;
    }

    private void DisplayList_Drop(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(JsonNodeModel)) as JsonNodeModel;
        var dropPos = e.GetPosition(DisplayList);
        var target  = GetNodeAtPoint(dropPos);

        if (dragged is null || target is null || dragged == target) return;
        if (dragged.Parent is null || dragged.Parent != target.Parent) return;

        var lbi = GetListBoxItemAtPoint(dropPos);
        bool insertBefore = true;
        if (lbi is not null)
        {
            var midY = lbi.TranslatePoint(new Point(0, lbi.ActualHeight / 2.0), DisplayList).Y;
            insertBefore = dropPos.Y < midY;
        }

        _vm.MoveNode(dragged, target, insertBefore);
        e.Handled = true;
    }

    private JsonNodeModel? GetNodeAtPoint(Point pos)
        => (GetListBoxItemAtPoint(pos)?.DataContext as JsonTreeDisplayItem)?.Node;

    private ListBoxItem? GetListBoxItemAtPoint(Point pos)
    {
        var hit = DisplayList.InputHitTest(pos) as DependencyObject;
        while (hit is not null)
        {
            if (hit is ListBoxItem lbi) return lbi;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
}
