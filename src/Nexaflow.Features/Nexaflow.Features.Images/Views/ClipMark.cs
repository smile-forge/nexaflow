using System.Windows;
using System.Windows.Controls.Primitives;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A histogram's clip mark: a corner triangle, lit while its end of the tones clips, pressed to keep that clipping tinted on
/// the picture. Pointing at it shows the clipping while it is pointed at; that is the view's to follow.
/// </summary>
public sealed class ClipMark : ToggleButton
{
    public static readonly DependencyProperty IsClippingProperty = DependencyProperty.Register(
        nameof(IsClipping), typeof(bool), typeof(ClipMark), new PropertyMetadata(false));

    /// <summary>Its end of the tones clips: the mark is lit.</summary>
    public bool IsClipping
    {
        get => (bool)GetValue(IsClippingProperty);
        set => SetValue(IsClippingProperty, value);
    }
}
