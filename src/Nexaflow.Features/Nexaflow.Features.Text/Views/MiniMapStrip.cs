using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Text.Views;

/// <summary>
/// A thin vertical strip drawn to the right of the editor that shows proportional
/// positions of search matches as gold tick marks.
/// </summary>
public sealed class MiniMapStrip : FrameworkElement
{
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(
            nameof(Background),
            typeof(Brush),
            typeof(MiniMapStrip),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public static readonly DependencyProperty MarksProperty =
        DependencyProperty.Register(
            nameof(Marks),
            typeof(IReadOnlyList<double>),
            typeof(MiniMapStrip),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Marks
    {
        get => (IReadOnlyList<double>?)GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    private static readonly SolidColorBrush TickBrush =
        new(Color.FromArgb(220, 255, 200, 0));

    // Top inset keeps tick marks within the vertical scrollbar's thumb-travel region.
    private const double TopPadding = 8.0;

    public static readonly DependencyProperty BottomPaddingProperty =
        DependencyProperty.Register(
            nameof(BottomPadding),
            typeof(double),
            typeof(MiniMapStrip),
            new FrameworkPropertyMetadata(
                TopPadding,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Bottom inset in device-independent pixels. Increase by the horizontal scrollbar
    /// height when it becomes visible so tick marks stay within the thumb-travel region.
    /// </summary>
    public double BottomPadding
    {
        get => (double)GetValue(BottomPaddingProperty);
        set => SetValue(BottomPaddingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Background is not null)
            dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var marks = Marks;
        if (marks is null || marks.Count == 0) return;

        var bottom = BottomPadding;
        var h      = ActualHeight - TopPadding - bottom;
        var w      = ActualWidth;
        foreach (var mark in marks)
        {
            var y = TopPadding + mark * h;
            dc.DrawRectangle(TickBrush, null, new Rect(0, Math.Clamp(y - 1, TopPadding, TopPadding + h - 2), w, 2));
        }
    }
}
