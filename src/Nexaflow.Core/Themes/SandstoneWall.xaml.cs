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
/// A self-contained cut-sandstone wall backdrop used by the Sandstone theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.Window</c> template in <c>Theme.Sandstone.xaml</c>; carries no
/// dependency on the shell or any feature. Lays an ashlar masonry of tone-varied blocks with recessed
/// mortar joints and lit top edges, then gives it life with a large warm sun glow that travels slowly
/// across the wall and a few soft shadows drifting over the stone. Procedural so it adapts to the
/// region size; never participates in hit-testing.
/// </summary>
public partial class SandstoneWall : UserControl
{
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

    public SandstoneWall()
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

        double w = ActualWidth, h = ActualHeight;
        StopClocks();
        Layer.Children.Clear();

        AddBlocks(w, h);
        AddSunSweep(w, h);
        AddShadowPlay(w, h);

        ApplyPauseState();
    }

    // ── Masonry: courses of tone-varied ashlar blocks, recessed joints, lit top edges ─
    private void AddBlocks(double w, double h)
    {
        var mortar    = Frozen(new SolidColorBrush(Color.FromArgb(165, 0x5A, 0x46, 0x30)));
        var highlight = Frozen(new SolidColorBrush(Color.FromArgb(70, 0xFF, 0xF2, 0xD4)));
        const double courseH = 78;
        int row = 0;

        for (double y = -courseH * 0.35; y < h; y += courseH, row++)
        {
            double offset = (row % 2 == 0) ? 0 : -(150 + _rng.Next(90));
            for (double x = offset; x < w;)
            {
                double bw = 140 + _rng.Next(120);
                // Natural stone tone variation around a warm sandstone base.
                int dv = _rng.Next(-20, 16);
                var fill = Color.FromRgb(
                    Clamp(0xC4 + dv + _rng.Next(-6, 6)),
                    Clamp(0xA8 + dv),
                    Clamp(0x7A + dv - _rng.Next(0, 9)));

                var block = new Rectangle
                {
                    Width  = bw + 1.5,
                    Height = courseH + 1.5,
                    Fill   = Frozen(new SolidColorBrush(fill)),
                    Stroke = mortar,
                    StrokeThickness = 1.5,
                };
                Canvas.SetLeft(block, x);
                Canvas.SetTop(block, y);
                Layer.Children.Add(block);

                // Sunlit top lip of the cut stone.
                var lip = new Rectangle { Width = Math.Max(0, bw - 4), Height = 1.5, Fill = highlight };
                Canvas.SetLeft(lip, x + 2);
                Canvas.SetTop(lip, y + 2.5);
                Layer.Children.Add(lip);

                x += bw;
            }
        }
    }

    // ── Sun sweep: a broad warm glow that travels slowly across the wall ───────
    private void AddSunSweep(double w, double h)
    {
        var rg = new RadialGradientBrush
        {
            Center         = new Point(0.5, 0.4),
            GradientOrigin = new Point(0.5, 0.35),
            RadiusX        = 0.6,
            RadiusY        = 0.85,
        };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(155, 0xFF, 0xE7, 0xB0), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(52, 0xFF, 0xD7, 0x8E), 0.5));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x8E), 1));
        rg.Freeze();

        var sun = new Rectangle { Width = w * 0.95, Height = h * 1.3, Fill = rg, RenderTransform = new TranslateTransform() };
        Canvas.SetLeft(sun, 0);
        Canvas.SetTop(sun, -h * 0.15);
        Layer.Children.Add(sun);

        // One slow drift right, then back — the sun progressing across the wall over ~75s each way.
        var sweep = new DoubleAnimation(-w * 0.42, w * 0.5, new Duration(TimeSpan.FromSeconds(75)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Animate(sun.RenderTransform, TranslateTransform.XProperty, sweep, AmbientFrameRate);
        Loop(sun, OpacityProperty, 0.72, 1.0, 19, 0, AmbientFrameRate);
    }

    // ── Shadow play: a few soft shadows drifting slowly over the stone ─────────
    private void AddShadowPlay(double w, double h)
    {
        for (int i = 0; i < 3; i++)
        {
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0x28, 0x1B, 0x0F), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x28, 0x1B, 0x0F), 1));
            rg.Freeze();

            double sw = w * (0.42 + _rng.NextDouble() * 0.3), sh = h * (0.42 + _rng.NextDouble() * 0.3);
            var shade = new Ellipse { Width = sw, Height = sh, Fill = rg, Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(shade, _rng.NextDouble() * w - sw / 2);
            Canvas.SetTop(shade, _rng.NextDouble() * h - sh / 2);
            Layer.Children.Add(shade);

            Loop(shade, OpacityProperty, 0.1, 0.42, 12 + _rng.Next(8), i * 3, AmbientFrameRate);
            Loop(shade.RenderTransform, TranslateTransform.XProperty, -w * 0.12, w * 0.12, 32 + _rng.Next(20), i * 4, AmbientFrameRate);
            Loop(shade.RenderTransform, TranslateTransform.YProperty, -h * 0.06, h * 0.06, 26 + _rng.Next(16), i * 5, AmbientFrameRate);
        }
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

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
