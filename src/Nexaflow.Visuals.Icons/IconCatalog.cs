using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Nexaflow.Visuals.Icons;

/// <summary>An icon a picker offers: what it is, the words it is shown and found by, and the text that draws it.</summary>
public sealed record IconEntry(IconRef Icon, string Name, string Keywords, string Glyph);

/// <summary>
/// Every icon there is to pick: the curated emoji and both faces of the bundled Fluent UI System Icons font (MIT —
/// <c>Assets/Fonts/LICENSE</c>). The Fluent names come from the font's own name map, so a font update is a file swap.
/// <para>
/// Drawing and picking cost differently, and drawing is what every window does: <see cref="GlyphFor"/> needs only the
/// name → glyph lookup, streamed out of the map the first time a Fluent icon is drawn (never, for an all-emoji
/// ribbon). The sorted, searchable entries a picker lists are built from that lookup only when a picker first asks.
/// </para>
/// </summary>
public static class IconCatalog
{
    private const string FluentMapResource = "Nexaflow.Visuals.Icons.FluentSystemIcons-Resizable.json";
    private const string FluentPrefix = "ic_fluent_";

    /// <summary>Drawn in place of a Fluent icon the bundled font no longer names.</summary>
    public const string MissingGlyph = "▢";

    private static readonly Lazy<FontFamily> Font = new(LoadFluentFont);
    private static readonly Lazy<IReadOnlyDictionary<IconRef, string>> Glyphs = new(LoadGlyphs);
    private static readonly Lazy<FluentEntries> Entries = new(BuildEntries);

    /// <summary>The bundled Fluent font, read from the <c>IconFonts</c> folder beside this assembly. Both faces share
    /// it; regular and filled are separate glyphs within it.</summary>
    public static FontFamily FluentFont => Font.Value;

    /// <summary>The font an icon draws with — null for an emoji, which takes the surrounding text's font and its
    /// fallback, as ribbon icons always have.</summary>
    public static FontFamily? FontFor(IconRef icon) => icon.IsFluent ? Font.Value : null;

    /// <summary>The text that draws <paramref name="icon"/> in <see cref="FontFor"/>: the emoji itself, the Fluent
    /// glyph, or <see cref="MissingGlyph"/> for a Fluent name the font lacks.</summary>
    public static string GlyphFor(IconRef icon)
    {
        if (icon.IsEmpty) return string.Empty;
        if (!icon.IsFluent) return icon.Value;
        return Glyphs.Value.TryGetValue(icon, out var glyph) ? glyph : MissingGlyph;
    }

    public static bool Contains(IconRef icon)
        => !icon.IsEmpty && (!icon.IsFluent || Glyphs.Value.ContainsKey(icon));

    /// <summary>Every icon in <paramref name="set"/>, or in all sets when null: emoji first, then regular, then filled.</summary>
    public static IReadOnlyList<IconEntry> All(IconSet? set = null) => set switch
    {
        IconSet.Emoji         => EmojiIcons.All,
        IconSet.FluentRegular => Entries.Value.Regular,
        IconSet.FluentFilled  => Entries.Value.Filled,
        _                     => Entries.Value.Everything,
    };

    /// <summary>
    /// The icons matching <paramref name="query"/>, best first: the exact name, then names that start with it, then
    /// names with a word starting with each word of the query, then the same against keywords, then names merely
    /// containing it. An empty query is <see cref="All"/>.
    /// </summary>
    public static IReadOnlyList<IconEntry> Search(string? query, IconSet? set = null)
    {
        var source = All(set);
        var q = Normalise(query);
        if (q.Length == 0) return source;

        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return source
            .Select((entry, order) => (entry, order, score: Score(entry, q, words)))
            .Where(x => x.score < int.MaxValue)
            .OrderBy(x => x.score)
            .ThenBy(x => x.entry.Name.Length)
            .ThenBy(x => x.order)
            .Select(x => x.entry)
            .ToList();
    }

    private static int Score(IconEntry entry, string q, string[] words)
    {
        var name = entry.Name;
        if (name == q) return 0;
        if (name.StartsWith(q, StringComparison.Ordinal)) return 1;
        if (EveryWordStarts(name, words)) return 2;
        if (EveryWordStarts(entry.Keywords, words)) return 3;
        if (name.Contains(q, StringComparison.Ordinal)) return 4;
        return int.MaxValue;
    }

    private static bool EveryWordStarts(string text, string[] words)
    {
        if (text.Length == 0) return false;
        var candidates = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => candidates.Any(c => c.StartsWith(w, StringComparison.Ordinal)));
    }

    private static string Normalise(string? text)
        => string.Join(' ', (text ?? string.Empty).Trim().ToLowerInvariant().Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // ── Loading ──────────────────────────────────────────────────────────────

    private static FontFamily LoadFluentFont()
    {
        // Base URI must be the folder (trailing slash); "./#Family" selects the family by name — as Smufl loads Bravura.
        var dir = Path.Combine(Path.GetDirectoryName(typeof(IconCatalog).Assembly.Location) ?? AppContext.BaseDirectory,
            "IconFonts") + Path.DirectorySeparatorChar;
        return new FontFamily(new Uri(dir), "./#FluentSystemIcons-Resizable");
    }

    /// <summary>
    /// Streams the map (<c>"ic_fluent_arrow_clockwise_20_filled": 57345</c>, one per line) into name → glyph, keeping
    /// the first size of each icon. No document is built and nothing else is kept.
    /// </summary>
    private static IReadOnlyDictionary<IconRef, string> LoadGlyphs()
    {
        byte[] json;
        using (var stream = typeof(IconCatalog).Assembly.GetManifestResourceStream(FluentMapResource)
                   ?? throw new InvalidOperationException($"The Fluent icon map '{FluentMapResource}' is not embedded."))
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

    /// <summary><c>ic_fluent_arrow_clockwise_20_filled</c> → the filled <c>arrow_clockwise</c>.</summary>
    internal static bool TryParseName(string key, out IconRef icon)
    {
        icon = default;
        if (!key.StartsWith(FluentPrefix, StringComparison.Ordinal)) return false;

        var face = key.LastIndexOf('_');
        if (face <= FluentPrefix.Length) return false;
        var filled = key.AsSpan(face + 1) switch
        {
            "filled"  => true,
            "regular" => false,
            _         => (bool?)null,
        };
        if (filled is null) return false;

        var size = key.LastIndexOf('_', face - 1);
        if (size <= FluentPrefix.Length || face - size < 2) return false;
        foreach (var c in key.AsSpan(size + 1, face - size - 1))
            if (!char.IsAsciiDigit(c)) return false;

        icon = IconRef.Fluent(key[FluentPrefix.Length..size], filled.Value);
        return true;
    }

    private sealed record FluentEntries(
        IReadOnlyList<IconEntry> Regular,
        IReadOnlyList<IconEntry> Filled,
        IReadOnlyList<IconEntry> Everything);

    private static FluentEntries BuildEntries()
    {
        var regular = new List<IconEntry>();
        var filled  = new List<IconEntry>();
        foreach (var (icon, glyph) in Glyphs.Value)
            (icon.Set == IconSet.FluentFilled ? filled : regular)
                .Add(new IconEntry(icon, icon.Value.Replace('_', ' '), string.Empty, glyph));

        regular.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        filled.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new FluentEntries(regular, filled, [.. EmojiIcons.All, .. regular, .. filled]);
    }
}
