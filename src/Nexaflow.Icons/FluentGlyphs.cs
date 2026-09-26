using System.Text.Json;

namespace Nexaflow.Icons;

/// <summary>
/// Which glyph each Fluent UI System Icons name draws, read from the font's own name map — the font itself is
/// Nexaflow.Visuals.Icons', and a glyph is set in it by its family name (<see cref="Family"/>).
/// <para>
/// The map is streamed into name → glyph the first time a Fluent icon is asked for, keeping the first size of each icon,
/// so nothing pays for it that never draws one.
/// </para>
/// </summary>
public static class FluentGlyphs
{
    private const string MapResource = "Nexaflow.Icons.FluentSystemIcons-Resizable.json";
    private const string Prefix = "ic_fluent_";

    /// <summary>The family name of the font the glyphs are in.</summary>
    public const string Family = "FluentSystemIcons-Resizable";

    private static readonly Lazy<IReadOnlyDictionary<IconRef, string>> Glyphs = new(Load);

    /// <summary>Every Fluent icon, both faces, with the glyph that draws it.</summary>
    public static IReadOnlyDictionary<IconRef, string> All => Glyphs.Value;

    /// <summary>The glyph a Fluent icon draws — or null for an emoji, or for a name the font has no icon of.</summary>
    public static string? Of(IconRef icon) => icon.IsFluent && Glyphs.Value.TryGetValue(icon, out var glyph) ? glyph : null;

    /// <summary>
    /// Streams the map (<c>"ic_fluent_arrow_clockwise_20_filled": 57345</c>, one per line) into name → glyph, keeping
    /// the first size of each icon. No document is built and nothing else is kept.
    /// </summary>
    private static IReadOnlyDictionary<IconRef, string> Load()
    {
        byte[] json;
        using (var stream = typeof(FluentGlyphs).Assembly.GetManifestResourceStream(MapResource)
                   ?? throw new InvalidOperationException($"The Fluent icon map '{MapResource}' is not embedded."))
        using (var buffer = new MemoryStream((int)stream.Length))
        {
            stream.CopyTo(buffer);
            json = buffer.GetBuffer()[..(int)buffer.Length];
        }

        var glyphs = new Dictionary<IconRef, string>(6000);
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true });
        string? key = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                key = reader.GetString();
                continue;
            }
            if (reader.TokenType != JsonTokenType.Number || key is null || !TryParseName(key, out var icon)) continue;
            glyphs.TryAdd(icon, char.ConvertFromUtf32(reader.GetInt32()));
        }
        return glyphs;
    }

    private static bool TryParseName(string key, out IconRef icon)
    {
        icon = default;
        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var face = key.LastIndexOf('_');
        if (face <= Prefix.Length) return false;
        var filled = key.AsSpan(face + 1) switch
        {
            "filled"  => true,
            "regular" => false,
            _         => (bool?)null,
        };
        if (filled is null) return false;

        var size = key.LastIndexOf('_', face - 1);
        if (size <= Prefix.Length || face - size < 2) return false;
        foreach (var c in key.AsSpan(size + 1, face - size - 1))
            if (!char.IsAsciiDigit(c)) return false;

        icon = IconRef.Fluent(key[Prefix.Length..size], filled.Value);
        return true;
    }
}
