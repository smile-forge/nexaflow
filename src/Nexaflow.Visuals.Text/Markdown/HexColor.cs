using System;
using System.Globalization;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A colour written into a markdown block's settings, as plain bytes.
///
/// <para>
/// Kept apart from WPF's <c>Color</c> so a block model, and everything that parses into one, stays free
/// of a UI thread — the renderer is where this becomes a brush.
/// </para>

/// </summary>
public readonly record struct HexColor(byte A, byte R, byte G, byte B)
{
    /// <summary>
    /// Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>; the leading hash is optional.
    /// </summary>
    public static bool TryParse(string? text, out HexColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string hex = text.Trim().TrimStart('#');
        if (hex.Length is not (3 or 6 or 8) || !hex.All(char.IsAsciiHexDigit)) return false;

        if (hex.Length == 3)   // #ABC is #AABBCC
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        byte Component(int index) =>
            byte.Parse(hex.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        color = hex.Length == 8
            ? new HexColor(Component(0), Component(1), Component(2), Component(3))
            : new HexColor(0xFF, Component(0), Component(1), Component(2));
        return true;
    }
}
