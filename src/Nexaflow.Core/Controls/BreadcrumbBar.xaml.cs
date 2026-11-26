using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Localization;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Core.Controls;

public partial class BreadcrumbBar : UserControl
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments),
            typeof(ObservableCollection<BreadcrumbSegment>), typeof(BreadcrumbBar),
            new PropertyMetadata(null, OnSegmentsChanged));

    public ObservableCollection<BreadcrumbSegment>? Segments
    {
        get => (ObservableCollection<BreadcrumbSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public static readonly DependencyProperty DisplayFiltersProperty =
        DependencyProperty.Register(nameof(DisplayFilters),
            typeof(IReadOnlyList<IBreadcrumbDisplayFilter>), typeof(BreadcrumbBar),
            new PropertyMetadata(null, (d, _) => ((BreadcrumbBar)d).Refresh()));

    /// <summary>The window's runtime's display filters, in stacking order: what restates each crumb before it is drawn
    /// (<see cref="IBreadcrumbDisplayFilter"/>). Null draws every crumb as its page stated it.</summary>
    public IReadOnlyList<IBreadcrumbDisplayFilter>? DisplayFilters
    {
        get => (IReadOnlyList<IBreadcrumbDisplayFilter>?)GetValue(DisplayFiltersProperty);
        set => SetValue(DisplayFiltersProperty, value);
    }

    private IReadOnlyList<BreadcrumbSegment> _segs = [];

    /// <summary>How each of <see cref="_segs"/> reads, once the filters have restated it — composed once per change,
    /// so laying out to fit only shortens.</summary>
    private IReadOnlyList<BreadcrumbDisplay> _displays = [];

    public BreadcrumbBar()
    {
        InitializeComponent();
        // Recompute compression when the available width changes. The bar is star-sized by its
        // host column, so its width is independent of the crumb content — no measure feedback loop.
        SizeChanged += (_, _) => ApplyLayout();
    }

    /// <summary>
    /// Raised when a breadcrumb segment requests opening a different tab.
    /// Arguments are the <see cref="PageKinds"/> constant and optional page parameters.
    /// The shell resolves which tab factory to use; the breadcrumb control stays decoupled from Page.
    /// </summary>
    public event Action<string, Dictionary<string, string>?>? OpenTabRequested;

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var b = (BreadcrumbBar)d;
        if (e.OldValue is ObservableCollection<BreadcrumbSegment> old)
            old.CollectionChanged -= b.Segments_Changed;
        if (e.NewValue is ObservableCollection<BreadcrumbSegment> @new)
            @new.CollectionChanged += b.Segments_Changed;

        // Refresh unconditionally so a null new value (e.g. the active page going away when the last
        // tab closes) clears the bar instead of leaving the previous tab's crumbs on screen.
        b.Refresh();
    }

    private void Segments_Changed(object? s, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        _segs = Segments?.ToList() ?? [];
        var filters = DisplayFilters ?? [];
        _displays = [.. _segs.Select(s => BreadcrumbDrawing.Compose(s, filters))];
        // Right-clicking the bar itself — a separator, the gap after the last crumb — answers with where you
        // are: the deepest crumb that names a location. Crumbs that name one of their own override this below.
        ContextMenu = CopyPathMenu(_displays.LastOrDefault(d => d.CopyText is not null)?.CopyText);
        ApplyLayout();
    }

    // ── Adaptive layout ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders the crumbs, compressing them when they exceed the available width:
    /// 1. shorten the currently-longest crumb that can be (<see cref="BreadcrumbDrawing.DeepestLevel"/>) one step —
    ///    a middle part of a many-part crumb elided, or a middle crumb cut to 3, 2, then 1 letters — picking the
    ///    longest remaining each step;
    /// 2. if it still doesn't fit, collapse the middle to
    ///    <c>[first] › (long path) › [second-last] › [last]</c> so the user can always jump to the
    ///    root or up one level even though "(long path)" itself isn't clickable.
    /// </summary>
    private void ApplyLayout()
    {
        int n = _segs.Count;
        if (n == 0) { CrumbPanel.Children.Clear(); return; }

        double available = AvailableWidth();
        var levels    = new int[n];   // 0 = whole; see BreadcrumbDrawing.Shortened
        bool collapsed = false;

        while (true)
        {
            RenderState(levels, collapsed);

            if (available <= 0) return;                  // not laid out yet — SizeChanged will recompute
            if (MeasuredWidth() <= available) return;    // fits
            if (collapsed) return;                       // phase 2 is the final form

            int pick = LongestShrinkable(levels);
            if (pick >= 0) { levels[pick]++; continue; } // phase 1: shorten the longest crumb that can be
            if (n >= 4)    { collapsed = true; continue; } // phase 2: collapse the middle
            return;                                      // nothing left to compress
        }
    }

    /// <summary>Content width available to the crumb row (host Border padding 16,0 + its border).</summary>
    private double AvailableWidth()
        => ActualWidth - 32 - (BorderThickness.Left + BorderThickness.Right);

    private double MeasuredWidth()
    {
        double total = 0;
        foreach (UIElement child in CrumbPanel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            total += child.DesiredSize.Width;
        }
        return total;
    }

    /// <summary>Index of the longest crumb that one more step would shorten, or -1 if none.</summary>
    private int LongestShrinkable(int[] levels)
    {
        int best = -1, bestLen = -1;
        for (int i = 0; i < _displays.Count; i++)
        {
            var display = _displays[i];
            if (levels[i] >= BreadcrumbDrawing.DeepestLevel(display, atEnd: i == 0 || i == _displays.Count - 1)) continue;
            string shown = BreadcrumbDrawing.Shortened(display, levels[i]);
            string next  = BreadcrumbDrawing.Shortened(display, levels[i] + 1);
            if (next.Length < shown.Length && shown.Length > bestLen)
            {
                bestLen = shown.Length;
                best    = i;
            }
        }
        return best;
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void RenderState(int[] levels, bool collapsed)
    {
        CrumbPanel.Children.Clear();
        int n = _segs.Count;

        if (collapsed)
        {
            // [first] › (long path) › [second-last] › [last]
            CrumbPanel.Children.Add(BuildCrumb(0, levels[0], isActive: false));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCollapsedPlaceholder(_displays.Skip(1).Take(n - 3)));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(n - 2, levels[n - 2], isActive: false));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(n - 1, levels[n - 1], isActive: true));
            return;
        }

        for (int i = 0; i < n; i++)
        {
            if (i > 0) AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(i, levels[i], isActive: i == n - 1));
        }
    }

    private void AddSeparator() =>
        CrumbPanel.Children.Add(new TextBlock
        {
            Text       = "›",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize   = 12,
            Margin     = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

    /// <summary>The non-clickable "(long path)" stand-in for the collapsed middle segments.</summary>
    private UIElement BuildCollapsedPlaceholder(IEnumerable<BreadcrumbDisplay> hidden) =>
        new TextBlock
        {
            Text       = Str.Get("Shell.Breadcrumb.LongPath"),
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize   = 12,
            FontStyle  = FontStyles.Italic,
            Margin     = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip    = string.Join("  ›  ", hidden.Select(h => h.Text))
        };

    /// <summary>The crumb at <paramref name="index"/>, its text shortened to <paramref name="level"/>.</summary>
    private UIElement BuildCrumb(int index, int level, bool isActive)
    {
        var seg         = _segs[index];
        var display     = _displays[index];
        var displayLabel = BreadcrumbDrawing.Shortened(display, level);
        var hasChildren = seg.Children.Count > 0;

        // Label + optional chevron. Text is the (possibly shortened) display label; the tooltip
        // always carries the whole of it so a shortened crumb is still legible on hover.
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(new TextBlock
        {
            Text       = displayLabel,
            FontSize   = 12,
            Foreground = (Brush)FindResource(isActive ? "TextBrush" : "TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (hasChildren)
            inner.Children.Add(new TextBlock
            {
                Text       = "▾",
                FontSize   = 9,
                Opacity    = 0.6,
                Margin     = new Thickness(3, 1, 0, 0),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

        var btn = new Button
        {
            Content  = inner,
            Style    = (Style)FindResource("RibbonButton"),
            Padding  = new Thickness(6, 2, 6, 2),
            ToolTip  = display.ToolTip
        };

        // Null when the crumb names no location — the right-click then bubbles to the bar's own menu.
        btn.ContextMenu = CopyPathMenu(display.CopyText);

        if (seg.TargetPageKind is { } pageKind)
        {
            btn.Click += (_, _) => OpenTabRequested?.Invoke(pageKind, seg.TargetPageParams);
            return btn;
        }

        if (seg.Navigate is { } navigate && !hasChildren)
        {
            btn.Click += (_, _) => navigate();
            return btn;
        }

        if (!hasChildren) return btn;

        // Dropdown popup
        var popup = new Popup
        {
            StaysOpen        = false,
            Placement        = PlacementMode.Bottom,
            PlacementTarget  = btn,
            AllowsTransparency = true
        };

        var popupBorder = new Border
        {
            Style         = (Style)FindResource("PopupBorder"),
            Padding       = new Thickness(8),
            MinWidth      = 180
        };
        var list = new ListBox
        {
            Style             = (Style)FindResource("DarkListBox"),
            ItemContainerStyle = (Style)FindResource("DarkListBoxItem")
        };
        foreach (var child in seg.Children)
        {
            list.Items.Add(new TextBlock
            {
                Text       = child,
                FontSize   = 12,
                Foreground = (Brush)FindResource("TextMutedBrush")
            });
        }
        popupBorder.Child = list;
        popup.Child       = popupBorder;

        btn.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        return btn;
    }

    // ── Copy path ────────────────────────────────────────────────────────────

    /// <summary>
    /// The <b>Copy path</b> menu for a crumb (or for the bar) that names a location, or <c>null</c> when it
    /// names none — a null <see cref="FrameworkElement.ContextMenu"/> lets the right-click bubble to the
    /// nearest ancestor that has one, which is how a pathless crumb still answers with the current location.
    /// </summary>
    private ContextMenu? CopyPathMenu(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var item = new MenuItem
        {
            Header           = Str.Get("Shell.Breadcrumb.CopyPath"),
            Command          = CopyPathCommand,
            CommandParameter = path,
        };
        AutomationProperties.SetAutomationId(item, "Breadcrumb_CopyPath");

        var menu = new ContextMenu();
        menu.Items.Add(item);
        return menu;
    }

    /// <summary>Copies the path it is handed, through the clipboard this bar's window offers.</summary>
    private ICommand CopyPathCommand => _copyPath ??= new RelayCommand<string>(path =>
    {
        if (string.IsNullOrEmpty(path)) return;
        // Another process can hold the clipboard open; failing to copy is not worth an error dialog.
        _ = ClipboardRouter.Write(this, ComposedPayload.OfText(path));
    });

    private ICommand? _copyPath;
}
