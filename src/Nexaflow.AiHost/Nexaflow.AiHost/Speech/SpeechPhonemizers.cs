using MisakiSharp;

namespace Nexaflow.AiHost.Speech;

/// <summary>Text to the phonemes a voice reads, for one language.</summary>
internal interface IPhonemizer
{
    string Phonemize(string text);
}

/// <summary>
/// The phonemisers the host's voices read through, by language. Each is built on first use — English's lexicons are
/// tens of megabytes — and kept for the life of the host.
/// </summary>
internal sealed class SpeechPhonemizers
{
    private readonly Dictionary<string, Lazy<IPhonemizer>> _byLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(() => new EnglishPhonemizer()),
    };

    public bool Has(string language) => _byLanguage.ContainsKey(language);

    public IPhonemizer For(string language) => _byLanguage[language].Value;
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
}
