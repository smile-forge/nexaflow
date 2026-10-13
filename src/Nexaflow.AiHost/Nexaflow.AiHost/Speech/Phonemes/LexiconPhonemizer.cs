using System.Text;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// Text to the phonemes a Kokoro voice reads, for a language with a lexicon built for its voices: the text normalised
/// into words and pauses, each word found — the language's fixed words, the lexicon, an elided clitic, a hyphenated
/// word's parts, an identifier's parts, English, a compound's parts, and last its letter-to-sound rules — then the
/// words changed by what is next to them, and joined as Kokoro reads them: a space between words, a pause's
/// punctuation after the word before it.
/// </summary>
internal sealed class LexiconPhonemizer(PhonemeLexicon lexicon, PhonemeLanguage language, EnglishPhonemizer english) : IPhonemizer
{
    public string Phonemize(string text)
    {
        var said = new List<SaidWord>();
        var pauses = new Dictionary<int, string>();   // the pause after the word at an index
        foreach (var unit in language.Normalizer.Normalize(text))
        {
            switch (unit)
            {
                case SpokenWord word:
                    var phonemes = word.Phonemes ?? Find(word);
                    if (phonemes.Length > 0) said.Add(new SaidWord(word.Text, phonemes));
                    break;
                case SpokenPause pause when said.Count > 0:
                    pauses[said.Count - 1] = pause.Mark;
                    said[^1].PauseAfter = true;
                    break;
            }
        }
        if (said.Count == 0) return "";
        said[^1].PauseAfter = true;

        language.Context(said);
        for (int i = 0; i < said.Count; i++)
            if (!said[i].PauseAfter && language.Unstressed.Contains(said[i].Text.ToLowerInvariant()))
                said[i].Phonemes = PhonemeLanguage.Destressed(said[i].Phonemes);

        var joined = new StringBuilder();
        for (int i = 0; i < said.Count; i++)
        {
            if (i > 0) joined.Append(' ');
            joined.Append(said[i].Phonemes);
            if (pauses.TryGetValue(i, out var mark)) joined.Append(mark);
        }
        return joined.ToString();
    }

    /// <summary>A word's phonemes: found whole, or as the parts it was said in; empty when it has no sound.</summary>
    private string Find(SpokenWord word)
    {
        if (word.Parts is { } parts && lexicon.Find(word.Text) is null)
            return language.Joined([.. parts.Select(p => Find(p, depth: 0))]);
        return Find(word.Text, depth: 0);
    }

    internal string Find(string word, int depth)
    {
        if (language.Fixed.TryGetValue(word, out var phonemes) || language.Fixed.TryGetValue(word.ToLowerInvariant(), out phonemes))
            return phonemes;
        if (lexicon.Find(word) is { } found) return found;
        if (depth > 2) return language.LetterToSound(word);

        if (language.Clitic(word) is var (clitic, host)) return clitic + Find(host, depth + 1);

        if (word.Contains('-'))
        {
            var parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return language.Joined([.. parts.Select(p => Find(p, depth + 1))]);
        }

        // An identifier written as one word (JavaScript, iPhone): its parts.
        if (CamelParts(word) is { Count: > 1 } camel)
            return string.Join(' ', camel.Select(p => Find(p, depth + 1)));

        if (english.Known(word) is { } said) return said;
        if (language.Compound(word, w => lexicon.Find(w)) is { } compound)
            return language.Joined([.. compound.Select(p => p.StartsWith('~') ? p[1..] : Find(p, depth + 1))]);
        return language.LetterToSound(word);
    }

    private static List<string>? CamelParts(string word)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 1; i < word.Length; i++)
            if (char.IsLower(word[i - 1]) && char.IsUpper(word[i])) { parts.Add(word[start..i]); start = i; }
        if (start == 0) return null;
        parts.Add(word[start..]);
        return parts;
    }
}

