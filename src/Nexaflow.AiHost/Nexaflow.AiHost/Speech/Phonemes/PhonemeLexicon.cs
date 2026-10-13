using System.IO.Compression;
using System.Text;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// Words and the phonemes a voice reads for them, from a lexicon built for its voices
/// (<c>tools/ai-models/kokoro-lexicons</c>): a gzip of <c>word TAB phonemes</c> lines, most used first. Keys are
/// lower-case except where case changes the sound (German <c>Weg</c> / <c>weg</c>), so a word is looked for as written,
/// then lower-cased.
/// </summary>
internal sealed class PhonemeLexicon
{
    private readonly Dictionary<string, string> _phonemes;

    private PhonemeLexicon(Dictionary<string, string> phonemes) => _phonemes = phonemes;

    public int Count => _phonemes.Count;

    public static PhonemeLexicon Load(string path)
    {
        using var file = File.OpenRead(path);
        using var unzipped = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(unzipped, Encoding.UTF8);
        var phonemes = new Dictionary<string, string>(400_000, StringComparer.Ordinal);
        for (string? line; (line = reader.ReadLine()) is not null; )
        {
            int tab = line.IndexOf('\t');
            if (tab > 0) phonemes.TryAdd(line[..tab], line[(tab + 1)..]);
        }
        phonemes.TrimExcess();
        return new PhonemeLexicon(phonemes);
    }

    /// <summary>A lexicon of the given entries — a test's, or one made in memory.</summary>
    public static PhonemeLexicon Of(params (string Word, string Phonemes)[] entries)
        => new(entries.ToDictionary(e => e.Word, e => e.Phonemes, StringComparer.Ordinal));

    /// <summary>The phonemes for <paramref name="word"/> as written, or lower-cased; null when it has neither.</summary>
    public string? Find(string word)
    {
        if (_phonemes.TryGetValue(word, out var phonemes)) return phonemes;
        var lower = word.ToLowerInvariant();
        return lower != word && _phonemes.TryGetValue(lower, out phonemes) ? phonemes : null;
    }
}
