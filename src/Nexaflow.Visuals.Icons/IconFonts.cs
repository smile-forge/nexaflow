using System.IO;
using System.Windows.Media;
using Nexaflow.Icons;

namespace Nexaflow.Visuals.Icons;

/// <summary>
/// The font WPF draws an icon's glyph (<see cref="IconCatalog.GlyphFor"/>) in: the bundled Fluent UI System Icons font for a
/// Fluent icon, the surrounding text's for an emoji.
/// </summary>
public static class IconFonts
{
    private static readonly Lazy<FontFamily> Font = new(LoadFluentFont);

    /// <summary>The bundled Fluent font, read from the <c>IconFonts</c> folder beside this assembly. Both faces share
    /// it; regular and filled are separate glyphs within it.</summary>
    public static FontFamily FluentFont => Font.Value;

    /// <summary>The font an icon draws with — null for an emoji, which takes the surrounding text's font and its
    /// fallback, as ribbon icons always have.</summary>
    public static FontFamily? FontFor(IconRef icon) => icon.IsFluent ? Font.Value : null;

    private static FontFamily LoadFluentFont()
    {
        // Base URI must be the folder (trailing slash); "./#Family" selects the family by name — as Smufl loads Bravura.
        var dir = Path.Combine(Path.GetDirectoryName(typeof(IconFonts).Assembly.Location) ?? AppContext.BaseDirectory,
            "IconFonts") + Path.DirectorySeparatorChar;
        return new FontFamily(new Uri(dir), "./#" + FluentGlyphs.Family);
    }
}
