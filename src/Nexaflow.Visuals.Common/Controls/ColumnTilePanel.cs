using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Lays tiles out in as many equal columns as the width allows — one when narrow, more as it widens — each tile going
/// to the shortest column so a tall tile does not leave holes beside it. A tile marked <see cref="IsWideProperty"/>
/// takes a row of its own across every column, below everything placed before it.
/// </summary>
/// <remarks>
/// Order is kept: tiles fill left to right, top to bottom, in the order they are given, so collapsing one moves the
/// tiles after it up rather than shuffling the page.
/// </remarks>
public sealed class ColumnTilePanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(ColumnTilePanel),
        new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ColumnTilePanel),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Set on a child (or its item container) to give it the full width.</summary>
    public static readonly DependencyProperty IsWideProperty = DependencyProperty.RegisterAttached(
        "IsWide", typeof(bool), typeof(ColumnTilePanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetIsWide(DependencyObject element) => (bool)element.GetValue(IsWideProperty);
    public static void SetIsWide(DependencyObject element, bool value) => element.SetValue(IsWideProperty, value);

    /// <summary>The narrowest a column may be before the panel uses one column fewer.</summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>The gap between columns and between tiles.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    Rect[] _placed = [];

    /// <summary>How many columns <paramref name="width"/> holds.</summary>
    public int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return 1;
        return Math.Max(1, (int)((width + Spacing) / (MinColumnWidth + Spacing)));
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? MinColumnWidth : available.Width;
        double height = Layout(width, measure: true);
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Layout(final.Width, measure: false);
        for (int i = 0; i < InternalChildren.Count && i < _placed.Length; i++) InternalChildren[i].Arrange(_placed[i]);
        return final;
    }

    /// <summary>Places every visible child; measures them first when <paramref name="measure"/>. Returns the height used.</summary>
    double Layout(double width, bool measure)
    {
        int columns = ColumnsFor(width);
        double spacing = Spacing, column = (width - spacing * (columns - 1)) / columns;
        var heights = new double[columns];
        _placed = new Rect[InternalChildren.Count];

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed) continue;

            bool wide = GetIsWide(child) || columns == 1;
            double childWidth = wide ? width : column;
            if (measure) child.Measure(new Size(childWidth, double.PositiveInfinity));
            double childHeight = child.DesiredSize.Height;

            if (wide)
            {
                double top = heights.Max();
                _placed[i] = new Rect(0, top, width, childHeight);
                Array.Fill(heights, top + childHeight + spacing);
            }
            else
            {
                int shortest = Array.IndexOf(heights, heights.Min());
                _placed[i] = new Rect(shortest * (column + spacing), heights[shortest], column, childHeight);
                heights[shortest] += childHeight + spacing;
            }
        }
        return Math.Max(0, heights.Max() - spacing);
    }
}
