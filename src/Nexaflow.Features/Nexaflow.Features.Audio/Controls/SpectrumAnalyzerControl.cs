using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Audio.Controls;

/// <summary>
/// Draws the spectrum analyser bars directly via <see cref="OnRender"/>. Feed it band magnitudes
/// (0..1, low→high frequency) via <see cref="Bands"/>; each bar is filled with a vertical gradient
/// from <see cref="LowBrush"/> (bottom) to <see cref="HighBrush"/> (top). Colours come from the caller
/// (theme brushes) — the control hard-codes none. Mirrors <c>SparklineChart</c>.
/// </summary>
public sealed class SpectrumAnalyzerControl : FrameworkElement
{
    public static readonly DependencyProperty BandsProperty =
        DependencyProperty.Register(nameof(Bands), typeof(IReadOnlyList<float>), typeof(SpectrumAnalyzerControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowBrushProperty =
        DependencyProperty.Register(nameof(LowBrush), typeof(Brush), typeof(SpectrumAnalyzerControl),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighBrushProperty =
        DependencyProperty.Register(nameof(HighBrush), typeof(Brush), typeof(SpectrumAnalyzerControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Band magnitudes 0..1, low frequency first. Reassign each tick to repaint.</summary>
    public IReadOnlyList<float>? Bands
    {
        get => (IReadOnlyList<float>?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    /// <summary>Colour at the base of each bar.</summary>
    public Brush? LowBrush
    {
        get => (Brush?)GetValue(LowBrushProperty);
        set => SetValue(LowBrushProperty, value);
    }

    /// <summary>Colour at the top of each bar.</summary>
    public Brush? HighBrush
    {
        get => (Brush?)GetValue(HighBrushProperty);
        set => SetValue(HighBrushProperty, value);
    }

    private LinearGradientBrush? _barBrush;
    private Color _cachedLow, _cachedHigh;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var bands = Bands;
        if (bands is not { Count: > 0 }) return;

        var brush = BarBrush();
        int n = bands.Count;
        double gap = n > 64 ? 1 : 2;
        double barW = (w - gap * (n - 1)) / n;
        if (barW < 1) { barW = w / n; gap = 0; }

        for (int i = 0; i < n; i++)
        {
            double mag = Math.Clamp(bands[i], 0f, 1f);
            double bh = mag * h;
            if (bh < 1) continue;
            double x = i * (barW + gap);
            dc.DrawRectangle(brush, null, new Rect(x, h - bh, barW, bh));
        }
    }

    /// <summary>One frozen top→bottom gradient reused across bars/frames; rebuilt only when colours change.</summary>
    private Brush BarBrush()
    {
        var low  = (LowBrush  as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        var high = (HighBrush as SolidColorBrush)?.Color ?? Colors.White;

        if (_barBrush is null || low != _cachedLow || high != _cachedHigh)
        {
            _barBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),   // top
                EndPoint   = new Point(0, 1),   // bottom
                GradientStops =
                {
                    new GradientStop(high, 0),
                    new GradientStop(low, 1),
                },
            };
            _barBrush.Freeze();
            _cachedLow = low;
            _cachedHigh = high;
        }
        return _barBrush;
    }
}
