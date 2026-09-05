using System;
using System.Collections.Generic;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Core.Themes;

/// <summary>
/// A self-contained animated polar-dusk backdrop used by the Arctic theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.Window</c> template in <c>Theme.Arctic.xaml</c>; carries no
/// dependency on the shell or any feature. The reference photograph's composition, built procedurally
/// and in depth order: stars and an aurora still holding at the zenith, cirrus and cloud banks drifting
/// over a low sun, three graded ranges on the horizon, then a sea of pack ice and icebergs creeping
/// across a shimmering sun path. Adapts to the region size; never hit-tests.
/// <para>
/// <b>Depth is the organising idea.</b> Nothing is placed at a "distance" that only means smaller —
/// every layer also loses contrast toward <see cref="Haze"/>, gains a lighter sunward face, and moves
/// more slowly. That triple is what separates a stack of shapes from a scene, and it is why each
/// range and each berg takes a haze factor rather than just a size.
/// </para>
/// <para>
/// Everything here is <em>ambient</em> motion — nothing crosses the window in less than two minutes —
/// so every clock is capped to <see cref="AnimatedScene.AmbientFrameRate"/>. The judder that cap
/// normally causes needs motion the eye can track; a berg moves about five pixels a second.
/// </para>
/// <para>
/// <b>Static detail is grouped, animated detail is not.</b> The base caches every direct child of
/// <c>SceneLayer</c> as a texture, so the ranges, the star field and the distant ice each go in as a
/// single Canvas and cost one texture rather than a hundred. Anything that animates stays a top-level
/// child with only its root transform or opacity moving — an animation nested inside a cached group
/// would invalidate that whole texture every frame, which is worse than not caching it at all. That
/// split is also why a berg's reflection is drawn into the berg and left still: the shimmer passing
/// over it does the rippling, more cheaply and more convincingly than moving the reflection would.
/// </para>
/// </summary>
public partial class ArcticScene : AnimatedScene
{
    /// <summary>Where the sea meets the sky, as a fraction of region height. The sky gradient in the
    /// XAML is authored to reach its horizon colour here.</summary>
    private const double HorizonFraction = 0.42;

    /// <summary>Where the sun sits behind the range — centre-right, as in the reference.</summary>
    private const double SunFraction = 0.58;

    /// <summary>
    /// The sun is right of centre and every berg spends most of its crossing left of it, so the light
    /// comes from the right for effectively the whole field. Fixed rather than computed per berg:
    /// a berg drifts the full width, so a per-position choice would have to flip mid-crossing, and
    /// one consistent light direction is what makes a set of separate sprites read as one scene.
    /// </summary>
    private const bool LitFromLeft = false;

    /// <summary>What distance blends toward: the warm grey-violet of a low-sun haze. Lerping a berg's
    /// or a peak's colours toward this is the whole depth cue — far things lose contrast, not size.</summary>
    private static readonly Color Haze = Color.FromRgb(0xB0, 0x9C, 0x9E);

    /// <summary>
    /// Where the sea meets the sky: the colour BOTH sides of the horizon arrive at. The range's feet
    /// dissolve into it and the sea's first gradient stop is it, so the two meet on one tone instead of
    /// on an edge. Fading each side toward its own idea of "distant" is what put a drawn-looking stripe
    /// across the full width — they have to agree on the colour, not merely both go pale.
    /// </summary>
    private static readonly Color Horizon = Color.FromRgb(0x6C, 0x7A, 0x86);

    /// <summary>One band of the range, from the furthest back to the nearest. Grouped as a record so
    /// the three read as one progression — rock darkening, haze clearing, peaks rising — rather than
    /// as three call sites that happen to have been given consistent numbers.</summary>
    private readonly record struct Ridge(
        Color Rock, double HazeAmount, double HeightFraction, double SpanDivisor, byte SnowAlpha, bool Gullies);

    private static readonly Ridge[] Ranges =
    [
        new(Color.FromRgb(0x5C, 0x6B, 0x84), 0.66, 0.40, 130, 64, false),
        new(Color.FromRgb(0x3A, 0x4A, 0x61), 0.38, 0.52, 200, 128, false),
        new(Color.FromRgb(0x21, 0x2F, 0x41), 0.00, 0.64, 320, 215, true),
    ];

    private readonly Random _rng = new();

    public ArcticScene() => InitializeComponent();

    protected override Panel SceneLayer => Layer;

    protected override void BuildScene(double w, double h)
    {
        double hz   = h * HorizonFraction;
        double sunX = w * SunFraction;
        double area = w * h;

        // Strict back-to-front, with two deliberate exceptions noted where they happen: the horizon
        // haze goes over the range's feet, and the shimmer goes over the bergs' reflections.
        AddStars(w, hz, Math.Clamp((int)(area / 14_000.0), 40, 110));
        AddAurora(w, hz);
        AddSunGlow(w, hz, sunX);
        AddCirrus(w, hz, Math.Clamp((int)(w / 260.0), 3, 7));
        AddClouds(w, hz, Math.Clamp((int)(w / 300.0), 3, 7));

        foreach (var ridge in Ranges) AddRange(w, hz, sunX, ridge);

        AddSea(w, h, hz);
        AddSwell(w, h, hz, Math.Clamp((int)((h - hz) / 90.0), 3, 8));
        AddHorizonHaze(w, hz);            // over the range's feet: that is what makes them distant
        AddPackIce(w, h, hz, Math.Clamp((int)(w / 34.0), 18, 54));
        AddSunPath(w, h, hz, sunX);
        AddBergs(w, h, hz, Math.Clamp((int)(area / 105_000.0), 5, 14));
        AddShimmer(w, h, hz, Math.Clamp((int)(area / 38_000.0), 12, 34));
        AddFloes(w, h, hz, Math.Clamp((int)(w / 320.0), 3, 7));
        AddVignette(w, h);
    }

