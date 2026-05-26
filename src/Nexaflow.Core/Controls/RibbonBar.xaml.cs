using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

public partial class RibbonBar : UserControl
{
    private const double MinWidthBeforeOverflow = 500;

    // ── Dependency properties ──────────────────────────────────────────────

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource),
            typeof(ObservableCollection<RibbonItem>), typeof(RibbonBar),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty RibbonActionCommandProperty =
        DependencyProperty.Register(nameof(RibbonActionCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty EditClickCommandProperty =
        DependencyProperty.Register(nameof(EditClickCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty PinTabToRibbonCommandProperty =
        DependencyProperty.Register(nameof(PinTabToRibbonCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty OpenInNewWindowCommandProperty =
        DependencyProperty.Register(nameof(OpenInNewWindowCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty DeleteItemCommandProperty =
        DependencyProperty.Register(nameof(DeleteItemCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty PinFromHandlerCommandProperty =
        DependencyProperty.Register(nameof(PinFromHandlerCommand),
            typeof(ICommand), typeof(RibbonBar));

    public static readonly DependencyProperty WorkContextProperty =
        DependencyProperty.Register(nameof(WorkContext),
            typeof(WorkContext), typeof(RibbonBar));

    public ObservableCollection<RibbonItem>? ItemsSource
    {
        get => (ObservableCollection<RibbonItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public ICommand? RibbonActionCommand
    {
        get => (ICommand?)GetValue(RibbonActionCommandProperty);
        set => SetValue(RibbonActionCommandProperty, value);
    }
    public ICommand? EditClickCommand
    {
        get => (ICommand?)GetValue(EditClickCommandProperty);
        set => SetValue(EditClickCommandProperty, value);
    }
    public ICommand? PinTabToRibbonCommand
    {
        get => (ICommand?)GetValue(PinTabToRibbonCommandProperty);
        set => SetValue(PinTabToRibbonCommandProperty, value);
    }
    public ICommand? OpenInNewWindowCommand
    {
        get => (ICommand?)GetValue(OpenInNewWindowCommandProperty);
        set => SetValue(OpenInNewWindowCommandProperty, value);
    }
    public ICommand? DeleteItemCommand
    {
        get => (ICommand?)GetValue(DeleteItemCommandProperty);
        set => SetValue(DeleteItemCommandProperty, value);
    }
    public WorkContext? WorkContext
    {
        get => (WorkContext?)GetValue(WorkContextProperty);
        set => SetValue(WorkContextProperty, value);
    }
    public ICommand? PinFromHandlerCommand
    {
        get => (ICommand?)GetValue(PinFromHandlerCommandProperty);
        set => SetValue(PinFromHandlerCommandProperty, value);
    }

    private ContextMenu BuildItemContextMenu(RibbonItem item)
    {
        var menu = new ContextMenu();

        var openInNew = new MenuItem { Header = "Open in new Window" };
        openInNew.Click += (_, _) =>
        {
            if (OpenInNewWindowCommand?.CanExecute(item) == true)
                OpenInNewWindowCommand.Execute(item);
        };
        menu.Items.Add(openInNew);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) =>
        {
            if (DeleteItemCommand?.CanExecute(item) == true)
                DeleteItemCommand.Execute(item);
        };
        menu.Items.Add(delete);

        return menu;
    }

    // Maps each direct child of ItemsPanel to the source RibbonItem(s) it represents.
    // A column that pairs two compact items maps to both.
    private readonly Dictionary<UIElement, List<RibbonItem>> _childItems = [];

    public RibbonBar()
    {
        InitializeComponent();
        AllowDrop = true;
        DragOver  += RibbonBar_DragOver;
        Drop      += RibbonBar_Drop;
    }

    // ── Drag-and-drop ──────────────────────────────────────────────────────

    private void RibbonBar_DragOver(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(EditBtn);
        if (pos.X >= 0 && pos.X <= EditBtn.ActualWidth)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(Page)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (WorkContext is { } ctx)
        {
            foreach (var h in FeatureManager.Instance.GetRibbonPinHandlers(ctx))
            {
                if (e.Data.GetDataPresent(h.ContentKind))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void RibbonBar_Drop(object sender, DragEventArgs e)
    {
        int insertAt = ComputeInsertIndex(e.GetPosition(ItemsPanel));

        if (e.Data.GetData(typeof(Page)) is Page tab)
        {
            PinTabToRibbonCommand?.Execute(new TabPinRequest(tab, insertAt));
            e.Handled = true;
            return;
        }

        if (WorkContext is { } dropCtx)
        {
            foreach (var h in FeatureManager.Instance.GetRibbonPinHandlers(dropCtx))
            {
                if (e.Data.GetData(h.ContentKind) is { } payload)
                {
                    PinFromHandlerCommand?.Execute(new RibbonPinRequest(h.ContentKind, payload, insertAt));
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private int ComputeInsertIndex(Point posInPanel)
    {
        if (ItemsSource is null || ItemsPanel.Children.Count == 0)
            return -1;

        for (int i = 0; i < ItemsPanel.Children.Count; i++)
        {
            var child     = ItemsPanel.Children[i];
            var transform = child.TransformToAncestor(ItemsPanel);
            var origin    = transform.Transform(new Point(0, 0));
            double mid    = origin.X + child.RenderSize.Width / 2;
            if (posInPanel.X < mid)
            {
                // Return the source index of the first item this child represents.
                if (_childItems.TryGetValue(child, out var sourceItems) && sourceItems.Count > 0)
                    return ItemsSource.IndexOf(sourceItems[0]);
                return i;
            }
        }
        return -1;
    }

    // ── Collection change ──────────────────────────────────────────────────

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var rb = (RibbonBar)d;
        if (e.OldValue is ObservableCollection<RibbonItem> old)
            old.CollectionChanged -= rb.Items_CollectionChanged;
        if (e.NewValue is ObservableCollection<RibbonItem> @new)
        {
            @new.CollectionChanged += rb.Items_CollectionChanged;
            rb.RebuildItems();
        }
    }

    private void Items_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        => RebuildItems();

    // ── Layout ─────────────────────────────────────────────────────────────

    private void RebuildItems()
    {
        if (ItemsSource is null)
        {
            ItemsPanel.Children.Clear();
            return;
        }

        RebuildWithCompact(forceAllCompact: false);
        Dispatcher.InvokeAsync(MeasureLayout, System.Windows.Threading.DispatcherPriority.Render);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MeasureLayout();
    }

    /// <summary>
    /// Two-pass layout:
    ///   Pass 1 — each item rendered at its preferred size (<see cref="RibbonItem.IsHalf"/>).
    ///            If everything fits, done.
    ///   Pass 2 — all items forced compact regardless of preference.
    ///            If everything fits, done.
    ///   Pass 3 — compact items + overflow button for items that don't fit,
    ///            but only when available width >= <see cref="MinWidthBeforeOverflow"/>.
    /// </summary>
    private void MeasureLayout()
    {
        if (ItemsSource is null || ItemsPanel.Children.Count == 0) return;

        EditBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        OverflowBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double editWidth     = EditBtn.DesiredSize.Width;
        double overflowWidth = OverflowBtn.DesiredSize.Width;
        double available     = ActualWidth - editWidth;

        if (available <= 0) return;

        // ── Pass 1: preferred sizes ───────────────────────────────────────
        RebuildWithCompact(forceAllCompact: false);
        double pass1Total = MeasureChildrenTotal();

        if (pass1Total <= available)
        {
            OverflowBtn.Visibility = Visibility.Collapsed;
            OverflowList.Children.Clear();
            return;
        }

        // ── Pass 2: all compact ───────────────────────────────────────────
        RebuildWithCompact(forceAllCompact: true);
        double pass2Total = MeasureChildrenTotal();

        if (pass2Total <= available)
        {
            OverflowBtn.Visibility = Visibility.Collapsed;
            OverflowList.Children.Clear();
            return;
        }

        // ── Pass 3: compact + overflow ────────────────────────────────────
        if (available < MinWidthBeforeOverflow)
            return; // too narrow for overflow to be meaningful — keep what's visible

        OverflowBtn.Visibility = Visibility.Visible;
        double usable = available - overflowWidth;
        double used   = 0;
        OverflowList.Children.Clear();

        foreach (UIElement child in ItemsPanel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = child.DesiredSize.Width;
            if (used + w <= usable)
            {
                child.Visibility = Visibility.Visible;
                used += w;
            }
            else
            {
                child.Visibility = Visibility.Collapsed;
                if (_childItems.TryGetValue(child, out var overflowItems))
                    foreach (var oi in overflowItems)
                        OverflowList.Children.Add(BuildOverflowEntry(oi));
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="ItemsPanel"/> children.
    /// Consecutive compact items are paired into a shared vertical column so they
    /// stack top/bottom instead of sitting side-by-side. A lone compact item at
    /// the top of its column is top-aligned, not centred.
    /// When <paramref name="forceAllCompact"/> is false each item uses its own
    /// <see cref="RibbonItem.IsHalf"/> preference; when true every button is compact.
    /// </summary>
    private void RebuildWithCompact(bool forceAllCompact)
    {
        if (ItemsSource is null) return;
        ItemsPanel.Children.Clear();
        _childItems.Clear();

        var items = ItemsSource.ToList();
        int i = 0;
        while (i < items.Count)
        {
            var item    = items[i];
            bool compact = forceAllCompact || item.IsHalf;

            if (compact && item.Kind != RibbonItemKind.Separator)
            {
                // Look ahead: is the next item also compact (and not a separator)?
                bool hasNext = i + 1 < items.Count
                    && (forceAllCompact || items[i + 1].IsHalf)
                    && items[i + 1].Kind != RibbonItemKind.Separator;

                if (hasNext)
                {
                    // Pair: stack top + bottom in a vertical column.
                    var col = new StackPanel
                    {
                        Orientation       = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Margin            = new Thickness(2, 0, 2, 0)
                    };
                    col.Children.Add(BuildCompactButton(items[i],   topHalf: true));
                    col.Children.Add(BuildCompactButton(items[i+1], topHalf: false));
                    _childItems[col] = [items[i], items[i+1]];
                    ItemsPanel.Children.Add(col);
                    i += 2;
                }
                else
                {
                    // Lone compact — top-aligned.
                    var col = new StackPanel
                    {
                        Orientation       = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin            = new Thickness(2, 0, 2, 0)
                    };
                    col.Children.Add(BuildCompactButton(item, topHalf: true));
                    _childItems[col] = [item];
                    ItemsPanel.Children.Add(col);
                    i++;
                }
            }
            else
            {
                var el = BuildElement(item, compact: false);
                _childItems[el] = [item];
                ItemsPanel.Children.Add(el);
                i++;
            }
        }
    }

    private double MeasureChildrenTotal()
    {
        double total = 0;
        foreach (UIElement child in ItemsPanel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            total += child.DesiredSize.Width;
        }
        return total;
    }

    // ── Element builders ───────────────────────────────────────────────────

    private UIElement BuildElement(RibbonItem item, bool compact)
    {
        return item.Kind switch
        {
            RibbonItemKind.Separator => new Rectangle
            {
                Width  = 1,
                Fill   = (Brush)FindResource("BorderBrush"),
                Margin = new Thickness(4, 14, 4, 10),
                VerticalAlignment = VerticalAlignment.Stretch
            },
            _ => compact ? BuildCompactButton(item) : BuildFullButton(item)
        };
    }

    private Brush ItemForeground(RibbonItem item)
    {
        if (item.AccentColor is { Length: > 0 } hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* fall through */ }
        }
        return (Brush)FindResource(item.IsActive ? "AccentBrush" : "TextMutedBrush");
    }

    private FrameworkElement BuildFullButton(RibbonItem item)
    {
        var fg = ItemForeground(item);
        var sp = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 10, 0, 2)
        };
        sp.Children.Add(new TextBlock
        {
            Text                = item.Icon,
            FontSize            = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = fg
        });
        sp.Children.Add(new TextBlock
        {
            Text                = item.Label,
            FontSize            = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = fg,
            Margin              = new Thickness(0, 4, 0, 0)
        });

        var btn = new Button
        {
            Content  = sp,
            Style    = (Style)FindResource("RibbonButton"),
            Tag      = item,
            ToolTip  = item.Label,
            Margin   = new Thickness(2, 0, 2, 0),
            MinWidth = 45,
            ContextMenu = BuildItemContextMenu(item)
        };
        btn.Click += FullBtn_Click;
        return btn;
    }

    private FrameworkElement BuildCompactButton(RibbonItem item, bool topHalf = false)
    {
        var fg      = ItemForeground(item);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text              = item.Icon,
            FontSize          = 16,
            Margin            = new Thickness(0, 0, 6, 0),
            Foreground        = fg,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text              = item.Label,
            FontSize          = 11,
            Foreground        = fg,
            VerticalAlignment = VerticalAlignment.Center
        });

        var btn = new Button
        {
            Content           = content,
            Style             = (Style)FindResource("RibbonHalfButton"),
            Tag               = item,
            ToolTip           = item.Label,
            Padding           = new Thickness(10, 4, 10, 4),
            Margin            = topHalf ? new Thickness(0, 4, 0, 2) : new Thickness(0, 0, 0, 4),
            MinWidth          = 45,
            VerticalAlignment = VerticalAlignment.Top,
            ContextMenu       = BuildItemContextMenu(item)
        };
        btn.Click += FullBtn_Click;
        return btn;
    }

    private UIElement BuildOverflowEntry(RibbonItem item)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = item.Icon, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = item.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = sp,
            Style   = (Style)FindResource("RibbonOverflowEntry"),
            Tag     = item,
            Padding = new Thickness(8, 6, 8, 6)
        };
        btn.Click += (_, _) =>
        {
            OverflowPopup.IsOpen = false;
            RibbonActionCommand?.Execute(item);
        };
        return btn;
    }

    private void FullBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RibbonItem item)
            RibbonActionCommand?.Execute(item);
    }

    private void OverflowBtn_Click(object sender, RoutedEventArgs e)
        => OverflowPopup.IsOpen = !OverflowPopup.IsOpen;

    private void EditBtn_Click(object sender, RoutedEventArgs e)
        => EditClickCommand?.Execute(null);

    /// <summary>
    /// Briefly flashes the visual element representing <paramref name="item"/> to signal
    /// that a duplicate drop was rejected.
    /// </summary>
    public void FlashItem(RibbonItem item)
    {
        foreach (var (child, items) in _childItems)
        {
            if (!items.Contains(item)) continue;

            var anim = new DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse    = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            child.BeginAnimation(UIElement.OpacityProperty, anim);
            break;
        }
    }
}

