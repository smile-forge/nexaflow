using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Controls;

public partial class TabStrip : UserControl
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty TabsProperty =
        DependencyProperty.Register(nameof(Tabs),
            typeof(ObservableCollection<TabEntry>), typeof(TabStrip),
            new PropertyMetadata(null, OnTabsChanged));

    public static readonly DependencyProperty ActivateTabCommandProperty =
        DependencyProperty.Register(nameof(ActivateTabCommand), typeof(ICommand), typeof(TabStrip));

    public static readonly DependencyProperty CloseTabCommandProperty =
        DependencyProperty.Register(nameof(CloseTabCommand), typeof(ICommand), typeof(TabStrip));

    public static readonly DependencyProperty PinTabToRibbonCommandProperty =
        DependencyProperty.Register(nameof(PinTabToRibbonCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>Invoked with the TabEntry when a tab is torn off to the desktop.</summary>
    public static readonly DependencyProperty TearOffTabCommandProperty =
        DependencyProperty.Register(nameof(TearOffTabCommand), typeof(ICommand), typeof(TabStrip));

    /// <summary>Invoked with the TabEntry when a tab from another window is dropped here.</summary>
    public static readonly DependencyProperty ReceiveTabCommandProperty =
        DependencyProperty.Register(nameof(ReceiveTabCommand), typeof(ICommand), typeof(TabStrip));

    public ObservableCollection<TabEntry>? Tabs
    {
        get => (ObservableCollection<TabEntry>?)GetValue(TabsProperty);
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

    public TabStrip()
    {
        InitializeComponent();
        AllowDrop = true;
        Drop      += TabStrip_Drop;
        DragOver  += TabStrip_DragOver;
    }

    private static void OnTabsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ts = (TabStrip)d;
        if (e.OldValue is ObservableCollection<TabEntry> old)
            old.CollectionChanged -= ts.Tabs_CollectionChanged;
        if (e.NewValue is ObservableCollection<TabEntry> @new)
        {
            @new.CollectionChanged += ts.Tabs_CollectionChanged;
            ts.RebuildAndLayout();
        }
    }

    private void Tabs_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        // Move repositions a tab without removing it — both NewItems and OldItems reference
        // the same entry, so naively subscribing then unsubscribing would kill the handler.
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.NewItems is not null)
                foreach (TabEntry t in e.NewItems)
                    t.PropertyChanged += Tab_PropertyChanged;
            if (e.OldItems is not null)
                foreach (TabEntry t in e.OldItems)
                    t.PropertyChanged -= Tab_PropertyChanged;
        }

        RebuildAndLayout();
    }

    private void Tab_PropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabEntry.IsActive))
            Dispatcher.Invoke(RefreshActiveStates);
        else if (e.PropertyName == nameof(TabEntry.Title) && s is TabEntry changed)
            Dispatcher.Invoke(() => RefreshTabLabel(changed));
    }

    private void RefreshTabLabel(TabEntry tab)
    {
        foreach (UIElement child in VisiblePanel.Children)
        {
            if (child is Border b && b.Tag == tab &&
                b.Child is StackPanel sp)
            {
                foreach (UIElement c in sp.Children)
                    if (c is TextBlock tb && tb.Text != tab.Icon && tb.Text != "✕")
                    {
                        tb.Text = tab.Title;
                        break;
                    }
                break;
            }
        }
        // Also re-measure overflow since the width may have changed
        MeasureOverflow();
    }

    private void RefreshActiveStates()
    {
        for (int i = 0; i < VisiblePanel.Children.Count; i++)
        {
            if (VisiblePanel.Children[i] is Border b && b.Tag is TabEntry t)
                ApplyActiveStyle(b, t.IsActive);
        }
    }

    private void RebuildAndLayout()
    {
        VisiblePanel.Children.Clear();
        OverflowList.Children.Clear();

        if (Tabs is null) return;

        foreach (var tab in Tabs)
            VisiblePanel.Children.Add(BuildTabElement(tab));

        Dispatcher.InvokeAsync(MeasureOverflow, System.Windows.Threading.DispatcherPriority.Render);
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
                if (child is Border b && b.Tag is TabEntry tab)
                    OverflowList.Children.Add(BuildOverflowItem(tab));
            }
        }
    }

    private Border BuildTabElement(TabEntry tab)
    {
        // Inner content
        var icon = new TextBlock { Text = tab.Icon, FontSize = 13, Margin = new Thickness(0,0,6,0) };
        var label = new TextBlock { Text = tab.Title, FontSize = 12 };
        var closeBtn = new TextBlock
        {
            Text     = "✕",
            FontSize = 12,
            Opacity  = 0,
            Margin   = new Thickness(6, 0, 0, 0),
            Cursor   = Cursors.Hand,
            Foreground = (Brush)FindResource("TextDimBrush")
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(closeBtn);

        var border = new Border
        {
            Child   = row,
            Padding = new Thickness(12, 0, 12, 0),
            Tag     = tab,
            Cursor  = Cursors.Hand,
            CornerRadius = new CornerRadius(6, 6, 0, 0)
        };

        ApplyActiveStyle(border, tab.IsActive);

        // Hover
        border.MouseEnter += (_, _) =>
        {
            closeBtn.Opacity = 1;
            if (!tab.IsActive)
            {
                border.Background = (Brush)FindResource("Surface2Brush");
                label.Foreground  = (Brush)FindResource("TextBrush");
                icon.Foreground   = (Brush)FindResource("TextBrush");
            }
        };
        border.MouseLeave += (_, _) =>
        {
            closeBtn.Opacity = 0;
            ApplyActiveStyle(border, tab.IsActive);
        };

        // Close-button brightens on its own hover
        closeBtn.MouseEnter += (_, _) =>
        {
            closeBtn.Foreground = (Brush)FindResource("TextBrush");
            closeBtn.Opacity    = 1;
        };
        closeBtn.MouseLeave += (_, _) =>
        {
            closeBtn.Foreground = (Brush)FindResource("TextDimBrush");
            // keep opacity=1 while border is still hovered
        };

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

            var data = new DataObject(typeof(TabEntry), tab);
            DragDrop.DoDragDrop(border, data, DragDropEffects.Move);

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

        return border;
    }

    private void ApplyActiveStyle(Border b, bool active)
    {
        b.Background = active
            ? (Brush)FindResource("Surface2Brush")
            : Brushes.Transparent;

        if (b.Child is StackPanel sp)
        {
            var accentBrush = (Brush)FindResource(active ? "AccentBrush" : "TextMutedBrush");
            foreach (UIElement c in sp.Children)
            {
                if (c is TextBlock tb && tb.Text != "✕")
                    tb.Foreground = accentBrush;
            }
        }

        // Bottom accent line
        b.BorderThickness = new Thickness(0, 0, 0, 2);
        b.BorderBrush     = active ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
    }

    private UIElement BuildOverflowItem(TabEntry tab)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = tab.Icon,  FontSize = 13, Margin = new Thickness(0,0,8,0) });
        sp.Children.Add(new TextBlock { Text = tab.Title, FontSize = 12 });

        var btn = new Button
        {
            Content  = sp,
            Style    = (Style)FindResource("RibbonButton"),
            Padding  = new Thickness(8, 6, 8, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left
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

    // ── Cross-window drop target ──────────────────────────────────────────

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TabEntry)))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TabEntry))) return;
        var tab = (TabEntry)e.Data.GetData(typeof(TabEntry));

        // If the tab already belongs to this strip, nothing to do
        if (Tabs?.Contains(tab) == true) return;

        if (ReceiveTabCommand?.CanExecute(tab) == true)
            ReceiveTabCommand.Execute(tab);

        e.Handled = true;
    }
}
