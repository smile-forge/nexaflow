using System.Diagnostics;

using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nexaflow.AvaloniaShell.Scenes;

/// <summary>
/// Base for a theme's animated backdrop: the control the app puts behind a window or a region. It owns everything
/// every scene needs and nothing about any particular art: the build/rebuild cycle, the motions (so a rebuild can
/// stop the previous set and a minimise can pause them), the staggering and the caching. A scene supplies only
/// <see cref="SceneRoot"/>, <see cref="SceneLayer"/> and <see cref="BuildScene"/>.
/// <para>
/// A scene is the only forever-animating part of the shell, so its per-frame cost is the shell's idle cost. Every
/// motion therefore runs on the compositor: a key-frame animation started on the element's composition visual,
/// evaluated on the render thread, so an animating scene costs the UI thread nothing between builds. The properties
/// animated are ones layout never writes (<c>Translation</c>, <c>RotationAngle</c>, <c>Scale</c>, about the visual's
/// <c>CenterPoint</c>), plus <c>Opacity</c>, which a static element keeps at the value layout would write. Every
/// element is cached as a texture after a build, so a frame is a texture composite rather than a re-rasterisation
/// of every gradient and stroke.
/// </para>
/// <para>
/// Staggering is a seek into the cycle, never a delayed start: a motion entered <c>phase</c> seconds in is started
/// with its key frames rotated by that phase, so it is moving from its first frame. Pausing is the same seek: a
/// paused scene stops every animation and, resumed, enters each one again where <see cref="SceneTime"/> says it is.
/// </para>
/// </summary>
public abstract class AnimatedScene : Control
{
    private static readonly TimeSpan ResizeSettle = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _resizeDebounce = new() { Interval = ResizeSettle };
    private readonly Dictionary<Visual, SceneTarget> _targets = new();

    // The time the motions have actually advanced: it runs while the scene is built and running, stops while it is
    // paused, and survives a rebuild. See SceneTime.
    private readonly Stopwatch _runTime = new();

    // The scene's ancestors up to its top level, watched while attached: the scene runs only while all are visible.
    private readonly List<Visual> _lineage = new();

    private Control? _root;
    private TopLevel? _host;
    private bool _built;
    private bool _building;
    private bool _running;
    private Size _builtSize;
    private TimeSpan _builtAt;

