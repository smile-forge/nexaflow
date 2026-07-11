using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.GraphViewer.Converters;

/// <summary>
/// Resolves a theme resource key to a frozen brush at paint time (Hard Rule: a feature never hard-codes a colour —
/// it reads a token, with a literal only as a last-resort fallback when the theme can't be resolved, e.g. a headless
/// context). Shared by the hyperedge glyphs and any other code-drawn surface that needs a themed colour.
/// </summary>
internal static class ThemeBrush
{
    public static SolidColorBrush Resolve(string key, Color fallback)
    {
        var resources = Application.Current?.Resources;
        Color? colour = resources?[key] switch
        {
            SolidColorBrush b => b.Color,
            Color c => c,
            _ => null,
        };
        var brush = new SolidColorBrush(colour ?? fallback);
        brush.Freeze();
        return brush;
    }
}
