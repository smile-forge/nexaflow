using System.Windows.Media;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A colour chosen in a picker as a picture keeps it: ARGB, a swatch resolved through the palette.</summary>
internal static class SpecColours
{
    /// <summary><paramref name="spec"/> as 0xAARRGGBB; red where it names nothing.</summary>
    internal static uint Argb(ColorSpec spec)
    {
        var colour = spec.Kind switch
        {
            ColorSpecKind.Swatch => (SwatchPalette.Resolve(spec.SwatchKey) as SolidColorBrush)?.Color ?? Colors.Red,
            ColorSpecKind.Custom => spec.Color,
            _                    => Colors.Red,
        };
        return (uint)colour.A << 24 | (uint)colour.R << 16 | (uint)colour.G << 8 | colour.B;
    }
}
