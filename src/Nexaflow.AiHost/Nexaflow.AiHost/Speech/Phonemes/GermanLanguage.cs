namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// German beyond its lexicon: a compound the lexicon does not hold found as words it does, with the linking sounds
/// between them, and stressed as German stresses a compound — on its first part; the words said without stress in a
/// phrase, as measured on German replies; a word nothing knows said by <see cref="GermanLetterToSound"/>. In a German
/// phrase only stress changes between words, so there is no more to its context than that.
/// </summary>
internal sealed class GermanLanguage : PhonemeLanguage
{
    /// <summary>A compound's parts are at least this long: shorter ones are too often a suffix or a stray syllable.</summary>
    private const int ShortestPart = 3;

    /// <summary>Linking elements between a compound's parts, and how they sound.</summary>
    private static readonly (string Written, string Said)[] Links = [("s", "s"), ("es", "əs"), ("n", "n"), ("en", "ən"), ("er", "ɐ"), ("e", "ə")];

    /// <summary>Endings that are not words of a compound: a split that ends on one has found a suffix, not a word.</summary>
    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "ung", "ungen", "heit", "keit", "lich", "liche", "lichen", "chen", "schaft", "isch", "ische", "bar", "sam", "los", "haft",
        "tum", "nis", "ling", "lein", "ern", "ten", "end", "ende", "enden", "est", "este", "esten", "ester", "sten", "ste", "ster", "stes", "stem",
    };

    private static readonly Dictionary<string, string> Said = new(StringComparer.Ordinal)
    {
        ["Nexaflow"] = "nˈɛksafloː", ["nexaflow"] = "nˈɛksafloː",
    };

    /// <summary>The words German replies say without stress in a phrase, as measured against espeak-ng in context.</summary>
    private static readonly HashSet<string> Light = new(StringComparer.Ordinal)
    {
        "die", "ich", "und", "sie", "das", "du", "ist", "für", "der", "von", "den", "auf", "zu", "in", "dir", "mir", "wie", "ein", "um",
        "dass", "sind", "als", "es", "nach", "dem", "dann", "dich", "hast", "mich", "mit", "sich", "da", "habe", "hat", "am", "so", "bei",
        "ihr", "wir", "im", "bin", "noch", "war", "sein", "er", "je", "pro", "durch", "dies", "des", "man", "zum", "bis", "denn", "euch",
        "doch", "ins", "ihn", "eine", "einen", "einem", "einer", "eines", "zur", "vom", "aus", "oder", "aber", "wenn", "ob", "wird", "kann",
    };

    public override SpeechNormalizer Normalizer { get; } = new GermanNormalizer();
    public override IReadOnlyDictionary<string, string> Fixed => Said;
    public override IReadOnlySet<string> Unstressed => Light;

    public override string LetterToSound(string word) => GermanLetterToSound.Phonemes(word);

    public override string Joined(IReadOnlyList<string> phonemes) => FirstStressed(phonemes);

    /// <summary>
    /// The fewest words of the lexicon, each <see cref="ShortestPart"/> letters or more, that the word is made of, with
    /// a linking element allowed between two; a linking element comes back as <c>~</c> and its sound. Null when no
    /// split covers it, it has only one part, or it ends on a suffix.
    /// </summary>
    public override IReadOnlyList<string>? Compound(string word, Func<string, string?> find)
    {
        var s = word.ToLowerInvariant();
        int n = s.Length;
        if (n < 2 * ShortestPart) return null;

        var best = new List<string>?[n + 1];
        best[0] = [];
        for (int i = 0; i < n; i++)
        {
            if (best[i] is not { } upTo) continue;
            var starts = new List<(int At, string? Link)> { (i, null) };
            if (i > 0)
                foreach (var (written, said) in Links)
                    if (string.CompareOrdinal(s, i, written, 0, written.Length) == 0) starts.Add((i + written.Length, "~" + said));
            foreach (var (start, link) in starts)
                for (int end = start + ShortestPart; end <= n; end++)
                {
                    var part = s[start..end];
                    if (find(part) is null && find(Capitalised(part)) is null) continue;
                    List<string> candidate = [.. upTo, .. link is null ? Array.Empty<string>() : [link], part];
                    if (best[end] is null || Cost(candidate).CompareTo(Cost(best[end]!)) < 0) best[end] = candidate;
                }
        }

        var parts = best[n];
        if (parts is null || parts.Count(p => !p.StartsWith('~')) < 2 || Suffixes.Contains(parts[^1])) return null;
        return parts;
    }

    private static string Capitalised(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>Fewest words first, then the shortest linking elements.</summary>
    private static (int Words, int Links) Cost(List<string> parts)
        => (parts.Count(p => !p.StartsWith('~')), parts.Where(p => p.StartsWith('~')).Sum(p => p.Length - 1));
}

