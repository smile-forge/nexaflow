using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.IO.Pe;

namespace Nexaflow.Features.Executable.Controls;

/// <summary>
/// A horizontal strip showing entropy across the whole file, one column per sample.
/// <para>
/// Drawn rather than charted because the shape is the message: a uniform mid band is ordinary code,
/// a flat low run is padding, and a solid high block is compressed or encrypted data. A packed
/// binary is recognisable here at a glance, before any number is read.
/// </para>
/// <para>
/// Colours come from feature-owned theme tokens through dependency properties, so a theme retunes
/// the ramp without this control knowing anything about it. The literals are last-resort fallbacks
/// for a theme that somehow supplies none.
/// </para>
/// </summary>
public sealed class EntropyHeatmap : FrameworkElement
{
    public static readonly DependencyProperty BucketsProperty =
        DependencyProperty.Register(nameof(Buckets), typeof(IReadOnlyList<double>), typeof(EntropyHeatmap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowBrushProperty =
        DependencyProperty.Register(nameof(LowBrush), typeof(Brush), typeof(EntropyHeatmap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRampChanged));

    public static readonly DependencyProperty MidBrushProperty =
        DependencyProperty.Register(nameof(MidBrush), typeof(Brush), typeof(EntropyHeatmap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRampChanged));

    public static readonly DependencyProperty HighBrushProperty =
        DependencyProperty.Register(nameof(HighBrush), typeof(Brush), typeof(EntropyHeatmap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRampChanged));

    public static readonly DependencyProperty ThresholdBrushProperty =
        DependencyProperty.Register(nameof(ThresholdBrush), typeof(Brush), typeof(EntropyHeatmap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Invoked with the zero-based sample index when the strip is clicked. The strip is a map of the
    /// file, so a click on a suspicious band should be able to go straight to those bytes.
    /// </summary>
    public static readonly DependencyProperty BucketCommandProperty =
        DependencyProperty.Register(nameof(BucketCommand), typeof(ICommand), typeof(EntropyHeatmap),
            new PropertyMetadata(null));

    public ICommand? BucketCommand
    {
        get => (ICommand?)GetValue(BucketCommandProperty);
        set => SetValue(BucketCommandProperty, value);
    }

    public IReadOnlyList<double>? Buckets
    {
        get => (IReadOnlyList<double>?)GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public Brush? LowBrush
    {
        get => (Brush?)GetValue(LowBrushProperty);
        set => SetValue(LowBrushProperty, value);
    }

    public Brush? MidBrush
    {
        get => (Brush?)GetValue(MidBrushProperty);
        set => SetValue(MidBrushProperty, value);
    }

    public Brush? HighBrush
    {
        get => (Brush?)GetValue(HighBrushProperty);
        set => SetValue(HighBrushProperty, value);
    }

    public Brush? ThresholdBrush
    {
        get => (Brush?)GetValue(ThresholdBrushProperty);
        set => SetValue(ThresholdBrushProperty, value);
    }

    /// <summary>Per-column brushes, rebuilt only when the ramp changes rather than per paint.</summary>
    private Brush[]? _ramp;

    private static void OnRampChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntropyHeatmap)d)._ramp = null;

    public EntropyHeatmap()
    {
        Cursor = Cursors.Cross;
        // A FrameworkElement with no background is not hit-testable; the strip needs to be.
        Focusable = false;
    }

    /// <summary>Painting a transparent backdrop is what makes the whole strip clickable, including
    /// the empty space above a short bar.</summary>
    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        RenderStrip(context);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        int count = Buckets?.Count ?? 0;
        if (count == 0 || ActualWidth <= 0 || BucketCommand is null) return;

        int index = (int)(e.GetPosition(this).X / ActualWidth * count);
        index = Math.Clamp(index, 0, count - 1);

        if (BucketCommand.CanExecute(index)) BucketCommand.Execute(index);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var buckets = Buckets;
        if (buckets is null || buckets.Count == 0 || ActualWidth <= 0) return;

        int index = Math.Clamp((int)(e.GetPosition(this).X / ActualWidth * buckets.Count), 0, buckets.Count - 1);
        ToolTip = $"Sample {index + 1} of {buckets.Count} — {buckets[index]:F2} bits/byte" +
                  (BucketCommand is null ? "" : "\nClick to view these bytes in the hex editor");
    }

    /// <summary>Draws the strip itself: one column per sample, height and colour both driven by
    /// entropy so the two reinforce each other.</summary>
    private void RenderStrip(DrawingContext context)
    {
        var buckets = Buckets;
        double width = ActualWidth, height = ActualHeight;
        if (buckets is null || buckets.Count == 0 || width <= 0 || height <= 0) return;

        var ramp = _ramp ??= BuildRamp();

        // One rectangle per column, snapped so a 512-sample strip in a 400px panel has no seams.
        double columnWidth = width / buckets.Count;
        for (int i = 0; i < buckets.Count; i++)
        {
            double value = Math.Clamp(buckets[i] / 8.0, 0, 1);
            var brush = ramp[Math.Clamp((int)(value * (ramp.Length - 1)), 0, ramp.Length - 1)];

            double x     = i * columnWidth;
            double right = Math.Min(width, (i + 1) * columnWidth);
            // Scale the bar by entropy too, so height and colour reinforce each other.
            double bar   = Math.Max(1, height * value);
            context.DrawRectangle(brush, null, new Rect(x, height - bar, Math.Max(1, right - x), bar));
        }

        // The packing threshold, so "above this line is suspicious" is visible without a legend.
        if (ThresholdBrush is { } marker)
        {
            double y = height - height * (PeEntropy.PackedThreshold / 8.0);
            context.DrawRectangle(marker, null, new Rect(0, y, width, 1));
        }
    }

    private Brush[] BuildRamp()
    {
        var low  = ColorOf(LowBrush,  Color.FromRgb(0x2F, 0x6F, 0xB5));
        var mid  = ColorOf(MidBrush,  Color.FromRgb(0xC9, 0xA2, 0x27));
        var high = ColorOf(HighBrush, Color.FromRgb(0xC4, 0x46, 0x3A));

        const int steps = 64;
        var ramp = new Brush[steps];
        for (int i = 0; i < steps; i++)
        {
            double t = i / (double)(steps - 1);
            var colour = t < 0.5 ? Lerp(low, mid, t * 2) : Lerp(mid, high, (t - 0.5) * 2);
            var brush  = new SolidColorBrush(colour);
            brush.Freeze();
            ramp[i] = brush;
        }
        return ramp;
    }

    private static Color ColorOf(Brush? brush, Color fallback)
        => brush is SolidColorBrush solid ? solid.Color : fallback;

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
