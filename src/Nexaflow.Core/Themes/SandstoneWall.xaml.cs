using System;
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
    private readonly Random _rng = new();
    private bool _built;
    private readonly DispatcherTimer _resizeDebounce;

    public SandstoneWall()
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

        AddBlocks(w, h);
        AddSunSweep(w, h);
        AddShadowPlay(w, h);
    }

    // ── Masonry: courses of tone-varied ashlar blocks, recessed joints, lit top edges ─
    private void AddBlocks(double w, double h)
    {
        var mortar    = new SolidColorBrush(Color.FromArgb(165, 0x5A, 0x46, 0x30));
        var highlight = new SolidColorBrush(Color.FromArgb(70, 0xFF, 0xF2, 0xD4));
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
                    Fill   = new SolidColorBrush(fill),
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
        sun.RenderTransform.BeginAnimation(TranslateTransform.XProperty, sweep);
        Loop(sun, OpacityProperty, 0.72, 1.0, 19, 0);
    }

    // ── Shadow play: a few soft shadows drifting slowly over the stone ─────────
    private void AddShadowPlay(double w, double h)
    {
        for (int i = 0; i < 3; i++)
        {
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0x28, 0x1B, 0x0F), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x28, 0x1B, 0x0F), 1));

            double sw = w * (0.42 + _rng.NextDouble() * 0.3), sh = h * (0.42 + _rng.NextDouble() * 0.3);
            var shade = new Ellipse { Width = sw, Height = sh, Fill = rg, Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(shade, _rng.NextDouble() * w - sw / 2);
            Canvas.SetTop(shade, _rng.NextDouble() * h - sh / 2);
            Layer.Children.Add(shade);

            Loop(shade, OpacityProperty, 0.1, 0.42, 12 + _rng.Next(8), i * 3);
            Loop(shade.RenderTransform, TranslateTransform.XProperty, -w * 0.12, w * 0.12, 32 + _rng.Next(20), i * 4);
            Loop(shade.RenderTransform, TranslateTransform.YProperty, -h * 0.06, h * 0.06, 26 + _rng.Next(16), i * 5);
        }
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

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
