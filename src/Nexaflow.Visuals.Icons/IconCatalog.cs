
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Nexaflow.Visuals.Icons;

/// <summary>An icon a picker offers: what it is, the words it is shown and found by, and the text that draws it.</summary>
public sealed record IconEntry(IconRef Icon, string Name, string Keywords, string Glyph);

/// <summary>
/// Every icon there is to pick: the curated emoji and both faces of the bundled Fluent UI System Icons font (MIT —
/// <c>Assets/Fonts/LICENSE</c>). The Fluent names are read once, on first use, from the font's own name map, so a
/// font update is a file swap. Resolves a saved <see cref="IconRef"/> back to the font and glyph that draw it.
/// </summary>
public static partial class IconCatalog
{
    private const string FluentMapResource = "Nexaflow.Visuals.Icons.FluentSystemIcons-Resizable.json";

    /// <summary>Drawn in place of a Fluent icon the bundled font no longer names.</summary>
    public const string MissingGlyph = "▢";

    /// <summary>The bundled Fluent font, read from the <c>IconFonts</c> folder beside this assembly. Both faces share
    /// it; regular and filled are separate glyphs within it.</summary>
    public static FontFamily FluentFont { get; } = LoadFluentFont();

    private static FontFamily LoadFluentFont()
    {
        // Base URI must be the folder (trailing slash); "./#Family" selects the family by name — as Smufl loads Bravura.
        var dir = Path.Combine(Path.GetDirectoryName(typeof(IconCatalog).Assembly.Location) ?? AppContext.BaseDirectory,
            "IconFonts") + Path.DirectorySeparatorChar;
        return new FontFamily(new Uri(dir), "./#FluentSystemIcons-Resizable");
    }

    private static readonly Lazy<FluentMap> Fluent = new(LoadFluent);

    /// <summary>The font an icon draws with — null for an emoji, which takes the surrounding text's font and its
    /// fallback, as ribbon icons always have.</summary>
    public static FontFamily? FontFor(IconRef icon) => icon.IsFluent ? FluentFont : null;

    /// <summary>The text that draws <paramref name="icon"/> in <see cref="FontFor"/>: the emoji itself, the Fluent
    /// glyph, or <see cref="MissingGlyph"/> for a Fluent name the font lacks.</summary>
    public static string GlyphFor(IconRef icon)
    {
        if (icon.IsEmpty) return string.Empty;
        if (!icon.IsFluent) return icon.Value;
        return Fluent.Value.Glyphs.TryGetValue(icon, out var glyph) ? glyph : MissingGlyph;
    }

    public static bool Contains(IconRef icon)
        => !icon.IsEmpty && (!icon.IsFluent || Fluent.Value.Glyphs.ContainsKey(icon));

    /// <summary>Every icon in <paramref name="set"/>, or in all sets when null: emoji first, then regular, then filled.</summary>
    public static IReadOnlyList<IconEntry> All(IconSet? set = null) => set switch
    {
        IconSet.Emoji         => EmojiIcons.All,
        IconSet.FluentRegular => Fluent.Value.Regular,
        IconSet.FluentFilled  => Fluent.Value.Filled,
        _                     => Fluent.Value.Everything,
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

    // ── The Fluent name map ──────────────────────────────────────────────────

    private sealed record FluentMap(
        IReadOnlyDictionary<IconRef, string> Glyphs,
        IReadOnlyList<IconEntry> Regular,
        IReadOnlyList<IconEntry> Filled,
        IReadOnlyList<IconEntry> Everything);

    [GeneratedRegex(@"^ic_fluent_(?<name>.+)_\d+_(?<face>regular|filled)$")]
    private static partial Regex FluentName();

    private static FluentMap LoadFluent()
    {
        using var stream = typeof(IconCatalog).Assembly.GetManifestResourceStream(FluentMapResource)
            ?? throw new InvalidOperationException($"The Fluent icon map '{FluentMapResource}' is not embedded.");
        using var map = JsonDocument.Parse(stream);

        var glyphs  = new Dictionary<IconRef, string>();
        var regular = new List<IconEntry>();
        var filled  = new List<IconEntry>();
        foreach (var property in map.RootElement.EnumerateObject())
        {
            var match = FluentName().Match(property.Name);
            if (!match.Success || property.Value.ValueKind != JsonValueKind.Number) continue;

            var name = match.Groups["name"].Value;
            var icon = IconRef.Fluent(name, filled: match.Groups["face"].Value == "filled");
            if (glyphs.ContainsKey(icon)) continue;   // a second size of an icon already taken

            var glyph = char.ConvertFromUtf32(property.Value.GetInt32());
            glyphs[icon] = glyph;
            (icon.Set == IconSet.FluentFilled ? filled : regular)
                .Add(new IconEntry(icon, name.Replace('_', ' '), string.Empty, glyph));
        }

        regular.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        filled.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new FluentMap(glyphs, regular, filled, [.. EmojiIcons.All, .. regular, .. filled]);
    }
}
