using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Icons;

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

    public static readonly DependencyProperty RuntimeProperty =
        DependencyProperty.Register(nameof(Runtime),
            typeof(WorkspaceRuntime), typeof(RibbonBar));

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
    public WorkspaceRuntime? Runtime
    {
        get => (WorkspaceRuntime?)GetValue(RuntimeProperty);
        set => SetValue(RuntimeProperty, value);
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


    // ── Per-item elements ──────────────────────────────────────────────────
    //
    // The ribbon is drawn in every window and edited rarely, so each item's element is built once and kept.
    // Layout passes and window resizes re-parent the same buttons; the widths each arrangement takes are cached
    // until an item or the collection changes, so a resize that keeps the arrangement costs a comparison.

    private readonly Dictionary<RibbonItem, FrameworkElement> _elements = [];

    // Maps each direct child of ItemsPanel to the source RibbonItem(s) it represents.
    // A column that pairs two compact items maps to both.
    private readonly Dictionary<UIElement, List<RibbonItem>> _childItems = [];

    // Items that did not fit; their entries are built when the overflow popup opens.
    private readonly List<RibbonItem> _overflowItems = [];

    private bool? _shownCompact;
    private double? _preferredWidth, _compactWidth;
    private bool _layoutQueued;

    // Single live flyout for the pop-out style pickers, anchored to the right-clicked button.
    private Popup? _styleFlyout;

    public RibbonBar()
    {
        InitializeComponent();
        AllowDrop = true;
        DragOver  += RibbonBar_DragOver;
        Drop      += RibbonBar_Drop;
    }

    private FrameworkElement ElementFor(RibbonItem item)
    {
        if (_elements.TryGetValue(item, out var existing)) return existing;

        FrameworkElement element;
        if (item.Kind == RibbonItemKind.Separator)
        {
            var line = new Rectangle
            {
                Width             = 1,
                Margin            = new Thickness(4, 14, 4, 10),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            line.SetResourceReference(Shape.FillProperty, "BorderBrush");
            element = line;
        }
        else
        {
            // The menu's items are filled when it opens: an empty ContextMenu costs nothing until then.
            var button = new RibbonItemButton { Item = item, Tag = item, ContextMenu = new ContextMenu() };
            button.ContextMenuOpening += ItemButton_ContextMenuOpening;
            button.Click              += ItemButton_Click;
            element = button;
        }

        PropertyChangedEventManager.AddHandler(item, OnItemChanged, string.Empty);
        _elements[item] = element;
        return element;
    }

    /// <summary>A change that can move the layout (size, text, glyph, shape) drops the cached widths and lays out
    /// again once; a colour change is the button's alone.</summary>
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RibbonItem.Foreground) or nameof(RibbonItem.Background)
                           or nameof(RibbonItem.BorderColor) or nameof(RibbonItem.IsActive)) return;
        InvalidateLayout();
    }

    private void Forget(RibbonItem item)
    {
        if (!_elements.Remove(item, out var element)) return;
        PropertyChangedEventManager.RemoveHandler(item, OnItemChanged, string.Empty);
        (VisualTreeHelper.GetParent(element) as Panel)?.Children.Remove(element);
    }

    // ── Context menu ───────────────────────────────────────────────────────

    private void ItemButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RibbonItem item, ContextMenu: { } menu } anchor) return;
        menu.Items.Clear();
        FillItemContextMenu(menu, item, anchor);
    }

    private void FillItemContextMenu(ContextMenu menu, RibbonItem item, FrameworkElement anchor)
    {
        var openInNew = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.OpenInNewWindow") };
        openInNew.Click += (_, _) =>
        {
            if (OpenInNewWindowCommand?.CanExecute(item) == true)
                OpenInNewWindowCommand.Execute(item);
        };
        menu.Items.Add(openInNew);
        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.Rename") };
        rename.Click += (_, _) =>
            Shell?.ShowPrompt(
                Str.Get("Shell.Ribbon.Rename.Title"), Str.Get("Shell.Ribbon.Rename.Prompt"), item.Label,
                onConfirm: name =>
                {
                    name = name.Trim();
                    if (string.IsNullOrEmpty(name) || name == item.Label) return;
                    item.Label = name;   // PropertyChanged -> VM Save() -> ribbon.json + live-sync; the button re-reads it
                },
                onCancel: () => { });
        menu.Items.Add(rename);

        var recolour = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.Recolour") };
        recolour.Items.Add(ColourEntry(Str.Get("Shell.Ribbon.Menu.TextColour"),       item, anchor, RibbonColourSlot.Foreground));
        recolour.Items.Add(ColourEntry(Str.Get("Shell.Ribbon.Menu.BackgroundColour"), item, anchor, RibbonColourSlot.Background));
        recolour.Items.Add(ColourEntry(Str.Get("Shell.Ribbon.Menu.BorderColour"),     item, anchor, RibbonColourSlot.Border));
        menu.Items.Add(recolour);

        var changeIcon = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.ChangeIcon") };
        changeIcon.Click += (_, _) => ShowIconFlyout(item, anchor);
        menu.Items.Add(changeIcon);

        var shape = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.Shape") };
        foreach (var (value, name) in RibbonShapeNames.All())
        {
            var entry = new MenuItem { Header = name, IsCheckable = true, IsChecked = item.Shape == value };
            entry.Click += (_, _) => item.Shape = value;
            shape.Items.Add(entry);
        }
        menu.Items.Add(shape);

        var resize = new MenuItem { Header = item.IsHalf ? Str.Get("Shell.Ribbon.Menu.Grow") : Str.Get("Shell.Ribbon.Menu.Shrink") };
        resize.Click += (_, _) => item.IsHalf = !item.IsHalf;
        menu.Items.Add(resize);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = Str.Get("Shell.Ribbon.Menu.Delete") };
        delete.SetResourceReference(ForegroundProperty, "DangerBrush");
        delete.Click += (_, _) =>
        {
            if (DeleteItemCommand?.CanExecute(item) == true)
                DeleteItemCommand.Execute(item);
        };
        menu.Items.Add(delete);
    }

    private MenuItem ColourEntry(string header, RibbonItem item, FrameworkElement anchor, RibbonColourSlot slot)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => ShowColourFlyout(header, item, anchor, slot);
        return entry;
    }

    // ── Per-button style flyouts (the shared colour and icon pickers) ──────

    private void ShowColourFlyout(string title, RibbonItem item, FrameworkElement anchor, RibbonColourSlot slot)
    {
        var picker = new ColorPicker
        {
            Width            = 340,
            AutomationPrefix = "RibbonQuick_Colour",
            DefaultColor     = RibbonColourSlots.ThemeDefault(slot, TryFindResource),
        };
        picker.SetBinding(ColorPicker.ValueProperty,
            new Binding(RibbonColourSlots.PropertyName(slot)) { Source = item, Mode = BindingMode.TwoWay });
        // An outline colour with no outline would change nothing; the first one picked brings a thin outline.
        if (slot == RibbonColourSlot.Border)
            picker.ValueChanged += (_, _) =>
            {
                if (item.BorderWeight == RibbonBorderWeight.None && !item.BorderColor.IsDefault)
                    item.BorderWeight = RibbonBorderWeight.Thin;
            };

        var panel = new StackPanel();
        var caption = new TextBlock { Text = title, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        panel.Children.Add(caption);
        panel.Children.Add(picker);
        OpenFlyout(panel, anchor);
    }

    private void ShowIconFlyout(RibbonItem item, FrameworkElement anchor)
    {
        var picker = new IconPicker
        {
            Width            = 340,
            Height           = 320,
            AutomationPrefix = "RibbonQuick_Icons",
            SelectedIcon     = item.Icon,
        };
        picker.ViewModel.Picked += icon =>
        {
            item.Icon = icon;
            CloseFlyout();
        };
        OpenFlyout(picker, anchor);
        picker.Loaded += (_, _) => picker.FocusSearch();
    }

    private void OpenFlyout(UIElement content, FrameworkElement anchor)
    {
        CloseFlyout();

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(8),
            Child           = content
        };
        card.SetResourceReference(Border.BackgroundProperty, "DeepBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 16, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black
        };

        _styleFlyout = new Popup
        {
            Child              = card,
            PlacementTarget    = anchor,
            Placement          = PlacementMode.Bottom,
            StaysOpen          = false,
            AllowsTransparency = true,
            PopupAnimation     = PopupAnimation.Fade
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

        if (Runtime is { } ctx)
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

        if (Runtime is { } dropCtx)
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
        foreach (var item in rb._elements.Keys.ToList())
            rb.Forget(item);
        if (e.NewValue is ObservableCollection<RibbonItem> @new)
            @new.CollectionChanged += rb.Items_CollectionChanged;
        rb.RebuildItems();
    }

    private void Items_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        HashSet<RibbonItem> live = ItemsSource is null ? [] : new(ItemsSource);
        foreach (var item in _elements.Keys.Where(i => !live.Contains(i)).ToList())
            Forget(item);
        RebuildItems();
    }

    // ── Layout ─────────────────────────────────────────────────────────────

    private void RebuildItems()
    {
        InvalidateWidths();
        if (ItemsSource is null)
        {
            DetachAll();
            return;
        }

        Arrange(compact: false);
        QueueLayout();
    }

    private void InvalidateLayout()
    {
        InvalidateWidths();
        QueueLayout();
    }

    private void InvalidateWidths()
    {
        _preferredWidth = _compactWidth = null;
        _shownCompact   = null;
    }

    /// <summary>Several changes in one dispatcher turn lay out once.</summary>
    private void QueueLayout()
    {
        if (_layoutQueued) return;
        _layoutQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _layoutQueued = false;
            MeasureLayout();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MeasureLayout();
    }

    /// <summary>
    /// Three arrangements, each tried only if the one before does not fit:
    ///   1 — each item at its preferred size (<see cref="RibbonItem.IsHalf"/>);
    ///   2 — every item compact;
    ///   3 — compact, plus an overflow button for what still does not fit — only when the available width is at
    ///       least <see cref="MinWidthBeforeOverflow"/>.
    /// Each arrangement's width is measured once and cached until an item changes.
    /// </summary>
    private void MeasureLayout()
    {
        if (ItemsSource is null || ItemsSource.Count == 0) return;

        EditBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        OverflowBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double available = ActualWidth - EditBtn.DesiredSize.Width;
        if (available <= 0) return;

        _preferredWidth ??= WidthOf(compact: false);
        if (_preferredWidth <= available)
        {
            Arrange(compact: false);
            HideOverflow();
            return;
        }

        _compactWidth ??= WidthOf(compact: true);
        Arrange(compact: true);
        if (_compactWidth <= available || available < MinWidthBeforeOverflow)
        {
            // Fits — or too narrow for an overflow to be meaningful, so keep what shows.
            HideOverflow();
            return;
        }

        OverflowBtn.Visibility = Visibility.Visible;
        double usable = available - OverflowBtn.DesiredSize.Width;
        double used   = 0;
        _overflowItems.Clear();

        foreach (UIElement child in ItemsPanel.Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = child.DesiredSize.Width;
            if (used + w <= usable)
            {
                used += w;
                continue;
            }
            child.Visibility = Visibility.Collapsed;
            if (_childItems.TryGetValue(child, out var overflowItems))
                _overflowItems.AddRange(overflowItems);
        }
    }

    private double WidthOf(bool compact)
    {
        Arrange(compact);
        return MeasureChildrenTotal();
    }

    private void HideOverflow()
    {
        OverflowBtn.Visibility = Visibility.Collapsed;
        OverflowPopup.IsOpen   = false;
        _overflowItems.Clear();
        foreach (UIElement child in ItemsPanel.Children)
            child.Visibility = Visibility.Visible;
    }

    private void DetachAll()
    {
        foreach (var column in _childItems.Keys.OfType<StackPanel>())
            column.Children.Clear();
        ItemsPanel.Children.Clear();
        _childItems.Clear();
    }

    /// <summary>
    /// Puts the kept elements into <see cref="ItemsPanel"/> for one arrangement; a no-op when it already holds it.
    /// Consecutive compact items are paired into a shared vertical column so they stack top/bottom instead of
    /// sitting side-by-side. A lone compact item at the top of its column is top-aligned, not centred.
    /// When <paramref name="compact"/> is false each item uses its own <see cref="RibbonItem.IsHalf"/> preference;
    /// when true every button is compact.
    /// </summary>
    private void Arrange(bool compact)
    {
        if (ItemsSource is null || _shownCompact == compact) return;
        DetachAll();
        _shownCompact = compact;

        var items = ItemsSource.ToList();
        int i = 0;
        while (i < items.Count)
        {
            var item      = items[i];
            bool isCompact = compact || item.IsHalf;

            if (isCompact && item.Kind != RibbonItemKind.Separator)
            {
                // Look ahead: is the next item also compact (and not a separator)?
                bool hasNext = i + 1 < items.Count
                    && (compact || items[i + 1].IsHalf)
                    && items[i + 1].Kind != RibbonItemKind.Separator;

                // A pair stacks top + bottom in a column centred as a group, so it lines up with the full-height
                // buttons and neither half clips past the ribbon edge; a lone compact item is top-aligned.
                var column = new StackPanel
                {
                    Orientation       = Orientation.Vertical,
                    VerticalAlignment = hasNext ? VerticalAlignment.Center : VerticalAlignment.Top,
                    Margin            = new Thickness(2, 0, 2, 0)
                };
                var top = Place(items[i], compactButton: true, hasNext ? new Thickness(0, 0, 0, 2) : new Thickness(0, 4, 0, 2));
                column.Children.Add(top);
                if (hasNext)
                    column.Children.Add(Place(items[i + 1], compactButton: true, new Thickness(0)));

                _childItems[column] = hasNext ? [items[i], items[i + 1]] : [item];
                ItemsPanel.Children.Add(column);
                i += hasNext ? 2 : 1;
            }
            else
            {
                var el = Place(item, compactButton: false, new Thickness(2, 0, 2, 0));
                _childItems[el] = [item];
                ItemsPanel.Children.Add(el);
                i++;
            }
        }
    }

    private FrameworkElement Place(RibbonItem item, bool compactButton, Thickness buttonMargin)
    {
        var element = ElementFor(item);
        element.Visibility = Visibility.Visible;
        if (element is RibbonItemButton button)
        {
            button.IsCompact         = compactButton;
            button.Margin            = buttonMargin;
            button.VerticalAlignment = compactButton ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        }
        return element;
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

    // ── Overflow ───────────────────────────────────────────────────────────

    private UIElement BuildOverflowEntry(RibbonItem item)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new IconGlyph { Icon = item.Icon, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = item.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = sp,
            Style   = (Style)FindResource("RibbonOverflowEntry"),
            Tag     = item,
            Padding = new Thickness(8, 6, 8, 6)
        };
        AutomationProperties.SetAutomationId(btn, "RibbonOverflow_" + item.Label);
        btn.Click += (_, _) =>
        {
            OverflowPopup.IsOpen = false;
            RibbonActionCommand?.Execute(item);
        };
        return btn;
    }

    private void ItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RibbonItem item })
            RibbonActionCommand?.Execute(item);
    }

    private void OverflowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!OverflowPopup.IsOpen)
        {
            OverflowList.Children.Clear();
            foreach (var item in _overflowItems)
                OverflowList.Children.Add(BuildOverflowEntry(item));
        }
        OverflowPopup.IsOpen = !OverflowPopup.IsOpen;
    }

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