    protected AnimatedScene()
    {
        IsHitTestVisible = false;
        ClipToBounds     = true;
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Build(force: false); };
    }

    /// <summary>The scene's whole visual: its backdrop with <see cref="SceneLayer"/> inside it, filling the scene.</summary>
    protected Control? SceneRoot
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value)) return;
            if (_root is not null)
            {
                LogicalChildren.Remove(_root);
                VisualChildren.Remove(_root);
            }
            _root = value;
            if (_root is not null)
            {
                LogicalChildren.Add(_root);
                VisualChildren.Add(_root);
            }
            InvalidateMeasure();
        }
    }

    /// <summary>The panel the scene's elements are added to, inside <see cref="SceneRoot"/>.</summary>
    protected abstract Panel SceneLayer { get; }

    /// <summary>
    /// Populate <see cref="SceneLayer"/> for a region of this size, and declare its motions. Called on first layout
    /// and again only when the region actually changes size. Everything is procedural so it re-fits the region.
    /// </summary>
    protected abstract void BuildScene(double width, double height);

    /// <summary>
    /// How long this scene has been running: across rebuilds, and not counting time paused, which is exactly the
    /// time its motions have advanced. A scene whose art follows a cycle longer than a resize is likely to wait for
    /// enters each rebuild here, so the cycle carries on where it was rather than starting over. Starts again from
    /// zero when the scene leaves the visual tree, along with everything else.
    /// </summary>
    protected TimeSpan SceneTime => _runTime.Elapsed;

    /// <summary>Rebuild at the current size: for a property of the scene that changes its art.</summary>
    protected void InvalidateScene()
    {
        if (_built) Build(force: true);
    }

    // A backdrop never asks for room: it fills whatever its host gives it.
    protected override Size MeasureOverride(Size availableSize)
    {
        _root?.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // Run only while the scene can be seen: in a shown top level that is not minimised, itself and every ancestor
    // visible. NOT merely while focused, so a window parked on a second monitor keeps animating while the user works
    // elsewhere. Full occlusion by another window is not detectable without fragile Win32 region polling.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = TopLevel.GetTopLevel(this);
        for (var ancestor = this.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            _lineage.Add(ancestor);
            ancestor.PropertyChanged += OnLineagePropertyChanged;
        }
        ApplyRunState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _resizeDebounce.Stop();
        foreach (var ancestor in _lineage) ancestor.PropertyChanged -= OnLineagePropertyChanged;
        _lineage.Clear();
        _host = null;
        if (_running) StopMotions();
        _running = false;
        _runTime.Reset();
        _built = false;
        _targets.Clear();
        SceneLayer.Children.Clear();
    }

    // A scene re-attached at the size it had raises no size change, so it builds once it is laid out again.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (!_built) Build(force: false);
    }

    // First layout builds immediately; later resizes rebuild once the size settles.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_built) { Build(force: false); return; }
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) ApplyRunState();
    }

    private void OnLineagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty) ApplyRunState();
    }

    private bool CanRun
    {
        get
        {
            if (!_built || !IsVisible || _host is null or Window { WindowState: WindowState.Minimized }) return false;
            foreach (var ancestor in _lineage)
                if (!ancestor.IsVisible) return false;
            return true;
        }
    }

    private void ApplyRunState()
    {
        bool run = CanRun;
        if (run == _running) return;
        _running = run;
        if (run)
        {
            _runTime.Start();
            StartMotions();
        }
        else
        {
            StopMotions();
            _runTime.Stop();
        }
    }

    private void Build(bool force)
    {
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;

        // A scene is a function of its size, and layout settles over more than one pass, the passes after the first
        // arriving at the size already built. Rebuilding for those would tear down and restart every motion a moment
        // after the scene appeared, which reads as the whole backdrop stuttering once on startup.
        if (!force && _built && Math.Abs(size.Width  - _builtSize.Width)  < 0.5
                             && Math.Abs(size.Height - _builtSize.Height) < 0.5) return;

        if (_running) StopMotions();
        _targets.Clear();
        SceneLayer.Children.Clear();

        _built     = true;
        _builtSize = size;
        _building  = true;
        try
        {
            BuildScene(size.Width, size.Height);
        }
        finally
        {
            _building = false;
        }

        // Everything a scene draws is immutable vector art whose only per-frame change is its opacity or its
        // transform. Cached, a frame is a texture composite; uncached, every gradient fill and stroke would be
        // re-rasterised on every frame. A scene that draws a sprite scaled up by its RenderTransform calls Cache()
        // itself while it still knows the scale; this only fills in what it didn't.
        foreach (var child in SceneLayer.Children) CacheBelow(child);

        _builtAt = SceneTime;
        if (_running) StartMotions();
        else          ApplyRunState();
    }

    // A carrier exists so its sprite can move independently inside it; caching the carrier would re-render its
    // texture whenever the sprite moved, so the sprite is what is cached.
    private static void CacheBelow(Control element)
    {
        if (element is SceneCarrier carrier) CacheBelow(carrier.Sprite);
        else element.CacheMode ??= new BitmapCache();
    }

    private void StartMotions()
    {
        var since = SceneTime - _builtAt;
        foreach (var target in _targets.Values) target.Start(since);
    }

    private void StopMotions()
    {
        foreach (var target in _targets.Values) target.Stop();
    }

    /// <summary>
    /// Cache <paramref name="element"/> as a texture rendered at <paramref name="renderAtScale"/>. Pass the scale
    /// the element is drawn at: a sprite scaled up by its RenderTransform and cached at 1x is resampled upwards,
    /// and softens.
    /// </summary>
    protected static void Cache(Visual element, double renderAtScale)
        => element.CacheMode = new BitmapCache { RenderAtScale = Math.Max(1.0, renderAtScale) };

    /// <summary>
    /// Wrap <paramref name="sprite"/> in a carrier: an uncached panel the size of the sprite, placed in the layer in
    /// its stead, so the sprite can take a second motion on a property it already animates (a crossing on the
    /// carrier, a bob on the sprite) or be skewed (<see cref="LoopSkewX"/>). The sprite is cached, the carrier not.
    /// </summary>
    protected SceneCarrier Carry(Control sprite) => new(sprite);

    /// <summary>The point, in <paramref name="target"/>'s own coordinates, it rotates and scales about.</summary>
    protected void SetPivot(Visual target, Point pivot) => TargetOf(target).Pivot = pivot;

    /// <summary>A forever, auto-reversing sine-eased oscillation, entered at <paramref name="phaseSeconds"/>.</summary>
    protected void Loop(Visual target, SceneChannel channel,
                        double from, double to, double seconds, double phaseSeconds)
        => Animate(target, channel, new SceneCycle(2 * seconds, from)
                                       .To(0.5, to,   SceneCycle.SineInOut)
                                       .To(1.0, from, SceneCycle.SineInOut),
                   phaseSeconds);

    /// <summary>
    /// Run <paramref name="cycle"/> forever on <paramref name="target"/>'s <paramref name="channel"/>, entered at
    /// <paramref name="phaseSeconds"/> into it. Declared from <see cref="BuildScene"/>; started when the build ends,
    /// stopped on a rebuild, paused with the scene.
    /// </summary>
    protected void Animate(Visual target, SceneChannel channel, SceneCycle cycle, double phaseSeconds = 0)
    {
        var (property, map) = Realisation(channel);
        Declare(target, property, cycle.Keys(map), cycle.Seconds, phaseSeconds);
    }

    /// <summary>
    /// Drive <paramref name="channel"/> by <paramref name="curve"/> (seconds into a cycle of
    /// <paramref name="cycleSeconds"/>, to a value), entered at <paramref name="phaseSeconds"/>. For motion that
    /// answers to a model the whole scene shares rather than to its own loop: every element that follows the model is
    /// entered at the same phase, so they stay in step for as long as the scene runs. The curve is sampled into the
    /// cycle's key frames at <see cref="SceneCycle.SamplesPerSecond"/>, linear between, so it must be smooth at that
    /// scale; a jump at the cycle's end is kept a jump.
    /// </summary>
    protected void Follow(Visual target, SceneChannel channel, Func<double, double> curve,
                          double cycleSeconds, double phaseSeconds)
    {
        var (property, map) = Realisation(channel);
        Declare(target, property, SceneCycle.Sample(cycleSeconds, u => map(curve(u * cycleSeconds))),
                cycleSeconds, phaseSeconds % cycleSeconds);
    }

    /// <summary>
    /// A forever, auto-reversing sine-eased horizontal skew of <paramref name="carrier"/>'s sprite about
    /// <paramref name="pivot"/> (sprite coordinates), from <paramref name="fromDegrees"/> to
    /// <paramref name="toDegrees"/>: what a WPF <c>SkewTransform.AngleX</c> does, entered at
    /// <paramref name="phaseSeconds"/>.
    /// <para>
    /// The compositor has no skew, so the shear is factored exactly as rotation, scale, rotation: the carrier
    /// rotates and scales, the sprite rotates, about the same pivot. The factors are a non-linear function of the
    /// angle, so they are sampled into key frames at <see cref="SceneCycle.SamplesPerSecond"/>.
    /// </para>
    /// </summary>
    protected void LoopSkewX(SceneCarrier carrier, Point pivot,
                             double fromDegrees, double toDegrees, double seconds, double phaseSeconds)
    {
        double period = 2 * seconds;
        int n = SceneCycle.SampleCount(period);
        var scale = new CycleKey[n + 1];
        var outer = new CycleKey[n + 1];
        var inner = new CycleKey[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            var shear = ShearFactors.Of(Math.Tan(SceneCycle.PingPong(fromDegrees, toDegrees, u) * Math.PI / 180));
            scale[i] = new CycleKey(u, shear.Scale, SceneCycle.Linear);
            outer[i] = new CycleKey(u, new Vector3D(shear.Outer, 0, 0), SceneCycle.Linear);
            inner[i] = new CycleKey(u, new Vector3D(shear.Inner, 0, 0), SceneCycle.Linear);
        }

        SetPivot(carrier, pivot);
        SetPivot(carrier.Sprite, pivot);
        Declare(carrier,        CompositionChannel.Scale,         scale, period, phaseSeconds);
        Declare(carrier,        CompositionChannel.RotationAngle, outer, period, phaseSeconds);
        Declare(carrier.Sprite, CompositionChannel.RotationAngle, inner, period, phaseSeconds);
    }

    /// <summary>
    /// <paramref name="data"/> fitted to a <paramref name="width"/> x <paramref name="height"/> box the way a WPF
    /// <c>Path</c> with <c>Stretch.Fill</c> fits it: its bounds scaled to the box less the stroke, inset by half the
    /// stroke, so the stroke lands inside the box and keeps its own thickness.
    /// </summary>
    protected static Geometry FitGeometry(string data, double width, double height, double strokeThickness = 0)
    {
        var geometry = Geometry.Parse(data);
        var bounds   = geometry.Bounds;
        double inset = strokeThickness / 2;
        double sx = bounds.Width  > 0 ? Math.Max(width  - strokeThickness, 0) / bounds.Width  : 1;
        double sy = bounds.Height > 0 ? Math.Max(height - strokeThickness, 0) / bounds.Height : 1;
        geometry.Transform = new MatrixTransform(
            Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(inset, inset));
        return geometry;
    }

    private static (CompositionChannel Property, Func<double, Vector3D> Map) Realisation(SceneChannel channel) => channel switch
    {
        SceneChannel.Opacity    => (CompositionChannel.Opacity,       v => new Vector3D(v, 0, 0)),
        SceneChannel.TranslateX => (CompositionChannel.Translation,   v => new Vector3D(v, 0, 0)),
        SceneChannel.TranslateY => (CompositionChannel.Translation,   v => new Vector3D(0, v, 0)),
        SceneChannel.Rotation   => (CompositionChannel.RotationAngle, v => new Vector3D(v * Math.PI / 180, 0, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
    };

    private void Declare(Visual target, CompositionChannel property, CycleKey[] keys, double seconds, double phaseSeconds)
    {
        TargetOf(target).Add(new SceneMotion(property, keys, seconds, phaseSeconds));
    }

    private SceneTarget TargetOf(Visual element)
    {
        if (!_building)
            throw new InvalidOperationException("A scene declares its motions and pivots from BuildScene, which a rebuild runs again.");
        if (!_targets.TryGetValue(element, out var target))
            _targets.Add(element, target = new SceneTarget(element));
        return target;
    }

    /// <summary>
    /// A sprite's stand-in in the layer: an uncached panel holding only the sprite, so the sprite can move inside it.
    /// Made by <see cref="Carry"/>.
    /// </summary>
    protected sealed class SceneCarrier : Panel
    {
        internal SceneCarrier(Control sprite)
        {
            IsHitTestVisible = false;
            Sprite = sprite;
            Children.Add(sprite);
        }

        /// <summary>The element the carrier carries.</summary>
        public Control Sprite { get; }
    }

    /// <summary>The composition properties a motion can drive.</summary>
    private enum CompositionChannel { Opacity, Translation, RotationAngle, Scale }

    /// <summary>One element's motions, and the composition visual they run on while the scene runs.</summary>
    private sealed class SceneTarget(Visual element)
    {
        private readonly List<SceneMotion> _motions = new(2);
        private CompositionVisual? _visual;

        public Point? Pivot { get; set; }

        public void Add(SceneMotion motion)
        {
            foreach (var existing in _motions)
                if (existing.Property == motion.Property)
                    throw new InvalidOperationException(
                        $"A {element.GetType().Name} already has a motion on its {motion.Property}: TranslateX and "
                        + "TranslateY share one Translation, so the second goes on a carrier (Carry).");
            _motions.Add(motion);
        }

        public void Start(TimeSpan since)
        {
            var visual = ElementComposition.GetElementVisual(element)
                ?? throw new InvalidOperationException(
                    $"A {element.GetType().Name} the scene animates has no composition visual: every animated element "
                    + "must be in SceneLayer, directly or inside a sprite, when BuildScene returns.");
            if (Pivot is { } pivot) visual.CenterPoint = new Vector3D(pivot.X, pivot.Y, 0);
            foreach (var motion in _motions) motion.StartOn(visual, since);
            _visual = visual;
        }

        public void Stop()
        {
            if (_visual is { IsDisposed: false } visual)
                foreach (var motion in _motions) visual.StopAnimation(motion.PropertyName);
            _visual = null;
        }
    }

    /// <summary>A forever-repeating key-frame cycle on one composition property, entered at a phase.</summary>
    private sealed class SceneMotion(CompositionChannel property, CycleKey[] keys, double seconds, double phaseSeconds)
    {
        public CompositionChannel Property => property;

        public string PropertyName => property switch
        {
            CompositionChannel.Opacity       => nameof(CompositionVisual.Opacity),
            CompositionChannel.Translation   => nameof(CompositionVisual.Translation),
            CompositionChannel.RotationAngle => nameof(CompositionVisual.RotationAngle),
            CompositionChannel.Scale         => nameof(CompositionVisual.Scale),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
        };

        public void StartOn(CompositionVisual visual, TimeSpan since)
        {
            double into = (phaseSeconds + since.TotalSeconds) % seconds;
            if (into < 0) into += seconds;
            var entered = CycleKey.EnteredAt(keys, into / seconds);

            var compositor = visual.Compositor;
            KeyFrameAnimation animation;
            if (property is CompositionChannel.Opacity or CompositionChannel.RotationAngle)
            {
                var scalar = compositor.CreateScalarKeyFrameAnimation();
                foreach (var key in entered) scalar.InsertKeyFrame((float)key.At, (float)key.Value.X, key.Easing);
                animation = scalar;
            }
            else
            {
                var vector = compositor.CreateVector3DKeyFrameAnimation();
                foreach (var key in entered) vector.InsertKeyFrame((float)key.At, key.Value, key.Easing);
                animation = vector;
            }
            animation.Duration          = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            animation.StopBehavior      = AnimationStopBehavior.LeaveCurrentValue;
            visual.StartAnimation(PropertyName, animation);
        }
    }

    /// <summary>
    /// A WPF <c>SkewTransform.AngleX</c> shear <c>[[1, k], [0, 1]]</c> (k the angle's tangent) as
    /// <c>R(Outer) · diag(Scale.X, Scale.Y) · R(Inner)</c>, its singular value decomposition.
    /// </summary>
    private readonly record struct ShearFactors(double Outer, Vector3D Scale, double Inner)
    {
        public static ShearFactors Of(double k)
        {
            // The left singular vectors are the eigenvectors of H·Hᵀ = [[1 + k², k], [k, 1]], at an angle a with
            // tan 2a = 2/k; atan2(2, k) keeps a continuous through k = 0, where any basis decomposes the identity.
            double a = 0.5 * Math.Atan2(2, k);
            double ca = Math.Cos(a), sa = Math.Sin(a);

            // R(-a)·H = diag(s1, s2)·R(b): its rows are orthogonal, their lengths the singular values, and the
            // normalised rows are R(b).
            double r0x = ca, r0y = k * ca + sa;
            double r1x = -sa, r1y = ca - k * sa;
            double s1 = Math.Sqrt(r0x * r0x + r0y * r0y);
            double s2 = Math.Sqrt(r1x * r1x + r1y * r1y);
            double b  = Math.Atan2(r1x / s2, r0x / s1);
            return new ShearFactors(a, new Vector3D(s1, s2, 1), b);
        }
    }
}

/// <summary>What a scene's motion drives on an element.</summary>
public enum SceneChannel
{
    /// <summary>The element's opacity, 0 to 1.</summary>
    Opacity,
    /// <summary>A horizontal offset from where layout put the element, after its RenderTransform.</summary>
    TranslateX,
    /// <summary>A vertical offset from where layout put the element. Shares one property with <see cref="TranslateX"/>.</summary>
    TranslateY,
    /// <summary>A rotation in degrees, clockwise, about the element's pivot (<c>SetPivot</c>), after its RenderTransform.</summary>
    Rotation,
}

/// <summary>
/// One cycle of a motion: a value at each fraction of <see cref="Seconds"/>, eased in from the key before, holding
/// after the last key to the end of the cycle. The cycle repeats forever; where it ends on a value other than the one
/// it starts on, it jumps back.
/// </summary>
public sealed class SceneCycle
{
    /// <summary>How densely a curve is sampled into key frames (<c>Follow</c>, a skew), linear between samples.</summary>
    public const double SamplesPerSecond = 20;

    private const int MinSamples = 32;
    private const int MaxSamples = 2048;

    internal static readonly IEasing Linear    = new LinearEasing();
    internal static readonly IEasing SineInOut = new SineEaseInOut();

    private readonly List<(double At, double Value, IEasing Easing)> _keys;

    public SceneCycle(double seconds, double startValue)
    {
        if (!(seconds > 0)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A cycle lasts some time.");
        Seconds = seconds;
        _keys   = [(0, startValue, Linear)];
    }

    /// <summary>How long one cycle lasts.</summary>
    public double Seconds { get; }

    /// <summary>Reach <paramref name="value"/> at <paramref name="at"/> (a fraction of the cycle), by <paramref name="easing"/> (linear by default).</summary>
    public SceneCycle To(double at, double value, IEasing? easing = null)
    {
        if (at > 1 || at < _keys[^1].At + CycleKey.MinSpacing)
            throw new ArgumentOutOfRangeException(nameof(at), at, "Keys rise through the cycle, from 0 to at most 1.");
        _keys.Add((at, value, easing ?? Linear));
        return this;
    }

    internal CycleKey[] Keys(Func<double, Vector3D> map)
    {
        bool hold = _keys[^1].At < 1;
        var keys  = new CycleKey[_keys.Count + (hold ? 1 : 0)];
        for (int i = 0; i < _keys.Count; i++)
            keys[i] = new CycleKey(_keys[i].At, map(_keys[i].Value), _keys[i].Easing);
        if (hold) keys[^1] = new CycleKey(1, keys[^2].Value, Linear);
        return keys;
    }

    /// <summary>A cycle of <paramref name="seconds"/> sampled from <paramref name="valueAt"/> (a fraction of the cycle, to a value).</summary>
    internal static CycleKey[] Sample(double seconds, Func<double, Vector3D> valueAt)
    {
        int n = SampleCount(seconds);
        var keys = new CycleKey[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            keys[i] = new CycleKey(u, valueAt(u), Linear);
        }
        return keys;
    }

    /// <summary>How many samples a sampled cycle of <paramref name="seconds"/> is drawn with.</summary>
    internal static int SampleCount(double seconds)
        => Math.Clamp((int)Math.Ceiling(seconds * SamplesPerSecond), MinSamples, MaxSamples);

    /// <summary>A sine-eased swing from <paramref name="from"/> to <paramref name="to"/> and back over one cycle, at fraction <paramref name="u"/>.</summary>
    internal static double PingPong(double from, double to, double u)
        => u < 0.5 ? from + (to - from) * SineInOut.Ease(2 * u)
                   : to + (from - to) * SineInOut.Ease(2 * u - 1);
}

/// <summary>A key of a cycle, in composition units: the value reached at <see cref="At"/>, eased in by <see cref="Easing"/>.</summary>
internal readonly record struct CycleKey(double At, Vector3D Value, IEasing Easing)
{
    // Fractions of a cycle closer than this are one instant: a seek that close to a key lands on it, and a jump back
    // to a cycle's start takes this long. Ten microseconds of a second-long cycle, a few milliseconds of the longest.
    private const double Resolution = 1e-5;

    internal const double MinSpacing = 4 * Resolution;

    /// <summary>
    /// <paramref name="keys"/> (0 to 1, rising) rotated to begin at <paramref name="fraction"/>: the same forever
    /// motion, seeked. A segment the seek splits keeps its exact easing curve in its two parts.
    /// </summary>
    public static List<CycleKey> EnteredAt(CycleKey[] keys, double fraction)
    {
        var result = new List<CycleKey>(keys.Length + 3);
        if (fraction < Resolution || fraction > 1 - Resolution)
        {
            result.AddRange(keys);
            return result;
        }

        int j = 0;
        while (keys[j + 1].At <= fraction) j++;            // keys[j].At <= fraction < keys[j + 1].At
        if (fraction - keys[j].At < Resolution) fraction = keys[j].At;
        else if (keys[j + 1].At - fraction < Resolution) fraction = keys[++j].At;
        if (j == 0 && fraction == 0)
        {
            result.AddRange(keys);
            return result;
        }

        bool split = fraction > keys[j].At;
        double x0  = 0;
        var entry  = keys[j].Value;
        if (split)
        {
            x0    = (fraction - keys[j].At) / (keys[j + 1].At - keys[j].At);
            entry = Lerp(keys[j].Value, keys[j + 1].Value, keys[j + 1].Easing.Ease(x0));
        }

        result.Add(new CycleKey(0, entry, SceneCycle.Linear));
        int first = j + 1;
        if (split)
        {
            var right = keys[j + 1];
            result.Add(new CycleKey(right.At - fraction, right.Value, new SegmentEasing(right.Easing, x0, 1)));
            first = j + 2;
        }
        for (int i = first; i < keys.Length; i++)
            result.Add(keys[i] with { At = keys[i].At - fraction });

        // The cycle's end runs on into its start; a cycle that ends elsewhere than it starts jumps back.
        double wrap = 1 - fraction;
        if (!Same(keys[^1].Value, keys[0].Value))
            result.Add(new CycleKey(wrap + Resolution, keys[0].Value, SceneCycle.Linear));
        for (int i = 1; i <= j; i++)
            result.Add(keys[i] with { At = wrap + keys[i].At });

        if (split)
            result.Add(new CycleKey(1, entry, new SegmentEasing(keys[j + 1].Easing, 0, x0)));
        return result;
    }

    private static Vector3D Lerp(Vector3D a, Vector3D b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static bool Same(Vector3D a, Vector3D b)
        => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;

    /// <summary>The part of <paramref name="whole"/> from <paramref name="from"/> to <paramref name="to"/>, as an easing of its own.</summary>
    private sealed class SegmentEasing(IEasing whole, double from, double to) : Easing
    {
        private readonly double _start = whole.Ease(from);
        private readonly double _span  = whole.Ease(to) - whole.Ease(from);

        // A part over which the whole curve does not move draws a constant value, whatever this returns.
        public override double Ease(double progress)
            => Math.Abs(_span) < 1e-12 ? progress : (whole.Ease(from + progress * (to - from)) - _start) / _span;
    }
}
