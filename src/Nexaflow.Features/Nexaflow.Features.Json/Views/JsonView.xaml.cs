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

        // Subscribe BEFORE LoadAsync so the very first layout's scroll change is caught
        DisplayList.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(DisplayList_ScrollChanged),
            handledEventsToo: true);

        Loaded   += async (_, _) => await vm.LoadAsync(CancellationToken.None);
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

    // ListBox uses logical scrolling by default: e.VerticalOffset is the index
    // of the first visible item, e.ViewportHeight is the visible row count.
    // We feed that range to the VM, which checks whether any virtual placeholder
    // sits in (or just below) the viewport — if so, load the next batch.
    private void DisplayList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_vm.HasVirtualItems) return;
        if (e.ViewportHeight <= 0) return;

        var first = (int)e.VerticalOffset;
        var last  = first + (int)e.ViewportHeight;
        _vm.SetVisibleRange(first, last);
        _vm.TriggerVirtualLoads();
    }

    // ── Inline AvalonEdit (Text mode) ────────────────────────────────────────
    // Virtualization may recycle the same TextEditor instance to a different
    // inline content row, so:
    //   * always read item from DataContext at handler-fire time (no closure capture)
    //   * subscribe handlers idempotently (clear-tag-then-attach)
    //   * skip Text reassignment if already in sync, to avoid layout thrash that
    //     causes the outer ListBox to jitter

    private void InlineEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor) return;
        if (editor.DataContext is not JsonInlineContentDisplayItem item) return;

        if (editor.Text != item.RawJson)
            editor.Text = item.RawJson;

        // Idempotent subscription: remove first in case Loaded already fired for
        // this instance (recycling, re-template, etc.).
        editor.LostFocus      -= InlineEditor_LostFocus;
        editor.LostFocus      += InlineEditor_LostFocus;
        editor.PreviewMouseWheel -= InlineEditor_PreviewMouseWheel;
        editor.PreviewMouseWheel += InlineEditor_PreviewMouseWheel;
        editor.MouseEnter     -= InlineEditor_MouseEnter;
        editor.MouseEnter     += InlineEditor_MouseEnter;
    }

    private void InlineEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        // Read item from current DataContext, NOT a captured variable — virtualization
        // may have recycled this editor to a different row since Loaded ran.
        if (sender is not TextEditor editor) return;
        if (editor.DataContext is not JsonInlineContentDisplayItem item) return;
        if (editor.Text == item.RawJson) return;
        _vm.CommitRawJson(item.Node, editor.Text);
    }

    private void InlineEditor_MouseEnter(object sender, MouseEventArgs e)
    {
        // Give the editor focus so wheel events go to its internal ScrollViewer first.
        if (sender is TextEditor editor && !editor.TextArea.IsKeyboardFocusWithin)
            editor.TextArea.Focus();
    }

    private void InlineEditor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Manually drive the editor's internal ScrollViewer and mark the event handled,
        // so wheel scrolling over the editor never bubbles to the outer ListBox.
        if (sender is not TextEditor editor) return;
        var sv = FindScrollViewer(editor);
        if (sv is null) return;

        // 3 lines per wheel notch — same feel as the outer list
        var delta = e.Delta > 0 ? -3 : 3;
        for (var i = 0; i < Math.Abs(delta); i++)
        {
            if (e.Delta > 0) sv.LineUp();
            else             sv.LineDown();
        }
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
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

    // ── Table mode rows (root array) ─────────────────────────────────────────
    // Column widths are aligned across rows via SharedSizeGroup (ListBox has
    // Grid.IsSharedSizeScope="True"). Auto-sized cells with a min width.

    private void TableHeader_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid) return;
        if (grid.DataContext is not JsonTableHeaderDisplayItem header) return;
        BuildTableGrid(grid, header.Columns, header.Columns, isHeader: true);
    }

    private void TableRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid) return;
        if (grid.DataContext is not JsonTableRowDisplayItem row) return;
        BuildTableGrid(grid, row.Columns, row.Values, isHeader: false);
    }

    private void BuildTableGrid(Grid grid, IReadOnlyList<string> columns,
                                IReadOnlyList<string> values, bool isHeader)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        var headerBrush = (Brush)FindResource("TextBrush");
        var cellBrush   = (Brush)FindResource("TextBrush");

        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width           = GridLength.Auto,
                MinWidth        = 80,
                SharedSizeGroup = $"JsonCol{i}",
            });
        }

        for (var i = 0; i < values.Count && i < columns.Count; i++)
        {
            var tb = new TextBlock
            {
                Text                = values[i] ?? string.Empty,
                FontSize            = 12,
                Margin              = new Thickness(8, 3, 8, 3),
                VerticalAlignment   = VerticalAlignment.Center,
                TextTrimming        = TextTrimming.CharacterEllipsis,
                Foreground          = isHeader ? headerBrush : cellBrush,
                FontWeight          = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }
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
