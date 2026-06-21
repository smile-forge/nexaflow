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
    // Derive the assembly name at runtime so a rename of the host assembly (AssemblyName) can't
    // silently break these pack URIs — a failed Load() would otherwise return null and crash the merge.
    private static readonly string PackBase =
        $"pack://application:,,,/{typeof(ThemeManager).Assembly.GetName().Name};component/Themes/";

    /// <summary>The theme currently merged into <see cref="Application.Resources"/>. Lets callers tell
    /// whether a saved <see cref="ShellConfig.Theme"/> differs from what is actually on screen.</summary>
    internal static ThemeOption Current { get; private set; } = ThemeOption.Dark;

    internal static void Apply(ThemeOption theme, IReadOnlyList<Uri>? contributions = null)
    {
        Current = theme;

        var dicts = new List<ResourceDictionary>
        {
            LoadRequired($"{PackBase}Colors.{theme}.xaml"),   // 1. palette
            LoadRequired($"{PackBase}Tokens.xaml"),           // 2. region tokens
        };

        if (contributions is not null)                  // 3. feature contributions (optional)
            foreach (var uri in contributions)
                if (Load(uri) is { } d) dicts.Add(d);

        if (Load($"{PackBase}Theme.{theme}.xaml") is { } themeOverrides)  // 4. overrides + scenes (optional)
            dicts.Add(themeOverrides);

        dicts.Add(LoadRequired($"{PackBase}Styles.xaml"));     // 5. styles

        var app = Application.Current.Resources;
        var merged = app.MergedDictionaries;
        merged.Clear();
        foreach (var d in dicts)
            merged.Add(d);

        FreezeResources(app);
    }

    /// <summary>
    /// Freezes every <see cref="Freezable"/> resource (brushes, gradients, effects) once all theme
    /// layers are merged. Frozen resources are immutable, so they are safe to read from any UI thread
    /// (a prerequisite for per-window UI threads) and let WPF skip per-use change tracking — a small
    /// render win. A theme switch rebuilds the window rather than mutating these, so freezing has no
    /// downside. Each resource is resolved through the top-level dictionary so cross-layer
    /// <c>{StaticResource}</c> colours (e.g. a <c>Tokens.xaml</c> brush over a palette colour) bind
    /// before freezing; styles/templates aren't <see cref="Freezable"/> and are skipped. Best-effort
    /// per key — one failure never blocks the apply.
    /// </summary>
    internal static void FreezeResources(ResourceDictionary root)
    {
        foreach (var key in CollectKeys(root, []))
        {
            try
            {
                if (root[key] is Freezable { CanFreeze: true, IsFrozen: false } f)
                    f.Freeze();
            }
            catch { /* leave this one unfrozen rather than fail startup / theme switch */ }
        }
    }

    /// <summary>Every resource key across <paramref name="dict"/> and its merged layers (deduped).</summary>
    private static HashSet<object> CollectKeys(ResourceDictionary dict, HashSet<object> keys)
    {
        foreach (var key in dict.Keys)
            keys.Add(key);
        foreach (var child in dict.MergedDictionaries)
            CollectKeys(child, keys);
        return keys;
    }

    /// <summary>Loads a mandatory theme layer, surfacing a clear error naming the URI if it cannot be
    /// found — instead of returning null and crashing deep in the dictionary merge (e.g. when a renamed
    /// host assembly breaks the pack URI).</summary>
    private static ResourceDictionary LoadRequired(string uri)
    {
        try { return new ResourceDictionary { Source = new Uri(uri) }; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Required theme resource failed to load: {uri}", ex);
        }
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
