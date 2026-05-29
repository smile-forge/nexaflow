using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.ViewModels;

namespace Nexaflow.Features.Tabular.Views;

/// <summary>
/// Header strip + flat list of row visuals in a vertical StackPanel. Horizontal scroll is
/// synced between the header and body via a single ScrollBar. Vertical scrolling is also a
/// custom ScrollBar — its <see cref="ScrollBar.Maximum"/> is the file row count, so the
/// thumb represents focal position even though only 150 row visuals exist.
///
/// Header zones:
///   ▸ type icon + name label  → left-click toggles IsSelected
///   ▸ sort glyph               → left-click cycles sort (small mode only — hidden in large)
///   ▸ right-edge thumb (3 px) → drag to resize column width
/// Row visuals are pooled — clicks fire <see cref="VirtualizedRowsControl.RowClicked"/>
/// with Ctrl / Shift modifiers so the ViewModel can implement Excel-style row selection.
/// </summary>
public partial class VirtualizedRowsControl : UserControl
{
    public const double DefaultRowHeight = 24;
    public const double IndexColumnWidth = 56;
    public const double ResizeGripWidth  = 4;

    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(ObservableCollection<TabularColumnViewModel>),
            typeof(VirtualizedRowsControl), new PropertyMetadata(null, OnColumnsChanged));

    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(ObservableCollection<TabularRowViewModel>),
            typeof(VirtualizedRowsControl), new PropertyMetadata(null, OnRowsChanged));

    public static readonly DependencyProperty TotalRowCountProperty =
        DependencyProperty.Register(nameof(TotalRowCount), typeof(int),
            typeof(VirtualizedRowsControl), new PropertyMetadata(0, OnTotalRowCountChanged));

    public static readonly DependencyProperty IsSortableProperty =
        DependencyProperty.Register(nameof(IsSortable), typeof(bool),
            typeof(VirtualizedRowsControl), new PropertyMetadata(false, (d, _) =>
                ((VirtualizedRowsControl)d).RebuildHeader()));

    public static readonly DependencyProperty CommentTooltipProperty =
        DependencyProperty.Register(nameof(CommentTooltip), typeof(string),
            typeof(VirtualizedRowsControl), new PropertyMetadata(string.Empty, (d, _) =>
                ((VirtualizedRowsControl)d).RebuildHeader()));

    public ObservableCollection<TabularColumnViewModel>? Columns
    {
        get => (ObservableCollection<TabularColumnViewModel>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }
    public ObservableCollection<TabularRowViewModel>? Rows
    {
        get => (ObservableCollection<TabularRowViewModel>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }
    public int TotalRowCount
    {
        get => (int)GetValue(TotalRowCountProperty);
        set => SetValue(TotalRowCountProperty, value);
    }
    public bool IsSortable
    {
        get => (bool)GetValue(IsSortableProperty);
        set => SetValue(IsSortableProperty, value);
    }
    public string CommentTooltip
    {
        get => (string)GetValue(CommentTooltipProperty);
        set => SetValue(CommentTooltipProperty, value);
    }

    public event Action<int>? FocalRowChanged;
    /// <summary>
    /// Raised when the header's WPF ContextMenu is about to open (right-click). The
    /// handler receives the column and a fresh, empty <see cref="ContextMenu"/> already
    /// parented into WPF's logical tree — populate it synchronously. This lets WPF's
    /// standard MenuItem rendering produce proper submenus, which the previous
    /// new-and-set-IsOpen-true approach didn't reliably do.
    /// </summary>
    public event Action<TabularColumnViewModel, ContextMenu>? HeaderContextMenuOpening;
    public event Action<TabularRowViewModel, ModifierKeys>? RowClicked;

    // ── Row visual pool ──────────────────────────────────────────────────

    private sealed class RowVisual
    {
        public required Border       Container { get; init; }
        public required StackPanel   Stack     { get; init; }
        public required TextBlock    IndexCell { get; init; }
        public required TextBlock[]  DataCells { get; init; }
        public          TabularRowViewModel? Bound;
        public          PropertyChangedEventHandler? BoundHandler;
    }

    private readonly List<RowVisual>  _rowPool = new();
    private readonly Dictionary<TabularColumnViewModel, HeaderCell> _headerCells = new();
    private readonly DispatcherTimer  _focalDebounce = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private double _totalWidth = IndexColumnWidth;
    private int    _columnCount;
    private int    _pendingFocal;
    private bool   _suppressEcho;
    private bool   _rowsUpdateScheduled;

    public VirtualizedRowsControl()
    {
        InitializeComponent();
        _focalDebounce.Tick += (_, _) => { _focalDebounce.Stop(); FocalRowChanged?.Invoke(_pendingFocal); };
        Loaded              += (_, _) => { RebuildAll(); UpdateScrollExtents(); };
        SizeChanged         += (_, _) => UpdateScrollExtents();
        // Wheel scrolling has to drive the synthetic VScroll (which controls focal row +
        // window reload) instead of the inner ScrollViewer (which only contains the
        // ~150-row window — wheel would otherwise stop at the bottom of that window).
        PreviewMouseWheel   += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (VScroll.Maximum <= 0) return;
        const double RowsPerNotch = 3.0;
        var delta = -(e.Delta / 120.0) * RowsPerNotch;
        VScroll.Value = System.Math.Max(VScroll.Minimum,
                                        System.Math.Min(VScroll.Maximum, VScroll.Value + delta));
        e.Handled = true;
    }

    // ── DP change handlers ────────────────────────────────────────────────

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (VirtualizedRowsControl)d;
        if (e.OldValue is INotifyCollectionChanged oldNcc) oldNcc.CollectionChanged -= ctl.OnColumnsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNcc) newNcc.CollectionChanged += ctl.OnColumnsCollectionChanged;
        if (e.OldValue is ObservableCollection<TabularColumnViewModel> oldCols)
            foreach (var c in oldCols) c.PropertyChanged -= ctl.OnColumnPropertyChanged;
        if (e.NewValue is ObservableCollection<TabularColumnViewModel> newCols)
            foreach (var c in newCols) c.PropertyChanged += ctl.OnColumnPropertyChanged;
        ctl.RebuildAll();
    }

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (VirtualizedRowsControl)d;
        if (e.OldValue is INotifyCollectionChanged oldNcc) oldNcc.CollectionChanged -= ctl.OnRowsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNcc) newNcc.CollectionChanged += ctl.OnRowsCollectionChanged;
        ctl.UpdateRowVisuals();
        ctl.UpdateScrollExtents();
    }

    private static void OnTotalRowCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((VirtualizedRowsControl)d).UpdateScrollExtents();

    private void OnColumnsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { } added)   foreach (TabularColumnViewModel c in added)   c.PropertyChanged += OnColumnPropertyChanged;
        if (e.OldItems is { } removed) foreach (TabularColumnViewModel c in removed) c.PropertyChanged -= OnColumnPropertyChanged;
        RebuildAll();
    }

    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var col = (TabularColumnViewModel)sender!;
        switch (e.PropertyName)
        {
            case nameof(TabularColumnViewModel.IsSelected):    UpdateHeaderSelectedVisual(col); break;
            case nameof(TabularColumnViewModel.SortDirection): UpdateHeaderSortGlyph(col);      break;
            case nameof(TabularColumnViewModel.Header):
            case nameof(TabularColumnViewModel.DisplayType):   UpdateHeaderLabelVisual(col);    break;
            case nameof(TabularColumnViewModel.Width):         UpdateColumnWidths();            break;
        }
    }

    private void OnRowsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        // VM updates the Window via Clear() + 150x Add(). Each notification calls
        // UpdateRowVisuals — and the Clear notification leaves the visual pool with zero
        // visible rows, which a render pass between the Clear and the first Add will
        // briefly draw as a blank frame. Coalesce the entire batch into one dispatcher
        // tick so the user only sees the final state.
        if (_rowsUpdateScheduled) return;
        _rowsUpdateScheduled = true;
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            _rowsUpdateScheduled = false;
            UpdateRowVisuals();
            UpdateScrollExtents();
        }), DispatcherPriority.Background);
    }

    // ── Header rendering ──────────────────────────────────────────────────

    private sealed class HeaderCell
    {
        public required Grid       Container { get; init; }
        public required Border     LabelBox  { get; init; }
        public required TextBlock  IconText  { get; init; }
        public required TextBlock  LabelText { get; init; }
        public required TextBlock  SortGlyph { get; init; }
        public required Border     SortBox   { get; init; }
    }

    private void RebuildAll()
    {
        RebuildHeader();
        RebuildRowPool();
        UpdateRowVisuals();
    }

    private void RebuildHeader()
    {
        HeaderHost.Children.Clear();
        _headerCells.Clear();
        _totalWidth = IndexColumnWidth;

        HeaderHost.Children.Add(BuildIndexHeader());
        if (Columns is null) return;
        foreach (var col in Columns)
        {
            var hc = BuildColumnHeader(col);
            _headerCells[col] = hc;
            HeaderHost.Children.Add(hc.Container);
            _totalWidth += col.Width;
        }
    }

    private FrameworkElement BuildIndexHeader()
    {
        var hasComments = !string.IsNullOrEmpty(CommentTooltip);
        var label = new TextBlock
        {
            Text          = hasComments ? "ⓘ" : "#",
            Foreground    = (Brush)FindResource(hasComments ? "AccentBrush" : "TextDimBrush"),
            FontWeight    = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
            Cursor        = hasComments ? Cursors.Help : Cursors.Arrow,
        };
        if (hasComments)
        {
            // Build the tooltip content as an explicit left-aligned monospace TextBlock so
            // the comment lines render flush-left (and any columnar content lines up). A
            // bare string tooltip inherits the parent's TextAlignment when WPF wraps it.
            label.ToolTip = new TextBlock
            {
                Text                = CommentTooltip,
                TextAlignment       = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontFamily          = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize            = 12,
            };
            ToolTipService.SetShowDuration(label, 30_000);
        }
        return new Border
        {
            Width             = IndexColumnWidth,
            Padding           = new Thickness(8, 6, 8, 6),
            BorderBrush       = (Brush)FindResource("BorderBrush"),
            BorderThickness   = new Thickness(0, 0, 1, 0),
            Background        = Brushes.Transparent,
            Child             = label,
        };
    }

    private HeaderCell BuildColumnHeader(TabularColumnViewModel col)
    {
        // Layout per header cell:
        //   [icon + label (clickable, selectable)] [sort glyph (clickable if sortable)] [resize thumb]
        var grid = new Grid { Width = col.Width };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ResizeGripWidth) });

        var iconText = new TextBlock
        {
            Text              = CsvDataTypeIcons.Glyph(col.DisplayType),
            Foreground        = (Brush)FindResource("TextMutedBrush"),
            FontSize          = 11,
            Margin            = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var labelText = new TextBlock
        {
            Text              = col.Header,
            Foreground        = (Brush)FindResource("TextBrush"),
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };
        var labelStack = new StackPanel { Orientation = Orientation.Horizontal };
        labelStack.Children.Add(iconText);
        labelStack.Children.Add(labelText);

        // Only the label area toggles column selection.
        var labelBox = new Border
        {
            Padding           = new Thickness(8, 6, 4, 6),
            Background        = col.IsSelected ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Cursor            = Cursors.Hand,
            Child             = labelStack,
        };
        labelBox.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            bool additive = Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift;
            ToggleSelected(col, additive);
        };

        // Attach a WPF-managed ContextMenu so its submenus render through the standard
        // popup machinery (the previous IsOpen=true approach did not produce submenu
        // popouts reliably). Items are populated in ContextMenuOpening (fires BEFORE
        // the popup is laid out) so the submenu arrows render the first time.
        AttachHeaderContextMenu(labelBox, col);
        Grid.SetColumn(labelBox, 0);

        // Sort glyph zone — clickable when sortable. Shows ▲/▼ when active, ↕ when sortable
        // but inactive, blank when not sortable so it never gets in the way of the label.
        var sortGlyph = new TextBlock
        {
            Text              = SortGlyphFor(col.SortDirection, IsSortable),
            Foreground        = col.SortDirection == SortDirection.None
                                    ? (Brush)FindResource("TextDimBrush")
                                    : (Brush)FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 11,
        };
        var sortBox = new Border
        {
            Padding           = new Thickness(4, 6, 8, 6),
            Background        = Brushes.Transparent,
            Cursor            = IsSortable ? Cursors.Hand : Cursors.Arrow,
            Child             = sortGlyph,
            Tag               = col,
        };
        if (IsSortable)
            sortBox.MouseLeftButtonUp += (_, _) => CycleSort(col);
        AttachHeaderContextMenu(sortBox, col);
        Grid.SetColumn(sortBox, 1);

        // Resize handle on the right edge.
        var thumb = new Thumb
        {
            Width  = ResizeGripWidth,
            Cursor = Cursors.SizeWE,
            Background = (Brush)FindResource("BorderBrush"),
            Template   = new ControlTemplate(typeof(Thumb))
            {
                VisualTree = ThumbVisual(),
            },
        };
        var capturedCol = col;
        thumb.DragDelta += (_, e) =>
        {
            var newW = System.Math.Max(40, capturedCol.Width + e.HorizontalChange);
            capturedCol.Width = newW;
        };
        Grid.SetColumn(thumb, 2);

        grid.Children.Add(labelBox);
        grid.Children.Add(sortBox);
        grid.Children.Add(thumb);

        return new HeaderCell
        {
            Container = grid,
            LabelBox  = labelBox,
            IconText  = iconText,
            LabelText = labelText,
            SortGlyph = sortGlyph,
            SortBox   = sortBox,
        };
    }

    private void AttachHeaderContextMenu(FrameworkElement target, TabularColumnViewModel col)
    {
        // Build the menu freshly on every right-click. WPF only evaluates
        // MenuItem.HasItems (and therefore decides whether to render a submenu arrow +
        // popup) when the item is first added to the open menu's visual tree. Mutating
        // Items on an already-assigned ContextMenu inside ContextMenuOpening leaves
        // submenu rendering in an inconsistent state for some WPF builds, so we hand
        // WPF a brand-new fully-populated ContextMenu instance each time.
        target.ContextMenuOpening += (s, e) =>
        {
            var fresh = new ContextMenu();
            HeaderContextMenuOpening?.Invoke(col, fresh);
            target.ContextMenu = fresh;
        };
        // Seed an initial menu so the very first right-click has something to show.
        var initial = new ContextMenu();
        HeaderContextMenuOpening?.Invoke(col, initial);
        target.ContextMenu = initial;
    }

    private static FrameworkElementFactory ThumbVisual()
    {
        var rect = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
        rect.SetValue(System.Windows.Shapes.Shape.FillProperty,
            new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)));
        return rect;
    }

    private static string SortGlyphFor(SortDirection dir, bool sortable) => dir switch
    {
        SortDirection.Asc  => "▲",
        SortDirection.Desc => "▼",
        _                  => sortable ? "↕" : " ",
    };

    private void UpdateHeaderLabelVisual(TabularColumnViewModel col)
    {
        if (!_headerCells.TryGetValue(col, out var hc)) return;
        hc.IconText.Text  = CsvDataTypeIcons.Glyph(col.DisplayType);
        hc.LabelText.Text = col.Header;
    }

    private void UpdateHeaderSortGlyph(TabularColumnViewModel col)
    {
        if (!_headerCells.TryGetValue(col, out var hc)) return;
        hc.SortGlyph.Text       = SortGlyphFor(col.SortDirection, IsSortable);
        hc.SortGlyph.Foreground = col.SortDirection == SortDirection.None
            ? (Brush)FindResource("TextDimBrush")
            : (Brush)FindResource("AccentBrush");
    }

    private void UpdateHeaderSelectedVisual(TabularColumnViewModel col)
    {
        if (!_headerCells.TryGetValue(col, out var hc)) return;
        hc.LabelBox.Background = col.IsSelected
            ? (Brush)FindResource("AccentBrush")
            : Brushes.Transparent;
        TintColumnSelection(col);
    }

    private void TintColumnSelection(TabularColumnViewModel col)
    {
        if (Columns is null) return;
        int colIdx = Columns.IndexOf(col);
        if (colIdx < 0) return;
        Brush? tint = col.IsSelected
            ? new SolidColorBrush(Color.FromArgb(0x33, 0x4F, 0x8E, 0xF7))
            : null;
        foreach (var rv in _rowPool)
            if (colIdx < rv.DataCells.Length)
                rv.DataCells[colIdx].Background = tint;
    }

    private void UpdateColumnWidths()
    {
        if (Columns is null) return;
        _totalWidth = IndexColumnWidth;
        foreach (var col in Columns)
        {
            if (_headerCells.TryGetValue(col, out var hc)) hc.Container.Width = col.Width;
            _totalWidth += col.Width;
        }
        foreach (var rv in _rowPool)
        {
            for (int i = 0; i < rv.DataCells.Length && i < Columns.Count; i++)
                rv.DataCells[i].Width = Columns[i].Width;
            rv.Stack.InvalidateMeasure();
        }
        UpdateScrollExtents();
    }

    private void ToggleSelected(TabularColumnViewModel col, bool additive)
    {
        if (Columns is null) return;
        if (!additive)
            foreach (var c in Columns) if (c != col && c.IsSelected) c.IsSelected = false;
        col.IsSelected = !col.IsSelected;
    }

    private void CycleSort(TabularColumnViewModel col)
    {
        col.SortDirection = col.SortDirection switch
        {
            SortDirection.None => SortDirection.Asc,
            SortDirection.Asc  => SortDirection.Desc,
            _                  => SortDirection.None,
        };
    }

    // ── Row visual pool ──────────────────────────────────────────────────

    private void RebuildRowPool()
    {
        int desired = Columns?.Count ?? 0;
        if (desired == _columnCount && _rowPool.Count > 0) return;

        // Column count changed — unbind, drop, rebuild.
        foreach (var rv in _rowPool) UnbindRow(rv);
        RowsHost.Children.Clear();
        _rowPool.Clear();
        _columnCount = desired;
    }

    private void UpdateRowVisuals()
    {
        if (Rows is null || Columns is null) return;
        EnsurePoolSize(Rows.Count);

        int i = 0;
        foreach (var row in Rows)
        {
            var rv = _rowPool[i++];
            rv.Container.Visibility = Visibility.Visible;
            BindRow(rv, row);

            rv.IndexCell.Text = (row.AbsoluteIndex + 1).ToString();
            for (int c = 0; c < rv.DataCells.Length; c++)
                rv.DataCells[c].Text = c < row.Cells.Count ? row.Cells[c] : string.Empty;
            ApplyRowAppearance(rv);
        }
        for (; i < _rowPool.Count; i++)
        {
            UnbindRow(_rowPool[i]);
            _rowPool[i].Container.Visibility = Visibility.Collapsed;
        }
        // Defensive: the body ScrollViewer can pick up an internal vertical offset from
        // keyboard / focus / unintercepted events. Always reset to 0 so the rebuilt
        // window's first row sits at the top of the viewport.
        BodyScroller.ScrollToVerticalOffset(0);
    }

    private void EnsurePoolSize(int needed)
    {
        while (_rowPool.Count < needed)
        {
            var visual = BuildRowVisual();
            RowsHost.Children.Add(visual.Container);
            _rowPool.Add(visual);
        }
    }

    private RowVisual BuildRowVisual()
    {
        var idx = new TextBlock
        {
            Foreground    = (Brush)FindResource("TextDimBrush"),
            Width         = IndexColumnWidth,
            Padding       = new Thickness(8, 4, 8, 4),
            FontFamily    = new FontFamily("Cascadia Code, Consolas, monospace"),
            TextAlignment = TextAlignment.Right,
        };

        var colCount = Columns?.Count ?? 0;
        var dataCells = new TextBlock[colCount];
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(idx);
        for (int c = 0; c < colCount; c++)
        {
            dataCells[c] = new TextBlock
            {
                Foreground        = (Brush)FindResource("TextBrush"),
                Width             = Columns![c].Width,
                Padding           = new Thickness(8, 4, 8, 4),
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(dataCells[c]);
        }

        var border = new Border
        {
            Child       = stack,
            Background  = (Brush)FindResource("BgBrush"),
            Cursor      = Cursors.Arrow,
        };
        var visual = new RowVisual
        {
            Container = border,
            Stack     = stack,
            IndexCell = idx,
            DataCells = dataCells,
        };
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (visual.Bound is { } row) RowClicked?.Invoke(row, Keyboard.Modifiers);
            e.Handled = true;
        };
        return visual;
    }

    private void BindRow(RowVisual rv, TabularRowViewModel row)
    {
        if (rv.Bound == row) return;
        UnbindRow(rv);
        rv.Bound        = row;
        rv.BoundHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(TabularRowViewModel.IsSelected)) ApplyRowAppearance(rv);
        };
        row.PropertyChanged += rv.BoundHandler;
    }

    private void UnbindRow(RowVisual rv)
    {
        if (rv.Bound is { } r && rv.BoundHandler is { } h) r.PropertyChanged -= h;
        rv.Bound        = null;
        rv.BoundHandler = null;
    }

    private void ApplyRowAppearance(RowVisual rv)
    {
        if (rv.Bound is not { } row) return;
        if (row.IsSelected)
            rv.Container.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x4F, 0x8E, 0xF7));
        else
            rv.Container.Background = row.IsAlternate
                ? (Brush)FindResource("SurfaceBrush")
                : (Brush)FindResource("BgBrush");
    }

    // ── Scrolling ─────────────────────────────────────────────────────────

    private void UpdateScrollExtents()
    {
        // ScrollBar.Value is the *top visible row index*. ViewportSize is sized against
        // rows-on-screen so the thumb is proportionally correct, but Maximum keeps a small
        // bottom buffer (BottomBufferRows) so the user can scroll just past the last row
        // and feel that they actually reached the end — the last ~15 entries stay visible
        // with the rest of the viewport empty.
        const double EstRowHeight     = 24;
        const int    BottomBufferRows = 15;
        int onScreenRows = System.Math.Max(1, (int)(BodyScroller.ActualHeight / EstRowHeight));
        VScroll.Minimum      = 0;
        VScroll.Maximum      = System.Math.Max(0, TotalRowCount - BottomBufferRows);
        VScroll.ViewportSize = onScreenRows;
        VScroll.IsEnabled    = VScroll.Maximum > 0;

        // Horizontal scrollbar shows iff content is wider than the viewport.
        double viewport = BodyScroller.ActualWidth;
        double maxHoriz = System.Math.Max(0, _totalWidth - viewport);
        HScroll.Minimum      = 0;
        HScroll.Maximum      = maxHoriz;
        HScroll.ViewportSize = System.Math.Max(1, viewport);
        HScroll.Visibility   = maxHoriz > 0.5 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnVScrollValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEcho) return;
        // Value == top visible row → that's the focal row. Window is loaded starting AT
        // that row (no half-window offset that used to make Value=0 unreachable for rows 0..24).
        _pendingFocal = (int)e.NewValue;
        _focalDebounce.Stop();
        _focalDebounce.Start();
    }

    private void OnHScrollValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEcho) return;
        HeaderScroller.ScrollToHorizontalOffset(e.NewValue);
        BodyScroller.ScrollToHorizontalOffset(e.NewValue);
    }

    private void OnBodyScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 && !_suppressEcho)
        {
            HeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
            _suppressEcho = true;
            HScroll.Value = e.HorizontalOffset;
            _suppressEcho = false;
        }
        if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) UpdateScrollExtents();
    }

    /// <summary>Programmatic scroll to a given file row — used when the VM resets focal.</summary>
    public void ScrollToRow(int absoluteRow)
    {
        _suppressEcho = true;
        VScroll.Value = System.Math.Max(0, System.Math.Min(VScroll.Maximum, absoluteRow));
        _suppressEcho = false;
    }
}
