using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    public BreadcrumbBar() => InitializeComponent();

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
        {
            @new.CollectionChanged += b.Segments_Changed;
            b.Rebuild();
        }
    }

    private void Segments_Changed(object? s, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        CrumbPanel.Children.Clear();
        if (Segments is null) return;

        for (int i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];

            // Separator
            if (i > 0)
                CrumbPanel.Children.Add(new TextBlock
                {
                    Text       = "›",
                    Foreground = (Brush)FindResource("TextDimBrush"),
                    FontSize   = 12,
                    Margin     = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

            bool isLast = i == Segments.Count - 1;
            CrumbPanel.Children.Add(BuildCrumb(seg, isLast));
        }
    }

    private UIElement BuildCrumb(BreadcrumbSegment seg, bool isActive)
    {
        var hasChildren = seg.Children.Count > 0;

        // Label + optional chevron
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(new TextBlock
        {
            Text       = seg.Label,
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
