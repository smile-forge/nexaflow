using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A tab control whose tabs run down a left-hand rail instead of across the top — the shape a
/// multi-section inspector page wants (process details, executable inspection) where each tab is a
/// whole panel rather than a peer document.
/// <para>
/// Defaults to <see cref="System.Windows.Controls.TabControl.TabStripPlacement"/> Left and, more
/// importantly, to a <see cref="Selector.SelectedValuePath"/> of <c>"Tag"</c>: sections are bound by
/// a stable tag string rather than by index, so reordering the tabs never silently re-points
/// anything keyed off the selection (scoped search, most of all).
/// </para>
/// </summary>
public class VerticalTabControl : TabControl
{
    /// <summary>Width of the tab rail. A <see cref="GridLength"/> so a caller can use Auto or a star.</summary>
    public static readonly DependencyProperty RailWidthProperty =
        DependencyProperty.Register(nameof(RailWidth), typeof(GridLength), typeof(VerticalTabControl),
            new FrameworkPropertyMetadata(new GridLength(150), FrameworkPropertyMetadataOptions.AffectsMeasure));

    static VerticalTabControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VerticalTabControl),
            new FrameworkPropertyMetadata(typeof(VerticalTabControl)));
    }

    public VerticalTabControl()
    {
        TabStripPlacement = Dock.Left;
        SelectedValuePath = "Tag";

        // Without this the selected tab's content is sized to itself rather than to the panel, so a
        // page's scrollbar ends up floating in the middle of the tab instead of at its right edge.
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment   = VerticalAlignment.Stretch;
    }

    public GridLength RailWidth
    {
        get => (GridLength)GetValue(RailWidthProperty);
        set => SetValue(RailWidthProperty, value);
    }

    /// <summary>
    /// Forces the selected-content host to stretch, whichever template ends up applied.
    /// <para>
    /// A <see cref="ContentPresenter"/> in a <see cref="ControlTemplate"/> aliases its alignment to
    /// the templated parent's <see cref="Control.HorizontalContentAlignment"/>, which
    /// <see cref="Control"/> defaults to <see cref="HorizontalAlignment.Left"/>. A page hosted in it
    /// is then measured at its natural width: star columns collapse to their content and page
    /// scrollbars float mid-panel instead of sitting at the right edge.
    /// </para>
    /// <para>
    /// Setting it in the template, or in <see cref="OnApplyTemplate"/>, is not enough — the alias is
    /// established <em>after</em> the parent applies its template, so it overwrites both. Re-applying
    /// once the tree is loaded is what actually sticks. Re-applied on selection change too, because
    /// the host is re-prepared for each newly selected tab.
    /// </para>
    /// </summary>
    private void StretchContentHost()
    {
        var host = GetTemplateChild("PART_SelectedContentHost") as FrameworkElement
                   ?? FindContentHost(this);
        if (host is null) return;

        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.VerticalAlignment   = VerticalAlignment.Stretch;
    }

    /// <summary>Fallback for a template that does not name its host the conventional way.</summary>
    private static ContentPresenter? FindContentHost(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ContentPresenter presenter) return presenter;
            if (FindContentHost(child) is { } found) return found;
        }
        return null;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StretchContentHost();

        // The alias lands after this method returns, so queue a second pass behind it.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StretchContentHost);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StretchContentHost);
    }

    protected override DependencyObject GetContainerForItemOverride() => new VerticalTabItem();

    /// <summary>Accepts any <see cref="TabItem"/> so hand-written tabs still work; generated
    /// containers are the styled <see cref="VerticalTabItem"/>.</summary>
    protected override bool IsItemItsOwnContainerOverride(object item) => item is TabItem;
}

/// <summary>
/// A tab in a <see cref="VerticalTabControl"/>: a full-width row with a selection accent bar down
/// its leading edge.
/// </summary>
public class VerticalTabItem : TabItem
{
    static VerticalTabItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VerticalTabItem),
            new FrameworkPropertyMetadata(typeof(VerticalTabItem)));
    }
}
