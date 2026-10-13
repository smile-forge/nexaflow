using MisakiSharp;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Speech.Phonemes;
using System.Collections.Concurrent;

namespace Nexaflow.AiHost.Speech;

/// <summary>Text to the phonemes a voice reads, for one language.</summary>
internal interface IPhonemizer
{
    string Phonemize(string text);
}

/// <summary>
/// The phonemisers the host's voices read through, by language: English through MisakiSharp, French and German from the
/// lexicon a voice names. Each is built on first use — a lexicon is hundreds of thousands of words — and kept for the
/// life of the host; English, which they fall back on for English words, is built once and shared.
/// </summary>
internal sealed class SpeechPhonemizers
{
    /// <summary>The languages read from a lexicon, and what each knows beyond it.</summary>
    private static readonly Dictionary<string, Func<PhonemeLanguage>> FromLexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = () => new FrenchLanguage(),
        ["de"] = () => new GermanLanguage(),
    };

    private readonly Lazy<EnglishPhonemizer> _english = new(() => new EnglishPhonemizer());
    private readonly ConcurrentDictionary<(string Language, string Lexicon), Lazy<IPhonemizer>> _read = new();

    /// <summary>Whether a voice in <paramref name="language"/> can be read: English always, French and German with
    /// their lexicon.</summary>
    public bool Reads(string language, string? lexicon)
        => language.Equals("en", StringComparison.OrdinalIgnoreCase) || (FromLexicon.ContainsKey(language) && lexicon is not null);

    public IPhonemizer For(string language, string? lexicon)
    {
        if (language.Equals("en", StringComparison.OrdinalIgnoreCase)) return _english.Value;
        if (!FromLexicon.TryGetValue(language, out var rules) || lexicon is null)
            throw new HostRequestException(HostErrorKind.Invalid, $"This host cannot read {language} without its lexicon.");
        return _read.GetOrAdd((language.ToLowerInvariant(), lexicon), key => new Lazy<IPhonemizer>(
            () => new LexiconPhonemizer(PhonemeLexicon.Load(key.Lexicon), rules(), _english.Value))).Value;
    }
}

/// <summary>
/// American English through MisakiSharp (Apache-2.0), the C# port of misaki — the G2P Kokoro's English voices were
/// trained with: lexicon, stress, numbers, currency, homographs; words outside the lexicon through its espeak-free
/// fallback. Not thread-safe, so one phrase at a time.
/// </summary>
internal sealed class EnglishPhonemizer : IPhonemizer
{
    private readonly EnglishG2P _g2p;
    private readonly object _gate = new();

    public EnglishPhonemizer()
    {
        var fallback = new EspeakFallback(british: false, espeakProvider: EspeakReplay.Provider("en-us"));
        _g2p = new EnglishG2P(british: false, espeakFallback: fallback.Phonemize);
    }

    public string Phonemize(string text)
    {
        lock (_gate) return _g2p.Phonemize(text).Item1;
    }

    /// <summary>A word's phonemes when MisakiSharp's English lexicon knows it — rated 3 and above; below is a guess — and
    /// null otherwise: the English a French or German reply is full of, where a guess at a word that is not English would
    /// read worse than that language's own rules.</summary>
    public string? Known(string word)
    {
        if (word.Length == 0 || !word.All(char.IsAsciiLetter)) return null;
        (string Phonemes, List<EnglishG2P.MToken> Tokens) said;
        lock (_gate) said = _g2p.Phonemize(word);
        return said.Phonemes.Length == 0 || said.Tokens.Any(t => t.Rating is null or < 3) ? null : said.Phonemes;
    }
}
