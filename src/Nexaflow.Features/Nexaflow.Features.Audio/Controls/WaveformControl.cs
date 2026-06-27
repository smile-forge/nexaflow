using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Audio.Controls;

/// <summary>
/// Draws a static peak-envelope waveform (mirrored about the mid-line) that doubles as a seek bar:
/// the portion left of <see cref="Progress"/> uses <see cref="PlayedBrush"/>, the rest
/// <see cref="UnplayedBrush"/>, with a playhead at the boundary. Clicking emits
/// <see cref="SeekRequested"/> with the 0..1 fraction clicked. Colours come from the caller (theme
/// brushes). The envelope geometry is cached and rebuilt only when the peaks or size change; only the
/// clip moves as playback advances.
/// </summary>
public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty =
        DependencyProperty.Register(nameof(Peaks), typeof(IReadOnlyList<float>), typeof(WaveformControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(WaveformControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayedBrushProperty =
        DependencyProperty.Register(nameof(PlayedBrush), typeof(Brush), typeof(WaveformControl),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnplayedBrushProperty =
        DependencyProperty.Register(nameof(UnplayedBrush), typeof(Brush), typeof(WaveformControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Peak envelope (0..1 per bucket), left→right.</summary>
    public IReadOnlyList<float>? Peaks
    {
        get => (IReadOnlyList<float>?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    /// <summary>Playback progress 0..1; splits played vs unplayed and positions the playhead.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush? PlayedBrush
    {
        get => (Brush?)GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    public Brush? UnplayedBrush
    {
        get => (Brush?)GetValue(UnplayedBrushProperty);
        set => SetValue(UnplayedBrushProperty, value);
    }

    /// <summary>Raised when the user clicks the waveform, carrying the 0..1 position they clicked.</summary>
    public event EventHandler<double>? SeekRequested;

    public WaveformControl() => Cursor = Cursors.Hand;

    private Geometry? _cache;
    private object? _cacheKey;
    private double _cacheW, _cacheH;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ActualWidth <= 0) return;
        double frac = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        SeekRequested?.Invoke(this, frac);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var peaks = Peaks;
        if (peaks is not { Count: > 0 }) return;

        var geo = EnvelopeGeometry(peaks, w, h);
        double progX = Math.Clamp(Progress, 0, 1) * w;

        if (UnplayedBrush is { } unplayed && progX < w)
        {
            dc.PushClip(new RectangleGeometry(new Rect(progX, 0, w - progX, h)));
            dc.DrawGeometry(unplayed, null, geo);
            dc.Pop();
        }

        if (PlayedBrush is { } played)
        {
            if (progX > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, progX, h)));
                dc.DrawGeometry(played, null, geo);
                dc.Pop();
            }
            dc.DrawRectangle(played, null, new Rect(Math.Max(0, progX - 0.5), 0, 1, h));
        }
    }

    private Geometry EnvelopeGeometry(IReadOnlyList<float> peaks, double w, double h)
    {
        if (_cache is not null && ReferenceEquals(_cacheKey, peaks) && _cacheW == w && _cacheH == h)
            return _cache;

        double mid = h / 2;
        int n = peaks.Count;
        double Step(int i) => n == 1 ? 0 : (double)i / (n - 1) * w;
        float P(int i) => Math.Clamp(peaks[i], 0f, 1f);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, mid - P(0) * mid), isFilled: true, isClosed: true);
            for (int i = 1; i < n; i++)
                ctx.LineTo(new Point(Step(i), mid - P(i) * mid), isStroked: false, isSmoothJoin: false);
            for (int i = n - 1; i >= 0; i--)
                ctx.LineTo(new Point(Step(i), mid + P(i) * mid), isStroked: false, isSmoothJoin: false);
        }
        geo.Freeze();

        _cache = geo;
        _cacheKey = peaks;
        _cacheW = w;
        _cacheH = h;
        return geo;
    }
}
