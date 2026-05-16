using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Animated status indicator for the AI input bar.
/// States (highest priority first):
///   IsBusy=true          → orange ellipse, size-pulse (AI is working)
///   HandlerSymbol != null → indigo circle with symbol text (single handler identified)
///   IsListening=true     → indigo→cyan gradient ellipse, opacity-pulse (ambiguous/no handler)
///   default              → green ellipse, steady (ready)
/// </summary>
public class AiStatusDot : ContentControl
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(AiStatusDot),
            new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty HandlerSymbolProperty =
        DependencyProperty.Register(nameof(HandlerSymbol), typeof(string), typeof(AiStatusDot),
            new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty IsListeningProperty =
        DependencyProperty.Register(nameof(IsListening), typeof(bool), typeof(AiStatusDot),
            new PropertyMetadata(false, OnStateChanged));

    public bool    IsBusy        { get => (bool)GetValue(IsBusyProperty);        set => SetValue(IsBusyProperty, value); }
    public string? HandlerSymbol { get => (string?)GetValue(HandlerSymbolProperty); set => SetValue(HandlerSymbolProperty, value); }
    public bool    IsListening   { get => (bool)GetValue(IsListeningProperty);   set => SetValue(IsListeningProperty, value); }

    // ── Colors ────────────────────────────────────────────────────────────

    private static readonly Color GreenColor  = Color.FromRgb(0x22, 0xD3, 0xA5);
    private static readonly Color OrangeColor = Color.FromRgb(0xF9, 0x73, 0x16);
    private static readonly Color IndigoColor = Color.FromRgb(0x63, 0x66, 0xF1);
    private static readonly Color CyanColor   = Color.FromRgb(0x06, 0xB6, 0xD4);

    // ── Visual children ───────────────────────────────────────────────────

    private readonly Ellipse    _dot;
    private readonly Border     _symbolBorder;
    private readonly TextBlock  _symbolText;

    // ── Storyboards ───────────────────────────────────────────────────────

    private readonly Storyboard _busySb;    // orange size pulse
    private readonly Storyboard _listenSb;  // gradient opacity pulse

    public AiStatusDot()
    {
        // Main ellipse — used for ready, busy, and listening states
        _dot = new Ellipse
        {
            Width               = 28,
            Height              = 28,
            Fill                = new SolidColorBrush(GreenColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };

        // Symbol display — used when a single handler is identified
        _symbolText = new TextBlock
        {
            FontSize            = 13,
            FontWeight          = FontWeights.SemiBold,
            Foreground          = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        _symbolBorder = new Border
        {
            Width               = 28,
            Height              = 28,
            CornerRadius        = new CornerRadius(14),
            Background          = new SolidColorBrush(IndigoColor),
            Child               = _symbolText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Visibility          = Visibility.Collapsed
        };

        var grid = new Grid();
        grid.Children.Add(_dot);
        grid.Children.Add(_symbolBorder);
        Content = grid;

        Width  = 28;
        Height = 28;

        // ── Busy storyboard: size pulse ───────────────────────────────────
        var sizeAnim = new DoubleAnimation(24, 28, new Duration(TimeSpan.FromSeconds(0.8)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        _busySb = new Storyboard();
        _busySb.Children.Add(sizeAnim);
        Storyboard.SetTarget(sizeAnim, _dot);
        Storyboard.SetTargetProperty(sizeAnim, new PropertyPath(WidthProperty));

        // ── Listen storyboard: opacity pulse ──────────────────────────────
        var opacityAnim = new DoubleAnimation(0.4, 1.0, new Duration(TimeSpan.FromSeconds(1.4)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        _listenSb = new Storyboard();
        _listenSb.Children.Add(opacityAnim);
        Storyboard.SetTarget(opacityAnim, _dot);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));

        ApplyStyle();
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AiStatusDot)d).ApplyStyle();

    private void ApplyStyle()
    {
        _busySb.Stop();
        _listenSb.Stop();
        _dot.Opacity = 1.0;
        _dot.Width   = 28;

        if (IsBusy)
        {
            _symbolBorder.Visibility = Visibility.Collapsed;
            _dot.Visibility          = Visibility.Visible;
            _dot.Fill                = new SolidColorBrush(OrangeColor);
            _busySb.Begin();
            ToolTip = "AI busy…";
            return;
        }

        if (HandlerSymbol is { Length: > 0 } sym)
        {
            _dot.Visibility          = Visibility.Collapsed;
            _symbolBorder.Visibility = Visibility.Visible;
            _symbolText.Text         = sym;
            ToolTip = $"Handler: {sym}";
            return;
        }

        if (IsListening)
        {
            _symbolBorder.Visibility = Visibility.Collapsed;
            _dot.Visibility          = Visibility.Visible;
            _dot.Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(IndigoColor, 0.0),
                    new GradientStop(CyanColor,   1.0)
                },
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
            _listenSb.Begin();
            ToolTip = "Listening…";
            return;
        }

        // Default: ready
        _symbolBorder.Visibility = Visibility.Collapsed;
        _dot.Visibility          = Visibility.Visible;
        _dot.Fill                = new SolidColorBrush(GreenColor);
        ToolTip = "AI ready";
    }
}
