using System;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Core.Themes;

public enum OceanSceneVariant
{
    /// <summary>Full reef — sunlight, coral, drifting fish, rising bubbles. For the whole window.</summary>
    Full,
    /// <summary>Slim strip — light + a few bubbles. For short regions like the AI bar.</summary>
    Strip,
}

/// <summary>
/// A self-contained animated sunlit-reef backdrop used by the Ocean theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.{Region}</c> templates in <c>Theme.Ocean.xaml</c>; carries no
/// dependency on the shell or any feature. Colours follow the Ocean HTML: a bright teal→sand water
/// column, white-cyan god rays + caustics, warm floor light pools, vivid reef fish and coral.
/// Everything is procedural so it adapts to the region size, and it never participates in hit-testing.
/// </summary>
public partial class OceanReefScene : AnimatedScene
{
    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(OceanSceneVariant),
            typeof(OceanReefScene), new PropertyMetadata(OceanSceneVariant.Full));

    public OceanSceneVariant Variant
    {
        get => (OceanSceneVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private readonly Random _rng = new();

    public OceanReefScene() => InitializeComponent();

    protected override Panel SceneLayer => Layer;

    protected override void BuildScene(double w, double h)
    {
        if (Variant == OceanSceneVariant.Full)
        {
            double area  = w * h;
            int fish     = Math.Clamp((int)(area / 200_000.0), 5, 14);
            int bubbles  = Math.Clamp((int)(area / 70_000.0), 12, 40);
            int coral    = Math.Clamp((int)(w / 230.0), 4, 10);

            AddSunGlow(w, h);
            AddGodRays(w, h, Math.Clamp((int)(w / 230.0), 4, 9));
            AddRocks(w, h);
            AddFloorCaustics(w, h);
            AddCoral(w, h, coral);
            AddFish(w, h, fish);
            AddTurtle(w, h);
            AddSeahorse(w, h);
            AddBubbles(w, h, bubbles);
        }
        else
        {
            AddGodRays(w, h, 3);
            AddBubbles(w, h, count: 10);
        }

           // a rebuild starts clocks running; honour minimise if we're hidden
    }

    // ── Sun glow: bright caustic light pouring in from the surface above ───────
    private void AddSunGlow(double w, double h)
    {
        var rg = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.0),
            Center         = new Point(0.5, 0.0),
            RadiusX        = 0.62,
            RadiusY        = 0.72,
        };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xE1, 0xFF, 0xF5), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(46, 0x96, 0xE6, 0xEB), 0.5));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x96, 0xE6, 0xEB), 0.78));
        rg.Freeze();

        var glow = new Rectangle { Width = w, Height = h, Fill = rg };
        Layer.Children.Add(glow);
        Loop(glow, OpacityProperty, 0.7, 1.0, 7, 0, AmbientFrameRate);
    }

    // ── God rays: bright shafts streaming down from the surface, drifting + breathing ─
    private void AddGodRays(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double x = w * (0.06 + 0.88 * i / Math.Max(1, n - 1));
            double topW = 55 + _rng.Next(85);
            double botW = topW * 2.6;
            double slant = (_rng.NextDouble() - 0.5) * w * 0.14;   // shafts fan out as they descend

            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xEA, 0xFF, 0xFF), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(64, 0xCD, 0xFA, 0xFF), 0.45));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xC8, 0xFF, 0xF6), 1));
            fill.Freeze();

            var ray = new Polygon
            {
                Points =
                [
                    new Point(x - topW / 2, -30),
                    new Point(x + topW / 2, -30),
                    new Point(x + slant + botW / 2, h + 30),
                    new Point(x + slant - botW / 2, h + 30),
                ],
                Fill = fill,
                Opacity = 0.3,
                RenderTransform = new TranslateTransform(),
            };
            Layer.Children.Add(ray);

            Loop(ray, OpacityProperty, 0.2, 0.6, 5 + _rng.Next(4), i * 0.7, AmbientFrameRate);
            Loop(ray.RenderTransform, TranslateTransform.XProperty, -30, 30, 8 + _rng.Next(5), i * 0.5, AmbientFrameRate);
        }
    }

    // ── Rocks: dark reef boulders the coral grows from, silhouetted on the floor ─
    private void AddRocks(double w, double h)
    {
        Color[] greys =
        [
            Color.FromArgb(240, 0x0A, 0x2E, 0x3A),
            Color.FromArgb(240, 0x0E, 0x39, 0x47),
            Color.FromArgb(240, 0x07, 0x26, 0x31),
        ];
        int n = Math.Clamp((int)(w / 240.0), 3, 8);
        for (int i = 0; i < n; i++)
        {
            double rw = 150 + _rng.Next(240);
            double rh = 55 + _rng.Next(80);
            var rock = new Path
            {
                Data    = Frozen(Geometry.Parse("M0,1 C0.12,0.12 0.4,0 0.55,0.05 C0.78,0.12 0.9,0.2 1,1 Z")),
                Fill    = Frozen(new SolidColorBrush(greys[i % greys.Length])),
                Stretch = Stretch.Fill,
                Width   = rw,
                Height  = rh,
            };
            Canvas.SetLeft(rock, w * (i / (double)n) + _rng.Next(60) - 40);
            Canvas.SetTop(rock, h - rh + _rng.Next(12));
            Layer.Children.Add(rock);
        }
    }

    // ── Floor caustics: warm pools of light shimmering on the sand ─────────────
    private void AddFloorCaustics(double w, double h)
    {
        int n = Math.Clamp((int)(w / 300.0), 2, 6);
        for (int i = 0; i < n; i++)
        {
            double cw = 180 + _rng.Next(180), ch = 70 + _rng.Next(60);
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(95, 0xFF, 0xFF, 0xF0), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xF0), 1));
            rg.Freeze();

            var pool = new Ellipse { Width = cw, Height = ch, Fill = rg, Opacity = 0 };
            Canvas.SetLeft(pool, w * (0.05 + 0.9 * i / Math.Max(1, n - 1)) - cw / 2);
            Canvas.SetTop(pool, h - ch * 0.7);
            Layer.Children.Add(pool);

            Loop(pool, OpacityProperty, 0.15, 0.55, 3 + _rng.Next(3), i * 0.6, AmbientFrameRate);
        }
    }

    // ── Coral: vivid branching silhouettes anchored to the floor, gently swaying ─
    private void AddCoral(double w, double h, int n)
    {
        Color[] tints =
        [
            Color.FromArgb(225, 0xFF, 0x7E, 0x6B), // coral red
            Color.FromArgb(225, 0xFF, 0x9E, 0x4D), // orange
            Color.FromArgb(225, 0xD8, 0x6F, 0xE0), // purple
            Color.FromArgb(225, 0xFF, 0x6F, 0x9D), // pink
            Color.FromArgb(225, 0x4F, 0xD6, 0xB8), // mint
        ];
        for (int i = 0; i < n; i++)
        {
            double ch = 60 + _rng.Next(80);
            double x  = w * (0.05 + 0.9 * i / (n - 1));

            var coral = new Path
            {
                Data = Frozen(Geometry.Parse(
                    "M25,100 L25,52 C25,38 12,38 12,20 M25,60 C25,48 40,46 40,26 M25,70 C25,62 8,58 8,42")),
                Stroke             = Frozen(new SolidColorBrush(tints[i % tints.Length])),
                StrokeThickness    = 7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                Stretch            = Stretch.Fill,
                Width              = 50,
                Height             = ch,
                RenderTransform    = new SkewTransform { CenterX = 25, CenterY = ch },
            };
            Canvas.SetLeft(coral, x - 25);
            Canvas.SetTop(coral, h - ch);
            Layer.Children.Add(coral);

            Loop(coral.RenderTransform, SkewTransform.AngleXProperty, -3.5, 3.5, 4 + _rng.Next(3), i * 0.4, AmbientFrameRate);
        }
    }

    // ── Fish: vivid reef fish drifting across, bobbing, looping off-screen ──────
    private void AddFish(double w, double h, int count)
    {
        Color[] palette =
        [
            Color.FromArgb(235, 0xFF, 0xCF, 0x5A), // yellow tang
            Color.FromArgb(235, 0x5F, 0xE3, 0xEE), // bright cyan
            Color.FromArgb(235, 0xFF, 0x8A, 0x5B), // clownfish orange
            Color.FromArgb(235, 0xFF, 0x6F, 0x9D), // pink
            Color.FromArgb(235, 0xB9, 0x8C, 0xFF), // violet
        ];
        for (int i = 0; i < count; i++)
        {
            bool   toRight = _rng.Next(2) == 0;
            double scale   = 0.7 + _rng.NextDouble() * 1.0;
            double baseY   = h * (0.10 + 0.66 * _rng.NextDouble());

            var fish = MakeFish(palette[i % palette.Length]);
            Canvas.SetTop(fish, baseY);

            var move = new TranslateTransform();
            fish.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(toRight ? scale : -scale, scale, 18, 8),
                    move,
                },
            };
            Cache(fish, scale);          // cached at the scale it is drawn at, so a big fish is not a soft one
            Layer.Children.Add(fish);

            double startX = toRight ? -140 : w + 140;
            double endX   = toRight ? w + 140 : -140;
            double dur    = 16 + _rng.Next(16);

            Canvas.SetLeft(fish, startX);   // parked at its start point; the crossing translates from there

            var cross = new DoubleAnimation(0, endX - startX, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                };
                Animate(move, TranslateTransform.XProperty, cross, phaseSeconds: _rng.NextDouble() * dur);
            Loop(move, TranslateTransform.YProperty, -10, 10, 2.4 + _rng.NextDouble() * 2, _rng.NextDouble());
        }
    }

    private static FrameworkElement MakeFish(Color color)
    {
        var brush  = Frozen(new SolidColorBrush(color));
        // A soft darker outline keeps a bright fish legible over the bright reef.
        var stroke = Frozen(new SolidColorBrush(Color.FromArgb(70, 0x06, 0x2A, 0x34)));
        var fish   = new Canvas { Width = 36, Height = 16 };

        var tail = new Polygon
        {
            Points          = [new Point(8, 8), new Point(0, 1), new Point(0, 15)],
            Fill            = brush,
            Stroke          = stroke,
            StrokeThickness = 0.75,
        };
        var body = new Ellipse { Width = 26, Height = 14, Fill = brush, Stroke = stroke, StrokeThickness = 0.75 };
        Canvas.SetLeft(body, 8);
        Canvas.SetTop(body, 1);
        var eye = new Ellipse { Width = 2.6, Height = 2.6, Fill = Brushes.Black, Opacity = 0.6 };
        Canvas.SetLeft(eye, 28);
        Canvas.SetTop(eye, 6);

        fish.Children.Add(tail);
        fish.Children.Add(body);
        fish.Children.Add(eye);
        return fish;
    }

    // ── Turtle: a single, rare visitor that cruises across, then waits off-screen ──
    // Unlike the fish (which loop back-to-back), the turtle crosses once per long cycle and is
    // parked off-screen for the rest — so a sighting feels like an occasional treat, not décor.
    private void AddTurtle(double w, double h)
    {
        Color[] shells =
        [
            Color.FromArgb(235, 0x5C, 0x8A, 0x57), // olive green
            Color.FromArgb(235, 0x47, 0x7A, 0x63), // teal green
            Color.FromArgb(235, 0x6E, 0x8B, 0x49), // moss
        ];

        bool   toRight = _rng.Next(2) == 0;
        double scale   = 0.9 + _rng.NextDouble() * 0.5;          // a touch larger than the reef fish
        // Weighted toward the lower water column (sqrt skews the fraction high) — turtles hug the reef.
        double baseY   = h * (0.28 + 0.50 * Math.Sqrt(_rng.NextDouble()));

        var turtle = MakeTurtle(shells[_rng.Next(shells.Length)]);
        Canvas.SetTop(turtle, baseY);

        var move = new TranslateTransform();
        turtle.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(toRight ? scale : -scale, scale, 28, 17),  // flip to face travel direction
                move,
            },
        };
        Cache(turtle, scale);
        Layer.Children.Add(turtle);

        double startX = toRight ? -160 : w + 160;
        double endX   = toRight ?  w + 160 : -160;
        Canvas.SetLeft(turtle, startX);                          // parked off-screen until a crossing begins

        double crossDur = 50 + _rng.Next(28);                    // a slow, deliberate cruise (fish: 16–32s)
        double gap      = 34 + _rng.Next(30);                    // long off-screen wait between crossings
        double total    = crossDur + gap;
        double crossFr   = crossDur / total;

        // Cross during the first slice of the cycle, then hold off-screen at the far edge for the rest.
        // The wrap from far-edge back to start happens off-screen, so it's never seen.
        var cross = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration       = new Duration(TimeSpan.FromSeconds(total)),
        };
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(0,             KeyTime.FromPercent(0)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(endX - startX, KeyTime.FromPercent(crossFr)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(endX - startX, KeyTime.FromPercent(1)));
        Animate(move, TranslateTransform.XProperty, cross, phaseSeconds: _rng.NextDouble() * total);

        Loop(move, TranslateTransform.YProperty, -8, 8, 3.6 + _rng.NextDouble() * 2, _rng.NextDouble());
    }

    private FrameworkElement MakeTurtle(Color shell)
    {
        var shellBrush = Frozen(new SolidColorBrush(shell));
        // Head & near flippers a shade lighter/greyer so they read against the carapace…
        var limbC      = Color.FromArgb(shell.A,
            (byte)Math.Min(255, shell.R + 28), (byte)Math.Min(255, shell.G + 26), (byte)Math.Min(255, shell.B + 22));
        var limbBrush  = Frozen(new SolidColorBrush(limbC));
        // …and the far-side pair darker, so they sit visually behind the body.
        var limbFar    = Frozen(new SolidColorBrush(Color.FromArgb(shell.A,
            (byte)(limbC.R * 0.72), (byte)(limbC.G * 0.72), (byte)(limbC.B * 0.72))));
        var stroke     = Frozen(new SolidColorBrush(Color.FromArgb(90, 0x05, 0x24, 0x2C)));
        var scute      = Frozen(new SolidColorBrush(Color.FromArgb(120,
            (byte)(shell.R * 0.6), (byte)(shell.G * 0.6), (byte)(shell.B * 0.6))));

        var turtle = new Canvas { Width = 56, Height = 34 };   // built facing right

        // A flipper centred at (cx,cy) with radii (rx,ry), tilted about its own centre.
        Ellipse Flipper(double cx, double cy, double rx, double ry, double angle, Brush fill)
        {
            var f = new Ellipse
            {
                Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke, StrokeThickness = 0.9,
                RenderTransform = new RotateTransform(angle, rx, ry),
            };
            Canvas.SetLeft(f, cx - rx); Canvas.SetTop(f, cy - ry);
            return f;
        }

        // Far-side pair — drawn first (behind the shell), darker, peeking out so the turtle reads
        // as four-limbed rather than one front-left + one back-right leg.
        turtle.Children.Add(Flipper(13, 20, 7,  3, -12, limbFar));  // far rear
        turtle.Children.Add(Flipper(41, 19, 11, 4,  12, limbFar));  // far front

        // Head + eye — drawn before the shell so the neck emerges from under the carapace's front edge.
        var head = new Ellipse { Width = 13, Height = 10, Fill = limbBrush, Stroke = stroke, StrokeThickness = 0.9 };
        Canvas.SetLeft(head, 39.5); Canvas.SetTop(head, 12);
        turtle.Children.Add(head);

        var eye = new Ellipse { Width = 2.4, Height = 2.4, Fill = Brushes.Black, Opacity = 0.6 };
        Canvas.SetLeft(eye, 47.8); Canvas.SetTop(eye, 13.8);
        turtle.Children.Add(eye);

        // Underbody — the connecting mass the flippers attach to.
        var body = new Ellipse { Width = 30, Height = 16, Fill = limbBrush };
        Canvas.SetLeft(body, 10); Canvas.SetTop(body, 13);
        turtle.Children.Add(body);

        // Carapace.
        var carapace = new Ellipse { Width = 34, Height = 24, Fill = shellBrush, Stroke = stroke, StrokeThickness = 1.2 };
        Canvas.SetLeft(carapace, 8); Canvas.SetTop(carapace, 4);
        turtle.Children.Add(carapace);

        // Scute ridges across the shell.
        turtle.Children.Add(new Path
        {
            Data               = Frozen(Geometry.Parse("M25,5 L25,27 M17,7 L18,26 M33,7 L32,26")),
            Stroke             = scute,
            StrokeThickness    = 1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        // Soft top highlight.
        var hi = new Ellipse { Width = 14, Height = 6, Fill = Frozen(new SolidColorBrush(Color.FromArgb(56, 0xFF, 0xFF, 0xFF))) };
        Canvas.SetLeft(hi, 12); Canvas.SetTop(hi, 8);
        turtle.Children.Add(hi);

        // Near rear flipper (in front of the shell, pairs with the far rear).
        turtle.Children.Add(Flipper(12, 25, 8.5, 3.5, -24, limbBrush));

        // Near front flipper — the big one; paddles slowly to sell the swim, rocking about its centre.
        var paddleRot = new RotateTransform(24, 12.5, 4.5);
        var front = new Ellipse
        {
            Width = 25, Height = 9, Fill = limbBrush, Stroke = stroke, StrokeThickness = 0.9,
            RenderTransform = paddleRot,
        };
        Canvas.SetLeft(front, 29.5); Canvas.SetTop(front, 20.5);
        turtle.Children.Add(front);
        Loop(paddleRot, RotateTransform.AngleProperty, 14, 32, 2.2 + _rng.NextDouble() * 1.2, _rng.NextDouble());

        return turtle;
    }


    // ── Seahorse: rarer still than the turtle, and drifting at half its pace ────
    // Same cross-then-park cycle as the turtle, over the same distance in twice the time — so exactly
    // half the speed — and parked off-screen far longer between crossings, making it the scarcer of the
    // two sightings.
    private void AddSeahorse(double w, double h)
    {
        Color[] tints =
        [
            Color.FromArgb(235, 0xF2, 0xB0, 0x3C), // golden
            Color.FromArgb(235, 0xE8, 0x74, 0x3C), // burnt orange
            Color.FromArgb(235, 0xE0, 0x5C, 0x7A), // rose
        ];

        bool   toRight = _rng.Next(2) == 0;
        double scale   = 0.75 + _rng.NextDouble() * 0.45;        // smaller than the turtle
        // Lower in the column than the turtle — a seahorse hovers among the coral, not in open water.
        double baseY   = h * (0.32 + 0.46 * Math.Sqrt(_rng.NextDouble()));

        var seahorse = MakeSeahorse(tints[_rng.Next(tints.Length)]);
        Canvas.SetTop(seahorse, baseY);

        var move = new TranslateTransform();
        var sway = new RotateTransform(0, 13, 23);               // the upright body rocks as it drifts
        seahorse.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(toRight ? scale : -scale, scale, 13, 23),  // flip to face travel direction
                sway,
                move,
            },
        };
        Cache(seahorse, scale);
        Layer.Children.Add(seahorse);

        double startX = toRight ? -160 : w + 160;
        double endX   = toRight ?  w + 160 : -160;
        Canvas.SetLeft(seahorse, startX);                        // parked off-screen until a crossing begins

        // Same ±160 span the turtle crosses, so doubling its 50–78s makes the rate half, not merely slower.
        double crossDur = 100 + _rng.Next(56);
        double gap      = 150 + _rng.Next(120);                  // far longer off-screen than the turtle's 34–64s
        double total    = crossDur + gap;

        var cross = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration       = new Duration(TimeSpan.FromSeconds(total)),
        };
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(0,             KeyTime.FromPercent(0)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(endX - startX, KeyTime.FromPercent(crossDur / total)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(endX - startX, KeyTime.FromPercent(1)));
        Animate(move, TranslateTransform.XProperty, cross, phaseSeconds: _rng.NextDouble() * total);

        Loop(move, TranslateTransform.YProperty, -12, 12, 5 + _rng.NextDouble() * 3, _rng.NextDouble());
        Loop(sway, RotateTransform.AngleProperty, -6, 6, 3.2 + _rng.NextDouble() * 1.6, _rng.NextDouble());
    }

    private FrameworkElement MakeSeahorse(Color tint)
    {
        var bodyBrush = Frozen(new SolidColorBrush(tint));
        // Fins are the translucent part of a seahorse — barely more than tinted water.
        var finBrush  = Frozen(new SolidColorBrush(Color.FromArgb(150,
            (byte)Math.Min(255, tint.R + 40), (byte)Math.Min(255, tint.G + 40), (byte)Math.Min(255, tint.B + 40))));
        var stroke    = Frozen(new SolidColorBrush(Color.FromArgb(90, 0x05, 0x24, 0x2C)));
        var ridge     = Frozen(new SolidColorBrush(Color.FromArgb(110,
            (byte)(tint.R * 0.62), (byte)(tint.G * 0.62), (byte)(tint.B * 0.62))));

        var seahorse = new Canvas { Width = 26, Height = 46 };   // built upright, facing right

        // Dorsal fin — behind the body, on the back. It is what actually propels a seahorse, so it
        // flutters continuously while the body itself barely moves.
        var finRot = new RotateTransform(0, 4, 1);               // hinged where it meets the back
        var dorsal = new Ellipse { Width = 4, Height = 10, Fill = finBrush, RenderTransform = finRot };
        Canvas.SetLeft(dorsal, 9.5); Canvas.SetTop(dorsal, 17);
        seahorse.Children.Add(dorsal);
        Loop(finRot, RotateTransform.AngleProperty, -16, 16, 0.5 + _rng.NextDouble() * 0.25, _rng.NextDouble());

        // The spine, in three strokes of falling weight — a fat chest, a tapering tail, then the curl.
        // A single uniform stroke cannot taper, and closing a filled silhouette around the curl is fiddly;
        // thin-to-thick draw order hides each join under the heavier segment above it.
        Path Segment(string data, double thickness) => new()
        {
            Data               = Frozen(Geometry.Parse(data)),
            Stroke             = bodyBrush,
            StrokeThickness    = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };

        seahorse.Children.Add(Segment("M12.5,29 C14,33 15.5,35.5 13,38 C10.5,40.5 6.8,39.6 6.4,36.8 C6.1,34.4 8.6,33 10,34.6", 3.2));
        seahorse.Children.Add(Segment("M14,20 C12,24 12,26 12.6,29.4", 5.4));
        seahorse.Children.Add(Segment("M14.5,9 C18.2,12.6 18,16.6 14.4,20.4", 8));

        // Bony rings across the body.
        seahorse.Children.Add(new Path
        {
            Data               = Frozen(Geometry.Parse("M12.6,13.4 L17.6,14.6 M11.8,17.6 L17,18 M11.6,22 L15.4,22.6")),
            Stroke             = ridge,
            StrokeThickness    = 1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        // Pectoral fin just behind the cheek — the small one that steers.
        var pecRot = new RotateTransform(0, 1, 1);
        var pec = new Ellipse { Width = 4.6, Height = 5.6, Fill = finBrush, RenderTransform = pecRot };
        Canvas.SetLeft(pec, 17.8); Canvas.SetTop(pec, 12.8);
        seahorse.Children.Add(pec);
        Loop(pecRot, RotateTransform.AngleProperty, -18, 12, 0.6 + _rng.NextDouble() * 0.3, _rng.NextDouble());

        // Snout, then coronet, then the head — the head's outline lands last and covers both joins.
        seahorse.Children.Add(new Polygon
        {
            Points          = [new Point(15, 7.6), new Point(23.4, 10.2), new Point(23.4, 11.5), new Point(15, 12.2)],
            Fill            = bodyBrush,
            Stroke          = stroke,
            StrokeThickness = 0.8,
        });

        seahorse.Children.Add(new Path
        {
            Data               = Frozen(Geometry.Parse("M11.6,5 L10.7,2.5 M14.2,4 L14.2,1.8 M16.8,4.8 L18,2.7")),
            Stroke             = bodyBrush,
            StrokeThickness    = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        var head = new Ellipse { Width = 11.5, Height = 10, Fill = bodyBrush, Stroke = stroke, StrokeThickness = 0.9 };
        Canvas.SetLeft(head, 8.5); Canvas.SetTop(head, 4);
        seahorse.Children.Add(head);

        var eye = new Ellipse { Width = 2, Height = 2, Fill = Brushes.Black, Opacity = 0.6 };
        Canvas.SetLeft(eye, 15); Canvas.SetTop(eye, 7.4);
        seahorse.Children.Add(eye);

        return seahorse;
    }

    // ── Bubbles: bright highlighted spheres rising, wobbling, fading, recycling ─
    private void AddBubbles(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double size = 3 + _rng.Next(9);
            var fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center         = new Point(0.5, 0.5),
                RadiusX        = 0.5,
                RadiusY        = 0.5,
            };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(235, 0xFF, 0xFF, 0xFF), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(55, 0xBE, 0xEF, 0xF4), 0.6));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBE, 0xEF, 0xF4), 1));
            fill.Freeze();

            var bubble = new Ellipse
            {
                Width           = size,
                Height          = size,
                Stroke          = Frozen(new SolidColorBrush(Color.FromArgb(150, 0xE6, 0xFF, 0xFF))),
                StrokeThickness = 1,
                Fill            = fill,
                Opacity         = 0,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(bubble, _rng.NextDouble() * w);
            Canvas.SetTop(bubble, h);
            Layer.Children.Add(bubble);

            double dur  = 5 + _rng.Next(7);
            double phase = _rng.NextDouble() * dur;   // rise and fade share one phase, so a bubble fades as it climbs

            var rise = new DoubleAnimation(0, -(h + 40), new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                };
                Animate(bubble.RenderTransform, TranslateTransform.YProperty, rise, phaseSeconds: phase);
                Loop(bubble.RenderTransform, TranslateTransform.XProperty, -6, 6, 1.4 + _rng.NextDouble() * 1.6, _rng.NextDouble());

            var fade = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                Duration       = new Duration(TimeSpan.FromSeconds(dur)),
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.15)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            Animate(bubble, OpacityProperty, fade, phaseSeconds: phase);
        }
    }
}
