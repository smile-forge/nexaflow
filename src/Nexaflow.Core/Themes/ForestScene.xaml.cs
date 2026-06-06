using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nexaflow.Core.Themes;

/// <summary>
/// A self-contained animated woodland backdrop used by the Nature (forest) theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.Window</c> template in <c>Theme.Nature.xaml</c>; carries no
/// dependency on the shell or any feature. Layers a sunlit-forest gradient with a warm sun wash,
/// golden god rays + dappled light, a swaying canopy and undergrowth, drifting dust motes,
/// fluttering butterflies and the occasional gliding bird. Procedural so it adapts to the region
/// size; never participates in hit-testing.
/// </summary>
public partial class ForestScene : UserControl
{
    private readonly Random _rng = new();
    private bool _built;
    private readonly DispatcherTimer _resizeDebounce;

    public ForestScene()
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

        double w = ActualWidth, h = ActualHeight, area = w * h;
        Layer.Children.Clear();

        AddSunWash(w, h);
        AddGodRays(w, h, Math.Clamp((int)(w / 240.0), 4, 8));
        AddTrunks(w, h);
        AddDapples(w, h, Math.Clamp((int)(area / 95_000.0), 9, 20));
        AddFerns(w, h, Math.Clamp((int)(w / 200.0), 4, 9));
        AddMotes(w, h, Math.Clamp((int)(area / 20_000.0), 28, 58));
        AddButterflies(w, h, Math.Clamp((int)(w / 360.0), 2, 5));
        AddBird(w, h);
    }

    // ── Sun wash: warm light pooling in from the canopy above ──────────────────
    private void AddSunWash(double w, double h)
    {
        var rg = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.45, 0.0),
            Center         = new Point(0.45, 0.0),
            RadiusX        = 0.7,
            RadiusY        = 0.8,
        };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xFF, 0xF1, 0xC4), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(44, 0xF2, 0xDE, 0x92), 0.5));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xF2, 0xDE, 0x92), 0.82));

        var wash = new Rectangle { Width = w, Height = h, Fill = rg };
        Layer.Children.Add(wash);
        Loop(wash, OpacityProperty, 0.65, 1.0, 8, 0);
    }

    // ── God rays: warm shafts of sunlight slanting down through the trees ───────
    private void AddGodRays(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double x = w * (0.06 + 0.88 * i / Math.Max(1, n - 1));
            double topW = 50 + _rng.Next(80);
            double botW = topW * 2.6;
            double slant = (_rng.NextDouble() - 0.5) * w * 0.16;

            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(135, 0xFF, 0xF2, 0xC8), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(54, 0xFF, 0xE6, 0xA4), 0.45));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xE6, 0xA4), 1));

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
                Opacity = 0.25,
                RenderTransform = new TranslateTransform(),
            };
            Layer.Children.Add(ray);

            Loop(ray, OpacityProperty, 0.16, 0.5, 6 + _rng.Next(4), i * 0.7);
            Loop(ray.RenderTransform, TranslateTransform.XProperty, -26, 26, 9 + _rng.Next(5), i * 0.5);
        }
    }

    // ── Trunks: a couple of soft tree silhouettes for depth (lightly wooded) ───
    private void AddTrunks(double w, double h)
    {
        double[] xs = [0.13, 0.85];
        foreach (var fx in xs)
        {
            double tw = 26 + _rng.Next(20);
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x12, 0x20, 0x14), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x10, 0x1E, 0x12), 0.5));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x12, 0x20, 0x14), 1));

            var trunk = new Rectangle { Width = tw, Height = h, Fill = fill };
            Canvas.SetLeft(trunk, w * fx - tw / 2);
            Canvas.SetTop(trunk, 0);
            Layer.Children.Add(trunk);
        }
    }

    // ── Dappled light: warm patches that drift and pulse like sun through leaves ─
    private void AddDapples(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            // Mix of broad soft pools and smaller brighter spots for a real dappled texture.
            bool   bright = _rng.Next(3) == 0;
            double cw = (bright ? 50 : 110) + _rng.Next(bright ? 70 : 150);
            double ch = cw * (0.55 + _rng.NextDouble() * 0.35);
            byte   a  = (byte)(bright ? 175 : 120);

            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(a, 0xFF, 0xEC, 0xA6), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 4), 0xFF, 0xE2, 0x8E), 0.55));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xE2, 0x8E), 1));

            var dapple = new Ellipse { Width = cw, Height = ch, Fill = rg, Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(dapple, _rng.NextDouble() * w - cw / 2);
            Canvas.SetTop(dapple, h * (0.12 + 0.8 * _rng.NextDouble()) - ch / 2);
            Layer.Children.Add(dapple);

            Loop(dapple, OpacityProperty, bright ? 0.3 : 0.2, bright ? 0.85 : 0.6, 3.5 + _rng.Next(4), i * 0.45);
            Loop(dapple.RenderTransform, TranslateTransform.XProperty, -24, 24, 9 + _rng.Next(6), i * 0.6);
        }
    }

    // ── Ferns: undergrowth fronds anchored to the floor, gently swaying ────────
    private void AddFerns(double w, double h, int n)
    {
        Color[] greens =
        [
            Color.FromArgb(225, 0x1E, 0x40, 0x26),
            Color.FromArgb(225, 0x16, 0x33, 0x1D),
            Color.FromArgb(225, 0x28, 0x4E, 0x30),
        ];
        for (int i = 0; i < n; i++)
        {
            double fh = 70 + _rng.Next(90);
            double x  = w * (0.04 + 0.92 * i / (n - 1));

            var fern = new Path
            {
                Data = Geometry.Parse(
                    "M25,100 L25,30 M25,44 L11,36 M25,44 L39,36 M25,58 L9,52 M25,58 L41,52 M25,72 L11,67 M25,72 L39,67 M25,86 L14,82 M25,86 L36,82"),
                Stroke             = new SolidColorBrush(greens[i % greens.Length]),
                StrokeThickness    = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                Stretch            = Stretch.Fill,
                Width              = 50,
                Height             = fh,
                RenderTransform    = new SkewTransform { CenterX = 25, CenterY = fh },
            };
            Canvas.SetLeft(fern, x - 25);
            Canvas.SetTop(fern, h - fh);
            Layer.Children.Add(fern);

            Loop(fern.RenderTransform, SkewTransform.AngleXProperty, -4, 4, 4 + _rng.Next(3), i * 0.4);
        }
    }

    // ── Motes: tiny dust specks floating in the light, drifting and twinkling ──
    private void AddMotes(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double size = 1.5 + _rng.NextDouble() * 2.5;
            var mote = new Ellipse
            {
                Width           = size,
                Height          = size,
                Fill            = new SolidColorBrush(Color.FromArgb(230, 0xFF, 0xF4, 0xCE)),
                Opacity         = 0,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(mote, _rng.NextDouble() * w);
            Canvas.SetTop(mote, _rng.NextDouble() * h);
            Layer.Children.Add(mote);

            Loop(mote.RenderTransform, TranslateTransform.YProperty, -16, 16, 6 + _rng.Next(7), _rng.NextDouble() * 3);
            Loop(mote.RenderTransform, TranslateTransform.XProperty, -12, 12, 5 + _rng.Next(6), _rng.NextDouble() * 3);
            Loop(mote, OpacityProperty, 0.15, 0.9, 1.4 + _rng.NextDouble() * 2.2, _rng.NextDouble() * 3);
        }
    }

    // ── Butterflies: flutter across with beating wings and a wandering path ────
    private void AddButterflies(double w, double h, int n)
    {
        Color[] palette =
        [
            Color.FromArgb(235, 0xE8, 0x85, 0x2C), // monarch orange
            Color.FromArgb(235, 0x5A, 0xA8, 0xE0), // blue
            Color.FromArgb(235, 0xF0, 0xCB, 0x49), // yellow
            Color.FromArgb(235, 0xE0, 0x6C, 0x9A), // pink
        ];
        for (int i = 0; i < n; i++)
        {
            bool   toRight = _rng.Next(2) == 0;
            double scale   = 0.8 + _rng.NextDouble() * 0.7;
            double baseY   = h * (0.18 + 0.6 * _rng.NextDouble());

            var fly = MakeButterfly(palette[i % palette.Length]);
            Canvas.SetTop(fly, baseY);

            var move = new TranslateTransform();
            fly.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(toRight ? scale : -scale, scale, 10, 8), move },
            };
            Layer.Children.Add(fly);

            double startX = toRight ? -60 : w + 60;
            double endX   = toRight ? w + 60 : -60;
            double dur    = 18 + _rng.Next(16);

            var cross = new DoubleAnimation(startX, endX, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = TimeSpan.FromSeconds(_rng.NextDouble() * dur),
            };
            move.BeginAnimation(TranslateTransform.XProperty, cross);
            // Bobbing flight path — larger, more erratic than a fish.
            Loop(move, TranslateTransform.YProperty, -26, 26, 1.8 + _rng.NextDouble() * 1.6, _rng.NextDouble());
        }
    }

    private FrameworkElement MakeButterfly(Color color)
    {
        var brush = new SolidColorBrush(color);
        var edge  = new SolidColorBrush(Color.FromArgb(120, 0x20, 0x18, 0x10));
        var fly   = new Canvas { Width = 20, Height = 16 };

        // Wing group scales horizontally about the body to "flap".
        var wings = new Canvas { Width = 20, Height = 16 };
        var flap  = new ScaleTransform(1, 1, 10, 8);
        wings.RenderTransform = flap;

        void Wing(double x, double y, double wd, double ht)
        {
            var e = new Ellipse { Width = wd, Height = ht, Fill = brush, Stroke = edge, StrokeThickness = 0.5 };
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            wings.Children.Add(e);
        }
        Wing(0.5, 1.5, 9, 7);    // upper-left
        Wing(2.5, 7.5, 7, 6);    // lower-left
        Wing(10.5, 1.5, 9, 7);   // upper-right
        Wing(10.5, 7.5, 7, 6);   // lower-right

        var body = new Ellipse { Width = 2, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(235, 0x25, 0x1C, 0x12)) };
        Canvas.SetLeft(body, 9);
        Canvas.SetTop(body, 2);

        fly.Children.Add(wings);
        fly.Children.Add(body);

        var flapAnim = new DoubleAnimation(1.0, 0.35, new Duration(TimeSpan.FromSeconds(0.16)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        flap.BeginAnimation(ScaleTransform.ScaleXProperty, flapAnim);
        return fly;
    }

    // ── Bird: a distant silhouette that glides across now and then ─────────────
    private void AddBird(double w, double h)
    {
        var bird = new Path
        {
            Data            = Geometry.Parse("M0,6 Q7,0 14,6 Q21,0 28,6"),
            Stroke          = new SolidColorBrush(Color.FromArgb(150, 0x14, 0x22, 0x16)),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };
        Canvas.SetTop(bird, h * 0.16);
        var move = new TranslateTransform();
        var beat = new ScaleTransform(1, 1, 14, 3);
        bird.RenderTransform = new TransformGroup { Children = { beat, move } };
        Layer.Children.Add(bird);

        // Cross once, then stay off-screen for a while before the next pass — "the odd bird".
        double period = 26;
        var cross = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = new Duration(TimeSpan.FromSeconds(period)) };
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(-40, KeyTime.FromPercent(0.0)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(w + 40, KeyTime.FromPercent(0.45)));
        cross.KeyFrames.Add(new LinearDoubleKeyFrame(w + 40, KeyTime.FromPercent(1.0)));
        move.BeginAnimation(TranslateTransform.XProperty, cross);
        Loop(move, TranslateTransform.YProperty, -10, 10, 5, 0);
        Loop(beat, ScaleTransform.ScaleYProperty, 0.5, 1.0, 0.4, 0);
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
