using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nexaflow.Core.Themes;

/// <summary>
/// A self-contained bright, playful backdrop used by the Sunny theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.Window</c> template in <c>Theme.Sunny.xaml</c>; carries no
/// dependency on the shell or any feature. A cheerful sky with a warm sun, drifting clouds, rising
/// colourful balloons and a sprinkle of confetti — "fun is here", but soft enough to sit behind
/// content. Procedural so it adapts to the region size; never participates in hit-testing.
/// </summary>
public partial class SunnyScene : UserControl
{
    private static readonly Color[] Playful =
    [
        Color.FromRgb(0xFF, 0x6B, 0x6B), // coral
        Color.FromRgb(0xFF, 0xD2, 0x3F), // yellow
        Color.FromRgb(0x4D, 0xA8, 0xFF), // sky blue
        Color.FromRgb(0x3D, 0xD6, 0x8C), // green
        Color.FromRgb(0xB1, 0x7D, 0xFF), // purple
        Color.FromRgb(0xFF, 0x9F, 0x45), // orange
        Color.FromRgb(0xFF, 0x7E, 0xB6), // pink
    ];

    // Frame-rate cap for the *ambient* layers only — the big, (near-)stationary decorative fills.
    // Halving their update rate is a real saving on those large blended surfaces yet imperceptible,
    // since the eye doesn't track them. Travelling sprites are NOT capped: DesiredFrameRate decouples a
    // clock from vsync, so any sub-refresh rate reads as judder on motion the eye follows.
    private const int AmbientFrameRate = 30;

    private readonly Random _rng = new();
    private bool _built;
    private readonly DispatcherTimer _resizeDebounce;

    // Every animation runs through a controllable clock we retain, so a rebuild can stop the previous
    // set (otherwise the cleared elements' Forever clocks keep ticking on the UI thread until GC) and so
    // the whole scene can be paused when the host window is minimised.
    private readonly List<ClockController> _clocks = new();
    private Window? _host;

