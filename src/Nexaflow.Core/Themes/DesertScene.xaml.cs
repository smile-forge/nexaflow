using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Core.Themes;

/// <summary>
/// A self-contained animated desert used by the Desert theme. Instantiated by <c>ThemedRegion</c> via the
/// <c>Scene.Window</c> template in <c>Theme.Desert.xaml</c>; carries no dependency on the shell or any feature.
/// Built procedurally and in depth order: a dusty sky over a low sun, two bands of dunes fading into the
/// horizon, one great dune with a shadowed slip face, and a rippled floor scattered with grass tufts and
/// shrubs. Adapts to the region size; never hit-tests.
/// <para>
/// <b>Everything that moves answers to one wind</b> (<see cref="DesertWind"/>). The bushes sway in its breeze
/// and lean into its gusts, a gust's streaks and swirls of sand ride its front across the window, the plumes
/// off the dune crests and the sand drifting over the ground thicken with it, and once a cycle it gathers
/// toward a storm — a wall of dust on the horizon, billows in the sky, the sun put out — that fades away
/// before it arrives. Each of those is a <see cref="AnimatedScene.Follow"/> of the one model, entered at the
/// one phase, so a gust reaches a bush at the moment its streaks pass it, and nothing has to be kept in step
/// by hand.
/// </para>
/// <para>
/// The weather outlives the art: <see cref="_wind"/> is made once per scene and a rebuild enters it at
/// <see cref="AnimatedScene.SceneTime"/> past where it opened, so resizing the window redraws the dunes but
/// carries on with the same afternoon. That is what keeps the storm to once an hour; see <see cref="DesertWind"/>.
/// </para>
/// <para>
/// As in the other scenes, static detail is grouped and animated detail is not: the dune bands, the great
/// dune and the whole floor (ripples, mounds, the bushes' shadows) each cost one texture, and every moving
/// sprite is a top-level child whose only animated properties are its transform and its opacity.
/// </para>
/// </summary>
public partial class DesertScene : AnimatedScene
{
    /// <summary>Where the far dunes meet the sky, as a fraction of region height. The sky gradient in the XAML
    /// reaches <see cref="Haze"/> here.</summary>
    private const double HorizonFraction = 0.50;

    /// <summary>Where the rippled floor in front of the great dune begins.</summary>
    private const double FloorFraction = 0.63;

    /// <summary>The sun, high and to the left: every lit face looks up-left and every slip face falls away from
    /// it into shadow, as in the reference photographs. One light for the whole scene, never one per sprite.</summary>
    private const double SunXFraction = 0.24, SunYFraction = 0.12;

    /// <summary>What distance fades toward — the warm gold the sky holds at the horizon. Far dunes and far
    /// bushes are lerped toward this rather than merely drawn smaller.</summary>
    private static readonly Color Haze      = Color.FromRgb(0xEE, 0xCF, 0x9C);
    private static readonly Color SandLit   = Color.FromRgb(0xF0, 0xC2, 0x82);
    private static readonly Color SandBody  = Color.FromRgb(0xD6, 0x9A, 0x58);
    private static readonly Color SandShade = Color.FromRgb(0xA4, 0x66, 0x38);
    private static readonly Color SandDeep  = Color.FromRgb(0xC2, 0x84, 0x4A);

    /// <summary>Blown sand catching the light: streaks, swirls, plumes and drift.</summary>
    private static readonly Color Grain = Color.FromRgb(0xF7, 0xDE, 0xAE);

    /// <summary>Blown sand in its own shade: the darker half of every streak, swirl and drift. Sand blowing
    /// over sunlit sand is nearly its colour, so each moving thing carries both — the shade reads against the
    /// bright floor and the light against the sky and the dune's shadow.</summary>
    private static readonly Color Dust = Color.FromRgb(0xB9, 0x80, 0x48);

    /// <summary>A dune band: where it stands, how high its crests rise, how wide its dunes are, and how far it
    /// has faded toward <see cref="Haze"/>. Grouped so the two bands read as one progression.</summary>
    private readonly record struct DuneBand(double BaseFraction, double MinRise, double MaxRise,
                                            double SpanDivisor, double HazeAmount, Color Plain);

    private static readonly DuneBand[] Bands =
    [
        new(0.500, 0.016, 0.045, 150, 0.70, Color.FromRgb(0xE8, 0xC2, 0x8A)),
        new(0.548, 0.040, 0.095, 290, 0.40, Color.FromRgb(0xE2, 0xB0, 0x72)),
    ];

    /// <summary>One bush as planted: its root, its height, how near it stands (0 far, 1 near), and its kind.</summary>
    private readonly record struct Planting(double X, double Y, double Height, double Nearness, bool Grass);

    private readonly DesertWind _wind = new(new Random());
    private readonly Random     _rng  = new();
    private double _phase;

    public DesertScene() => InitializeComponent();

    protected override Panel SceneLayer => Layer;

    protected override void BuildScene(double w, double h)
    {
        // The whole weather is entered at one phase, carried on from wherever the last build left it.
        _phase = _wind.OpeningSeconds + SceneTime.TotalSeconds;

        double hz    = h * HorizonFraction;
        double floor = h * FloorFraction;
        double unit  = Math.Clamp(h / 900.0, 0.6, 1.6);   // sprite sizes follow the window, within reason

        // Strict back-to-front, with the storm's sky layers slipped in behind the great dune — the storm comes
        // from the distance — and its dust veil over everything but the sand it carries.
        AddSunGlow(w, h);
        AddHighCloud(w, hz, Math.Clamp((int)(w / 420.0), 2, 5));
        AddDuneBand(w, h, Bands[0]);
        var midCrests = AddDuneBand(w, h, Bands[1]);
        AddHorizonHaze(w, hz);
        AddStormWall(w, h, hz);
        AddBillows(w, h, Math.Clamp((int)(w / 170.0), 7, 15));
        AddPlumes(midCrests.OrderBy(c => c.Y).Take(Math.Clamp((int)(w / 600.0), 1, 3)), unit * 0.7, Bands[1].HazeAmount, w);
        var (heroCrest, heroRidge) = AddGreatDune(w, h);
        AddPlumes([heroCrest, heroRidge], unit, 0.1, w);

        var bushes = PlanBushes(w, h, floor, Math.Clamp((int)(w * h / 110_000.0), 6, 16));
        AddFloor(w, h, floor, unit, bushes);
        AddDrift(w, floor + (h - floor) * 0.20, unit * 0.8, speed: 0.75, peak: 0.8);
        AddSheets(w, h, floor, unit);
        AddBushes(w, bushes);
        AddDrift(w, floor + (h - floor) * 0.70, unit * 1.3, speed: 1.25, peak: 1.0);

        AddDustVeil(w, h);
        AddStreaks(w, h, floor, unit);
        AddSwirls(w, h, floor, unit);
        AddVignette(w, h);
    }

    /// <summary><see cref="AnimatedScene.Follow"/> on the wind's cycle, at this build's phase.</summary>
    private void FollowWind(IAnimatable target, DependencyProperty prop,
                            Func<double, double> curve, int? fps)
        => Follow(target, prop, curve, _wind.CycleSeconds, _phase, fps);

    /// <summary>How far above the breeze the wind is at <paramref name="t"/>, at <paramref name="across"/>: 0 in
    /// the calm, toward 1 in the storm. Anything that only stirs when the wind does follows this.</summary>
    private double Stir(double t, double across) =>
        (_wind.StrengthAt(t, across) - DesertWind.Calm) / (1 - DesertWind.Calm);

    /// <summary>
    /// A period close to <paramref name="seconds"/> that divides the cycle exactly. A sway that does not would
    /// jump once a cycle when the clock wraps; one that does is seamless, and nobody can tell 3.1s from 3.0s.
    /// </summary>
    private double Fit(double seconds) =>
        _wind.CycleSeconds / Math.Max(1, Math.Round(_wind.CycleSeconds / seconds));

