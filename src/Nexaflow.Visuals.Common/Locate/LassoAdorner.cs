using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Locate;

/// <summary>
/// One lasso, round the element it adorns: thrown wide, it draws itself on as it pulls tight round the control, then
/// breathes until it is dismissed, when it fades. When it is one of a chain its step rides on the loop ("2 / 3").
/// <para>
/// Its colours are the theme's — <c>Locate.Stroke</c> for the loop, its glow and the step's badge, <c>Locate.BadgeText</c>
/// for the number — looked up as it is made, with a loud red as the last resort. It takes no input: a click lands on
/// whatever is under it, which is the point. It does answer UI Automation, as <c>Locate_Lasso</c>, so a journey can see
/// that a locate link drew one.
/// </para>
/// </summary>
internal sealed class LassoAdorner : Adorner
{
    public const string AutomationIdValue = "Locate_Lasso";

    private const double StrokeWidth = 3;
    private static readonly Duration DrawOn = new(TimeSpan.FromMilliseconds(450));
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(160);
    private static readonly Color LastResort = Color.FromRgb(0xFF, 0x2E, 0x4D);

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path _loop;
    private readonly Border? _badge;
    private Size _drawnFor = Size.Empty;

    public LassoAdorner(FrameworkElement target, int step, int of) : base(target)
    {
        IsHitTestVisible = false;
        IsClipEnabled    = false;
        AutomationProperties.SetAutomationId(this, AutomationIdValue);
        if (of > 1) AutomationProperties.SetName(this, $"{step} / {of}");

        var stroke = ThemeBrush(target, "Locate.Stroke", LastResort);
        _loop = new System.Windows.Shapes.Path
        {
            Stroke             = stroke,
            StrokeThickness    = StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            StrokeDashCap      = PenLineCap.Round,
            StrokeLineJoin     = PenLineJoin.Round,
            // A glow in its own colour, so it reads over busy content as well as over plain surfaces.
            Effect = new DropShadowEffect
            {
                Color       = (stroke as SolidColorBrush)?.Color ?? LastResort,
                ShadowDepth = 0,
                BlurRadius  = 12,
                Opacity     = 0.9,
            },
        };
        _canvas.Children.Add(_loop);

        if (of > 1)
        {
            _badge = new Border
            {
                Background   = stroke,
                CornerRadius = new CornerRadius(9),
                Padding      = new Thickness(6, 1, 6, 1),
                Child        = new TextBlock
                {
                    Text       = $"{step} / {of}",
                    FontSize   = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = ThemeBrush(target, "Locate.BadgeText", Colors.White),
                },
            };
            _canvas.Children.Add(_badge);
        }

        AddVisualChild(_canvas);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _canvas;

    protected override Size MeasureOverride(Size constraint)
    {
        _canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = AdornedElement.RenderSize;
        if (size != _drawnFor) Draw(size);
        _canvas.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LassoPeer(this);

    /// <summary>Fades the lasso out, then takes it off <paramref name="layer"/>. On a timer rather than on the fade's
    /// completion, which never comes where nothing is rendering.</summary>
    public void Dismiss(AdornerLayer layer)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, new Duration(FadeOut)));
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = FadeOut };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (layer.GetAdorners(AdornedElement) is { } on && Array.IndexOf(on, this) >= 0) layer.Remove(this);
        };
        timer.Start();
    }

    private void Draw(Size size)
    {
        var first = _drawnFor == Size.Empty;
        _drawnFor = size;

        var target = new Rect(size);
        var points = LassoGeometry.Loop(target);
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1), isStroked: true) { IsSmoothJoin = true });
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        _loop.Data = geometry;

        if (_badge is not null)
        {
            _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var anchor = LassoGeometry.BadgeAnchor(points);
            Canvas.SetLeft(_badge, anchor.X - _badge.DesiredSize.Width / 2);
            Canvas.SetTop(_badge,  anchor.Y - _badge.DesiredSize.Height / 2);
        }

        if (first)
        {
            Throw(LassoGeometry.Length(points), target);
        }
        else
        {
            // The control changed size mid-step: the loop is redrawn whole, since the dash that drew it on was cut to
            // the old length.
            _loop.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            _loop.StrokeDashArray = null;
        }
    }

    // Thrown wide and pulled tight: one dash as long as the loop slides in along it — dash lengths count in stroke
    // widths — while the whole loop shrinks onto the control. Then it breathes, so an eye that missed the throw still
    // finds it.
    private void Throw(double length, Rect target)
    {
        var dash = length / StrokeWidth;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        _loop.StrokeDashArray = [dash, dash];
        _loop.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                             new DoubleAnimation(dash, 0, DrawOn) { EasingFunction = ease });

        var tighten = new ScaleTransform(1, 1, target.X + target.Width / 2, target.Y + target.Height / 2);
        _canvas.RenderTransform = tighten;
        var pull = new DoubleAnimation(1.2, 1, DrawOn) { EasingFunction = ease };
        tighten.BeginAnimation(ScaleTransform.ScaleXProperty, pull);
        tighten.BeginAnimation(ScaleTransform.ScaleYProperty, pull);

        _loop.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(650))
        {
            BeginTime      = DrawOn.TimeSpan,
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private static Brush ThemeBrush(FrameworkElement scope, string key, Color lastResort)
        => scope.TryFindResource(key) as Brush ?? new SolidColorBrush(lastResort);

    private sealed class LassoPeer : FrameworkElementAutomationPeer
    {
        public LassoPeer(LassoAdorner owner) : base(owner) { }

        protected override string GetClassNameCore() => nameof(LassoAdorner);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override List<AutomationPeer>? GetChildrenCore() => null;   // the badge's number is the lasso's name
    }
}
