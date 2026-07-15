using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nexaflow.Core.Models;

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

    private IReadOnlyList<BreadcrumbSegment> _segs = [];

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
        ApplyLayout();
    }

    // ── Adaptive layout ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders the crumbs, compressing them when they exceed the available width:
    /// 1. shrink the currently-longest middle label (first/last are never shrunk) to its first
    ///    3 letters + …, then 2, then 1, picking the longest remaining each step;
    /// 2. if it still doesn't fit, collapse the middle to
    ///    <c>[first] › (long path) › [second-last] › [last]</c> so the user can always jump to the
    ///    root or up one level even though "(long path)" itself isn't clickable.
    /// </summary>
    private void ApplyLayout()
    {
        int n = _segs.Count;
        if (n == 0) { CrumbPanel.Children.Clear(); return; }

        double available = AvailableWidth();
        var levels    = new int[n];   // 0 = full; 1/2/3 = first 3/2/1 letters + …
        bool collapsed = false;

        while (true)
        {
            RenderState(levels, collapsed);

            if (available <= 0) return;                  // not laid out yet — SizeChanged will recompute
            if (MeasuredWidth() <= available) return;    // fits
            if (collapsed) return;                       // phase 2 is the final form

            int pick = LongestShrinkableMiddle(levels);
            if (pick >= 0) { levels[pick]++; continue; } // phase 1: shrink the longest middle crumb
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

    /// <summary>Index of the longest middle crumb that can still be shortened, or -1 if none.</summary>
    private int LongestShrinkableMiddle(int[] levels)
    {
        int best = -1, bestLen = -1;
        for (int i = 1; i < _segs.Count - 1; i++)
        {
            string shown = Truncate(_segs[i].Label, levels[i]);
            string next  = Truncate(_segs[i].Label, levels[i] + 1);
            if (levels[i] < 3 && next.Length < shown.Length && shown.Length > bestLen)
            {
                bestLen = shown.Length;
                best    = i;
            }
        }
        return best;
    }

    private static string Truncate(string label, int level)
    {
        if (level <= 0) return label;
        int keep = 4 - level;                       // 1 → 3 letters, 2 → 2, 3 → 1
        return label.Length <= keep ? label : label[..keep] + "…";
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void RenderState(int[] levels, bool collapsed)
    {
        CrumbPanel.Children.Clear();
        int n = _segs.Count;

        if (collapsed)
        {
            // [first] › (long path) › [second-last] › [last]
            CrumbPanel.Children.Add(BuildCrumb(_segs[0], _segs[0].Label, isActive: false));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCollapsedPlaceholder(_segs.Skip(1).Take(n - 3)));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(_segs[n - 2], _segs[n - 2].Label, isActive: false));
            AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(_segs[n - 1], _segs[n - 1].Label, isActive: true));
            return;
        }

        for (int i = 0; i < n; i++)
        {
            if (i > 0) AddSeparator();
            CrumbPanel.Children.Add(BuildCrumb(_segs[i], Truncate(_segs[i].Label, levels[i]), isActive: i == n - 1));
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
    private UIElement BuildCollapsedPlaceholder(IEnumerable<BreadcrumbSegment> hidden) =>
        new TextBlock
        {
            Text       = "(long path)",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize   = 12,
            FontStyle  = FontStyles.Italic,
            Margin     = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip    = string.Join("  ›  ", hidden.Select(h => h.Label))
        };

    private UIElement BuildCrumb(BreadcrumbSegment seg, string displayLabel, bool isActive)
    {
        var hasChildren = seg.Children.Count > 0;

        // Label + optional chevron. Text is the (possibly truncated) display label; the tooltip
        // always carries the full label so a shortened crumb is still legible on hover.
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
            ToolTip  = seg.Label
        };

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
}
