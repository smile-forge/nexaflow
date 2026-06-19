using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

/// <summary>A pickable colour from the shared swatch bank: the brush to show + the hex to store.</summary>
public sealed record SwatchOption(string Hex, Brush Brush);

/// <summary>
/// The shared categorical swatch bank (Tokens.xaml / per-theme overrides) resolved to brush+hex pairs.
/// Every per-profile colour picker draws from it — the Options workspaces list and the Configure
/// panel's identity page — so they offer the same palette the ribbon picker and project pie use.
/// </summary>
public static class WorkspaceSwatches
{
    private static readonly string[] Keys =
    [
        "Swatch.Blue",  "Swatch.Cyan",   "Swatch.Teal",  "Swatch.Green",
        "Swatch.Lime",  "Swatch.Yellow", "Swatch.Amber", "Swatch.Orange",
        "Swatch.Red",   "Swatch.Pink",   "Swatch.Purple","Swatch.Slate",
    ];

    /// <summary>
    /// Resolves the bank against the live application resources. Throws (not silently skips) on a
    /// missing key so a typo surfaces instead of a gap in the picker.
    /// </summary>
    public static IReadOnlyList<SwatchOption> Build()
    {
        var list = new List<SwatchOption>(Keys.Length);
        foreach (var key in Keys)
        {
            if (Application.Current?.Resources[key] is not SolidColorBrush b)
                throw new InvalidOperationException($"Swatch brush '{key}' not found.");
            list.Add(new SwatchOption(b.Color.ToString(), b));
        }
        return list;
    }
}