    public SunnyScene()
    {
        InitializeComponent();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Build(); };
        SizeChanged += OnSizeChanged;
        Loaded      += OnLoaded;
        Unloaded    += OnUnloaded;
    }

    // Pause the scene only when the window is genuinely hidden (minimised) — NOT merely unfocused, so a
    // window parked on a second monitor keeps animating while the user works elsewhere.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = Window.GetWindow(this);
        if (_host is null) return;
        _host.StateChanged -= OnHostStateChanged;   // idempotent across Loaded/Unloaded cycles
        _host.StateChanged += OnHostStateChanged;
        ApplyPauseState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _resizeDebounce.Stop();
        if (_host is not null) _host.StateChanged -= OnHostStateChanged;
        _host = null;
        StopClocks();
        _built = false;
        Layer.Children.Clear();
    }

    private void OnHostStateChanged(object? sender, EventArgs e) => ApplyPauseState();

    private void ApplyPauseState()
    {
        bool minimised = _host?.WindowState == WindowState.Minimized;
        foreach (var c in _clocks)
        {
            if (minimised) c.Pause();
            else           c.Resume();
        }
    }

    // Detach every live clock so the TimeManager stops ticking them immediately.
    private void StopClocks()
    {
        foreach (var c in _clocks) c.Remove();
        _clocks.Clear();
    }

    // First layout builds immediately; later resizes rebuild once the size settles, so the
    // procedural elements re-fit the region instead of staying pinned to the original size.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_built) { Build(); return; }
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void Build()
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;
        _built = true;

        double w = ActualWidth, h = ActualHeight, area = w * h;
        StopClocks();
        Layer.Children.Clear();

        AddSun(w, h);
        AddClouds(w, h, Math.Clamp((int)(w / 520.0), 2, 4));
        AddConfetti(w, h, Math.Clamp((int)(area / 45_000.0), 18, 40));
        AddBalloons(w, h, Math.Clamp((int)(area / 120_000.0), 8, 16));

        ApplyPauseState();
    }

    // ── Sun: a warm glow in the corner that gently breathes ────────────────────
    private void AddSun(double w, double h)
    {
        double d = Math.Min(w, h) * 0.9;
        var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(235, 0xFF, 0xF4, 0xC2), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0xFF, 0xE6, 0x8C), 0.32));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xE6, 0x8C), 1));
        rg.Freeze();

        var sun = new Ellipse { Width = d, Height = d, Fill = rg };
        Canvas.SetLeft(sun, w - d * 0.62);
        Canvas.SetTop(sun, -d * 0.34);
        Layer.Children.Add(sun);
        Loop(sun, OpacityProperty, 0.82, 1.0, 6, 0, AmbientFrameRate);
    }

    // ── Clouds: soft white puffs drifting slowly across ───────────────────────
    private void AddClouds(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var cloud = new Canvas { Opacity = 0.85 };
            double s = 0.8 + _rng.NextDouble() * 0.8;
            void Puff(double cx, double cy, double r)
            {
                var rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(235, 0xFF, 0xFF, 0xFF), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(180, 0xFF, 0xFF, 0xFF), 0.6));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1));
                rg.Freeze();
                var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = rg };
                Canvas.SetLeft(e, cx - r);
                Canvas.SetTop(e, cy - r);
                cloud.Children.Add(e);
            }
            Puff(50 * s, 34 * s, 34 * s);
            Puff(96 * s, 30 * s, 40 * s);
            Puff(146 * s, 36 * s, 30 * s);
            Puff(96 * s, 50 * s, 30 * s);

            double baseY = h * (0.06 + 0.4 * _rng.NextDouble());
            Canvas.SetTop(cloud, baseY);
            var tt = new TranslateTransform();
            cloud.RenderTransform = tt;
            Canvas.SetLeft(cloud, -200);
            Layer.Children.Add(cloud);

            double dur = 70 + _rng.Next(60);
            var drift = new DoubleAnimation(0, w + 260, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = TimeSpan.FromSeconds(_rng.NextDouble() * dur),
            };
            Animate(tt, TranslateTransform.XProperty, drift, AmbientFrameRate);
        }
    }

    // ── Confetti: small colourful flecks drifting down, twirling ───────────────
    private void AddConfetti(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double size = 5 + _rng.Next(7);
            var fleck = new Rectangle
            {
                Width   = size,
                Height  = size * (0.5 + _rng.NextDouble() * 0.6),
                RadiusX = 1.5, RadiusY = 1.5,
                Fill    = Frozen(new SolidColorBrush(Playful[_rng.Next(Playful.Length)])),
                Opacity = 0,
            };
            var rot = new RotateTransform(_rng.Next(360), fleck.Width / 2, fleck.Height / 2);
            var tt  = new TranslateTransform();
            fleck.RenderTransform = new TransformGroup { Children = { rot, tt } };
            Canvas.SetLeft(fleck, _rng.NextDouble() * w);
            Canvas.SetTop(fleck, -20);
            Layer.Children.Add(fleck);

            double dur = 9 + _rng.Next(9);
            var begin = TimeSpan.FromSeconds(_rng.NextDouble() * dur);
            var fall = new DoubleAnimation(0, h + 40, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = begin,
            };
            Animate(tt, TranslateTransform.YProperty, fall);
            Loop(tt, TranslateTransform.XProperty, -16, 16, 2.5 + _rng.NextDouble() * 2, _rng.NextDouble());

            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.5 + _rng.NextDouble() * 2)))
            { RepeatBehavior = RepeatBehavior.Forever };
            Animate(rot, RotateTransform.AngleProperty, spin);

            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = new Duration(TimeSpan.FromSeconds(dur)) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.12)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.82)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            Animate(fleck, OpacityProperty, fade);
        }
    }

    // ── Balloons: colourful balloons rising, swaying, fading in and out ────────
    private void AddBalloons(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var color = Playful[i % Playful.Length];
            double scale = 0.95 + _rng.NextDouble() * 0.85;
            var balloon = MakeBalloon(color);

            var move = new TranslateTransform();
            balloon.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), move } };
            Canvas.SetLeft(balloon, _rng.NextDouble() * w);
            Canvas.SetTop(balloon, h + 40);
            Layer.Children.Add(balloon);

            double dur = 26 + _rng.Next(22);
            var begin = TimeSpan.FromSeconds(_rng.NextDouble() * dur);
            var rise = new DoubleAnimation(0, -(h + 120), new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = begin,
            };
            Animate(move, TranslateTransform.YProperty, rise);
            Loop(move, TranslateTransform.XProperty, -14, 14, 3 + _rng.NextDouble() * 2.5, _rng.NextDouble());

            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = new Duration(TimeSpan.FromSeconds(dur)) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromPercent(0.12)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromPercent(0.8)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            Animate(balloon, OpacityProperty, fade);
        }
    }

    private static FrameworkElement MakeBalloon(Color color)
    {
        var c = new Canvas { Width = 40, Height = 60 };

        var body = new RadialGradientBrush { GradientOrigin = new Point(0.36, 0.3), Center = new Point(0.5, 0.45) };
        body.GradientStops.Add(new GradientStop(Lighten(color, 0.45), 0));
        body.GradientStops.Add(new GradientStop(color, 0.6));
        body.GradientStops.Add(new GradientStop(Darken(color, 0.12), 1));
        body.Freeze();

        var bulb = new Ellipse { Width = 36, Height = 44, Fill = body };
        Canvas.SetLeft(bulb, 2);
        Canvas.SetTop(bulb, 2);

        var knot = new Polygon
        {
            Points = [new Point(20, 44), new Point(16, 50), new Point(24, 50)],
            Fill   = Frozen(new SolidColorBrush(Darken(color, 0.1))),
        };
        var stringLine = new Path
        {
            Data = Frozen(Geometry.Parse("M20,50 Q24,56 19,60")),
            Stroke = Frozen(new SolidColorBrush(Color.FromArgb(120, 0x55, 0x55, 0x55))),
            StrokeThickness = 1,
        };
        var shine = new Ellipse { Width = 8, Height = 12, Fill = Frozen(new SolidColorBrush(Color.FromArgb(150, 0xFF, 0xFF, 0xFF))) };
        Canvas.SetLeft(shine, 10);
        Canvas.SetTop(shine, 9);

        c.Children.Add(bulb);
        c.Children.Add(knot);
        c.Children.Add(stringLine);
        c.Children.Add(shine);
        return c;
    }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R + (255 - c.R) * f, 0, 255),
        (byte)Math.Clamp(c.G + (255 - c.G) * f, 0, 255),
        (byte)Math.Clamp(c.B + (255 - c.B) * f, 0, 255));

    private static Color Darken(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * (1 - f), 0, 255),
        (byte)Math.Clamp(c.G * (1 - f), 0, 255),
        (byte)Math.Clamp(c.B * (1 - f), 0, 255));

    // ── Shared: a forever, auto-reversing eased oscillation ────────────────────
    private void Loop(IAnimatable target, DependencyProperty prop,
                      double from, double to, double seconds, double beginSeconds, int? fps = null)
        => Animate(target, prop, new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime      = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        }, fps);

    // Start the animation through a controllable clock (retained so the scene can be stopped on rebuild
    // or paused on minimise). An optional fps caps the update rate — passed only for ambient layers.
    private void Animate(IAnimatable target, DependencyProperty prop, AnimationTimeline anim, int? fps = null)
    {
        if (fps is int rate) Timeline.SetDesiredFrameRate(anim, rate);
        var clock = anim.CreateClock();
        target.ApplyAnimationClock(prop, clock);
        if (clock.Controller is { } controller) _clocks.Add(controller);
    }

    // Freeze a set-once brush/geometry so WPF can share it with the render thread and skip change
    // tracking. Everything here is immutable after creation (only opacity/transforms animate).
    private static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
