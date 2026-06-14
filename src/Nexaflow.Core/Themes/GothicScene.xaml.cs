using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nexaflow.Core.Themes;

public enum GothicSceneVariant
{
    /// <summary>Full night — moon, stars, clouds, the tower skyline, fog and circling bats. Whole window.</summary>
    Full,
    /// <summary>Slim strip — a faint moon glow, a few stars and a couple of bats. For short regions.</summary>
    Strip,
}

/// <summary>
/// A self-contained animated midnight-cathedral backdrop used by the Gothic theme. Instantiated by
/// <c>ThemedRegion</c> via the <c>Scene.Window</c> template in <c>Theme.Gothic.xaml</c>; carries no
/// dependency on the shell or any feature. Layers a bruised-purple night sky with a haloed full moon,
/// faint twinkling stars, slow-drifting clouds, a silhouetted gothic tower skyline (battlements,
/// pointed spires, a handful of arcane-lit lancet windows), low drifting fog and a colony of bats
/// flapping across the dark. Everything is procedural so it adapts to the region size; it never
/// participates in hit-testing.
/// </summary>
public partial class GothicScene : UserControl
{
    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(GothicSceneVariant),
            typeof(GothicScene), new PropertyMetadata(GothicSceneVariant.Full));

    public GothicSceneVariant Variant
    {
        get => (GothicSceneVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private readonly Random _rng = new();
    private bool _built;
    private readonly DispatcherTimer _resizeDebounce;

    public GothicScene()
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

        // The moon anchors the composition up in the right sky; the tower rises beneath it on the left
        // of centre so the silhouette reads backlit. Both scale with the region.
        double moonX = w * 0.74, moonY = h * 0.20;
        double moonR = Math.Clamp(Math.Min(w, h) * 0.085, 26, 88);

        if (Variant == GothicSceneVariant.Full)
        {
            double area  = w * h;
            int stars    = Math.Clamp((int)(area / 9_000.0), 30, 110);
            int clouds   = Math.Clamp((int)(w / 300.0), 3, 6);
            int bats     = Math.Clamp((int)(w / 200.0), 6, 14);

            AddMoon(moonX, moonY, moonR);
            AddStars(w, h, stars);
            AddTowerBackdropGlow(w, h);
            AddSkyline(w, h);
            AddTower(w, h);
            AddClouds(w, h, clouds);
            AddFog(w, h);
            AddBats(w, h, bats);
        }
        else
        {
            AddMoon(moonX, moonY, moonR * 0.7);
            AddStars(w, h, 18);
            AddBats(w, h, 3);
        }
    }

    // ── Moon: a pale disc behind a broad lilac halo, breathing slowly ──────────
    private void AddMoon(double cx, double cy, double r)
    {
        // Halo — a wide soft bloom of moonlight bleeding into the night.
        var halo = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center         = new Point(0.5, 0.5),
            RadiusX        = 0.5,
            RadiusY        = 0.5,
        };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xCB, 0xBE, 0xF0), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(60, 0x8E, 0x76, 0xC8), 0.4));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x6E, 0x58, 0xA8), 1));

        double hd = r * 7;
        var bloom = new Ellipse { Width = hd, Height = hd, Fill = halo };
        Canvas.SetLeft(bloom, cx - hd / 2);
        Canvas.SetTop(bloom, cy - hd / 2);
        Layer.Children.Add(bloom);
        Loop(bloom, OpacityProperty, 0.55, 0.9, 9, 0);

        // Disc — lit from the upper-left, cool and bone-pale.
        var disc = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.36, 0.32),
            Center         = new Point(0.5, 0.5),
            RadiusX        = 0.6,
            RadiusY        = 0.6,
        };
        disc.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xEE, 0xFF), 0));
        disc.GradientStops.Add(new GradientStop(Color.FromRgb(0xDE, 0xD3, 0xF2), 0.7));
        disc.GradientStops.Add(new GradientStop(Color.FromRgb(0xBE, 0xB0, 0xDE), 1));

        var moon = new Ellipse { Width = r * 2, Height = r * 2, Fill = disc };
        Canvas.SetLeft(moon, cx - r);
        Canvas.SetTop(moon, cy - r);
        Layer.Children.Add(moon);

        // A couple of faint craters so the disc isn't a flat circle.
        AddCrater(cx - r * 0.30, cy - r * 0.10, r * 0.26);
        AddCrater(cx + r * 0.28, cy + r * 0.22, r * 0.18);
        AddCrater(cx + r * 0.05, cy - r * 0.38, r * 0.12);
    }

    private void AddCrater(double cx, double cy, double rr)
    {
        var crater = new Ellipse
        {
            Width  = rr * 2,
            Height = rr * 2,
            Fill   = new SolidColorBrush(Color.FromArgb(40, 0x9A, 0x8C, 0xBE)),
        };
        Canvas.SetLeft(crater, cx - rr);
        Canvas.SetTop(crater, cy - rr);
        Layer.Children.Add(crater);
    }

    // ── Stars: faint cold pinpricks scattered across the upper sky, twinkling ──
    private void AddStars(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double size = 1.0 + _rng.NextDouble() * 2.2;
            var star = new Ellipse
            {
                Width   = size,
                Height  = size,
                Fill    = new SolidColorBrush(Color.FromArgb(235, 0xEC, 0xE6, 0xFF)),
                Opacity = 0,
            };
            Canvas.SetLeft(star, _rng.NextDouble() * w);
            Canvas.SetTop(star, _rng.NextDouble() * h * 0.62);   // keep clear of the tower mass below
            Layer.Children.Add(star);

            Loop(star, OpacityProperty, 0.1, 0.85, 1.4 + _rng.NextDouble() * 2.6, _rng.NextDouble() * 3);
        }
    }

    // ── A soft violet backlight behind the spire so the silhouette reads against the dark ─
    private void AddTowerBackdropGlow(double w, double h)
    {
        var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x6E, 0x46, 0xB4), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x4A, 0x2C, 0x80), 1));

        double gw = w * 0.6, gh = h * 0.7;
        var glow = new Ellipse { Width = gw, Height = gh, Fill = rg };
        Canvas.SetLeft(glow, w * 0.42 - gw / 2);
        Canvas.SetTop(glow, h - gh * 0.78);
        Layer.Children.Add(glow);
        Loop(glow, OpacityProperty, 0.5, 0.85, 11, 0);
    }

    // ── Skyline: low blunt towers in the distance for depth, anchored to the floor ──
    private void AddSkyline(double w, double h)
    {
        var fill = new SolidColorBrush(Color.FromRgb(0x08, 0x05, 0x12));
        int n = Math.Clamp((int)(w / 200.0), 4, 9);
        for (int i = 0; i < n; i++)
        {
            double tw = 50 + _rng.Next(90);
            double th = h * (0.10 + _rng.NextDouble() * 0.16);
            // Resolve the jittered x ONCE so the body and its merlons share it — computing the random
            // offset twice left the crenellations sitting beside their tower instead of on top of it.
            double x = w * (i / (double)n) + _rng.Next(40) - 20;
            var body = new Rectangle { Width = tw, Height = th, Fill = fill };
            Canvas.SetLeft(body, x);
            Canvas.SetTop(body, h - th);
            Layer.Children.Add(body);

            // A blunt merlon line so even the distant towers read as castellated.
            AddMerlons(x, h - th, tw, 5, fill);
        }
    }

    // ── The dominant gothic tower: tall body, battlements, a steep spire, side pinnacles,
    //    and a few lit lancet windows ──────────────────────────────────────────
    private void AddTower(double w, double h)
    {
        var stone = new SolidColorBrush(Color.FromRgb(0x05, 0x03, 0x0C));   // near-black silhouette
        var rim   = new SolidColorBrush(Color.FromArgb(90, 0x4A, 0x32, 0x78)); // faint moonlit edge

        double cx = w * 0.42;
        double bw = Math.Clamp(w * 0.11, 70, 150);     // body width
        double bh = h * 0.50;                           // body height
        double left = cx - bw / 2;
        double top  = h - bh;

        // Body.
        var body = new Rectangle { Width = bw, Height = bh, Fill = stone, Stroke = rim, StrokeThickness = 1 };
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        Layer.Children.Add(body);

        // Battlements across the top of the body.
        AddMerlons(left, top, bw, 9, stone);

        // Steep spire rising from the battlements (a tall isosceles triangle + a finial).
        double spireH = bh * 0.55;
        double spireBaseY = top - 8;
        var spire = new Polygon
        {
            Points =
            [
                new Point(left - 4, spireBaseY),
                new Point(left + bw + 4, spireBaseY),
                new Point(cx, spireBaseY - spireH),
            ],
            Fill = stone, Stroke = rim, StrokeThickness = 1,
        };
        Layer.Children.Add(spire);

        // Finial cross atop the spire — unmistakably gothic.
        AddFinial(cx, spireBaseY - spireH, stone, rim);

        // Side pinnacles flanking the body, each with its own little spire.
        AddPinnacle(left - bw * 0.32, h, bw * 0.34, bh * 0.62, stone, rim);
        AddPinnacle(left + bw + bw * 0.32 - bw * 0.34, h, bw * 0.34, bh * 0.62, stone, rim);

        // Lit lancet windows — arcane purple, one warm candle, all flickering slowly.
        double colGap = bw / 3;
        AddLancetWindow(left + colGap * 0.6, top + bh * 0.30, bw * 0.16, bh * 0.16, Color.FromRgb(0xC4, 0x9C, 0xFF));
        AddLancetWindow(left + colGap * 1.7, top + bh * 0.30, bw * 0.16, bh * 0.16, Color.FromRgb(0xFF, 0xBE, 0x7A));
        AddLancetWindow(left + colGap * 0.6, top + bh * 0.58, bw * 0.16, bh * 0.16, Color.FromRgb(0xB0, 0x8C, 0xF0));
        AddLancetWindow(left + colGap * 1.7, top + bh * 0.58, bw * 0.16, bh * 0.16, Color.FromRgb(0xC4, 0x9C, 0xFF));
    }

    // Small merlon teeth along the top edge of a tower body.
    private void AddMerlons(double left, double top, double width, double mh, Brush fill)
    {
        int teeth = Math.Max(3, (int)(width / 16));
        double tw = width / (teeth * 2 - 1);
        for (int i = 0; i < teeth; i++)
        {
            var merlon = new Rectangle { Width = tw, Height = mh, Fill = fill };
            Canvas.SetLeft(merlon, left + i * tw * 2);
            Canvas.SetTop(merlon, top - mh);
            Layer.Children.Add(merlon);
        }
    }

    // A thin cross finial sitting on a spire tip.
    private void AddFinial(double cx, double tipY, Brush fill, Brush rim)
    {
        var vert = new Rectangle { Width = 2.4, Height = 16, Fill = fill, Stroke = rim, StrokeThickness = 0.5 };
        Canvas.SetLeft(vert, cx - 1.2);
        Canvas.SetTop(vert, tipY - 16);
        Layer.Children.Add(vert);
        var horiz = new Rectangle { Width = 9, Height = 2.4, Fill = fill, Stroke = rim, StrokeThickness = 0.5 };
        Canvas.SetLeft(horiz, cx - 4.5);
        Canvas.SetTop(horiz, tipY - 12);
        Layer.Children.Add(horiz);
    }

    // A slimmer flanking tower with a pointed roof.
    private void AddPinnacle(double left, double floorY, double pw, double ph, Brush fill, Brush rim)
    {
        double top = floorY - ph;
        var body = new Rectangle { Width = pw, Height = ph, Fill = fill, Stroke = rim, StrokeThickness = 0.75 };
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        Layer.Children.Add(body);

        double roofH = pw * 1.1;
        var roof = new Polygon
        {
            Points = [new Point(left - 2, top), new Point(left + pw + 2, top), new Point(left + pw / 2, top - roofH)],
            Fill = fill, Stroke = rim, StrokeThickness = 0.75,
        };
        Layer.Children.Add(roof);
    }

    // A pointed-arch (lancet) window with a soft glow halo + a brighter core, flickering as if lit
    // by candle or witch-light within.
    private void AddLancetWindow(double left, double top, double ww, double wh, Color light)
    {
        double archH = ww * 0.8;

        // Outer halo so the light bleeds onto the surrounding stone.
        var halo = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(150, light.R, light.G, light.B), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1));
        double hd = Math.Max(ww, wh) * 2.6;
        var bloom = new Ellipse { Width = hd, Height = hd, Fill = halo, Opacity = 0 };
        Canvas.SetLeft(bloom, left + ww / 2 - hd / 2);
        Canvas.SetTop(bloom, top + wh / 2 - hd / 2);
        Layer.Children.Add(bloom);

        // The lit pane itself: a rectangle topped by a pointed arch.
        var pane = new Path
        {
            Data = Geometry.Parse(
                $"M0,{archH.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"L0,{(archH + wh).ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"L{ww.ToString(System.Globalization.CultureInfo.InvariantCulture)},{(archH + wh).ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"L{ww.ToString(System.Globalization.CultureInfo.InvariantCulture)},{archH.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"Q{(ww / 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},{(-archH * 0.4).ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"0,{archH.ToString(System.Globalization.CultureInfo.InvariantCulture)} Z"),
            Fill    = new SolidColorBrush(Color.FromArgb(220, light.R, light.G, light.B)),
            Opacity = 0,
        };
        Canvas.SetLeft(pane, left);
        Canvas.SetTop(pane, top);
        Layer.Children.Add(pane);

        // Independent slow flicker — the halo and pane share a rhythm but never quite go dark.
        double period = 3.5 + _rng.NextDouble() * 3.5;
        double phase  = _rng.NextDouble() * 2;
        Loop(bloom, OpacityProperty, 0.30, 0.75, period, phase);
        Loop(pane,  OpacityProperty, 0.55, 1.0, period, phase);
    }

    // ── Clouds: pale moonlit wisps drifting across the upper sky at varied heights ──
    private void AddClouds(double w, double h, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var cloud = MakeCloud();
            // Spread the banks across the upper sky at distinct heights (a ladder + jitter) rather than
            // clustering them at the moon line, so a few drift high and a few lower. moonY (~0.2h) sits
            // within the band, so a couple still cross the disc.
            double frac  = (i + 0.5) / n;
            double baseY = h * (0.04 + 0.42 * frac) + (h * 0.04) * (_rng.NextDouble() - 0.5);
            Canvas.SetTop(cloud, baseY);

            var move = new TranslateTransform();
            cloud.RenderTransform = move;
            Layer.Children.Add(cloud);

            bool toRight = _rng.Next(2) == 0;
            double startX = toRight ? -260 : w + 260;
            double endX   = toRight ? w + 260 : -260;
            double dur    = 55 + _rng.Next(45);

            Canvas.SetLeft(cloud, startX);
            var drift = new DoubleAnimation(0, endX - startX, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = TimeSpan.FromSeconds(_rng.NextDouble() * dur),
            };
            move.BeginAnimation(TranslateTransform.XProperty, drift);
        }
    }

    private FrameworkElement MakeCloud()
    {
        // A few overlapping moonlit-lavender wisps, soft-edged and faint — visible against the night and,
        // crucially, a lighter backdrop a dark bat can silhouette against away from the moon.
        var cloud = new Canvas { Width = 220, Height = 70 };
        int puffs = 4 + _rng.Next(3);
        for (int i = 0; i < puffs; i++)
        {
            double pw = 70 + _rng.Next(90), ph = pw * (0.45 + _rng.NextDouble() * 0.2);
            var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(72, 0x6E, 0x5C, 0x92), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0x52, 0x42, 0x78), 0.6));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x40, 0x32, 0x60), 1));
            var puff = new Ellipse { Width = pw, Height = ph, Fill = rg };
            Canvas.SetLeft(puff, i * 34 - 10 + _rng.Next(18));
            Canvas.SetTop(puff, (70 - ph) * 0.5 + _rng.Next(14) - 7);
            cloud.Children.Add(puff);
        }
        return cloud;
    }

    // ── Fog: low mist rolling along the foot of the tower, drifting + breathing ─
    private void AddFog(double w, double h)
    {
        int n = Math.Clamp((int)(w / 280.0), 3, 6);
        for (int i = 0; i < n; i++)
        {
            double fw = 300 + _rng.Next(280), fh = 110 + _rng.Next(80);
            var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x40, 0x32, 0x64), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x36, 0x2A, 0x56), 1));

            var bank = new Ellipse { Width = fw, Height = fh, Fill = rg, Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(bank, w * (0.05 + 0.9 * i / Math.Max(1, n - 1)) - fw / 2);
            Canvas.SetTop(bank, h - fh * 0.5);
            Layer.Children.Add(bank);

            Loop(bank, OpacityProperty, 0.3, 0.8, 6 + _rng.Next(4), i * 0.7);
            Loop(bank.RenderTransform, TranslateTransform.XProperty, -30, 30, 16 + _rng.Next(10), i * 0.9);
        }
    }

    // ── Bats: dark silhouettes flapping across the sky, bobbing, looping off-screen ─
    private void AddBats(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            bool   toRight = _rng.Next(2) == 0;
            double scale   = 0.75 + _rng.NextDouble() * 1.25;
            bool   swooper = _rng.Next(3) == 0;            // some dive in big arcs, most cruise
            double baseY   = h * (0.10 + 0.55 * _rng.NextDouble());

            var bat = MakeBat();
            Canvas.SetTop(bat, baseY);

            var move = new TranslateTransform();
            bat.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(toRight ? scale : -scale, scale, 15, 7),
                    move,
                },
            };
            Layer.Children.Add(bat);

            double startX = toRight ? -120 : w + 120;
            double endX   = toRight ? w + 120 : -120;
            double dur    = 14 + _rng.Next(16);

            // Park the bat off-screen at its start point until the crossing begins — otherwise the
            // delayed (BeginTime) animation leaves it stuck at canvas x=0 for up to a full crossing.
            Canvas.SetLeft(bat, startX);

            var cross = new DoubleAnimation(0, endX - startX, new Duration(TimeSpan.FromSeconds(dur)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime      = TimeSpan.FromSeconds(_rng.NextDouble() * dur),
            };
            move.BeginAnimation(TranslateTransform.XProperty, cross);

            // Erratic flight bob — swoopers dip far, cruisers barely waver.
            double amp = swooper ? 40 + _rng.Next(40) : 10 + _rng.Next(12);
            Loop(move, TranslateTransform.YProperty, -amp, amp, (swooper ? 2.6 : 3.2) + _rng.NextDouble() * 2, _rng.NextDouble());
        }
    }

    private FrameworkElement MakeBat()
    {
        // Near-black silhouette — reads as a true shadow against the moon and the now-lighter moonlit
        // clouds, which give it a paler backdrop to cross even away from the disc. Only the faintest
        // violet rim, so it stays a shadow rather than a drawn shape.
        var brush  = new SolidColorBrush(Color.FromArgb(238, 0x0C, 0x06, 0x14));
        var stroke = new SolidColorBrush(Color.FromArgb(70, 0x3A, 0x26, 0x60));
        var bat    = new Canvas { Width = 30, Height = 14 };

        // Wings live in their own canvas so a vertical squash animates a believable flap.
        var wings = new Canvas { Width = 30, Height = 14 };
        var flap  = new ScaleTransform(1, 1, 15, 7);
        wings.RenderTransform = flap;

        // Left membrane: swept up to the tip, scalloped trailing edge back to the body.
        var left = new Polygon
        {
            Points =
            [
                new Point(15, 5), new Point(8, 3), new Point(2, 5), new Point(1, 7),
                new Point(4, 6), new Point(5, 8), new Point(8, 6), new Point(9, 8),
                new Point(12, 6), new Point(13, 8), new Point(15, 7),
            ],
            Fill = brush, Stroke = stroke, StrokeThickness = 0.6,
        };
        // Right membrane: the mirror image about x = 15.
        var right = new Polygon
        {
            Points =
            [
                new Point(15, 5), new Point(22, 3), new Point(28, 5), new Point(29, 7),
                new Point(26, 6), new Point(25, 8), new Point(22, 6), new Point(21, 8),
                new Point(18, 6), new Point(17, 8), new Point(15, 7),
            ],
            Fill = brush, Stroke = stroke, StrokeThickness = 0.6,
        };
        wings.Children.Add(left);
        wings.Children.Add(right);

        // Body + two tiny ears.
        var bodyShape = new Ellipse { Width = 3, Height = 7, Fill = brush };
        Canvas.SetLeft(bodyShape, 13.5);
        Canvas.SetTop(bodyShape, 3.5);
        var ears = new Polygon
        {
            Points = [new Point(13.6, 4), new Point(14.3, 1.5), new Point(15, 4),
                      new Point(15, 4), new Point(15.7, 1.5), new Point(16.4, 4)],
            Fill = brush,
        };

        bat.Children.Add(wings);
        bat.Children.Add(bodyShape);
        bat.Children.Add(ears);

        // Quick wing-beat — wings sweep down and lift, never fully flat.
        var beat = new DoubleAnimation(1.0, 0.45, new Duration(TimeSpan.FromSeconds(0.22 + _rng.NextDouble() * 0.12)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        flap.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        return bat;
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