    // ── Stars: still out at the zenith, drowned by the low sun before they reach the horizon ──
    private void AddStars(double w, double hz, int n)
    {
        // The still ones go in as one group and cost one texture; only the few that twinkle need to
        // be their own child. A sky where every star pulses does not read as night, it reads as noise.
        var field = new Canvas();

        for (int i = 0; i < n; i++)
        {
            double y    = hz * 0.94 * Math.Pow(_rng.NextDouble(), 1.9);   // crowded toward the zenith
            double fade = 1 - y / (hz * 0.94);
            double r    = 0.5 + _rng.NextDouble() * 1.2;
            byte   a    = (byte)(24 + 170 * fade * (0.35 + _rng.NextDouble() * 0.65));

            var star = new Ellipse
            {
                Width  = r * 2,
                Height = r * 2,
                Fill   = Frozen(new SolidColorBrush(Color.FromArgb(a, 0xE8, 0xEE, 0xFF))),
            };
            Canvas.SetLeft(star, _rng.NextDouble() * w);
            Canvas.SetTop(star, y);

            if (_rng.NextDouble() < 0.16)
            {
                Layer.Children.Add(star);
                Loop(star, OpacityProperty, 0.2, 1.0, 2.5 + _rng.NextDouble() * 3.5,
                     _rng.NextDouble() * 4, AmbientFrameRate);
            }
            else
            {
                field.Children.Add(star);
            }
        }

        Layer.Children.Insert(0, field);
    }

    // ── Aurora: two faint curtains high in the sky, where it is still dark enough to see them ──
    private void AddAurora(double w, double hz)
    {
        // Kept to the upper sky, and to either side of where the sun sits (SunFraction, 0.58). An aurora
        // needs a dark sky and this one has exactly that at the zenith; draped over the sunset it would
        // only look wrong.
        double[] centres = [w * 0.20, w * 0.78];

        for (int c = 0; c < centres.Length; c++)
        {
            var curtain = new Canvas { Opacity = 0 };
            double cw    = w * (0.16 + _rng.NextDouble() * 0.12);
            double top   = hz * 0.04;
            double tall  = hz * (0.34 + _rng.NextDouble() * 0.22);
            int    bands = Math.Clamp((int)(cw / 7.0), 12, 34);

            // Vertical striations rather than one soft blob: the ribbing IS what an aurora looks
            // like, and each band is a plain gradient the cache turns into a couple of pixels.
            for (int i = 0; i < bands; i++)
            {
                var band = AuroraBand(i / (double)(bands - 1), cw / bands, tall);
                Canvas.SetLeft(band, i * (cw / bands));   // bands overlap slightly; each is 1.35 slots wide
                curtain.Children.Add(band);
            }

            Canvas.SetLeft(curtain, centres[c] - cw / 2);
            Canvas.SetTop(curtain, top);

            var sway = new SkewTransform { CenterX = cw / 2, CenterY = tall };
            curtain.RenderTransform = sway;
            Layer.Children.Add(curtain);

            // Slow: a curtain that visibly waves is a screensaver. This one only has to not be still.
            Loop(sway, SkewTransform.AngleXProperty, -7, 7, 26 + _rng.Next(14), c * 7.0, AmbientFrameRate);
            Loop(curtain, OpacityProperty, 0.18, 0.62, 17 + _rng.Next(9), c * 5.0, AmbientFrameRate);
        }
    }

