using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Core;

/// <summary>
/// Builds the application's merged resource dictionaries for a theme. A theme is no longer a single
/// colour file: it is assembled from layers whose merge order encodes precedence (earliest = lowest):
/// <list type="number">
///   <item><c>Colors.&lt;theme&gt;.xaml</c> — the raw palette.</item>
///   <item><c>Tokens.xaml</c> — region tokens (<c>{Region}.{Role}</c>) defaulting to the palette.</item>
///   <item>feature contributions — optional <see cref="Features.Common.IThemeContribution"/> dictionaries,
///         merged here so a theme can override them but they still fall back below it.</item>
///   <item><c>Theme.&lt;theme&gt;.xaml</c> — per-theme region overrides + <c>Scene.*</c> templates (optional;
///         absent for plain themes).</item>
///   <item><c>Styles.xaml</c> — control templates referencing the above by key.</item>
/// </list>
/// </summary>
internal static class ThemeManager
{
    private const string PackBase = "pack://application:,,,/Nexacore;component/Themes/";

    /// <summary>The theme currently merged into <see cref="Application.Resources"/>. Lets callers tell
    /// whether a saved <see cref="ShellConfig.Theme"/> differs from what is actually on screen.</summary>
    internal static ThemeOption Current { get; private set; } = ThemeOption.Dark;

    internal static void Apply(ThemeOption theme, IReadOnlyList<Uri>? contributions = null)
    {
        Current = theme;

        var dicts = new List<ResourceDictionary>
        {
            Load($"{PackBase}Colors.{theme}.xaml")!,   // 1. palette
            Load($"{PackBase}Tokens.xaml")!,           // 2. region tokens
        };

        if (contributions is not null)                  // 3. feature contributions (optional)
            foreach (var uri in contributions)
                if (Load(uri) is { } d) dicts.Add(d);

        if (Load($"{PackBase}Theme.{theme}.xaml") is { } themeOverrides)  // 4. overrides + scenes (optional)
            dicts.Add(themeOverrides);

        dicts.Add(Load($"{PackBase}Styles.xaml")!);     // 5. styles

        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        foreach (var d in dicts)
            merged.Add(d);
    }

    private static ResourceDictionary? Load(string uri) => Load(new Uri(uri));

    private static ResourceDictionary? Load(Uri uri)
    {
        // Optional layers (Theme.<name>.xaml, contributions) may not exist — treat a load failure
        // as "not supplied" rather than a hard error.
        try { return new ResourceDictionary { Source = uri }; }
        catch { return null; }
    }
}