    // ── Sun: a glow high to the left, which the storm all but puts out ───────────────────────────
    private void AddSunGlow(double w, double h)
    {
        double cx = w * SunXFraction, cy = h * SunYFraction;
        double rx = w * 0.42, ry = h * 0.50;

        var rg = new RadialGradientBrush();
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(240, 0xFF, 0xF8, 0xE6), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0xFF, 0xE8, 0xB8), 0.10));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0xFF, 0xD6, 0x98), 0.38));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xC8, 0x80), 1));
        rg.Freeze();

        // A radial gradient goes in a Rectangle: an Ellipse shows its own outline wherever the gradient still
        // has alpha at the shape's edge.
        var glow = new Rectangle { Width = rx * 2, Height = ry * 2, Fill = rg };
        Canvas.SetLeft(glow, cx - rx);
        Canvas.SetTop(glow, cy - ry);
        Layer.Children.Add(glow);

        // The sun going dim is the scene's barometer: the first sign the wind means it.
        FollowWind(glow, OpacityProperty, t => 1 - 0.78 * _wind.StormAt(t), AmbientFrameRate);
    }

    // ── High cloud: a few soft banks drifting over in the calm ────────────────────────────────────
    private void AddHighCloud(double w, double hz, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double s     = 0.8 + _rng.NextDouble() * 1.4;
            double baseY = hz * (0.05 + 0.38 * _rng.NextDouble());
            var    tint  = Lerp(Color.FromRgb(0xF7, 0xF3, 0xEA), Haze, baseY / (hz * 0.6));
            var    cloud = new Canvas { Opacity = 0.45 + _rng.NextDouble() * 0.3 };

            // Enough overlapping flattened puffs that none of them shows its own outline: a bank, not a count.
            int puffs = 7 + _rng.Next(5);
            for (int p = 0; p < puffs; p++)
            {
                double f  = p / (double)(puffs - 1);
                double r  = (12 + 18 * Math.Sin(f * Math.PI) + _rng.NextDouble() * 6) * s;
                double cx = (20 + 220 * f) * s;
                double cy = (28 - 8 * Math.Sin(f * Math.PI) + (_rng.NextDouble() - 0.5) * 8) * s;
                double sq = 0.5 + _rng.NextDouble() * 0.2;

                var rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, tint.R, tint.G, tint.B), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(60, tint.R, tint.G, tint.B), 0.5));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
                rg.Freeze();

                var puff = new Rectangle { Width = r * 2.6, Height = r * 2 * sq, Fill = rg };
                Canvas.SetLeft(puff, cx - r * 1.3);
                Canvas.SetTop(puff, cy - r * sq);
                cloud.Children.Add(puff);
            }

            double cw = 260 * s;
            Canvas.SetTop(cloud, baseY);
            Canvas.SetLeft(cloud, -cw);
            var move = new TranslateTransform();
            cloud.RenderTransform = move;
            Layer.Children.Add(cloud);

            double dur = 260 + _rng.Next(220);
            var drift = new DoubleAnimation(0, w + cw * 2,
                new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Animate(move, TranslateTransform.XProperty, drift, AmbientFrameRate, _rng.NextDouble() * dur);
        }
    }

    // ── A band of dunes: a plain to stand on, then dunes along it, each with its slip face in shadow ─
    private List<Point> AddDuneBand(double w, double h, DuneBand band)
    {
        double baseY = h * band.BaseFraction;
        var group  = new Canvas();
        var crests = new List<Point>();

        // The plain runs to the bottom of the window, so wherever nearer ground does not cover it there is
        // sand rather than sky.
        var plain = new Rectangle
        {
            Width  = w,
            Height = h - baseY + 2,
            Fill   = Frozen(new SolidColorBrush(Lerp(band.Plain, Haze, band.HazeAmount * 0.6))),
        };
        Canvas.SetTop(plain, baseY - 1);
        group.Children.Add(plain);

        int    n    = Math.Clamp((int)(w / band.SpanDivisor), 3, 16);
        double span = (w + 240) / n;
        for (int i = 0; i < n; i++)
        {
            double cx   = -120 + span * (i + 0.5) + (_rng.NextDouble() - 0.5) * span * 0.6;
            double rise = h * (band.MinRise + _rng.NextDouble() * (band.MaxRise - band.MinRise));
            double ww   = span * (0.9 + _rng.NextDouble() * 0.8);   // windward: long and gentle
            double lw   = span * (0.32 + _rng.NextDouble() * 0.3);  // lee: short and steep
            DrawDune(group, cx, baseY, rise, ww, lw, 1.5, band.HazeAmount);
            crests.Add(new Point(cx, baseY - rise));
        }

        Layer.Children.Add(group);
        return crests;
    }

    /// <summary>
    /// One dune of a band, drawn into <paramref name="group"/>: a windward slope rising from the left to a crest,
    /// a slip face falling away to the right, the slip face's shadow, and a lit brink along the crest.
    /// <paramref name="leeCurve"/> shapes the lee skyline — near 1 a straight slip face, higher a concave one.
    /// </summary>
    private void DrawDune(Canvas group, double cx, double baseY, double rise, double ww, double lw,
    double leeCurve, double haze)
    {
        // Windward rises with a rounded shoulder (its slope is gentle at the brink); the lee drops steeply away.
        // Their slopes disagree at the crest, and that disagreement is the sharp brink a dune has.
        var windward = new List<Point>();
        int wn = Math.Max(8, (int)(ww / 8));
        for (int i = 0; i <= wn; i++)
        {
            double u = i / (double)wn;
            windward.Add(new Point(cx - ww + ww * u, baseY - rise * Math.Sin(u * Math.PI / 2)));
        }
        var lee = new List<Point>();
        int ln = Math.Max(8, (int)(lw / 8));
        for (int i = 0; i <= ln; i++)
        {
            double v = i / (double)ln;
            lee.Add(new Point(cx + lw * v, baseY - rise * Math.Pow(1 - v, leeCurve)));
        }

        var body = new List<Point>(windward);
        body.AddRange(lee.Skip(1));
        body.Add(new Point(cx + lw, baseY + 2));
        body.Add(new Point(cx - ww, baseY + 2));

        var lit = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
        lit.GradientStops.Add(new GradientStop(Lerp(SandLit, Haze, haze), 0));
        lit.GradientStops.Add(new GradientStop(Lerp(SandBody, Haze, haze), 1));
        lit.Freeze();
        group.Children.Add(new Path { Data = Shape(body, closed: true), Fill = lit });

        // The slip face seen at a slant: under the lee skyline, bounded on the left by a curve dropping from the
        // crest to a foot a little downwind of it. Darkest just under the brink, lifting toward the foot.
        var slip = new List<Point>(lee) { new(cx + lw * 0.16, baseY + 2) };
        const int bn = 12;
        for (int i = 1; i < bn; i++)
        {
            double f = i / (double)bn;
            double x = cx + lw * 0.16 * (1 - f);
            double y = baseY + 2 - (rise + 2) * Math.Pow(f, 0.7);
            slip.Add(new Point(x + lw * 0.06 * Math.Sin(f * Math.PI), y));
        }

        var dark = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.3, 1) };
        var deep = Lerp(SandShade, Haze, haze);
        var foot = Lerp(SandDeep, Haze, haze);
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(235, deep.R, deep.G, deep.B), 0));
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(160, foot.R, foot.G, foot.B), 1));
        dark.Freeze();
        group.Children.Add(new Path { Data = Shape(slip, closed: true), Fill = dark });

        // The brink: a thin line of light along the last of the windward slope, where it turns over the crest.
        var brink = windward.Skip(windward.Count * 3 / 4).Concat(lee.Take(2)).ToList();
        var shine = Lerp(Grain, Haze, haze);
        group.Children.Add(new Path
        {
            Data            = Shape(brink, closed: false),
            Stroke          = Frozen(new SolidColorBrush(Color.FromArgb(150, shine.R, shine.G, shine.B))),
            StrokeThickness = 1.2,
        });
    }

    // ── Horizon haze: over the far dunes' feet, which is what puts them in the distance ───────────
    private void AddHorizonHaze(double w, double hz)
    {
        double band = hz * 0.16;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, Haze.R, Haze.G, Haze.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(150, Haze.R, Haze.G, Haze.B), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, Haze.R, Haze.G, Haze.B), 1));
        g.Freeze();

        var haze = new Rectangle { Width = w, Height = band, Fill = g };
        Canvas.SetTop(haze, hz - band * 0.55);
        Layer.Children.Add(haze);
    }

    // ── Storm wall: the dust rising on the horizon as the storm gathers ───────────────────────────
    private void AddStormWall(double w, double h, double hz)
    {
        double tall = h * 0.46;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xB2, 0x80, 0x4E), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0xB2, 0x80, 0x4E), 0.40));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(205, 0x9A, 0x6A, 0x3E), 0.80));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(215, 0xA8, 0x76, 0x46), 1));
        g.Freeze();

        var wall = new Rectangle { Width = w, Height = tall, Fill = g, Opacity = 0 };
        Canvas.SetTop(wall, hz + h * 0.04 - tall);

        // It grows up from the horizon as the storm builds, rather than fading in where it stands.
        var grow = new ScaleTransform(1, 1, 0, tall);
        wall.RenderTransform = grow;
        Layer.Children.Add(wall);

        FollowWind(grow, ScaleTransform.ScaleYProperty, t => 0.2 + 0.8 * Math.Sqrt(_wind.StormAt(t)), AmbientFrameRate);
        FollowWind(wall, OpacityProperty, t => Math.Min(1, 1.5 * _wind.StormAt(t)), AmbientFrameRate);
    }

    // ── Billows: the storm's dust clouds, rolling in across the sky from upwind ───────────────────
    private void AddBillows(double w, double h, int n)
    {
        var dark = Color.FromRgb(0x7A, 0x55, 0x36);
        var lit  = Color.FromRgb(0xD9, 0xA6, 0x68);
        double stormEnd = _wind.Storm.End;

        for (int i = 0; i < n; i++)
        {
            double s     = (1.2 + _rng.NextDouble() * 1.4) * Math.Clamp(h / 900.0, 0.7, 1.5);
            double baseY = h * (-0.06 + 0.46 * _rng.NextDouble());
            var    mass  = new Canvas { Opacity = 0 };

            // Lit along the top where the low sun catches it, dark in the belly: towers of dust have a form, and
            // without it they read as a stain on the sky.
            int puffs = 10 + _rng.Next(6);
            for (int p = 0; p < puffs; p++)
            {
                double f  = p / (double)(puffs - 1);
                double r  = (26 + 34 * Math.Sin(f * Math.PI) + _rng.NextDouble() * 14) * s;
                double cx = (30 + 300 * f + (_rng.NextDouble() - 0.5) * 30) * s;
                double cy = (70 - 34 * Math.Sin(f * Math.PI) + (_rng.NextDouble() - 0.5) * 26) * s;
                var    c  = Lerp(lit, dark, Math.Clamp(cy / (90 * s), 0, 1));

                var rg = new RadialGradientBrush { GradientOrigin = new Point(0.42, 0.36) };
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(175, c.R, c.G, c.B), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(110, c.R, c.G, c.B), 0.5));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                rg.Freeze();

                var puff = new Rectangle { Width = r * 2.4, Height = r * 2, Fill = rg };
                Canvas.SetLeft(puff, cx - r * 1.2);
                Canvas.SetTop(puff, cy - r);
                mass.Children.Add(puff);
            }

            double startX = w * (-0.35 + 1.15 * _rng.NextDouble());
            double travel = w * (0.25 + 0.2 * _rng.NextDouble());
            Canvas.SetLeft(mass, startX);
            Canvas.SetTop(mass, baseY);
            var move = new TranslateTransform();
            mass.RenderTransform = move;
            Layer.Children.Add(mass);

            double weight = 0.75 + 0.25 * _rng.NextDouble();
            FollowWind(mass, OpacityProperty, t => Math.Min(1, weight * 1.45 * Math.Pow(_wind.StormAt(t), 1.15)), AmbientFrameRate);
            FollowWind(move, TranslateTransform.XProperty, t => travel * Math.Clamp(t / stormEnd, 0, 1), AmbientFrameRate);
        }
    }

    /// <summary>
    /// The great dune, drawn the way the reference photographs show a big one: a sharp peak, and from it a crest
    /// that sweeps down and away in an S — dropping steeply off the summit, then easing into a long run to the
    /// floor. In front of the crest the near face is in sun; behind it a bowl falls into shadow, walled at the back
    /// by a far ridge that sags into a saddle and rises again to a lower hump. A smaller crescent crest cuts the
    /// near face, as dunes carry crests on crests. None of it is regular — no two curves share a shape — but all of
    /// it is smooth, because wind-laid sand is: a dune drawn as one outline over one fill read as an animal's
    /// flank, and one drawn with straight faces read as a cliff.
    /// <para>
    /// Wind and light agree with the rest of the scene: the sun is up to the left, so the near face catches it and
    /// the bowl, leaning away, does not; and the wind blows from the left, over the crest into the bowl — which is
    /// where sand avalanches and where the plumes stream. Returns the peak and a point down the crest for those.
    /// </para>
    /// </summary>
    private (Point Crest, Point Ridge) AddGreatDune(double w, double h)
    {
        var    group = new Canvas();
        double floor = h * (FloorFraction + 0.02);
        var    peak  = new Point(w * (0.24 + 0.08 * _rng.NextDouble()), h * (0.15 + 0.04 * _rng.NextDouble()));
        double ph    = _rng.NextDouble() * 6;

        // The crest: off the summit steeply, then a long, easing run to the floor, wandering a little on the way.
        var foot  = new Point(peak.X + w * (0.42 + 0.1 * _rng.NextDouble()), floor);
        var crest = Bezier(peak,
                           new Point(peak.X + w * 0.035, peak.Y + h * (0.17 + 0.04 * _rng.NextDouble())),
                           new Point(foot.X - w * (0.22 + 0.08 * _rng.NextDouble()), foot.Y - h * 0.035),
                           foot, 80)
            .Select((p, i) => new Point(p.X, p.Y + h * 0.004 * Math.Sin(i * 0.31 + ph) * Math.Sin(i / 80.0 * Math.PI)))
            .ToList();

        // The near face's skyline, up from the left edge to the peak: over a soft shoulder, then steepening onto the
        // summit, so it is never one straight slope.
        double leftY    = h * (0.36 + 0.05 * _rng.NextDouble());
        double rise     = leftY - peak.Y;
        var    windward = CatmullRom([new Point(-w * 0.2, leftY + h * 0.03),
                                      new Point(-w * 0.06, leftY),
                                      new Point(peak.X * (0.3 + 0.1 * _rng.NextDouble()), leftY - rise * (0.42 + 0.1 * _rng.NextDouble())),
                                      new Point(peak.X * 0.78, peak.Y + rise * (0.3 + 0.06 * _rng.NextDouble())),
                                      peak,
                                      new Point(peak.X + w * 0.02, peak.Y + h * 0.04)], 16);

        // The far ridge behind the bowl: from the peak down into a saddle, up to a lower hump, and away to the
        // horizon — far enough down at the right edge that the distant dunes show past it.
        var saddle = new Point(peak.X + w * (0.26 + 0.06 * _rng.NextDouble()), peak.Y + h * (0.10 + 0.03 * _rng.NextDouble()));
        var hump   = new Point(Math.Min(w * 0.9, saddle.X + w * (0.22 + 0.06 * _rng.NextDouble())), peak.Y + h * (0.05 + 0.03 * _rng.NextDouble()));
        var away   = new Point(w * 1.08, h * 0.45);
        var ridge  = CatmullRom([new Point(peak.X - w * 0.05, peak.Y + h * 0.03), peak, saddle, hump, away,
                                 new Point(w * 1.2, h * 0.5)], 24);

        // The bowl, in shadow: between the crest and the far ridge, down to the floor.
        var bowl = new List<Point>(crest) { new(w * 1.08, floor) };
        bowl.AddRange(ridge.AsEnumerable().Reverse());
        var bowlShape = Shape(bowl, closed: true);
        var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.3) };
        back.GradientStops.Add(new GradientStop(Lerp(SandShade, SandBody, 0.25), 0));
        back.GradientStops.Add(new GradientStop(Lerp(SandBody, Haze, 0.2), 0.7));
        back.GradientStops.Add(new GradientStop(Lerp(Lerp(SandBody, SandLit, 0.4), Haze, 0.45), 1));
        back.Freeze();
        group.Children.Add(new Path { Data = bowlShape, Fill = back });

        // The bowl's shadow deepens toward the crest that casts it; the far ridge's rounded top still catches the
        // sun. Both laid as bands stacked from their edge, clipped to the bowl, so the falloffs follow the dune.
        var shading = new Canvas { Clip = bowlShape };
        // The crest is carried on along the floor, or the shade would stop at a line rising from its foot.
        StackFrom(shading, crest.Append(new Point(w * 1.1, floor)).ToList(), new Vector(0.55, -0.85), w * 0.2, SandShade, 26, 12);
        StackFrom(shading, ridge, new Vector(0, 1), h * 0.05, Color.FromRgb(0xF6, 0xD6, 0xA0), 8, 14);
        group.Children.Add(shading);

        // The near face, in sun: under its skyline, down from the crest to the floor.
        var near = new List<Point>(windward);
        near.AddRange(crest.Skip(1));
        near.Add(new Point(-w * 0.06, floor));
        var nearShape = Shape(near, closed: true);
        var lit = new LinearGradientBrush { StartPoint = new Point(0.7, 0), EndPoint = new Point(0.2, 1) };
        lit.GradientStops.Add(new GradientStop(Color.FromRgb(0xF9, 0xDC, 0xA8), 0));
        lit.GradientStops.Add(new GradientStop(Lerp(SandLit, Haze, 0.15), 0.55));
        lit.GradientStops.Add(new GradientStop(Lerp(SandLit, Haze, 0.35), 1));
        lit.Freeze();
        group.Children.Add(new Path { Data = nearShape, Fill = lit });

        // A crescent crest on the near face, sweeping off the main one: its own sliver of shadow below it.
        var from = crest[(int)(crest.Count * (0.45 + 0.15 * _rng.NextDouble()))];
        var to   = new Point(Math.Max(w * 0.02, peak.X - w * (0.05 + 0.1 * _rng.NextDouble())), floor - h * 0.005);
        var hook = Bezier(from, new Point(from.X - w * 0.06, from.Y + h * 0.015),
                          new Point(to.X + w * 0.12, to.Y - h * 0.07), to, 40);
        var details = new Canvas { Clip = nearShape };
        var sliver = new List<Point>(hook);
        sliver.AddRange(hook.Select((p, i) => new Point(p.X + w * 0.01, p.Y + h * 0.045 * Math.Sin(i / 40.0 * Math.PI))).Reverse());
        var slant = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        slant.GradientStops.Add(new GradientStop(Color.FromArgb(150, SandShade.R, SandShade.G, SandShade.B), 0));
        slant.GradientStops.Add(new GradientStop(Color.FromArgb(0, SandShade.R, SandShade.G, SandShade.B), 1));
        slant.Freeze();
        details.Children.Add(new Path { Data = Shape(sliver, closed: true), Fill = slant });

        // Ripples across the near face, running with the crest.
        var ink = Lerp(SandBody, Haze, 0.1);
        for (int k = 1; k <= 8; k++)
        {
            double off = h * 0.02 * k * (1 + 0.2 * k);
            var line = crest.Select((p, i) => new Point(p.X - off * 0.5, p.Y + off + Math.Sin(i * 0.45 + k) * 1.4)).ToList();
            details.Children.Add(new Path
            {
                Data            = Shape(line, closed: false),
                Stroke          = Feathered(ink, (byte)(64 - 4 * k)),
                StrokeThickness = 1.1,
            });
        }
        group.Children.Add(details);

        // The brinks: hairlines of light where each face turns over into shadow.
        foreach (var (line, alpha) in new[] { (crest, (byte)235), (hook, (byte)170) })
        {
            group.Children.Add(new Path
            {
                Data            = Shape(line, closed: false),
                Stroke          = Feathered(Color.FromRgb(0xFD, 0xEC, 0xC8), alpha),
                StrokeThickness = 1.6,
            });
        }

        Layer.Children.Add(group);
        return (crest[0], crest[22]);
    }

    /// <summary>
    /// Shade (or light) falling off away from an edge: <paramref name="bands"/> polygons reaching from
    /// <paramref name="edge"/> out along <paramref name="away"/> to growing distances, each faintly tinted, so they
    /// stack densest at the edge. Stacked rather than tiled: tiles have to overlap to leave no gap, and the overlap
    /// shows as a line along every seam.
    /// </summary>
    private static void StackFrom(Canvas into, IReadOnlyList<Point> edge, Vector away, double reach, Color tint,
                                  int bands, byte alpha)
    {
        away.Normalize();
        var brush = Frozen(new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B)));
        for (int k = 0; k < bands; k++)
        {
            double d = reach * Math.Pow((k + 1) / (double)bands, 1.4);
            var band = edge.Select(p => p - away * 2).ToList();
            band.AddRange(edge.Reverse().Select(p => p + away * d));
            into.Children.Add(new Path { Data = Shape(band, closed: true), Fill = brush });
        }
    }

    /// <summary>A cubic Bézier from <paramref name="a"/> to <paramref name="d"/>, as <paramref name="n"/> segments.</summary>
    private static List<Point> Bezier(Point a, Point b, Point c, Point d, int n)
    {
        var points = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n, s = 1 - t;
            points.Add(new Point(s * s * s * a.X + 3 * s * s * t * b.X + 3 * s * t * t * c.X + t * t * t * d.X,
                                 s * s * s * a.Y + 3 * s * s * t * b.Y + 3 * s * t * t * c.Y + t * t * t * d.Y));
        }
        return points;
    }

    /// <summary>
    /// A smooth curve through <paramref name="through"/>'s inner points (the first and last only steer its ends),
    /// <paramref name="perSpan"/> segments between each pair — a skyline that passes exactly where it is told.
    /// </summary>
    private static List<Point> CatmullRom(IReadOnlyList<Point> through, int perSpan)
    {
        var points = new List<Point>();
        for (int i = 1; i < through.Count - 2; i++)
        {
            Point p0 = through[i - 1], p1 = through[i], p2 = through[i + 1], p3 = through[i + 2];
            for (int j = i == 1 ? 0 : 1; j <= perSpan; j++)
            {
                double t = j / (double)perSpan, t2 = t * t, t3 = t2 * t;
                points.Add(new Point(
                    0.5 * (2 * p1.X + (p2.X - p0.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (3 * p1.X - p0.X - 3 * p2.X + p3.X) * t3),
                    0.5 * (2 * p1.Y + (p2.Y - p0.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (3 * p1.Y - p0.Y - 3 * p2.Y + p3.Y) * t3)));
            }
        }
        return points;
    }

    // ── Plumes: sand streaming off the crests, long in a gust, a wisp in the calm ─────────────────
    private void AddPlumes(IEnumerable<Point> crests, double unit, double haze, double w)
    {
        var colour = Lerp(Grain, Haze, haze * 0.5);
        foreach (var crest in crests)
        {
            double length = unit * (140 + 110 * _rng.NextDouble());
            double thick  = unit * (9 + 7 * _rng.NextDouble());
            var    plume  = new Canvas { Width = length, Height = thick * 4, Opacity = 0 };

            // Several streams fanning slightly from the brink, brightest just past it and gone by the tail.
            int streams = 3 + _rng.Next(3);
            for (int k = 0; k < streams; k++)
            {
                double sh = thick * (0.6 + 0.8 * _rng.NextDouble());
                double sl = length * (0.55 + 0.45 * _rng.NextDouble());
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(150, colour.R, colour.G, colour.B), 0.10));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(60, colour.R, colour.G, colour.B), 0.55));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1));
                g.Freeze();

                var stream = new Rectangle
                {
                    Width           = sl,
                    Height          = sh,
                    RadiusX         = sh / 2,
                    RadiusY         = sh / 2,
                    Fill            = g,
                    RenderTransform = new RotateTransform(-6 + 9 * _rng.NextDouble(), 0, sh / 2),
                };
                Canvas.SetLeft(stream, length * 0.04 * _rng.NextDouble());
                Canvas.SetTop(stream, thick * 2 - sh / 2 + (_rng.NextDouble() - 0.5) * thick);
                plume.Children.Add(stream);
            }

            // Anchored at the brink and lifting off it a little: sand leaves a crest going up before it falls.
            var stretch = new ScaleTransform(1, 1);
            var group   = new TransformGroup();
            group.Children.Add(stretch);
            group.Children.Add(new RotateTransform(-5));
            plume.RenderTransformOrigin = new Point(0, 0.5);
            plume.RenderTransform       = group;
            Canvas.SetLeft(plume, crest.X - 2);
            Canvas.SetTop(plume, crest.Y - thick * 2.4);
            Layer.Children.Add(plume);

            double across = Math.Clamp(crest.X / w, 0, 1);
            double a = _rng.NextDouble() * 6, b = _rng.NextDouble() * 6;
            FollowWind(stretch, ScaleTransform.ScaleXProperty,
                t => 0.3 + 0.85 * Math.Pow(Stir(t, across), 0.8), AmbientFrameRate);
            FollowWind(plume, OpacityProperty,
                t =>
            {
                double stir = Stir(t, across);
                return (0.14 + 0.86 * Math.Pow(stir, 0.7)) * (0.88 + 0.12 * Flutter(t, a, b));
            }, AmbientFrameRate);
        }
    }

    // ── Bushes: where they grow ───────────────────────────────────────────────────────────────────
    private List<Planting> PlanBushes(double w, double h, double floor, int n)
    {
        // One bush per stretch of width, jittered, so they are scattered rather than clumped or ranked. Depth is
        // drawn separately and leans far, because there is more far ground than near.
        var plantings = new List<Planting>();
        double slot = w / n;
        for (int i = 0; i < n; i++)
        {
            double near = Math.Pow(_rng.NextDouble(), 1.25);
            double x    = slot * (i + 0.15 + 0.7 * _rng.NextDouble());
            double y    = floor + (h - floor) * (0.07 + 0.85 * near);
            double tall = h * (0.032 + 0.095 * near) * (0.75 + 0.5 * _rng.NextDouble());
            plantings.Add(new Planting(x, y, tall, near, _rng.NextDouble() < 0.6));
        }
        return plantings.OrderBy(p => p.Y).ToList();
    }

    // ── The floor: sand, its ripples, and the mounds and shadows the bushes stand on ──────────────
    private void AddFloor(double w, double h, double floor, double unit, List<Planting> bushes)
    {
        var group = new Canvas();

        // A gently undulating top edge where the floor meets the great dune's foot.
        double p1 = _rng.NextDouble() * 6, p2 = _rng.NextDouble() * 6;
        double Edge(double x) => floor + h * 0.012 * Math.Sin(x / (w * 0.11) + p1)
                                       + h * 0.006 * Math.Sin(x / (w * 0.037) + p2);
        var outline = new List<Point>();
        for (double x = -10; x <= w + 10; x += 8) outline.Add(new Point(x, Edge(x)));
        outline.Add(new Point(w + 10, h + 2));
        outline.Add(new Point(-10, h + 2));

        var sand = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sand.GradientStops.Add(new GradientStop(Lerp(SandLit, Haze, 0.3), 0));
        sand.GradientStops.Add(new GradientStop(SandLit, 0.35));
        sand.GradientStops.Add(new GradientStop(Lerp(SandBody, SandLit, 0.35), 1));
        sand.Freeze();
        group.Children.Add(new Path { Data = Shape(outline, closed: true), Fill = sand });

        // The seam with the dune is softened by a band of haze, or the floor reads as a cut-out laid over it.
        var seam = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        seam.GradientStops.Add(new GradientStop(Color.FromArgb(0, Haze.R, Haze.G, Haze.B), 0));
        seam.GradientStops.Add(new GradientStop(Color.FromArgb(110, Haze.R, Haze.G, Haze.B), 0.5));
        seam.GradientStops.Add(new GradientStop(Color.FromArgb(0, Haze.R, Haze.G, Haze.B), 1));
        seam.Freeze();
        var seamBand = new Rectangle { Width = w, Height = h * 0.05, Fill = seam };
        Canvas.SetTop(seamBand, floor - h * 0.025);
        group.Children.Add(seamBand);

        AddRipples(group, w, h, floor, unit);

        // Each bush's cast shadow, away from the sun, and the drift of sand it has gathered at its foot.
        foreach (var b in bushes)
        {
            double bw   = b.Height * (b.Grass ? 1.5 : 1.7);
            var    fade = 1 - 0.35 * (1 - b.Nearness);

            var cast = new RadialGradientBrush();
            cast.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(80 * fade), 0x7A, 0x48, 0x22), 0));
            cast.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x7A, 0x48, 0x22), 1));
            cast.Freeze();
            var shadow = new Rectangle { Width = bw * 1.3, Height = b.Height * 0.22, Fill = cast };
            Canvas.SetLeft(shadow, b.X - bw * 0.2);
            Canvas.SetTop(shadow, b.Y - b.Height * 0.12);
            group.Children.Add(shadow);

            var heap = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
            heap.GradientStops.Add(new GradientStop(Lerp(Color.FromRgb(0xF6, 0xD2, 0x98), Haze, 0.3 * (1 - b.Nearness)), 0));
            heap.GradientStops.Add(new GradientStop(Lerp(SandBody, Haze, 0.3 * (1 - b.Nearness)), 1));
            heap.Freeze();
            var mound = new Ellipse { Width = bw * 0.95, Height = b.Height * 0.2, Fill = heap };
            Canvas.SetLeft(mound, b.X - bw * 0.475);
            Canvas.SetTop(mound, b.Y - b.Height * 0.13);
            group.Children.Add(mound);
        }

        Layer.Children.Add(group);
    }

    /// <summary>
    /// The ripple field: rows of wind ripples, each a shadow line with a lit lip just above it, spaced wider and
    /// drawn heavier toward the viewer. Rows break into lengths of differing span, because a ripple never runs
    /// the width of a field, and every length fades in and out at its ends.
    /// </summary>
    private void AddRipples(Canvas group, double w, double h, double floor, double unit)
    {
        int rows = Math.Clamp((int)((h - floor) / (10 * unit)), 16, 60);
        var ink  = Color.FromRgb(0x9C, 0x5E, 0x30);
        var lip  = Color.FromRgb(0xFB, 0xE4, 0xB8);

        for (int r = 0; r < rows; r++)
        {
            double near  = Math.Pow((r + 0.5) / rows, 1.5);
            double y0    = floor + h * 0.02 + near * (h - floor - h * 0.02);
            double amp   = (1.2 + 6.5 * near) * unit;
            double wave  = (150 + 230 * _rng.NextDouble()) * (0.6 + near);
            double slope = (_rng.NextDouble() - 0.5) * 0.05;
            double ph1   = _rng.NextDouble() * 6, ph2 = _rng.NextDouble() * 6;
            double thick = (0.7 + 2.0 * near) * unit;

            double x = -20 - _rng.NextDouble() * w * 0.2;
            while (x < w + 20)
            {
                double len = w * (0.22 + 0.45 * _rng.NextDouble());
                var row = new List<Point>();
                for (double px = x; px <= x + len; px += 10)
                {
                    double y = y0 + amp * Math.Sin(px / wave * Math.PI * 2 + ph1)
                                  + amp * 0.4 * Math.Sin(px / (wave * 0.37) * Math.PI * 2 + ph2)
                                  + slope * (px - w / 2);
                    row.Add(new Point(px, y));
                }
                x += len + w * (0.02 + 0.08 * _rng.NextDouble());
                if (row.Count < 2) continue;

                group.Children.Add(new Path
                {
                    Data            = Shape(row, closed: false),
                    Stroke          = Feathered(ink, (byte)(60 + 70 * near)),
                    StrokeThickness = thick,
                });
                group.Children.Add(new Path
                {
                    Data            = Shape(row.Select(p => new Point(p.X, p.Y - thick * 1.1)).ToList(), closed: false),
                    Stroke          = Feathered(lip, (byte)(70 + 80 * near)),
                    StrokeThickness = thick * 0.7,
                });
            }
        }
    }

    // ── Drift: sand skimming the floor, flowing with the wind and racing in its gusts ─────────────
    private void AddDrift(double w, double y, double unit, double speed, double peak)
    {
        double band  = 28 * unit;
        var    sheet = new Canvas { Width = w * 2, Height = band, Opacity = 0 };

        // One width of wisps, laid down three times — a width either side — so that whatever spills past an edge
        // reappears at the other, and the sheet can wrap by exactly one width without a seam.
        int n = (int)(w / 9);
        for (int i = 0; i < n; i++)
        {
            double len = unit * (16 + 70 * _rng.NextDouble());
            double th  = unit * (0.8 + 1.6 * _rng.NextDouble());
            double x   = _rng.NextDouble() * w;
            double top = band * (0.15 + 0.85 * Math.Pow(_rng.NextDouble(), 0.6)) - th;   // denser at the ground
            var    fill = _rng.NextDouble() < 0.55 ? Feathered(Dust, (byte)(100 + 100 * _rng.NextDouble()))
                : Feathered(Grain, (byte)(110 + 120 * _rng.NextDouble()));
            for (int copy = -1; copy <= 1; copy++)
            {
                var wisp = new Rectangle { Width = len, Height = th, RadiusX = th / 2, RadiusY = th / 2, Fill = fill };
                Canvas.SetLeft(wisp, x + copy * w);
                Canvas.SetTop(wisp, top);
                sheet.Children.Add(wisp);
            }
        }

        Canvas.SetTop(sheet, y - band);
        var move = new TranslateTransform();
        sheet.RenderTransform = move;
        Layer.Children.Add(sheet);

        // The sheet's offset is how far the air has moved — so it crawls in the calm, races in a gust and never
        // jumps between. Scaled so a whole cycle is a whole number of widths, which makes the wrap seamless too.
        double pace  = 240 * unit * speed;
        double total = _wind.TravelPerCycle;
        double laps  = Math.Max(1, Math.Round(pace * total / w));
        double k     = laps * w / total;
        FollowWind(move, TranslateTransform.XProperty, t => -w + (k * _wind.TravelAt(t)) % w, null);
        FollowWind(sheet, OpacityProperty,
            t => peak * (0.08 + 0.92 * Math.Pow(Stir(t, 0.5), 0.85)), AmbientFrameRate);
    }

    // ── Sheets: the body of a gust, a low veil of sand racing over the floor with the front ──────
    /// <summary>
    /// What a gust looks like from a distance: not lines but a sheet of sand lifted off the floor, densest at the
    /// ground and thinning upward, sweeping across the window at the front's own speed — so it passes each bush
    /// just as the bush bends. The storm keeps them coming one after another.
    /// </summary>
    private void AddSheets(double w, double h, double floor, double unit)
    {
        var    lanes = new SandLanes(3);
        double wide  = w * 0.7;

        foreach (var g in _wind.Gusts)
        {
            // Its leading edge enters at the gust's start and crosses the window in the gust's crossing time.
            double speed = w / g.Crossing;
            double y     = floor + (h - floor) * (0.35 + 0.6 * _rng.NextDouble());
            lanes.Add(new SandPass(g.Start + g.Rise * 0.3, (w + wide) / speed, new Point(-wide, y),
                                   new Point(w, y + unit * 6), 0.45 + 0.7 * g.Peak, 0));
        }
        for (double t = 10; t < _wind.Storm.End; t += 2 + 3 * _rng.NextDouble())
        {
            double s = _wind.StormAt(t);
            if (s < 0.3) continue;
            double y = floor + (h - floor) * (0.2 + 0.75 * _rng.NextDouble());
            lanes.Add(new SandPass(t, (w + wide) / (w / 2.4), new Point(-wide, y), new Point(w, y), 0.4 + 0.6 * s, 0));
        }

        foreach (var lane in lanes.Schedule(_wind.CycleSeconds))
        {
            double tall  = unit * (55 + 40 * _rng.NextDouble());
            var    sheet = new Canvas { Width = wide, Height = tall, Opacity = 0 };

            // Densest toward the leading edge and at the ground, gone at the tail and the top: one brush runs
            // along the sheet, the mask runs up it.
            var along = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            along.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBE, 0x86, 0x50), 0));
            along.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0xBE, 0x86, 0x50), 0.35));
            along.GradientStops.Add(new GradientStop(Color.FromArgb(230, 0xBE, 0x86, 0x50), 0.85));
            along.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBE, 0x86, 0x50), 1));
            along.Freeze();
            var up = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            up.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            up.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0.6));
            up.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.92));
            up.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 0, 0), 1));
            up.Freeze();
            sheet.Children.Add(new Rectangle { Width = wide, Height = tall, Fill = along, OpacityMask = up });

            // Wisps through it, so it is sand moving and not a tinted pane.
            for (int i = 0, n = (int)(wide / 18); i < n; i++)
            {
                double len = unit * (30 + 120 * _rng.NextDouble());
                double th  = unit * (1 + 2 * _rng.NextDouble());
                var wisp = new Rectangle
                {
                    Width = len, Height = th, RadiusX = th / 2, RadiusY = th / 2,
                    Fill  = Feathered(_rng.NextDouble() < 0.5 ? Grain : Dust, (byte)(120 + 110 * _rng.NextDouble())),
                };
                Canvas.SetLeft(wisp, _rng.NextDouble() * (wide - len));
                Canvas.SetTop(wisp, tall * (0.35 + 0.62 * Math.Sqrt(_rng.NextDouble())) - th);
                sheet.Children.Add(wisp);
            }

            var move = new TranslateTransform(-wide - 40, 0);
            sheet.RenderTransform = move;
            Layer.Children.Add(sheet);

            FollowWind(move, TranslateTransform.XProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].At(t).X : -wide - 40, null);
            FollowWind(move, TranslateTransform.YProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].At(t).Y - tall : 0, null);
            FollowWind(sheet, OpacityProperty, t =>
                SandLanes.Find(lane, t) is int i ? Math.Min(1, lane[i].Opacity(t)) : 0, null);
        }
    }

    // ── Bushes: drawn, then set swaying ───────────────────────────────────────────────────────────
    private void AddBushes(double w, List<Planting> bushes)
    {
        foreach (var b in bushes)
        {
            double haze = 0.35 * (1 - b.Nearness);
            double bw;
            var sprite = b.Grass ? GrassTuft(b.Height, haze, out bw) : Shrub(b.Height, haze, out bw);
            Canvas.SetLeft(sprite, b.X - bw / 2);
            Canvas.SetTop(sprite, b.Y - b.Height);

            // Bent by a skew about the root: the root stays put and the tips travel furthest, which is how a
            // plant gives to the wind. Rotating it would swing the whole bush like a signpost.
            var bend = new SkewTransform(0, 0, bw / 2, b.Height);
            sprite.RenderTransform = bend;
            Layer.Children.Add(sprite);

            double across  = Math.Clamp(b.X / w, 0, 1);
            double period  = Fit(2.6 + 2.2 * _rng.NextDouble());
            double second  = Fit(period * 0.43);
            double p1      = _rng.NextDouble() * Math.PI * 2, p2 = _rng.NextDouble() * Math.PI * 2;
            double a       = _rng.NextDouble() * 6, c = _rng.NextDouble() * 6;
            double breeze  = b.Grass ? 3.2 : 1.4;    // degrees of sway in the calm
            double give    = b.Grass ? 40 : 15;      // degrees of lean in the strongest wind; a shrub is stiffer

            FollowWind(bend, SkewTransform.AngleXProperty, t =>
            {
                double stir = Stir(t, across);
                double sway = breeze * (0.7 * Math.Sin(t / period * Math.PI * 2 + p1)
                    + 0.3 * Math.Sin(t / second * Math.PI * 2 + p2));
                double lean = give * Math.Pow(stir, 0.85) * (0.8 + 0.2 * Flutter(t, a, c));

                // The sway gives way to the lean as the wind gets up: a bush pinned by a gust only shivers.
                // Negative AngleX carries the tips to the right, downwind.
                return -(sway * (1 - 0.6 * stir) + lean);
            }, AmbientFrameRate);
        }
    }

    /// <summary>A grass tuft: blades fanning from one root, leaning a little downwind even in the calm.</summary>
    private Canvas GrassTuft(double tall, double haze, out double wide)
    {
        wide = tall * 1.5;
        var tuft = new Canvas { Width = wide, Height = tall };
        double rootX = wide / 2;

        var dark  = Color.FromRgb(0x45, 0x58, 0x2A);
        var green = Color.FromRgb(0x6F, 0x8A, 0x3B);
        var sage  = Color.FromRgb(0x9A, 0xA8, 0x60);
        var straw = Color.FromRgb(0xCC, 0xB4, 0x78);

        // Back blades first and darker, front blades last and lighter, so the tuft has a near side.
        int blades = 18 + _rng.Next(16);
        for (int i = 0; i < blades; i++)
        {
            double front  = i / (double)(blades - 1);
            double spread = _rng.NextDouble() * 2 - 1;
            double angle  = (spread * 46 + 7) * Math.PI / 180;   // fanned, and set a few degrees downwind
            double len    = tall * (0.45 + 0.55 * (1 - Math.Abs(spread) * 0.6) * (0.7 + 0.3 * _rng.NextDouble()));
            double baseX  = rootX + spread * wide * 0.07 + (_rng.NextDouble() - 0.5) * wide * 0.05;
            double bw     = tall * (0.016 + 0.016 * _rng.NextDouble());

            // A blade bows outward and its tip droops — more so the further it leans — so no two are straight lines.
            var tip  = new Point(baseX + Math.Sin(angle) * len,
                                 tall - Math.Cos(angle) * len + len * 0.12 * Math.Abs(Math.Sin(angle)));
            var ctrl = new Point(baseX + Math.Sin(angle) * len * 0.3 + len * 0.06,
                                 tall - Math.Cos(angle) * len * 0.75);

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(baseX - bw, tall), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(ctrl.X - bw * 0.6, ctrl.Y), tip, isStroked: false, isSmoothJoin: true);
                ctx.QuadraticBezierTo(new Point(ctrl.X + bw * 0.6, ctrl.Y), new Point(baseX + bw, tall),
                                      isStroked: false, isSmoothJoin: true);
            }
            g.Freeze();

            var top = Lerp(Lerp(green, straw, _rng.NextDouble() * 0.7), sage, front * 0.4);
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            fill.GradientStops.Add(new GradientStop(Lerp(Lerp(dark, green, front * 0.5), Haze, haze), 0));
            fill.GradientStops.Add(new GradientStop(Lerp(top, Haze, haze), 1));
            fill.Freeze();

            tuft.Children.Add(new Path { Data = g, Fill = fill });
        }

        AddRootLip(tuft, wide, tall, haze);
        return tuft;
    }

    /// <summary>A shrub: a dome of small leaves over a few dark twigs, shaded away from the sun.</summary>
    private Canvas Shrub(double tall, double haze, out double wide)
    {
        wide = tall * 1.7;
        var shrub = new Canvas { Width = wide, Height = tall };
        double cx = wide / 2, cy = tall * 0.56, rx = wide * 0.42, ry = tall * 0.46;

        var twig = Frozen(new SolidColorBrush(Lerp(Color.FromRgb(0x5C, 0x46, 0x2E), Haze, haze)));
        int twigs = 5 + _rng.Next(4);
        for (int i = 0; i < twigs; i++)
        {
            double a = Math.PI * (1.15 + 0.7 * _rng.NextDouble());
            shrub.Children.Add(new Line
            {
                X1 = cx + (_rng.NextDouble() - 0.5) * wide * 0.08, Y1 = tall,
                X2 = cx + Math.Cos(a) * rx * 0.85, Y2 = cy + Math.Sin(a) * ry * 0.85,
                Stroke = twig, StrokeThickness = Math.Max(0.8, tall * 0.02),
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }

        // Three passes — shade, body, light — with the light pass pulled up and to the left, toward the sun.
        (Color Colour, int Count, double Bias)[] passes =
        [
        (Color.FromRgb(0x3A, 0x4C, 0x25), 70, 0.0),
        (Color.FromRgb(0x5A, 0x74, 0x33), 90, 0.25),
        (Color.FromRgb(0x92, 0xA6, 0x58), 45, 0.6),
        ];
        foreach (var (colour, count, bias) in passes)
        {
            for (int i = 0; i < count; i++)
            {
                double a = Math.PI + Math.PI * _rng.NextDouble();       // the upper half of the dome …
                if (_rng.NextDouble() < 0.25) a = Math.PI * _rng.NextDouble() * 0.9 + 0.05;   // … and a little below
                double d = Math.Sqrt(_rng.NextDouble()) * (1 - 0.35 * bias);
                double x = cx + Math.Cos(a) * rx * d - bias * rx * 0.25;
                double y = cy + Math.Sin(a) * ry * d * (a < Math.PI ? 0.45 : 1) - bias * ry * 0.25;
                // Small and many: big round leaves read as a cartoon's, and gaps between them as a sprite's edge.
                double r = tall * (0.028 + 0.03 * _rng.NextDouble());
                var    c = Lerp(Lerp(colour, Color.FromRgb(0xB8, 0xA8, 0x6A), _rng.NextDouble() * 0.2), Haze, haze);

                var leaf = new Ellipse
                {
                    Width           = r * 2,
                    Height          = r * 2 * (0.4 + 0.3 * _rng.NextDouble()),
                    Fill            = Frozen(new SolidColorBrush(c)),
                    RenderTransform = new RotateTransform(_rng.NextDouble() * 180, r, r),
                };
                Canvas.SetLeft(leaf, x - r);
                Canvas.SetTop(leaf, y - r);
                shrub.Children.Add(leaf);
            }
        }

        AddRootLip(shrub, wide, tall, haze);
        return shrub;
    }

    /// <summary>A lip of sand over the roots, so a bush grows out of the ground rather than standing on it.</summary>
    private static void AddRootLip(Canvas bush, double wide, double tall, double haze)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Lerp(Color.FromRgb(0xF6, 0xD2, 0x98), Haze, haze), 0));
        g.GradientStops.Add(new GradientStop(Lerp(SandBody, Haze, haze), 1));
        g.Freeze();
        var lip = new Ellipse { Width = wide * 0.4, Height = tall * 0.08, Fill = g };
        Canvas.SetLeft(lip, wide * 0.3);
        Canvas.SetTop(lip, tall * 0.95);
        bush.Children.Add(lip);
    }

    // ── Dust veil: the storm's air, over the whole scene ──────────────────────────────────────────
    private void AddDustVeil(double w, double h)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(230, 0x5C, 0x3E, 0x27), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(205, 0x96, 0x64, 0x38), 0.45));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(180, 0xB4, 0x7E, 0x48), 1));
        g.Freeze();

        var veil = new Rectangle { Width = w, Height = h, Fill = g, Opacity = 0 };
        Layer.Children.Add(veil);

        // A gust raises a breath of dust too, so the storm's veil is a thing the scene already knew how to do.
        FollowWind(veil, OpacityProperty,
            t => Math.Min(1, 1.05 * Math.Pow(_wind.StormAt(t), 1.1) + 0.12 * _wind.GustAt(t, 0.5)), AmbientFrameRate);

        // The sun still somewhere in the dust: a broad gold glow over the veil, as in a sandstorm photographed
        // into the light. Without it the storm is only a brown wash — with it the air itself is lit.
        var rg = new RadialGradientBrush { Center = new Point(0.62, 0.4), GradientOrigin = new Point(0.62, 0.4), RadiusX = 0.6, RadiusY = 0.55 };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0xFF, 0xD0, 0x86), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0xF0, 0xB0, 0x68), 0.4));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE0, 0x9A, 0x58), 1));
        rg.Freeze();
        var glow = new Rectangle { Width = w, Height = h, Fill = rg, Opacity = 0 };
        Layer.Children.Add(glow);
        FollowWind(glow, OpacityProperty, t => Math.Min(1, 1.1 * _wind.StormAt(t)), AmbientFrameRate);
    }

    // ── Streaks: sand blown low and fast across the window, riding each gust's front ──────────────
    private void AddStreaks(double w, double h, double floor, double unit)
    {
        var lanes = new SandLanes(Math.Clamp((int)(w / 25.0), 30, 64));

        foreach (var g in _wind.Gusts)
        {
            // A streak sets off where the front is when it gets there, and goes about as fast.
            int n = (int)Math.Round(lanes.Count * (0.5 + 0.9 * g.Peak));
            for (int k = 0; k < n; k++)
            {
                double x0     = w * (-0.15 + 0.9 * _rng.NextDouble());
                double launch = g.Start + _rng.NextDouble() * (g.Rise + g.Hold) + Math.Clamp(x0 / w, 0, 1) * g.Crossing;
                double speed  = w / g.Crossing * (0.9 + 0.3 * _rng.NextDouble());
                double travel = w * (0.45 + 0.45 * _rng.NextDouble());
                double y0     = floor - h * 0.06 + Math.Pow(_rng.NextDouble(), 0.8) * (h - floor + h * 0.04);
                lanes.Add(new SandPass(launch, travel / speed, new Point(x0, y0),
                new Point(x0 + travel, y0 - unit * (2 + 10 * _rng.NextDouble())),
                0.3 + 0.55 * g.Peak, 0));
            }
        }

        // The storm's own streaks: more of them the harder it blows, and higher, until sand fills the whole air.
        for (double t = 1; t < _wind.Storm.End;)
        {
            double s    = _wind.StormAt(t);
            double rate = (w / 1400.0) * 26 * s * s;
            if (rate < 0.15) { t += 0.7; continue; }

            double x0     = w * (-0.2 + 1.0 * _rng.NextDouble());
            double y0     = h * (0.2 + 0.78 * Math.Pow(_rng.NextDouble(), 0.7));
            double speed  = w / 2.0 * (0.8 + 0.7 * _rng.NextDouble());
            double travel = w * (0.35 + 0.5 * _rng.NextDouble());
            lanes.Add(new SandPass(t, travel / speed, new Point(x0, y0),
            new Point(x0 + travel, y0 - unit * (4 + 16 * _rng.NextDouble())),
            0.2 + 0.6 * s, 0));
            t += (0.5 + _rng.NextDouble()) / rate;
        }

        foreach (var lane in lanes.Schedule(_wind.CycleSeconds))
        {
            double len   = unit * (70 + 170 * _rng.NextDouble());
            double thick = unit * (1.4 + 2.4 * _rng.NextDouble());

            // A body of dust with a lit core, densest toward the head and trailing off behind: a streak travels, and
            // the gradient says which way.
            var streak = new Canvas { Width = len, Height = thick * 3, Opacity = 0 };
            streak.Children.Add(Comet(len, thick * 3, 0, Dust, 200));
            streak.Children.Add(Comet(len * 0.85, thick * 0.9, thick * 1.05, Grain, 245));
            var move = new TranslateTransform(-len - 40, 0);
            streak.RenderTransform = move;
            Layer.Children.Add(streak);

            // Travelling sprites the eye follows: no frame-rate cap, or they judder.
            double park = -len - 40;
            FollowWind(move, TranslateTransform.XProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].At(t).X - len * 0.5 : park, null);
            FollowWind(move, TranslateTransform.YProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].At(t).Y - thick * 1.5 : 0, null);
            FollowWind(streak, OpacityProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].Opacity(t) : 0, null);
        }
    }

    /// <summary>One stroke of a streak: a rounded bar of <paramref name="colour"/>, thin at the tail and strongest
    /// just short of the head.</summary>
    private static Rectangle Comet(double len, double thick, double top, Color colour, byte peak)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.6), colour.R, colour.G, colour.B), 0.3));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(peak, colour.R, colour.G, colour.B), 0.8));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1));
        g.Freeze();
        var bar = new Rectangle { Width = len, Height = thick, RadiusX = thick / 2, RadiusY = thick / 2, Fill = g };
        Canvas.SetTop(bar, top);
        return bar;
    }

    // ── Swirls: an eddy of sand spun up by a gust, carried along and let go ───────────────────────
    private void AddSwirls(double w, double h, double floor, double unit)
    {
        var lanes = new SandLanes(Math.Clamp((int)(w / 600.0), 2, 4));

        foreach (var g in _wind.Gusts.Where(g => g.Swirls))
        {
            double x0     = w * (0.02 + 0.5 * _rng.NextDouble());
            double launch = g.Start + g.Rise * 0.6 + x0 / w * g.Crossing;
            lanes.Add(Eddy(launch, x0, w, h, floor, 0.45 + 0.4 * g.Peak));
        }
        for (double t = 20; t < _wind.Storm.End; t += 14 + 16 * _rng.NextDouble())
        {
            double s = _wind.StormAt(t);
            if (s > 0.4) lanes.Add(Eddy(t, w * (-0.05 + 0.6 * _rng.NextDouble()), w, h, floor, 0.3 + 0.5 * s));
        }

        foreach (var lane in lanes.Schedule(_wind.CycleSeconds))
        {
            double r     = unit * (50 + 32 * _rng.NextDouble());
            var    swirl = EddySprite(r);

            // Spun, then flattened, so it reads as a ring of sand turning on the ground seen from the side —
            // then grown and carried.
            var spin = new RotateTransform();
            var grow = new ScaleTransform(1, 1);
            var move = new TranslateTransform(-4 * r, 0);
            var all  = new TransformGroup();
            all.Children.Add(spin);
            all.Children.Add(new ScaleTransform(1, 0.42));
            all.Children.Add(grow);
            all.Children.Add(move);
            swirl.RenderTransformOrigin = new Point(0.5, 0.5);
            swirl.RenderTransform       = all;
            swirl.Opacity               = 0;
            Canvas.SetLeft(swirl, -r);
            Canvas.SetTop(swirl, -r);
            Layer.Children.Add(swirl);

            double park = -4 * r;
            FollowWind(move, TranslateTransform.XProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].At(t).X : park, null);
            FollowWind(move, TranslateTransform.YProperty, t =>
            {
                if (SandLanes.Find(lane, t) is not int i) return 0;
                var p = lane[i];
                return p.At(t).Y + Math.Sin(p.Progress(t) * Math.PI * 3) * r * 0.15;
            }, null);
            FollowWind(spin, RotateTransform.AngleProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].Turns * 360 * lane[i].Progress(t) : 0, null);
            Func<double, double> size = t =>
                SandLanes.Find(lane, t) is int i ? 0.55 + 0.7 * Math.Pow(Math.Sin(lane[i].Progress(t) * Math.PI), 0.6) : 1;
            FollowWind(grow, ScaleTransform.ScaleXProperty, size, null);
            FollowWind(grow, ScaleTransform.ScaleYProperty, size, null);
            FollowWind(swirl, OpacityProperty, t =>
                SandLanes.Find(lane, t) is int i ? lane[i].Opacity(t) : 0, null);
        }
    }

    private SandPass Eddy(double launch, double x0, double w, double h, double floor, double peak)
    {
        double y0 = floor + (h - floor) * (0.15 + 0.7 * _rng.NextDouble());
        return new SandPass(launch, 5 + 4 * _rng.NextDouble(), new Point(x0, y0),
                            new Point(x0 + w * (0.22 + 0.25 * _rng.NextDouble()), y0 - h * (0.02 + 0.06 * _rng.NextDouble())),
                            peak, (2 + 2 * _rng.NextDouble()) * (_rng.NextDouble() < 0.5 ? 1 : -1));
    }

    /// <summary>An eddy: a soft disc of dust with three arms spiralling out of it, fading as they go.</summary>
    private Canvas EddySprite(double r)
    {
        var eddy = new Canvas { Width = r * 2, Height = r * 2 };

        var disc = new RadialGradientBrush();
        disc.GradientStops.Add(new GradientStop(Color.FromArgb(130, Dust.R, Dust.G, Dust.B), 0));
        disc.GradientStops.Add(new GradientStop(Color.FromArgb(60, Dust.R, Dust.G, Dust.B), 0.5));
        disc.GradientStops.Add(new GradientStop(Color.FromArgb(0, Dust.R, Dust.G, Dust.B), 1));
        disc.Freeze();
        eddy.Children.Add(new Rectangle { Width = r * 2, Height = r * 2, Fill = disc });

        // The arms' stroke fades by distance from the centre — absolute mapping, so all three share one falloff
        // rather than each fading across its own bounds.
        // Each arm is a band of dust with a lit edge along it, as the streaks are.
        RadialGradientBrush ArmBrush(Color c, byte peak)
        {
            var b = new RadialGradientBrush
            {
                MappingMode    = BrushMappingMode.Absolute,
                Center         = new Point(r, r),
                GradientOrigin = new Point(r, r),
                RadiusX        = r,
                RadiusY        = r,
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(peak, c.R, c.G, c.B), 0.1));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }
        var body  = ArmBrush(Dust, 170);
        var light = ArmBrush(Grain, 220);

        for (int k = 0; k < 3; k++)
        {
            double start = k * Math.PI * 2 / 3 + _rng.NextDouble() * 0.4;
            var points = new List<Point>();
            for (int i = 0; i <= 24; i++)
            {
                double f = i / 24.0;
                double d = r * (0.12 + 0.85 * f);
                double a = start + f * Math.PI * 1.7;
                points.Add(new Point(r + d * Math.Cos(a), r + d * Math.Sin(a)));
            }
            var arm = Shape(points, closed: false);
            foreach (var (brush, thickness) in new[] { (body, r * 0.14), (light, r * 0.05) })
            {
                eddy.Children.Add(new Path
                {
                    Data               = arm,
                    Stroke             = brush,
                    StrokeThickness    = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap   = PenLineCap.Round,
                });
            }
        }
        return eddy;
    }

    // ── Vignette: settles the corners, and buys back contrast for the shell drawn over the scene ──
    private void AddVignette(double w, double h)
    {
        var v = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.45),
            Center         = new Point(0.5, 0.45),
            RadiusX        = 0.75,
            RadiusY        = 0.8,
        };
        v.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x4A, 0x2C, 0x14), 0));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(10, 0x4A, 0x2C, 0x14), 0.55));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(34, 0x4A, 0x2C, 0x14), 0.82));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(76, 0x3E, 0x24, 0x10), 1));
        v.Freeze();

        Layer.Children.Add(new Rectangle { Width = w, Height = h, Fill = v });
    }

    /// <summary>
    /// One pass of a sprite over the scene: it sets off at <see cref="Start"/> from <see cref="From"/> and arrives
    /// at <see cref="To"/>, fading in and out at <see cref="Peak"/> opacity; an eddy also spins <see cref="Turns"/>
    /// times on the way.
    /// </summary>
    private readonly record struct SandPass(double Start, double Duration, Point From, Point To, double Peak, double Turns)
    {
        public double End => Start + Duration;

        public double Progress(double t) => Math.Clamp((t - Start) / Duration, 0, 1);

        public Point At(double t)
        {
            double u = Progress(t);
            return new Point(From.X + (To.X - From.X) * u, From.Y + (To.Y - From.Y) * u);
        }

        /// <summary>In over the first fifth, out over the last third — sand gathers quickly and thins slowly.</summary>
        public double Opacity(double t)
        {
            double u = Progress(t);
            double fade = u < 0.2 ? Smooth(u / 0.2) : u > 0.67 ? 1 - Smooth((u - 0.67) / 0.33) : 1;
            return Peak * fade;
        }
    }

    /// <summary>
    /// A pool of sprites shared out among many passes. Each pass goes to whichever sprite is free soonest, and is
    /// dropped if none is — so a scene keeps a fixed handful of sprites however many gusts the cycle holds, and a
    /// sprite's passes never overlap, which lets a frame find the one it is on with a single binary search.
    /// </summary>
    private sealed class SandLanes(int count)
    {
        private readonly List<SandPass> _passes = [];

        public int Count => count;

        public void Add(SandPass pass) => _passes.Add(pass);

        /// <summary>Share the passes out, keeping only those over before the cycle wraps.</summary>
        public SandPass[][] Schedule(double cycle)
        {
            var lanes = new List<SandPass>[count];
            var free  = new double[count];
            for (int i = 0; i < count; i++) lanes[i] = [];

            foreach (var pass in _passes.Where(p => p.Start >= 0 && p.End < cycle).OrderBy(p => p.Start))
            {
                int soonest = 0;
                for (int i = 1; i < count; i++) if (free[i] < free[soonest]) soonest = i;
                if (free[soonest] > pass.Start) continue;
                lanes[soonest].Add(pass);
                free[soonest] = pass.End + 0.05;
            }
            return lanes.Select(l => l.ToArray()).ToArray();
        }

        /// <summary>The pass <paramref name="lane"/> is on at <paramref name="t"/>, if any.</summary>
        public static int? Find(SandPass[] lane, double t)
        {
            int lo = 0, hi = lane.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (lane[mid].Start <= t) { found = mid; lo = mid + 1; }
                else                       hi = mid - 1;
            }
            return found >= 0 && t < lane[found].End ? found : null;
        }
    }

    /// <summary>A smooth, irregular wobble in [-1, 1] — three incommensurate sines, phased per element — for the
    /// buffeting a gust gives a bush or a plume. Only ever multiplied by the stir, so its cycle never shows.</summary>
    private static double Flutter(double t, double a, double b) =>
        (Math.Sin(5.1 * t + a) + 0.6 * Math.Sin(8.3 * t + b) + 0.4 * Math.Sin(13.7 * t + a + b)) / 2.0;

    private static double Smooth(double u)
    {
        u = Math.Clamp(u, 0, 1);
        return u * u * (3 - 2 * u);
    }

    /// <summary>An open or closed polyline as one frozen geometry — every dune, ripple and arm is one of these.</summary>
    private static StreamGeometry Shape(IReadOnlyList<Point> points, bool closed)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], isFilled: closed, isClosed: closed);
            ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A horizontal gradient that fades in and out across the shape, so a ripple or a wisp has no
    /// hard ends.</summary>
    private static LinearGradientBrush Feathered(Color c, byte peak)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(peak, c.R, c.G, c.B), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Freeze();
        return g;
    }

    /// <summary>Blend <paramref name="a"/> toward <paramref name="b"/> — the depth cue for anything far off.</summary>
    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
