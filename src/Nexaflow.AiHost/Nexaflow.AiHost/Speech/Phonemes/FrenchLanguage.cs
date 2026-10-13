using MisakiSharp;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// French beyond its lexicon: elided clitics (<c>l'</c>, <c>qu'</c>, <c>jusqu'</c>) hung on the word after them; a
/// hyphenated word stressed at its end, as French stresses a phrase; liaison — the consonant a word sounds before a
/// vowel (<c>les amis</c>, <c>vous avez</c>, <c>un an</c>); numbers said by what follows (<c>six</c>, <c>dix</c>,
/// <c>huit</c>); the words said without stress in a phrase, as measured on French replies. A word nothing knows is said
/// by MisakiSharp's espeak-free French (Apache-2.0).
/// </summary>
internal sealed class FrenchLanguage : PhonemeLanguage
{
    private readonly Lazy<EspeakG2P> _rules = new(() => new EspeakG2P("fr-fr"));
    private readonly object _gate = new();

    /// <summary>Clitics as they sound when elided.</summary>
    private static readonly Dictionary<string, string> Clitics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["l"] = "l", ["d"] = "d", ["j"] = "ʒ", ["qu"] = "k", ["n"] = "n", ["s"] = "s", ["c"] = "s", ["m"] = "m", ["t"] = "t",
        ["jusqu"] = "ʒysk", ["lorsqu"] = "lɔʁsk", ["puisqu"] = "pɥisk", ["quoiqu"] = "kwak", ["presqu"] = "pʁɛsk", ["quelqu"] = "kɛlk",
    };

    /// <summary>The consonant a word sounds before a vowel.</summary>
    private static readonly Dictionary<string, string> Liaison = new(StringComparer.Ordinal)
    {
        ["les"] = "z", ["des"] = "z", ["ces"] = "z", ["mes"] = "z", ["tes"] = "z", ["ses"] = "z", ["nos"] = "z", ["vos"] = "z",
        ["leurs"] = "z", ["aux"] = "z", ["nous"] = "z", ["vous"] = "z", ["ils"] = "z", ["elles"] = "z", ["deux"] = "z", ["trois"] = "z",
        ["quelques"] = "z", ["plusieurs"] = "z", ["certains"] = "z", ["certaines"] = "z", ["dans"] = "z", ["chez"] = "z", ["sans"] = "z",
        ["sous"] = "z", ["très"] = "z", ["plus"] = "z", ["quels"] = "z", ["quelles"] = "z", ["toutes"] = "z", ["peux"] = "z", ["veux"] = "z",
        ["un"] = "n", ["on"] = "n", ["en"] = "n", ["mon"] = "n", ["ton"] = "n", ["son"] = "n", ["aucun"] = "n", ["bien"] = "n", ["rien"] = "n",
        ["est"] = "t", ["c'est"] = "t", ["sont"] = "t", ["ont"] = "t", ["font"] = "t", ["vont"] = "t", ["tout"] = "t", ["petit"] = "t",
        ["grand"] = "t", ["quand"] = "t", ["peut"] = "t", ["doit"] = "t", ["était"] = "t", ["avait"] = "t", ["devrait"] = "t",
        ["pourrait"] = "t", ["serait"] = "t", ["aurait"] = "t", ["faut"] = "t", ["premier"] = "ʁ", ["dernier"] = "ʁ",
    };

    /// <summary>Words beginning with an h that blocks liaison and elision (h aspiré), and others that do the same.</summary>
    private static readonly HashSet<string> NoLiaisonBefore = new(StringComparer.OrdinalIgnoreCase)
    {
        "haut", "haute", "hauts", "hautes", "hasard", "héros", "hibou", "hache", "honte", "huit", "huitième", "onze", "onzième",
        "hall", "hamac", "hangar", "haricot", "hareng", "hors", "hockey", "homard", "housse", "hublot", "hurler", "oui", "ouistiti",
        "yaourt", "yoga", "hamburger", "handicap", "hollande", "hongrie",
    };

    /// <summary>Numbers whose last consonant is silent before a consonant and voiced before a vowel.</summary>
    private static readonly Dictionary<string, (bool SilentBeforeConsonant, string BeforeVowel)> Counted = new(StringComparer.Ordinal)
    {
        ["six"] = (true, "z"), ["dix"] = (true, "z"), ["huit"] = (true, "t"), ["tous"] = (true, "z"),
    };

    private static readonly Dictionary<string, string> Said = new(StringComparer.Ordinal)
    {
        ["y"] = "i",                       // the pronoun, said alone as the letter
        ["as"] = "ˈa",                     // tu as
        ["Nexaflow"] = "nɛksaflˈo", ["nexaflow"] = "nɛksaflˈo",
    };

    /// <summary>The words French replies say without stress in a phrase, as measured against espeak-ng in context.</summary>
    private static readonly HashSet<string> Light = new(StringComparer.Ordinal)
    {
        "de", "pour", "le", "la", "vous", "que", "tu", "je", "à", "un", "les", "une", "il", "ça", "et", "ce", "est", "en", "sur", "ou",
        "dans", "des", "votre", "c'est", "plus", "du", "cette", "par", "me", "très", "ton", "a", "te", "êtes", "où", "quand", "j'ai", "qui",
        "sous", "tout", "toutes", "ta", "chaque", "au", "ne", "ils", "faut", "ces", "aux", "tant", "quel", "mais", "qu'il", "elle", "d'un",
        "nous", "vos", "mon", "ma", "mes", "son", "sa", "ses", "se", "lui", "leur", "on", "avec", "sans", "notre", "nos", "leurs",
        "y", "qu'elle", "l'", "d'une",
    };

    public override SpeechNormalizer Normalizer { get; } = new FrenchNormalizer();
    public override IReadOnlyDictionary<string, string> Fixed => Said;
    public override IReadOnlySet<string> Unstressed => Light;

    public override (string Clitic, string Host)? Clitic(string word)
    {
        int apostrophe = word.IndexOf('\'');
        if (apostrophe <= 0 || apostrophe == word.Length - 1) return null;
        return Clitics.TryGetValue(word[..apostrophe], out var clitic) ? (clitic, word[(apostrophe + 1)..]) : null;
    }

    public override string LetterToSound(string word)
    {
        lock (_gate) return _rules.Value.Phonemize(word).Trim();
    }

    /// <summary>French stresses the end of a word group: only the last part keeps its stress.</summary>
    public override string Joined(IReadOnlyList<string> phonemes)
        => string.Concat(phonemes.Take(phonemes.Count - 1).Select(Destressed)) + (phonemes.Count > 0 ? phonemes[^1] : "");

    public override void Context(IReadOnlyList<SaidWord> words)
    {
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var next = !word.PauseAfter && i + 1 < words.Count ? words[i + 1] : null;
            var text = word.Text.ToLowerInvariant();
            bool vowelNext = next is not null && StartsWithVowel(next);

            if (Counted.TryGetValue(text, out var counted) && next is not null)
            {
                // six heures, dix ans: voiced before a vowel; six jours: silent before a consonant.
                var body = word.Phonemes.TrimEnd();
                word.Phonemes = vowelNext ? body[..^1] + counted.BeforeVowel : counted.SilentBeforeConsonant ? body[..^1] : body;
                continue;
            }
            if (vowelNext && Liaison.TryGetValue(text, out var consonant) && !word.Phonemes.EndsWith(consonant))
                word.Phonemes += consonant;
        }
    }

    /// <summary>Whether a word begins with a vowel sound liaison can carry onto — by its sound, not its spelling: OneDrive
    /// begins with w, as do the oi and ou words that do take it (les oiseaux), and the y and i words that begin with j (les
    /// yeux, les ions).</summary>
    private static bool StartsWithVowel(SaidWord word)
    {
        var text = word.Text.ToLowerInvariant();
        if (text.Length == 0 || NoLiaisonBefore.Contains(text)) return false;
        var sound = word.Phonemes.TrimStart('ˈ', 'ˌ');
        if (sound.Length == 0) return false;
        return sound[0] switch
        {
            'w' => text.StartsWith("oi") || text.StartsWith("ou") || text.StartsWith("oî") || text.StartsWith("où"),
            'j' or 'ɥ' => text[0] is 'y' or 'i' or 'h',
            _ => "aeiouyøœɛɔəɑ".Contains(sound[0]),
        };
    }
}
