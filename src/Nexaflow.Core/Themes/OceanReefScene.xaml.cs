using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

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
public partial class OceanReefScene : UserControl
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
    private bool _built;
    private readonly DispatcherTimer _resizeDebounce;

    public OceanReefScene()
    {
        InitializeComponent();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Build(); };
        SizeChanged += OnSizeChanged;
        Unloaded    += (_, _) => { _resizeDebounce.Stop(); _built = false; Layer.Children.Clear(); };
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
        Layer.Children.Clear();

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
            AddBubbles(w, h, bubbles);
        }
        else
        {
            AddGodRays(w, h, 3);
            AddBubbles(w, h, count: 10);
        }
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

        var glow = new Rectangle { Width = w, Height = h, Fill = rg };
        Layer.Children.Add(glow);
        Loop(glow, OpacityProperty, 0.7, 1.0, 7, 0);
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

            Loop(ray, OpacityProperty, 0.2, 0.6, 5 + _rng.Next(4), i * 0.7);
            Loop(ray.RenderTransform, TranslateTransform.XProperty, -30, 30, 8 + _rng.Next(5), i * 0.5);
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
                Data    = Geometry.Parse("M0,1 C0.12,0.12 0.4,0 0.55,0.05 C0.78,0.12 0.9,0.2 1,1 Z"),
                Fill    = new SolidColorBrush(greys[i % greys.Length]),
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

            var pool = new Ellipse { Width = cw, Height = ch, Fill = rg, Opacity = 0 };
            Canvas.SetLeft(pool, w * (0.05 + 0.9 * i / Math.Max(1, n - 1)) - cw / 2);
            Canvas.SetTop(pool, h - ch * 0.7);
            Layer.Children.Add(pool);

            Loop(pool, OpacityProperty, 0.15, 0.55, 3 + _rng.Next(3), i * 0.6);
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
                Data = Geometry.Parse(
                    "M25,100 L25,52 C25,38 12,38 12,20 M25,60 C25,48 40,46 40,26 M25,70 C25,62 8,58 8,42"),
                Stroke             = new SolidColorBrush(tints[i % tints.Length]),
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

            Loop(coral.RenderTransform, SkewTransform.AngleXProperty, -3.5, 3.5, 4 + _rng.Next(3), i * 0.4);
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
            Layer.Children.Add(fish);

            double startX = toRight ? -140 : w + 140;
            double endX   = toRight ? w + 140 : -140;
            double dur    = 16 + _rng.Next(16);

            // Park the fish off-screen at its start point until the crossing actually begins —
            // otherwise the delayed (BeginTime) animation leaves it sitting at canvas x=0 (the left
            // edge) for up to a full crossing, which reads as "stuck on the left" at startup/resize.
            Canvas.SetLeft(fish, startX);

            var cross = new DoubleAnimation(0, endX - startX, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = TimeSpan.FromSeconds(_rng.NextDouble() * dur),
            };
            move.BeginAnimation(TranslateTransform.XProperty, cross);
            Loop(move, TranslateTransform.YProperty, -10, 10, 2.4 + _rng.NextDouble() * 2, _rng.NextDouble());
        }
    }

    private static FrameworkElement MakeFish(Color color)
    {
        var brush  = new SolidColorBrush(color);
        // A soft darker outline keeps a bright fish legible over the bright reef.
        var stroke = new SolidColorBrush(Color.FromArgb(70, 0x06, 0x2A, 0x34));
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

            var bubble = new Ellipse
            {
                Width           = size,
                Height          = size,
                Stroke          = new SolidColorBrush(Color.FromArgb(150, 0xE6, 0xFF, 0xFF)),
                StrokeThickness = 1,
                Fill            = fill,
                Opacity         = 0,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(bubble, _rng.NextDouble() * w);
            Canvas.SetTop(bubble, h);
            Layer.Children.Add(bubble);

            double dur  = 5 + _rng.Next(7);
            var begin   = TimeSpan.FromSeconds(_rng.NextDouble() * dur);

            var rise = new DoubleAnimation(0, -(h + 40), new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = begin,
            };
            bubble.RenderTransform.BeginAnimation(TranslateTransform.YProperty, rise);
            Loop(bubble.RenderTransform, TranslateTransform.XProperty, -6, 6, 1.4 + _rng.NextDouble() * 1.6, _rng.NextDouble());

            var fade = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = begin,
                Duration       = new Duration(TimeSpan.FromSeconds(dur)),
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.15)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            bubble.BeginAnimation(OpacityProperty, fade);
        }
    }

    // ── Shared: a forever, auto-reversing eased oscillation ────────────────────
    private static void Loop(IAnimatable target, DependencyProperty prop,
                             double from, double to, double seconds, double beginSeconds)
        => target.BeginAnimation(prop, new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime      = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
}
