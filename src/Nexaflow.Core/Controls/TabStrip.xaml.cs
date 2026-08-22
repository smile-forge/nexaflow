using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nexaflow.Core.Converters;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Controls;

public partial class TabStrip : UserControl
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty TabsProperty =
        DependencyProperty.Register(nameof(Tabs),
            typeof(ObservableCollection<Page>), typeof(TabStrip),
            new PropertyMetadata(null, OnTabsChanged));

    public static readonly DependencyProperty ActivateTabCommandProperty =
        DependencyProperty.Register(nameof(ActivateTabCommand), typeof(ICommand), typeof(TabStrip));

    public static readonly DependencyProperty CloseTabCommandProperty =
        DependencyProperty.Register(nameof(CloseTabCommand), typeof(ICommand), typeof(TabStrip));

    public static readonly DependencyProperty PinTabToRibbonCommandProperty =
        DependencyProperty.Register(nameof(PinTabToRibbonCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>Invoked with the Page when a tab is torn off to the desktop.</summary>
    public static readonly DependencyProperty TearOffTabCommandProperty =
        DependencyProperty.Register(nameof(TearOffTabCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>Invoked with the Page when a tab from another window is dropped here.</summary>
    public static readonly DependencyProperty ReceiveTabCommandProperty =
        DependencyProperty.Register(nameof(ReceiveTabCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>The pane this strip renders — used as the command parameter for pane-scoped actions
    /// (split / close pane / focus-on-drop).</summary>
    public static readonly DependencyProperty PaneProperty =
        DependencyProperty.Register(nameof(Pane), typeof(ViewModels.Pane), typeof(TabStrip));

    /// <summary>"Split right" (parameter = the Page).</summary>
    public static readonly DependencyProperty SplitTabCommandProperty =
        DependencyProperty.Register(nameof(SplitTabCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>"Split" with an empty pane (no parameter).</summary>
    public static readonly DependencyProperty SplitEmptyCommandProperty =
        DependencyProperty.Register(nameof(SplitEmptyCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>"Close pane" (parameter = the Pane).</summary>
    public static readonly DependencyProperty ClosePaneCommandProperty =
        DependencyProperty.Register(nameof(ClosePaneCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>"Close except this" (parameter = the kept Page).</summary>
    public static readonly DependencyProperty CloseOthersCommandProperty =
        DependencyProperty.Register(nameof(CloseOthersCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>Marks this strip's pane the focused one (parameter = the Pane).</summary>
    public static readonly DependencyProperty PaneActivatedCommandProperty =
        DependencyProperty.Register(nameof(PaneActivatedCommand), typeof(ICommand), typeof(TabStrip));

    public ObservableCollection<Page>? Tabs
    {
        get => (ObservableCollection<Page>?)GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }
    public ICommand? ActivateTabCommand
    {
        get => (ICommand?)GetValue(ActivateTabCommandProperty);
        set => SetValue(ActivateTabCommandProperty, value);
    }
    public ICommand? CloseTabCommand
    {
        get => (ICommand?)GetValue(CloseTabCommandProperty);
        set => SetValue(CloseTabCommandProperty, value);
    }
    public ICommand? PinTabToRibbonCommand
    {
        get => (ICommand?)GetValue(PinTabToRibbonCommandProperty);
        set => SetValue(PinTabToRibbonCommandProperty, value);
    }
    public ICommand? TearOffTabCommand
    {
        get => (ICommand?)GetValue(TearOffTabCommandProperty);
        set => SetValue(TearOffTabCommandProperty, value);
    }
    public ICommand? ReceiveTabCommand
    {
        get => (ICommand?)GetValue(ReceiveTabCommandProperty);
        set => SetValue(ReceiveTabCommandProperty, value);
    }
    public ViewModels.Pane? Pane
    {
        get => (ViewModels.Pane?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }
    public ICommand? SplitTabCommand
    {
        get => (ICommand?)GetValue(SplitTabCommandProperty);
        set => SetValue(SplitTabCommandProperty, value);
    }
    public ICommand? SplitEmptyCommand
    {
        get => (ICommand?)GetValue(SplitEmptyCommandProperty);
        set => SetValue(SplitEmptyCommandProperty, value);
    }
    public ICommand? ClosePaneCommand
    {
        get => (ICommand?)GetValue(ClosePaneCommandProperty);
        set => SetValue(ClosePaneCommandProperty, value);
    }
    public ICommand? CloseOthersCommand
    {
        get => (ICommand?)GetValue(CloseOthersCommandProperty);
        set => SetValue(CloseOthersCommandProperty, value);
    }
    public ICommand? PaneActivatedCommand
    {
        get => (ICommand?)GetValue(PaneActivatedCommandProperty);
        set => SetValue(PaneActivatedCommandProperty, value);
    }

    public TabStrip()
    {
        InitializeComponent();
        AllowDrop = true;
        Drop      += TabStrip_Drop;
        DragOver  += TabStrip_DragOver;
        OverflowPopup.CustomPopupPlacementCallback = PlaceOverflowPopup;

        // Empty-area right-click → "Split" / "Close pane". Tab borders carry their own menu (built in
        // BuildTabElement) and take precedence when the click lands on a tab.
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnStripContextMenuOpening;
    }

    // Anchor the dropdown's right edge to the button's right edge so it opens inward and stays
    // inside the window (the overflow button sits at the right edge of the tab bar). Placement=Bottom
    // would anchor the left edge and spill the popup off the right of the screen.
    private static CustomPopupPlacement[] PlaceOverflowPopup(Size popupSize, Size targetSize, Point offset)
        => [ new CustomPopupPlacement(
                new Point(targetSize.Width - popupSize.Width, targetSize.Height),
                PopupPrimaryAxis.Horizontal) ];

    private static void OnTabsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ts = (TabStrip)d;
        if (e.OldValue is ObservableCollection<Page> old)
            old.CollectionChanged -= ts.Tabs_CollectionChanged;
        if (e.NewValue is ObservableCollection<Page> @new)
        {
            @new.CollectionChanged += ts.Tabs_CollectionChanged;
            ts.RebuildAndLayout();
        }
    }

    // Per-tab Title/Icon/IsActive are data-bound (see BuildTabElement + TabItemBorderStyle),
    // so a collection change only needs to add/remove the tab elements and re-measure overflow —
    // no per-page PropertyChanged subscription to keep the labels in sync.
    private void Tabs_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        => RebuildAndLayout();

    private void RebuildAndLayout()
    {
        VisiblePanel.Children.Clear();
        OverflowList.Children.Clear();

        if (Tabs is null) return;

        foreach (var tab in Tabs)
            VisiblePanel.Children.Add(BuildTabElement(tab));

        AnimateStripInIfFirst();

        Dispatcher.InvokeAsync(MeasureOverflow, System.Windows.Threading.DispatcherPriority.Render);
    }

    // Fades the strip in the first time it populates (workspace open / session restore), not on every
    // incremental add/remove. Animating the panel — rather than each tab — survives the wholesale rebuild
    // burst a multi-tab open triggers, so the tabs ease in together instead of popping. Resets when the
    // strip empties, so the next open animates again. Opacity only: never affects overflow measurement.
    private bool _tabsAnimatedIn;
    private void AnimateStripInIfFirst()
    {
        if (VisiblePanel.Children.Count == 0) { _tabsAnimatedIn = false; return; }
        if (_tabsAnimatedIn) return;
        _tabsAnimatedIn = true;
        VisiblePanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void VisiblePanel_SizeChanged(object sender, SizeChangedEventArgs e) { /* unused — we use UserControl SizeChanged */ }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MeasureOverflow();
    }

    private void MeasureOverflow()
    {
        if (Tabs is null) return;

        OverflowList.Children.Clear();

        // The available width is this UserControl's width (StackPanel ActualWidth tracks
        // its content and is therefore useless as a container-width measure).
        double available = ActualWidth;
        if (available <= 0) return;

        // Temporarily make all tabs visible and measure them.
        foreach (UIElement child in VisiblePanel.Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        double totalWidth = VisiblePanel.Children
            .Cast<UIElement>()
            .Sum(c => c.DesiredSize.Width);

        if (totalWidth <= available)
        {
            // Everything fits — show all, hide button
            OverflowBtn.Visibility = Visibility.Collapsed;
            return;
        }

        // Measure the overflow button so we can reserve its width.
        OverflowBtn.Visibility = Visibility.Visible;
        OverflowBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double btnWidth = OverflowBtn.DesiredSize.Width;
        double usable   = available - btnWidth;
        double used     = 0;

        foreach (UIElement child in VisiblePanel.Children)
        {
            double w = child.DesiredSize.Width;
            if (used + w <= usable)
            {
                child.Visibility = Visibility.Visible;
                used += w;
            }
            else
            {
                child.Visibility = Visibility.Collapsed;
                if (child is Border b && b.Tag is Page tab)
                    OverflowList.Children.Add(BuildOverflowItem(tab));
            }
        }
    }

    private Border BuildTabElement(Page tab)
    {
        // Inner content — Title/Icon are bound to the Page so they track changes automatically.
        var icon  = new TextBlock { FontSize = 13, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(Page.Icon)));

        // Shortened for the strip; the full name goes on the border below so hovering anywhere on the tab
        // reveals it, not just the few pixels of text. Both bind the Page's real Title, so a rename flows
        // through and nothing downstream (quick-open, ribbon pinning, session capture, AI context) ever sees
        // the shortened form.
        var label = new TextBlock { FontSize = 12 };
        label.SetBinding(TextBlock.TextProperty,
            new Binding(nameof(Page.Title)) { Converter = new TabTitleConverter() });

        var closeBtn = new TextBlock
        {
            Text     = "✕",
            FontSize = 12,
            Opacity  = 0,
            Margin   = new Thickness(6, 0, 0, 0),
            Cursor   = Cursors.Hand,
            Foreground = (Brush)FindResource("TextMutedBrush")
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(closeBtn);

        // DataContext = the Page drives the active/hover styling via TabItemBorderStyle's triggers.
        var border = new TabBorder
        {
            Child       = row,
            Tag         = tab,
            DataContext = tab,
            Style       = (Style)FindResource("TabItemBorderStyle")
        };
        // Null when nothing was hidden, which is how WPF is told to show no tooltip at all — one repeating
        // the label the user can already read is noise.
        border.SetBinding(ToolTipProperty,
            new Binding(nameof(Page.Title)) { Converter = new TabTitleTooltipConverter() });

        AutomationProperties.SetAutomationId(border, $"TabItem_{tab.PageKind}");
        AutomationProperties.SetAutomationId(closeBtn, $"CloseTab_{tab.PageKind}");

        // Close button reveals on tab hover; brightens on its own hover. Pure interaction —
        // the tab's background/foreground come from the style triggers above.
        border.MouseEnter += (_, _) => closeBtn.Opacity = 1;
        border.MouseLeave += (_, _) => closeBtn.Opacity = 0;
        closeBtn.MouseEnter += (_, _) =>
        {
            closeBtn.Foreground = (Brush)FindResource("TextBrush");
            closeBtn.Opacity    = 1;
        };
        closeBtn.MouseLeave += (_, _) => closeBtn.Foreground = (Brush)FindResource("TextMutedBrush");

        // Drag to ribbon — begin drag after a small movement while LMB is held.
        // Activation happens on mouse-UP (not down) so the drag-arm closure is still
        // alive when an inactive tab is clicked: if we activated on down, BringToFront
        // → Tabs.Move → RebuildAndLayout would recreate all borders and the old closure's
        // _dragArmed flag would be lost before MouseMove could ever fire.
        Point _dragStart  = default;
        bool  _dragArmed  = false;
        bool  _isDragging = false;

        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.Source == closeBtn) return;
            _dragStart  = e.GetPosition(border);
            _dragArmed  = true;
            _isDragging = false;
        };

        border.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_dragArmed && !_isDragging)
                ActivateTabCommand?.Execute(tab);
            _dragArmed  = false;
            _isDragging = false;
        };

        border.MouseMove += (_, e) =>
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var pos  = e.GetPosition(border);
            var diff = pos - _dragStart;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragArmed  = false;
            _isDragging = true;

            // The desktop isn't a WPF drop target, so DoDragDrop reports None there
            // and WPF would draw the "no-drop" cursor — yet dropping on the desktop is a
            // valid tear-off. Suppress the default cursors and always show a move cursor:
            // every outcome (re-dock to a strip, or tear off) is a Move.
            void GiveFeedback(object _, GiveFeedbackEventArgs fe)
            {
                fe.UseDefaultCursors = false;
                Mouse.SetCursor(Cursors.Hand);
                fe.Handled = true;
            }

            border.GiveFeedback += GiveFeedback;
            var data = new DataObject(typeof(Page), tab);
            try
            {
                DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
            }
            finally
            {
                border.GiveFeedback -= GiveFeedback;
            }

            _isDragging = false;

            // After the drag completes, check if it was dropped on the desktop
            // (i.e. not over any registered window).
            var cursorPos = WindowManager.GetCursorScreenPos();
            if (!WindowManager.IsPointOverAnyWindow(cursorPos))
            {
                // Only tear off if it's still in our tab list (wasn't transferred)
                if (Tabs?.Contains(tab) == true && TearOffTabCommand?.CanExecute(tab) == true)
                    TearOffTabCommand.Execute(tab);
            }
        };

        // Close
        closeBtn.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CloseTabCommand?.Execute(tab);
        };

        // Right-click menu — rebuilt on each open so the split / close-pane items reflect the live pane state.
        var menu = new ContextMenu();
        border.ContextMenu = menu;
        border.ContextMenuOpening += (_, _) => PopulateTabMenu(menu, tab);

        return border;
    }

    // ── Context menus ─────────────────────────────────────────────────────

    private void PopulateTabMenu(ContextMenu menu, Page tab)
    {
        menu.Items.Clear();

        int splitGroup = menu.Items.Count;
        AddIfExecutable(menu, "Split right",       SplitTabCommand,    tab);
        AddIfExecutable(menu, "Close except this", CloseOthersCommand, tab);
        if (menu.Items.Count > splitGroup) menu.Items.Add(new Separator());

        AddPinItem(menu, tab);
        menu.Items.Add(MakeItem("Open in New Window", TearOffTabCommand, tab));
        AddIfExecutable(menu, "Close pane", ClosePaneCommand, Pane);
    }

    // Strip background (not on a tab): offer "Split" when unsplit, "Close pane" when split.
    private void OnStripContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (IsWithinTab(e.OriginalSource)) return;   // a tab was clicked — its own menu handles it

        var menu = ContextMenu!;
        menu.Items.Clear();
        AddIfExecutable(menu, "Split",      SplitEmptyCommand, null);
        AddIfExecutable(menu, "Close pane", ClosePaneCommand,  Pane);
        if (menu.Items.Count == 0) e.Handled = true;  // nothing applicable → suppress the empty menu
    }

    private static bool IsWithinTab(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is TabBorder) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void AddPinItem(ContextMenu menu, Page tab)
    {
        var item = new MenuItem { Header = "Pin to Ribbon" };
        item.Click += (_, _) =>
        {
            var req = new TabPinRequest(tab);
            if (PinTabToRibbonCommand?.CanExecute(req) == true)
                PinTabToRibbonCommand.Execute(req);
        };
        menu.Items.Add(item);
    }

    private static void AddIfExecutable(ContextMenu menu, string header, ICommand? command, object? param)
    {
        if (command?.CanExecute(param) == true)
            menu.Items.Add(MakeItem(header, command, param));
    }

    private static MenuItem MakeItem(string header, ICommand? command, object? param)
        => new() { Header = header, Command = command, CommandParameter = param };

    private UIElement BuildOverflowItem(Page tab)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = tab.Icon,  FontSize = 13, Margin = new Thickness(0,0,8,0) });
        sp.Children.Add(new TextBlock { Text = tab.Title, FontSize = 12 });

        var btn = new Button
        {
            Content  = sp,
            Style    = (Style)FindResource("RibbonOverflowEntry"),   // stretches full width, content left-aligned
            Padding  = new Thickness(8, 6, 8, 6)
        };
        btn.Click += (_, _) =>
        {
            OverflowPopup.IsOpen = false;
            ActivateTabCommand?.Execute(tab);
        };
        return btn;
    }

    private void OverflowBtn_Click(object sender, RoutedEventArgs e)
        => OverflowPopup.IsOpen = !OverflowPopup.IsOpen;

    // ── Accessibility ─────────────────────────────────────────────────────

    /// Border with an automation peer so tab items appear in the UIA control view.
    private sealed class TabBorder : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer()
            => new FrameworkElementAutomationPeer(this);
    }

    // ── Cross-window drop target ──────────────────────────────────────────

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Page)))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Page))) return;
        var tab = (Page)e.Data.GetData(typeof(Page));

        // If the tab already belongs to this strip, nothing to do
        if (Tabs?.Contains(tab) == true) return;

        // Focus this strip's pane first so the receive lands here — whether the tab comes from another
        // pane of this window (a cross-pane move) or from another window (a cross-window move).
        if (Pane is not null && PaneActivatedCommand?.CanExecute(Pane) == true)
            PaneActivatedCommand.Execute(Pane);

        if (ReceiveTabCommand?.CanExecute(tab) == true)
            ReceiveTabCommand.Execute(tab);

        e.Handled = true;
    }
}
