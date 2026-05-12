using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Animated status indicator — green (ready) or orange+pulse (busy).
/// Hosted as a ContentControl wrapping an Ellipse.
/// </summary>
public class AiStatusDot : ContentControl
{
    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(AiStatusDot),
            new PropertyMetadata(false, OnIsBusyChanged));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    private readonly Ellipse   _dot;
    private readonly Storyboard _pulseSb;

    private static readonly Color GreenColor  = Color.FromRgb(0x22, 0xD3, 0xA5);
    private static readonly Color OrangeColor = Color.FromRgb(0xF9, 0x73, 0x16);

    public AiStatusDot()
    {
        _dot = new Ellipse
        {
            Width  = 28,
            Height = 28,
            Fill   = new SolidColorBrush(GreenColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };

        // Pulse animation for busy state
        var sizeAnim = new DoubleAnimation(24, 28, new Duration(TimeSpan.FromSeconds(0.8)))
        {
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        _pulseSb = new Storyboard();
        _pulseSb.Children.Add(sizeAnim);
        Storyboard.SetTarget(sizeAnim, _dot);
        Storyboard.SetTargetProperty(sizeAnim, new PropertyPath(WidthProperty));

        Content  = _dot;
        ToolTip  = "AI ready";
        Width    = 28;
        Height   = 28;
    }

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AiStatusDot)d).ApplyStyle();

    private void ApplyStyle()
    {
        if (IsBusy)
        {
            _dot.Fill = new SolidColorBrush(OrangeColor);
            _pulseSb.Begin();
            ToolTip = "AI busy…";
        }
        else
        {
            _pulseSb.Stop();
            _dot.Width = 28;
            _dot.Fill  = new SolidColorBrush(GreenColor);
            ToolTip    = "AI ready";
        }
    }
}
