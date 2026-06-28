using System.Windows.Media;

namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// Distinct colours used to tint materials a file doesn't colour itself, so each one is visually separable
/// in the viewport and the inspector. The view supplies the theme's categorical <c>Swatch.*</c> bank at load
/// time (keeping the colours theme-driven); <see cref="Fallback"/> is the last-resort literal set used only
/// when those resources can't be resolved — e.g. a unit test with no <c>Application</c>.
/// </summary>
public static class CategoricalPalette
{
    public static readonly IReadOnlyList<Color> Fallback =
    [
        Color.FromRgb(0x4F, 0x8E, 0xF7), // blue
        Color.FromRgb(0x3F, 0xB9, 0x50), // green
        Color.FromRgb(0xFF, 0x8B, 0x3D), // orange
        Color.FromRgb(0xAB, 0x47, 0xBC), // purple
        Color.FromRgb(0xF0, 0x62, 0x92), // pink
        Color.FromRgb(0x29, 0xB6, 0xF6), // cyan
        Color.FromRgb(0xE5, 0xC0, 0x7B), // amber
        Color.FromRgb(0x2B, 0xB5, 0xA0), // teal
        Color.FromRgb(0x9C, 0xCC, 0x65), // lime
        Color.FromRgb(0xEF, 0x53, 0x50), // red
        Color.FromRgb(0xE5, 0xC1, 0x00), // yellow
        Color.FromRgb(0x87, 0x94, 0xA8), // slate
    ];
}
