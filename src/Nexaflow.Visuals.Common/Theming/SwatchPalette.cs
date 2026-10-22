using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>A pickable colour from the shared categorical swatch bank: its resource key, the brush to
/// show, and the hex (for callers that persist or adapt to a colour string).</summary>
public sealed record SwatchOption(string Key, string Hex, Brush Brush)
{
    /// <summary>The hue's invariant name (the key without its <c>Swatch.</c> prefix) — for ids and matching, never shown.</summary>
    public string Name => Key.StartsWith("Swatch.", StringComparison.Ordinal) ? Key["Swatch.".Length..] : Key;

    /// <summary>The hue as a picker shows it, in the window's language.</summary>
    public string HueName => SwatchPalette.HueName(Key);
}

/// <summary>
/// The single source of truth for the categorical <c>Swatch.*</c> bank (defined in the app theme's
/// <c>Tokens.xaml</c>, per-theme overridable). Every per-workspace / per-feature colour picker draws
/// from here — the workspace pickers, the project status pips — so they all offer the same palette and a
/// theme retunes the whole bank centrally. Resolves against the live application resources.
/// </summary>
public static class SwatchPalette
{
    /// <summary>The canonical ordered swatch keys. MUST match the <c>Swatch.*</c> bank in Tokens.xaml.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "Swatch.Blue",  "Swatch.Cyan",   "Swatch.Teal",  "Swatch.Green",
        "Swatch.Lime",  "Swatch.Yellow", "Swatch.Amber", "Swatch.Orange",
        "Swatch.Red",   "Swatch.Pink",   "Swatch.Purple","Swatch.Slate",
    ];

    /// <summary>Resolves a single swatch key to its brush, or a grey fallback when the key/theme is
    /// absent (e.g. a headless unit test with no <see cref="Application"/>) so a missing token degrades
    /// gracefully rather than throwing mid-layout.</summary>
    public static Brush Resolve(string? key)
        => !string.IsNullOrEmpty(key) && Application.Current?.Resources[key] is Brush b ? b : Brushes.Gray;

    /// <summary>Resolves the whole bank to key + hex + brush triples for a picker. Throws (not silently
    /// skips) on a missing key so a typo in <see cref="Keys"/> surfaces instead of a gap in the picker.</summary>
    public static IReadOnlyList<SwatchOption> Build()
    {
        var list = new List<SwatchOption>(Keys.Count);
        foreach (var key in Keys)
        {
            if (Application.Current?.Resources[key] is not SolidColorBrush b)
                throw new InvalidOperationException($"Swatch brush '{key}' not found.");
            list.Add(new SwatchOption(key, b.Color.ToString(), b));
        }
        return list;
    }

    /// <summary>A swatch key's hue as a reader sees it, in the window's language; a key outside the bank comes back as itself.</summary>
    public static string HueName(string key) => key switch
    {
        "Swatch.Blue" => Str.Get("Common.Swatch.Blue"),
        "Swatch.Cyan" => Str.Get("Common.Swatch.Cyan"),
        "Swatch.Teal" => Str.Get("Common.Swatch.Teal"),
        "Swatch.Green" => Str.Get("Common.Swatch.Green"),
        "Swatch.Lime" => Str.Get("Common.Swatch.Lime"),
        "Swatch.Yellow" => Str.Get("Common.Swatch.Yellow"),
        "Swatch.Amber" => Str.Get("Common.Swatch.Amber"),
        "Swatch.Orange" => Str.Get("Common.Swatch.Orange"),
        "Swatch.Red" => Str.Get("Common.Swatch.Red"),
        "Swatch.Pink" => Str.Get("Common.Swatch.Pink"),
        "Swatch.Purple" => Str.Get("Common.Swatch.Purple"),
        "Swatch.Slate" => Str.Get("Common.Swatch.Slate"),
        _ => key,
    };
}
