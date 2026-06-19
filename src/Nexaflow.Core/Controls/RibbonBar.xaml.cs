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

    public static readonly DependencyProperty WorkspaceProperty =
        DependencyProperty.Register(nameof(Workspace),
            typeof(Workspace), typeof(RibbonBar));

    public static readonly DependencyProperty ShellProperty =
        DependencyProperty.Register(nameof(Shell),
            typeof(IShellServices), typeof(RibbonBar));

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
    public Workspace? Workspace
    {
        get => (Workspace?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }
    public ICommand? PinFromHandlerCommand
    {
        get => (ICommand?)GetValue(PinFromHandlerCommandProperty);
        set => SetValue(PinFromHandlerCommandProperty, value);
    }
    /// <summary>Shell services — used to route the Rename input through the window-level overlay.</summary>
    public IShellServices? Shell
    {
        get => (IShellServices?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    // Single live flyout for the pop-out colour/icon pickers, anchored to the right-clicked button.
    private Popup? _styleFlyout;

    private ContextMenu BuildItemContextMenu(RibbonItem item, FrameworkElement anchor)
    {
        var menu = new ContextMenu();

        var openInNew = new MenuItem { Header = "Open in new Window" };
        openInNew.Click += (_, _) =>
        {
            if (OpenInNewWindowCommand?.CanExecute(item) == true)
                OpenInNewWindowCommand.Execute(item);
        };
        menu.Items.Add(openInNew);

        // Separators are pure dividers — only buttons carry styling, so skip the style group for them.
        if (item.Kind != RibbonItemKind.Separator)
        {
            menu.Items.Add(new Separator());

            var rename = new MenuItem { Header = "Rename" };
            rename.Click += (_, _) =>
                Shell?.ShowPrompt(
                    "Rename button", "New name:", item.Label,
                    onConfirm: name =>
                    {
                        name = name.Trim();
                        if (string.IsNullOrEmpty(name) || name == item.Label) return;
                        item.Label = name;   // PropertyChanged -> VM Save() -> ribbon.json + live-sync
                        RebuildItems();      // button text is a literal, so re-render
                    },
                    onCancel: () => { });
            menu.Items.Add(rename);

            var recolour = new MenuItem { Header = "Recolour" };
            recolour.Click += (_, _) => ShowColourFlyout(item, anchor);
            menu.Items.Add(recolour);

            var changeIcon = new MenuItem { Header = "Change icon" };
            changeIcon.Click += (_, _) => ShowIconFlyout(item, anchor);
            menu.Items.Add(changeIcon);

            var resize = new MenuItem { Header = item.IsHalf ? "Grow" : "Shrink" };
            resize.Click += (_, _) => { item.IsHalf = !item.IsHalf; RebuildItems(); };
            menu.Items.Add(resize);
        }

        menu.Items.Add(new Separator());

        var delete = new MenuItem
        {
            Header     = "Delete",
            Foreground = (Brush)FindResource("DangerBrush")
        };
        delete.Click += (_, _) =>
        {
            if (DeleteItemCommand?.CanExecute(item) == true)
                DeleteItemCommand.Execute(item);
        };
        menu.Items.Add(delete);

        return menu;
    }

    // ── Per-button style flyouts (pop-out palette + icon grid) ─────────────

    private void ShowColourFlyout(RibbonItem item, FrameworkElement anchor)
    {
        var wrap = new WrapPanel { Width = 168 };
        wrap.Children.Add(BuildSwatch(item, null));   // "default" (no accent override)
        foreach (var key in RibbonStyleCatalog.SwatchKeys)
            if (SwatchHex(key) is { } hex)
                wrap.Children.Add(BuildSwatch(item, hex));
        OpenFlyout(wrap, anchor);
    }

    private Border BuildSwatch(RibbonItem item, string? hex)
    {
        Brush fill = hex is null
            ? (Brush)FindResource("TextMutedBrush")
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        var swatch = new Border
        {
            Width           = 22,
            Height          = 22,
            Margin          = new Thickness(3),
            CornerRadius    = new CornerRadius(11),
            Background      = fill,
            Cursor          = Cursors.Hand,
            ToolTip         = hex ?? "Default",
            BorderBrush     = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(item.AccentColor == hex ? 2 : 0)
        };
        swatch.MouseLeftButtonDown += (_, _) =>
        {
            item.AccentColor = hex;
            CloseFlyout();
            RebuildItems();
        };
        return swatch;
    }

    private void ShowIconFlyout(RibbonItem item, FrameworkElement anchor)
    {
        var wrap = new WrapPanel { Width = 238 };
        foreach (var icon in RibbonStyleCatalog.Icons)
        {
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text                = icon,
                    FontSize            = 18,
                    TextAlignment       = TextAlignment.Center,
                    Background          = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment   = VerticalAlignment.Stretch
                },
                Style   = (Style)FindResource("TopBarAction"),
                Width   = 34,
                Height  = 34,
                Padding = new Thickness(0),
                ToolTip = icon
            };
            btn.Click += (_, _) =>
            {
                item.Icon = icon;
                CloseFlyout();
                RebuildItems();
            };
            wrap.Children.Add(btn);
        }

        var scroll = new ScrollViewer
        {
            Content                       = wrap,
            MaxHeight                     = 200,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        OpenFlyout(scroll, anchor);
    }

    /// <summary>Resolve a swatch-bank key to its current-theme hex string, or null if absent.</summary>
    private string? SwatchHex(string key)
        => TryFindResource(key) is SolidColorBrush b ? b.Color.ToString() : null;

    private void OpenFlyout(UIElement content, FrameworkElement anchor)
    {
        CloseFlyout();

        var card = new Border
        {
            Background      = (Brush)FindResource("DeepBgBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(6),
            Child           = content
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 16, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black
        };

        _styleFlyout = new Popup
        {
            Child             = card,
            PlacementTarget   = anchor,
            Placement         = PlacementMode.Bottom,
            StaysOpen         = false,
            AllowsTransparency = true,
            PopupAnimation    = PopupAnimation.Fade
        };
        // Defer the open until the closing context menu has released the mouse/focus,
        // otherwise this StaysOpen=false popup is dismissed the instant it appears.
        var flyout = _styleFlyout;
        Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_styleFlyout, flyout)) flyout.IsOpen = true;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseFlyout()
    {
        if (_styleFlyout is null) return;
        _styleFlyout.IsOpen = false;
        _styleFlyout = null;
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

        if (Workspace is { } ctx)
        {
            foreach (var h in FeatureManager.Instance.GetRibbonPinHandlers(ctx))
                foreach (var fmt in h.AcceptedFormats)
                    if (e.Data.GetDataPresent(fmt))
                    {
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                        return;
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

        if (Workspace is { } dropCtx)
        {
            foreach (var h in FeatureManager.Instance.GetRibbonPinHandlers(dropCtx))
                foreach (var fmt in h.AcceptedFormats)
                    if (e.Data.GetData(fmt) is { } payload)
                    {
                        PinFromHandlerCommand?.Execute(new RibbonPinRequest(fmt, payload, insertAt));
                        e.Handled = true;
                        return;
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
                    // Pair: stack top + bottom in a vertical column, centred as a group so the pair
                    // lines up with the full-height buttons and neither half clips past the ribbon edge.
                    var col = new StackPanel
                    {
                        Orientation       = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(2, 0, 2, 0)
                    };
                    var top = BuildCompactButton(items[i],   topHalf: true);
                    var bot = BuildCompactButton(items[i+1], topHalf: false);
                    top.Margin = new Thickness(0, 0, 0, 2);   // 2px gap between the halves; no outer margin
                    bot.Margin = new Thickness(0, 0, 0, 0);
                    col.Children.Add(top);
                    col.Children.Add(bot);
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

    private const int MaxLabelChars = 14;

    /// <summary>Visible button caption: truncated with an ellipsis past <see cref="MaxLabelChars"/>;
    /// the full label still lives on the button's tooltip.</summary>
    private static string DisplayLabel(string label)
        => label.Length > MaxLabelChars ? label[..(MaxLabelChars - 1)] + "…" : label;

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
            // Trim 4 off the top (centred content nets ~2px higher) to ease it off the bottom edge.
            Margin              = new Thickness(0, 6, 0, 2)
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
            Text                = DisplayLabel(item.Label),
            FontSize            = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = fg,
            // 1px sides so a long label isn't crushed against the button edge.
            Margin              = new Thickness(1, 4, 1, 0)
        });

        var btn = new Button
        {
            Content  = sp,
            Style    = (Style)FindResource("RibbonButton"),
            Tag      = item,
            ToolTip  = item.Label,
            Margin   = new Thickness(2, 0, 2, 0),
            MinWidth = 45
        };
        btn.ContextMenu = BuildItemContextMenu(item, btn);
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
            Text              = DisplayLabel(item.Label),
            FontSize          = 11,
            Foreground        = fg,
            VerticalAlignment = VerticalAlignment.Center,
            // 1px sides so a long label isn't crushed against the button edge.
            Margin            = new Thickness(1, 0, 1, 0)
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
            VerticalAlignment = VerticalAlignment.Top
        };
        btn.ContextMenu = BuildItemContextMenu(item, btn);
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

