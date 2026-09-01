using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Base for a theme's animated backdrop — the control a <c>Scene.{Region}</c> template realises behind
/// a <see cref="ThemedRegion"/>. It owns everything every scene needs and nothing about any particular
/// art: the build/rebuild cycle, the clocks (so a rebuild can stop the previous set and a minimise can
/// pause them), the staggering, the freezing and the caching. A scene supplies only
/// <see cref="SceneLayer"/> and <see cref="BuildScene"/>.
/// <para>
/// A scene is the only forever-animating part of the shell, so its per-frame cost is the shell's idle
/// cost. Two things here exist for that reason and should not be undone casually: every element is
/// cached as a texture after a build, and every stagger is a <em>seek into</em> a running clock rather
/// than a delayed start.
/// </para>
/// </summary>
public abstract class AnimatedScene : UserControl
{
    /// <summary>
    /// Frame-rate cap for the <em>ambient</em> layers — the big, (near-)stationary translucent fills
    /// (glows, rays, caustics, sway). Halving their update rate is imperceptible since the eye doesn't
    /// track them. Travelling sprites are deliberately NOT capped: <c>DesiredFrameRate</c> decouples a
    /// clock from vsync, so any sub-refresh rate reads as judder on motion the eye follows.
    /// </summary>
    protected const int AmbientFrameRate = 30;

    // Every animation runs through a controllable clock we retain, so a rebuild can *stop* the previous
    // set — otherwise the cleared elements' Forever clocks keep ticking on the UI thread until GC — and
    // so the whole scene can be paused when the host window is minimised.
    private readonly List<ClockController> _clocks = new();
    private readonly DispatcherTimer _resizeDebounce;
    private Window? _host;
    private bool _built;
    private Size _builtSize;

    protected AnimatedScene()
    {
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Build(); };
        SizeChanged += OnSizeChanged;
        Loaded      += OnLoaded;
        Unloaded    += OnUnloaded;
    }

    /// <summary>The panel the scene's elements are added to — the derived scene's XAML <c>Layer</c>.</summary>
    protected abstract Panel SceneLayer { get; }

    /// <summary>
    /// Populate <see cref="SceneLayer"/> for a region of this size. Called on first layout and again
    /// only when the region actually changes size. Everything is procedural so it re-fits the region.
    /// </summary>
    protected abstract void BuildScene(double width, double height);

    // Pause the scene only when the window is genuinely hidden (minimised) — NOT merely unfocused, so a
    // window parked on a second monitor keeps animating while the user works elsewhere. (Full occlusion
    // by another window isn't detectable without fragile Win32 region polling, so we don't.)
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
        SceneLayer.Children.Clear();
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

    // Detach every live clock so the TimeManager stops ticking them immediately, rather than waiting
    // for the cleared elements to be garbage-collected.
    private void StopClocks()
    {
        foreach (var c in _clocks) c.Remove();
        _clocks.Clear();
    }

    // First layout builds immediately; later resizes rebuild once the size settles.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_built) { Build(); return; }
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void Build()
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;

        // A scene is a function of its size, and layout settles over more than one pass — the passes
        // after the first arrive at the size we already built. Rebuilding for those tore down and
        // restarted every clock a moment after the scene appeared, which read as the whole backdrop
        // stuttering once on startup. Only a real size change is worth a rebuild.
        var size = new Size(ActualWidth, ActualHeight);
        if (_built && Math.Abs(size.Width  - _builtSize.Width)  < 0.5
                   && Math.Abs(size.Height - _builtSize.Height) < 0.5) return;

        _built     = true;
        _builtSize = size;

        StopClocks();
        SceneLayer.Children.Clear();
        BuildScene(size.Width, size.Height);

        // Everything a scene draws is immutable vector art whose only per-frame change is its opacity
        // or its transform. Uncached, WPF re-rasterises every gradient fill and stroke on every frame —
        // measurably the largest single cost in an idle themed shell, and the reason a scene costs
        // multiples of what moving the same number of plain rectangles costs. Cached, a frame is a
        // texture composite. A scene that needs a sprite rendered at other than 1x calls Cache() itself
        // while it still knows the scale; this only fills in what it didn't.
        foreach (UIElement child in SceneLayer.Children)
            child.CacheMode ??= new BitmapCache();

        ApplyPauseState();   // a rebuild starts clocks running; honour minimise if we're hidden
    }

    /// <summary>
    /// Cache <paramref name="element"/> as a texture rendered at <paramref name="renderAtScale"/>.
    /// Pass the scale the element is drawn at: a sprite scaled up by a <see cref="ScaleTransform"/> and
    /// cached at 1x is resampled upwards, and softens.
    /// </summary>
    protected static void Cache(UIElement element, double renderAtScale)
        => element.CacheMode = new BitmapCache(Math.Max(1.0, renderAtScale));

    /// <summary>A forever, auto-reversing eased oscillation, entered at <paramref name="phaseSeconds"/>.</summary>
    protected void Loop(IAnimatable target, DependencyProperty prop,
                        double from, double to, double seconds, double phaseSeconds, int? fps = null)
        => Animate(target, prop, new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        }, fps, phaseSeconds);

    /// <summary>
    /// Start <paramref name="anim"/> through a controllable clock (retained so the scene can be stopped
    /// on rebuild or paused on minimise). An optional <paramref name="fps"/> caps the update rate —
    /// passed only for ambient layers. <paramref name="phaseSeconds"/> spreads elements around the cycle.
    /// </summary>
    protected void Animate(IAnimatable target, DependencyProperty prop, AnimationTimeline anim,
                           int? fps = null, double phaseSeconds = 0)
    {
        if (fps is int rate) Timeline.SetDesiredFrameRate(anim, rate);
        var clock = anim.CreateClock();
        target.ApplyAnimationClock(prop, clock);
        if (clock.Controller is not { } controller) return;

        // Stagger by seeking INTO a running cycle, never by delaying the start with BeginTime. A delayed
        // clock leaves its element frozen at whatever it was authored with — for up to the whole stagger
        // — and then snaps it to the animation's from-value when the clock finally fires. That is what
        // made the god rays sit still and jump before they began drifting, and it put every scene's
        // sprites through the same hitch on load.
        if (phaseSeconds > 0) controller.Seek(TimeSpan.FromSeconds(phaseSeconds), TimeSeekOrigin.BeginTime);

        _clocks.Add(controller);
    }

    /// <summary>
    /// Freeze a set-once brush/geometry so WPF can share it with the render thread and skip change
    /// tracking. Everything a scene builds is immutable after creation (only element opacity and
    /// transforms animate), so all of it is freezable.
    /// </summary>
    protected static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