    private Rectangle AuroraBand(double across, double bw, double tall)
    {
        // Green at the foot rising through cyan to a violet crown is the real colour order, and it is
        // what stops the curtain reading as a plain green smear.
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x8E, 0x6B, 0xE0), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(26, 0x8E, 0x6B, 0xE0), 0.16));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(44, 0x57, 0xC8, 0xE8), 0.48));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(58, 0x3F, 0xE0, 0xA8), 0.80));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x3F, 0xE0, 0xA8), 1));
        g.Freeze();

        // Ragged heights across the curtain, tallest in the middle, so it has an edge rather than a hem.
        double envelope = Math.Sin(Math.Clamp(across, 0, 1) * Math.PI);
        double height   = tall * (0.35 + 0.65 * envelope) * (0.72 + _rng.NextDouble() * 0.28);

        var band = new Rectangle { Width = bw * 1.35, Height = height, Fill = g };
        Canvas.SetTop(band, tall - height);
        return band;
    }

    // ── Sun glow: the low sun itself stays hidden behind the range; only its bloom shows ──────
    private void AddSunGlow(double w, double hz, double sunX)
    {
        double rx = w * 0.34, ry = hz * 0.95;
        var rg = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center         = new Point(0.5, 0.5),
        };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(210, 0xFF, 0xE0, 0xB0), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0xFF, 0xBE, 0x84), 0.34));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE8, 0x9E, 0x74), 1));
        rg.Freeze();

        var glow = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = rg };
        Canvas.SetLeft(glow, sunX - rx);
        Canvas.SetTop(glow, hz - ry);
        Layer.Children.Add(glow);
        Loop(glow, OpacityProperty, 0.78, 1.0, 9, 0, AmbientFrameRate);
    }

    // ── Cirrus: thin high wisps, the layer between the stars and the cloud banks ──────────────
    private void AddCirrus(double w, double hz, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double y  = hz * (0.10 + 0.42 * _rng.NextDouble());
            double cw = w * (0.18 + _rng.NextDouble() * 0.30);
            double ch = 5 + _rng.NextDouble() * 11;
            double lit = 1 - Math.Min(1, y / (hz * 0.72));

            var wisp = new Rectangle
            {
                Width           = cw,
                Height          = ch,
                RadiusX         = ch / 2,
                RadiusY         = ch / 2,
                Fill            = Feathered(Lerp(Color.FromRgb(0x9E, 0x8E, 0xB4),
                                                 Color.FromRgb(0xF0, 0xBE, 0x9A), lit), 38),
                Opacity         = 0.35 + _rng.NextDouble() * 0.35,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetTop(wisp, y);
            Canvas.SetLeft(wisp, -cw);
            Layer.Children.Add(wisp);

            // Higher air moves faster than the banks below it — the sky gets a parallax of its own.
            double dur = 140 + _rng.Next(150);
            var drift = new DoubleAnimation(0, w + cw * 2, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Animate(wisp.RenderTransform, TranslateTransform.XProperty, drift,
                    AmbientFrameRate, _rng.NextDouble() * dur);
        }
    }

    // ── Clouds: soft banks drifting one way across the sky, lit warm low down, lilac up high ──
    private void AddClouds(double w, double hz, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double s     = 0.9 + _rng.NextDouble() * 1.6;
            double baseY = hz * (0.08 + 0.62 * _rng.NextDouble());

            // Height in the sky IS the light: a bank near the horizon catches the sun, one at the
            // zenith only catches the dusk. One lerp does the whole job.
            double lit  = 1 - Math.Min(1, baseY / (hz * 0.78));
            var    tint = Lerp(Color.FromRgb(0x8E, 0x7C, 0x9E), Color.FromRgb(0xE9, 0xB0, 0x8C), lit);

            var cloud = new Canvas { Opacity = 0.34 + _rng.NextDouble() * 0.26 };

            // Each puff is a flattened ellipse with a gentle falloff, and there are enough of them
            // overlapping to lose their own outlines. Five round puffs at a firm alpha read as five
            // discs rather than one bank, and the giveaway is being able to count them.
            void Puff(double cx, double cy, double r, double squash)
            {
                var rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(118, tint.R, tint.G, tint.B), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(62, tint.R, tint.G, tint.B), 0.46));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
                rg.Freeze();
                var e = new Ellipse { Width = r * 2.4, Height = r * 2 * squash, Fill = rg };
                Canvas.SetLeft(e, cx - r * 1.2);
                Canvas.SetTop(e, cy - r * squash);
                cloud.Children.Add(e);
            }

            // Drawn at final size rather than through a ScaleTransform, so the base's 1x cache is the
            // right one and a big bank is not a soft one.
            int puffs = 8 + _rng.Next(5);
            for (int p = 0; p < puffs; p++)
            {
                double f  = p / (double)(puffs - 1);
                double cx = (24 + 236 * f) * s;
                double cy = (34 - 9 * Math.Sin(f * Math.PI) + (_rng.NextDouble() - 0.5) * 9) * s;
                double r  = (13 + 20 * Math.Sin(f * Math.PI) + _rng.NextDouble() * 7) * s;
                Puff(cx, cy, r, 0.62 + _rng.NextDouble() * 0.24);
            }

            double cw = 300 * s;
            Canvas.SetTop(cloud, baseY);
            Canvas.SetLeft(cloud, -cw);
            var move = new TranslateTransform();
            cloud.RenderTransform = move;
            Layer.Children.Add(cloud);

            // All one direction — weather has a wind. Speed varies, and that variation is the parallax.
            double dur = 190 + _rng.Next(230);
            var drift = new DoubleAnimation(0, w + cw * 2, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Animate(move, TranslateTransform.XProperty, drift, AmbientFrameRate, _rng.NextDouble() * dur);
        }
    }

    // ── One band of the range: silhouette, sunward and shaded faces, snow, and a lit ridge ────
    private void AddRange(double w, double hz, double sunX, Ridge r)
    {
        double maxH   = hz * r.HeightFraction;
        int    ridges = Math.Clamp((int)(w / r.SpanDivisor), 3, 14);
        var    rock   = Lerp(r.Rock, Haze, r.HazeAmount);

        // Vertices alternate peak, saddle, peak … between the two base corners, and are kept so every
        // face, cap and highlight can be built ON the silhouette rather than approximated over it.
        // Sitting free triangles on top of the peaks was the first attempt and read as stickers: their
        // edges had no reason to line up with the slopes underneath, and they didn't.
        var verts  = new List<Point> { new(-40, hz) };
        var peakIx = new List<int>();
        double span = (w + 80) / ridges;
        for (int i = 0; i < ridges; i++)
        {
            double x    = -40 + span * (i + 0.5) + (_rng.NextDouble() - 0.5) * span * 0.5;
            double prom = 0.46 + _rng.NextDouble() * 0.54;
            peakIx.Add(verts.Count);
            verts.Add(new Point(x, hz - maxH * prom));

            // The saddles cut close to the waterline, which is what lets the band behind show through
            // the gaps rather than being buried behind one continuous wall of rock.
            if (i < ridges - 1)
                verts.Add(new Point(x + span * 0.5 + (_rng.NextDouble() - 0.5) * span * 0.2,
                                    hz - maxH * (0.04 + _rng.NextDouble() * 0.20)));
        }
        verts.Add(new Point(w + 40, hz));

        // One Canvas for the whole band: nothing in a range moves, so it costs a single cached texture
        // however much detail goes into it. That is what makes the detail affordable.
        var band = new Canvas { Opacity = 1.0 - r.HazeAmount * 0.42 };
        band.Children.Add(new Polygon { Points = new PointCollection(verts), Fill = Frozen(new SolidColorBrush(rock)) });

        // Cold light first, warm cast second. Lerping rock straight at the sunlight colour turns a blue-grey
        // mountain khaki — a sunlit snow face is a paler COLD tone that has been warmed, not a warm tone.
        var litRock   = Frozen(new SolidColorBrush(
            Lerp(Lerp(rock, Color.FromRgb(0xA8, 0xBE, 0xD6), 0.17), Color.FromRgb(0xE8, 0xB4, 0x88), 0.07)));
        var shadeRock = Frozen(new SolidColorBrush(Color.FromArgb(38, 0x08, 0x10, 0x1E)));
        var snow      = Frozen(new SolidColorBrush(Color.FromArgb(r.SnowAlpha, 0xC8, 0xDD, 0xEA)));
        var snowShade = Frozen(new SolidColorBrush(Color.FromArgb((byte)(r.SnowAlpha * 0.55), 0x8E, 0xA8, 0xC0)));
        var rim       = Frozen(new SolidColorBrush(Color.FromArgb((byte)(40 + 60 * (1 - r.HazeAmount)), 0xE8, 0xA8, 0x70)));
        var gully     = Frozen(new SolidColorBrush(Color.FromArgb(46, 0x08, 0x0E, 0x1A)));

        foreach (int p in peakIx)
        {
            Point peak = verts[p], left = verts[p - 1], right = verts[p + 1];
            double drop = hz - peak.Y;
            if (drop < 6) continue;

            bool  sunRight = peak.X < sunX;
            Point litTo    = sunRight ? right : left;
            Point shadeTo  = sunRight ? left  : right;

            // Two faces per massif, each a quad from its ridge edge down to the waterline. This is the
            // whole reason the range stops reading as a cut-out: a silhouette has one tone, a mountain
            // has a side the light reaches and a side it doesn't.
            band.Children.Add(new Polygon
            {
                Points = [peak, litTo, new Point(litTo.X, hz), new Point(peak.X, hz)],
                Fill   = litRock,
            });
            band.Children.Add(new Polygon
            {
                Points = [peak, shadeTo, new Point(shadeTo.X, hz), new Point(peak.X, hz)],
                Fill   = shadeRock,
            });

            // Snow: the cap corners ride down the actual slopes, so the cap IS the top of this
            // mountain, and a sagging midpoint gives the snowline something other than a straight hem.
            double f  = 0.26 + _rng.NextDouble() * 0.18;
            Point  cl = Between(peak, left, f), cr = Between(peak, right, f);
            Point  sag = new((cl.X + cr.X) / 2, Math.Max(cl.Y, cr.Y) + drop * 0.11);
            band.Children.Add(new Polygon { Points = [peak, cr, sag, cl], Fill = snow });

            // The snow's own shaded half, split down the fall line — the cap gets the same two-tone
            // treatment as the rock, or it reads as a flat white triangle pasted on a shaded mountain.
            band.Children.Add(new Polygon
            {
                Points = [peak, sag, sunRight ? cl : cr],
                Fill   = snowShade,
            });

            if (r.Gullies)
            {
                // Two gullies down the near massifs. Enough to break the faces up; more would read as
                // hatching rather than as terrain.
                for (int g = 0; g < 3; g++)
                {
                    double t = 0.22 + g * 0.24 + _rng.NextDouble() * 0.12;
                    Point  a = Between(peak, shadeTo, t * 0.45);
                    // Fading out well above the waterline. Run to the base and a gully stops being a crease
                    // in a slope and becomes a slot cut through the mountain.
                    band.Children.Add(new Polyline
                    {
                        Points          = [a, new Point(a.X + drop * 0.10, a.Y + drop * 0.52)],
                        Stroke          = gully,
                        StrokeThickness = Math.Max(1, drop * 0.022),
                    });
                }
            }

            // Sunlight on the edge that faces it — a thin line along the real slope, not a wedge
            // beside it. Thick and round-capped, this reads as a pipe lying on the ridge.
            band.Children.Add(new Polyline
            {
                Points             = [peak, Between(peak, litTo, 0.66)],
                Stroke             = rim,
                StrokeThickness    = Math.Max(1.1, maxH * 0.016),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
            });
        }

        // Aerial perspective at the waterline. Dark rock meeting a lighter sea on a crisp line reads as a
        // cut-out however well the peaks are shaded — distance has to arrive at the FOOT of a range, not
        // only in its colour. Scaled by the band's own haze, so the furthest dissolves most.
        double fade  = maxH * 0.85;
        var    foot  = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foot.GradientStops.Add(new GradientStop(Color.FromArgb(0, Horizon.R, Horizon.G, Horizon.B), 0));
        foot.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(96 + 120 * r.HazeAmount), Horizon.R, Horizon.G, Horizon.B), 1));
        foot.Freeze();

        var dissolve = new Rectangle { Width = w + 80, Height = fade };
        dissolve.Fill = foot;
        Canvas.SetLeft(dissolve, -40);
        Canvas.SetTop(dissolve, hz - fade);
        band.Children.Add(dissolve);

        Layer.Children.Add(band);
    }

    // ── The sea: still, dark, and darkening toward the viewer so text over it stays readable ──
    private void AddSea(double w, double h, double hz)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        // The first two stops are the horizon, not the sea: water at the limit of sight is the colour of
        // the air in front of it. Opening on the sea's own blue put a hue change exactly on the horizon
        // line, which no amount of haze over the top could hide.
        g.GradientStops.Add(new GradientStop(Horizon, 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0x68, 0x7A), 0.04));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x33, 0x54, 0x68), 0.12));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x42, 0x54), 0.35));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x32, 0x43), 0.65));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x24, 0x34), 1));
        g.Freeze();

        var sea = new Rectangle { Width = w, Height = h - hz, Fill = g };
        Canvas.SetTop(sea, hz);
        Layer.Children.Add(sea);
    }

    // ── Swell: broad, barely-there bands across the sea, the tonal layer under the shimmer ────
    private void AddSwell(double w, double h, double hz, int n)
    {
        var swell = new Canvas();
        double depth = h - hz;

        for (int i = 0; i < n; i++)
        {
            double t  = (i + 0.5) / n;
            double y  = hz + depth * Math.Pow(t, 1.35);
            double bh = depth * (0.05 + 0.09 * t);

            // Alternating lighter and darker bands, each feathered at both ends so no band has a
            // start or a finish. Static, and worth far more than its cost: a plain gradient sea
            // reads as paper, and this is what gives it a surface without asking for a single clock.
            bool light = i % 2 == 0;
            var band = new Rectangle
            {
                Width  = w * 1.2,
                Height = bh,
                Fill   = Feathered(light ? Color.FromRgb(0x6E, 0x9C, 0xB4) : Color.FromRgb(0x0A, 0x1A, 0x28),
                                   (byte)(light ? 20 : 34)),
            };
            Canvas.SetLeft(band, -w * 0.1);
            Canvas.SetTop(band, y);
            swell.Children.Add(band);
        }

        Layer.Children.Add(swell);
    }

    // ── Horizon haze: the band of atmosphere the sky and the sea meet through ─────────────────
    // Without it the two gradients butt together in a hard line across the full width, which reads as
    // a seam rather than a horizon — and the range's feet sit on it too crisply to be miles away.
    private void AddHorizonHaze(double w, double hz)
    {
        double band = hz * 0.62;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE4, 0xB4, 0x8A), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(38, 0xE4, 0xB4, 0x8A), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xC8, 0x9C, 0x86), 1));
        g.Freeze();

        // Centred ON the horizon, not above it. Straddling the seam is the entire job — sat above it,
        // the sea's top edge stays a bright drawn line across the full width of the window.
        var haze = new Rectangle { Width = w, Height = band, Fill = g };
        Canvas.SetTop(haze, hz - band / 2);
        Layer.Children.Add(haze);
        Loop(haze, OpacityProperty, 0.75, 1.0, 13, 0, AmbientFrameRate);
    }

    // ── Pack ice: the scatter of small floes out at the horizon that makes the sea an ice field ──
    private void AddPackIce(double w, double h, double hz, int n)
    {
        // Static and heavily hazed. These are far enough off that drift would be invisible, so they
        // buy their depth for one cached texture and no clocks at all.
        var field = new Canvas();
        double depth = h - hz;

        for (int i = 0; i < n; i++)
        {
            double t  = Math.Pow(_rng.NextDouble(), 2.2);        // hard against the horizon
            double y  = hz + depth * t * 0.20;
            double fw = 6 + t * 44 + _rng.Next(8);
            double fh = Math.Max(1.4, fw * (0.10 + _rng.NextDouble() * 0.08));
            double haze = 0.80 - t * 1.6;

            var slab = new Polygon
            {
                Points =
                [
                    new Point(0, fh * 0.55), new Point(fw * 0.24, 0),
                    new Point(fw * 0.78, fh * 0.16), new Point(fw, fh * 0.62), new Point(fw * 0.4, fh),
                ],
                Fill = Frozen(new SolidColorBrush(Lerp(Color.FromRgb(0xDA, 0xEE, 0xF8), Haze,
                                                       Math.Clamp(haze, 0, 0.8)))),
                Opacity = 0.5 + 0.5 * Math.Min(1, t * 3),
            };
            Canvas.SetLeft(slab, _rng.NextDouble() * w);
            Canvas.SetTop(slab, y);
            field.Children.Add(slab);
        }

        Layer.Children.Add(field);
    }

    // ── Sun path: the pool of low light on the water, and the glints that break it up ─────────
    private void AddSunPath(double w, double h, double hz, double sunX)
    {
        double topW = w * 0.05, botW = w * 0.34, depth = h - hz;

        // Two nested pools rather than the obvious wedge polygon. A polygon's sides stay visibly
        // straight over the sea however faint the fill is, and a hard-edged triangle of light is the
        // one thing a sun path never looks like — but a single soft ellipse still shows its own rim,
        // so the falloff is split across two of different sizes and neither edge has anywhere to read.
        for (int i = 0; i < 2; i++)
        {
            double scale = i == 0 ? 1.0 : 0.52;
            var pool = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0),
                Center         = new Point(0.5, 0),
                RadiusX        = 0.5,
                RadiusY        = 0.9,
            };
            pool.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(i == 0 ? 54 : 74), 0xFF, 0xCE, 0x92), 0));
            pool.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(i == 0 ? 18 : 26), 0xF2, 0xB4, 0x7C), 0.45));
            pool.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xD8, 0x9A, 0x70), 1));
            pool.Freeze();

            // A Rectangle, not an Ellipse. An ellipse filled with a radial gradient shows its own outline
            // anywhere the gradient still has alpha where the shape stops, and over a dark sea that read as a
            // drawn arc across the water. A rectangle has no boundary inside the gradient's visible range.
            var glow = new Rectangle { Width = botW * 1.6 * scale, Height = depth * 1.8 * scale, Fill = pool };
            Canvas.SetLeft(glow, sunX - botW * 0.8 * scale);
            Canvas.SetTop(glow, hz);
            Layer.Children.Add(glow);
            Loop(glow, OpacityProperty, 0.6, 1.0, 8 + i * 3, i * 2.0, AmbientFrameRate);
        }

        // Broken glints within the path. Distributed by t^1.7 so they crowd toward the horizon, which
        // is the perspective — the same span of sea covers less height the further off it is.
        int n = Math.Clamp((int)(depth / 22.0), 12, 34);
        for (int i = 0; i < n; i++)
        {
            double t  = (i + _rng.NextDouble()) / n;
            double y  = hz + depth * Math.Pow(t, 1.7);
            double sw = (topW + (botW - topW) * t) * (0.35 + _rng.NextDouble() * 0.6);
            double sh = 1.5 + t * 3;

            var streak = new Rectangle
            {
                Width           = sw,
                Height          = sh,
                RadiusX         = sh / 2,
                RadiusY         = sh / 2,
                Fill            = Feathered(Color.FromRgb(0xFF, 0xD9, 0xA4), 175),
                Opacity         = 0,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(streak, sunX - sw / 2 + (_rng.NextDouble() - 0.5) * sw * 0.5);
            Canvas.SetTop(streak, y);
            Layer.Children.Add(streak);

            Loop(streak, OpacityProperty, 0.10, 0.80, 2.4 + _rng.NextDouble() * 3.4,
                 _rng.NextDouble() * 4, AmbientFrameRate);
            Loop(streak.RenderTransform, TranslateTransform.XProperty,
                 -(4 + t * 14), 4 + t * 14, 4 + _rng.NextDouble() * 4, _rng.NextDouble() * 4, AmbientFrameRate);
        }
    }

    // ── Icebergs: the slow ones. Drawn far to near, each with a still mirrored reflection ──────
    private void AddBergs(double w, double h, double hz, int count)
    {
        double depth = h - hz;
        for (int i = 0; i < count; i++)
        {
            // Spread evenly through the depth range, then jitter — so no two sit at the same distance
            // and the far ones are genuinely far rather than randomly clustered.
            double d      = Math.Clamp((i + 0.5) / count + (_rng.NextDouble() - 0.5) / count * 0.8, 0, 1);
            double waterY = hz + depth * (0.04 + 0.72 * d * d);      // quadratic: perspective
            double bh     = depth * (0.030 + 0.185 * d);
            double bw     = bh * (1.15 + _rng.NextDouble() * 0.95);
            double haze   = 0.72 * (1 - d);

            var berg = MakeBerg(bw, bh, haze, d);
            Canvas.SetTop(berg, waterY - bh);
            Canvas.SetLeft(berg, -bw - 60);

            var move = new TranslateTransform();
            berg.RenderTransform = move;
            Layer.Children.Add(berg);

            // Near bergs cross faster than far ones — the parallax that makes the field read as deep. The
            // absolute speed is the brief though, not the ratio: the nearest takes a quarter of an hour to
            // cross and the furthest closer to half, which is drift you notice having happened rather than
            // drift you can watch. Roughly a pixel a second.
            double dur = 1400 - 700 * d + _rng.Next(400);
            var drift = new DoubleAnimation(0, w + bw * 2 + 120, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Animate(move, TranslateTransform.XProperty, drift, AmbientFrameRate, _rng.NextDouble() * dur);
            Loop(move, TranslateTransform.YProperty, -2.2, 2.2,
                 15 + _rng.NextDouble() * 13, _rng.NextDouble() * 12, AmbientFrameRate);
        }
    }

    /// <summary>
    /// One berg, built as a solid rather than a silhouette: a mass behind, the main block split down a
    /// slanted fall line into a lit and a shaded plane, a top facet tilting toward the viewer, a wet
    /// band at the waterline, a cast shadow and the reflection — all in one canvas, so the caller
    /// animates a single transform and the whole thing caches as one texture.
    /// <para>
    /// <paramref name="nearness"/> (0 at the horizon, 1 in the foreground) gates the detail, and that is
    /// the point of it: a distant berg is a shaded silhouette because that is all the eye could resolve,
    /// while a near one has to show which way its surfaces face or it reads as a cut-out standing in the
    /// water. Giving every berg the full treatment flattens the field just as surely as giving none of
    /// them it — the depth is in the DIFFERENCE between the near ones and the far ones.
    /// </para>
    /// <para>
    /// Nothing inside animates: the shimmer drawn over the sea is what breaks the reflection up.
    /// </para>
    /// </summary>
    private Canvas MakeBerg(double bw, double bh, double haze, double nearness)
    {
        Color Ice(byte r, byte g, byte b) => Lerp(Color.FromRgb(r, g, b), Haze, haze);
        byte  Fade(double a) => (byte)Math.Clamp(a * (1 - haze * 0.7), 0, 255);

        // Faceted, not rounded: an ice mass calves along flat planes, so straight runs between the
        // peaks with a notch between each pair is the whole silhouette.
        int ridges = 2 + _rng.Next(3);
        var xs     = new double[ridges];
        var ys     = new double[ridges];
        int peakIx = _rng.Next(ridges);
        for (int i = 0; i < ridges; i++)
        {
            xs[i] = bw * (i + 0.5 + (_rng.NextDouble() - 0.5) * 0.55) / ridges;
            double prom = i == peakIx ? 1.0 : 0.46 + _rng.NextDouble() * 0.34;
            ys[i] = bh * (1 - prom);
        }

        var pts = new List<Point> { new(0, bh) };
        for (int i = 0; i < ridges; i++)
        {
            pts.Add(new Point(xs[i], ys[i]));
            if (i < ridges - 1)
                pts.Add(new Point((xs[i] + xs[i + 1]) / 2, bh * (1 - (0.30 + _rng.NextDouble() * 0.22))));
        }
        pts.Add(new Point(bw, bh));

        int   peakAt = 1 + 2 * peakIx;
        Point peak   = pts[peakAt];
        var   canvas = new Canvas { Width = bw, Height = bh * 2 };

        if (nearness > 0.30)
        {
            // A second, lower mass set back and to one side. Two overlapping silhouettes at different
            // values is the cheapest depth there is, and the one thing a single outline can never fake:
            // an object with a front and a back, rather than a shape with an edge.
            var backPts = new PointCollection();
            foreach (var p in pts)
                backPts.Add(new Point(bw * 0.24 + p.X * 0.70, bh - (bh - p.Y) * 0.62));

            canvas.Children.Add(new Polygon
            {
                Points  = backPts,
                Fill    = Frozen(new SolidColorBrush(Lerp(Ice(0x8F, 0xC0, 0xD6), Haze, 0.30))),
                Opacity = 0.92,
            });
            canvas.Children.Add(new Polygon
            {
                Points  = backPts,
                Fill    = Frozen(new SolidColorBrush(Color.FromArgb(Fade(38), 0x2C, 0x5E, 0x7E))),
                Opacity = 0.6,
            });

            // Cast shadow on the water, thrown away from the sun. Ice on a bare surface floats above
            // it; ice with a shadow sits in it.
            var cast = new RadialGradientBrush();
            cast.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(74), 0x07, 0x1C, 0x2C), 0));
            cast.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x07, 0x1C, 0x2C), 1));
            cast.Freeze();

            double cwid = bw * 1.05, chgt = Math.Max(5, bh * 0.20);
            var shadow = new Ellipse { Width = cwid, Height = chgt, Fill = cast };
            Canvas.SetLeft(shadow, (bw - cwid) / 2 + (LitFromLeft ? 1 : -1) * bw * 0.28);
            Canvas.SetTop(shadow, bh - chgt * 0.35);
            canvas.Children.Add(shadow);
        }

        // The shelf under the waterline: nine-tenths of a berg is down there, and showing a little of
        // it through the water is what stops the ice looking like it is resting on the surface.
        var shelf = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shelf.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(92), 0x6F, 0xB6, 0xCE), 0));
        shelf.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(30), 0x4A, 0x8C, 0xAA), 0.62));
        shelf.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x3A, 0x74, 0x92), 1));
        shelf.Freeze();
        canvas.Children.Add(new Polygon
        {
            Points =
            [
                new Point(-bw * 0.18, bh), new Point(bw * 1.18, bh),
                new Point(bw * 0.88, bh + bh * 0.34), new Point(bw * 0.12, bh + bh * 0.30),
            ],
            Fill = shelf,
        });

        var body = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        body.GradientStops.Add(new GradientStop(Ice(0xF2, 0xFB, 0xFF), 0));
        body.GradientStops.Add(new GradientStop(Ice(0xCD, 0xEB, 0xF5), 0.35));
        body.GradientStops.Add(new GradientStop(Ice(0x8F, 0xC6, 0xDA), 0.72));
        body.GradientStops.Add(new GradientStop(Ice(0x5E, 0x9C, 0xB8), 1));
        body.Freeze();
        canvas.Children.Add(new Polygon { Points = new PointCollection(pts), Fill = body });

        // The fall line runs from the summit to the waterline, offset toward the shaded side so it is
        // NOT vertical. That slant is what turns two flat halves into two planes meeting at an edge —
        // a vertical split just reads as the shape having been cut down the middle.
        var fall = new Point(peak.X + (LitFromLeft ? 1 : -1) * bw * 0.13, bh);

        var shaded = new PointCollection();
        for (int i = 0; i <= peakAt; i++) shaded.Add(pts[i]);
        shaded.Add(fall);

        var sunlit = new PointCollection();
        for (int i = peakAt; i < pts.Count; i++) sunlit.Add(pts[i]);
        sunlit.Add(fall);

        canvas.Children.Add(new Polygon
        {
            Points = LitFromLeft ? sunlit : shaded,
            Fill   = Frozen(new SolidColorBrush(Color.FromArgb(Fade(120), 0x3C, 0x74, 0x94))),
        });
        canvas.Children.Add(new Polygon
        {
            Points = LitFromLeft ? shaded : sunlit,
            Fill   = Frozen(new SolidColorBrush(Color.FromArgb(Fade(70), 0xFF, 0xFF, 0xFF))),
        });

        if (nearness > 0.25)
        {
            // The top facet: a quad hanging off the crest, tilted toward the viewer. This is the surface
            // you would be looking down onto, and it is the difference between a wedge and a block.
            Point tl = Between(peak, pts[peakAt - 1], 0.42), tr = Between(peak, pts[peakAt + 1], 0.42);
            canvas.Children.Add(new Polygon
            {
                Points =
                [
                    peak, tr,
                    new Point((tl.X + tr.X) / 2 - bw * 0.04, Math.Max(tl.Y, tr.Y) + bh * 0.17),
                    tl,
                ],
                Fill = Frozen(new SolidColorBrush(Color.FromArgb(Fade(150), 0xF8, 0xFD, 0xFF))),
            });

            // The wet band: ice darkens where the sea has been over it, and the line it stops at is the
            // near face turning under. Inset at the top so it stays inside the silhouette.
            var wet = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            wet.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x2E, 0x6A, 0x8C), 0));
            wet.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(86), 0x2E, 0x6A, 0x8C), 1));
            wet.Freeze();
            canvas.Children.Add(new Polygon
            {
                Points =
                [
                    new Point(bw * 0.10, bh - bh * 0.24), new Point(bw * 0.90, bh - bh * 0.20),
                    new Point(bw, bh), new Point(0, bh),
                ],
                Fill = wet,
            });
        }

        // A snow crown on the upper third: wind-packed snow sits brighter and bluer than the ice under
        // it, and the break between them is most of what makes a berg read as ice rather than as rock.
        canvas.Children.Add(new Polygon
        {
            Points =
            [
                peak,
                new Point(peak.X + bw * 0.16, peak.Y + bh * 0.30),
                new Point(peak.X - bw * 0.02, peak.Y + bh * 0.24),
                new Point(peak.X - bw * 0.17, peak.Y + bh * 0.33),
            ],
            Fill = Frozen(new SolidColorBrush(Color.FromArgb(Fade(150), 0xFA, 0xFE, 0xFF))),
        });

        // Crevasses down the shaded face. Thin, few, and only on the dark side — on the lit side they
        // would have to be highlights instead, and two sets of marks is one more than the shape needs.
        var crevasse = Frozen(new SolidColorBrush(Color.FromArgb(Fade(64), 0x2E, 0x63, 0x84)));
        int cuts = nearness > 0.5 ? 3 : 2;
        for (int c = 0; c < cuts; c++)
        {
            double t = 0.26 + c * 0.22 + _rng.NextDouble() * 0.10;
            double x = peak.X + (LitFromLeft ? bw - peak.X : -peak.X) * t;
            canvas.Children.Add(new Polyline
            {
                Points          = [new Point(x, peak.Y + bh * t * 0.5), new Point(x + bw * 0.02, bh)],
                Stroke          = crevasse,
                StrokeThickness = Math.Max(0.8, bw * 0.012),
            });
        }

        // Waterline wash — the bright collar where ice displaces water.
        var wash = new RadialGradientBrush();
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(126), 0xDF, 0xF3, 0xFA), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xDF, 0xF3, 0xFA), 1));
        wash.Freeze();
        double washW = bw * 1.3, washH = Math.Max(4, bh * 0.22);
        var collar = new Ellipse { Width = washW, Height = washH, Fill = wash };
        Canvas.SetLeft(collar, (bw - washW) / 2);
        Canvas.SetTop(collar, bh - washH / 2);
        canvas.Children.Add(collar);

        // Reflection: the same silhouette mirrored about the waterline, fading out downward.
        var mirror = new PointCollection();
        foreach (var p in pts) mirror.Add(new Point(p.X, 2 * bh - p.Y));

        var refl = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        refl.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(78), 0xB8, 0xDE, 0xEE), 0));
        refl.GradientStops.Add(new GradientStop(Color.FromArgb(Fade(24), 0x9A, 0xC6, 0xDA), 0.45));
        refl.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x7A, 0xAC, 0xC4), 1));
        refl.Freeze();
        canvas.Children.Add(new Polygon { Points = mirror, Fill = refl });

        return canvas;
    }

    // ── Shimmer: light broken across the whole sea. Drawn after the bergs, so it also cuts across
    //    their reflections — which is what a rippled surface actually does to one. ──────────────
    private void AddShimmer(double w, double h, double hz, int n)
    {
        double depth = h - hz;
        for (int i = 0; i < n; i++)
        {
            double t  = Math.Pow(_rng.NextDouble(), 1.6);      // crowded toward the horizon
            double y  = hz + depth * t;
            double sw = 18 + t * 100 + _rng.Next(40);
            double sh = 1 + t * 2.2;

            var streak = new Rectangle
            {
                Width           = sw,
                Height          = sh,
                RadiusX         = sh / 2,
                RadiusY         = sh / 2,
                Fill            = Feathered(Color.FromRgb(0xCF, 0xE9, 0xF5), 105),
                Opacity         = 0,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(streak, _rng.NextDouble() * (w + sw) - sw / 2);
            Canvas.SetTop(streak, y);
            Layer.Children.Add(streak);

            Loop(streak, OpacityProperty, 0.08, 0.62, 2.8 + _rng.NextDouble() * 4,
                 _rng.NextDouble() * 5, AmbientFrameRate);
            Loop(streak.RenderTransform, TranslateTransform.XProperty,
                 -(3 + t * 10), 3 + t * 10, 5 + _rng.NextDouble() * 5, _rng.NextDouble() * 5, AmbientFrameRate);
        }
    }

    // ── Floes: flat pancake ice in the near foreground, the layer closest to the viewer ────────
    private void AddFloes(double w, double h, double hz, int n)
    {
        double depth = h - hz;
        for (int i = 0; i < n; i++)
        {
            double d  = 0.72 + 0.26 * _rng.NextDouble();
            double y  = hz + depth * d;
            double fw = depth * (0.11 + 0.19 * _rng.NextDouble());
            double fh = fw * (0.10 + _rng.NextDouble() * 0.09);

            var top = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            top.GradientStops.Add(new GradientStop(Color.FromArgb(240, 0xE9, 0xF7, 0xFD), 0));
            top.GradientStops.Add(new GradientStop(Color.FromArgb(235, 0xB4, 0xDC, 0xEC), 0.55));
            top.GradientStops.Add(new GradientStop(Color.FromArgb(230, 0x7C, 0xB2, 0xCC), 1));
            top.Freeze();

            // An irregular slab rather than an ellipse — pack ice breaks along straight edges.
            var floe = new Canvas { Width = fw, Height = fh * 2 };
            floe.Children.Add(new Polygon
            {
                Points =
                [
                    new Point(fw * 0.06, fh * 0.42), new Point(fw * 0.28, 0),
                    new Point(fw * 0.72, fh * 0.10), new Point(fw, fh * 0.52),
                    new Point(fw * 0.74, fh), new Point(fw * 0.24, fh * 0.94),
                ],
                Fill            = top,
                Stroke          = Frozen(new SolidColorBrush(Color.FromArgb(110, 0xE4, 0xF5, 0xFC))),
                StrokeThickness = 1,
            });

            // A ridge across the slab, where two floes froze together. One line, and the flat white
            // lozenge becomes a piece of ice with a history.
            floe.Children.Add(new Polyline
            {
                Points          = [new Point(fw * 0.14, fh * 0.62), new Point(fw * 0.58, fh * 0.30),
                                   new Point(fw * 0.9, fh * 0.5)],
                Stroke          = Frozen(new SolidColorBrush(Color.FromArgb(90, 0x6E, 0xA6, 0xC2))),
                StrokeThickness = Math.Max(0.8, fh * 0.10),
            });

            var shadow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(95, 0x9C, 0xC8, 0xDC), 0));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x7A, 0xAC, 0xC4), 1));
            shadow.Freeze();
            var refl = new Rectangle { Width = fw * 0.86, Height = fh * 0.9, Fill = shadow };
            Canvas.SetLeft(refl, fw * 0.07);
            Canvas.SetTop(refl, fh);
            floe.Children.Add(refl);

            Canvas.SetTop(floe, y);
            Canvas.SetLeft(floe, -fw - 40);
            var move = new TranslateTransform();
            floe.RenderTransform = move;
            Layer.Children.Add(floe);

            double dur = 620 - 200 * d + _rng.Next(200);
            var drift = new DoubleAnimation(0, w + fw * 2 + 80, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Animate(move, TranslateTransform.XProperty, drift, AmbientFrameRate, _rng.NextDouble() * dur);
            Loop(move, TranslateTransform.YProperty, -1.6, 1.6,
                 11 + _rng.NextDouble() * 9, _rng.NextDouble() * 9, AmbientFrameRate);
        }
    }

    // ── Vignette: the last layer, and the one that earns its place twice over ─────────────────
    // It settles the corners the way a photograph's does, and it buys back contrast for whatever the
    // shell draws on top — the page is translucent here, so the scene's own edges are the text's
    // background. Cheap: one static element, no clocks.
    private void AddVignette(double w, double h)
    {
        var v = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.42),
            Center         = new Point(0.5, 0.42),
            RadiusX        = 0.72,
            RadiusY        = 0.78,
        };
        v.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x05, 0x0D, 0x16), 0));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(14, 0x05, 0x0D, 0x16), 0.55));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(46, 0x05, 0x0D, 0x16), 0.82));
        v.GradientStops.Add(new GradientStop(Color.FromArgb(96, 0x04, 0x0A, 0x13), 1));
        v.Freeze();

        Layer.Children.Add(new Rectangle { Width = w, Height = h, Fill = v });
    }

    /// <summary>A horizontal gradient that fades in and out across the shape, so a glint on the water
    /// has no hard ends. Every streak and band in the scene is one of these.</summary>
    private static LinearGradientBrush Feathered(Color c, byte peak)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(peak, c.R, c.G, c.B), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Freeze();
        return g;
    }

    /// <summary>A point a fraction of the way along the segment a→b. Used to place snow and sunlight
    /// ON a ridge's own edges, which is the difference between a snowcap and a sticker.</summary>
    private static Point Between(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Blend <paramref name="a"/> toward <paramref name="b"/> — the depth cue for anything
    /// far off, which loses contrast against the haze rather than merely getting smaller.</summary>
    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
