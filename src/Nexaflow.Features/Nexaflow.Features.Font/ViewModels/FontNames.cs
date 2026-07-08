using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// Helpers for reading the localized name dictionaries WPF exposes on <see cref="GlyphTypeface"/> and
/// <see cref="FontFamily"/>. Both are culture-keyed maps; we resolve to the current UI culture, then
/// English (US), then whatever is present.
/// </summary>
internal static class FontNames
{
    /// <summary>Best display string from a <see cref="GlyphTypeface"/> culture map, or null if empty.</summary>
    public static string? Pick(IDictionary<CultureInfo, string> names)
    {
        if (names is null || names.Count == 0) return null;
        if (names.TryGetValue(CultureInfo.CurrentUICulture, out var cur) && !string.IsNullOrWhiteSpace(cur)) return cur;
        var enUs = new CultureInfo("en-us");
        if (names.TryGetValue(enUs, out var en) && !string.IsNullOrWhiteSpace(en)) return en;
        return names.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>Family display name from a <see cref="FontFamily"/> (falls back to its source string).</summary>
    public static string Display(FontFamily family)
    {
        var names = family.FamilyNames;
        if (names is { Count: > 0 })
        {
            var tag = CultureInfo.CurrentUICulture.IetfLanguageTag;
            foreach (var kv in names)
                if (string.Equals(kv.Key.IetfLanguageTag, tag, System.StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            var first = names.Values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }

        // Source is a family name for installed fonts, or "file:///…#Family" for file-loaded ones.
        var src = family.Source ?? "Font";
        int hash = src.LastIndexOf('#');
        return hash >= 0 && hash < src.Length - 1 ? src[(hash + 1)..] : src;
    }
}
