using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>A pickable colour from the shared categorical swatch bank: its resource key, the brush to
/// show, and the hex (for callers that persist or adapt to a colour string).</summary>
public sealed record SwatchOption(string Key, string Hex, Brush Brush)
{
    /// <summary>Friendly hue name for a picker (the key without its <c>Swatch.</c> prefix).</summary>
    public string Name => Key.StartsWith("Swatch.", StringComparison.Ordinal) ? Key["Swatch.".Length..] : Key;
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
}
