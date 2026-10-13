using MisakiSharp;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>A word as it will be said, for the rules that change it by what is next to it.</summary>
internal sealed class SaidWord(string text, string phonemes)
{
    public string Text { get; } = text;
    public string Phonemes { get; set; } = phonemes;

    /// <summary>A pause follows: no sound carries across it.</summary>
    public bool PauseAfter { get; set; }
}

/// <summary>
/// What a language knows beyond its lexicon: how its text is said (<see cref="Normalizer"/>), the words it says its own
/// way, how a word that is not in the lexicon is still found (a clitic, a compound, by rule), how a word made of parts
/// is stressed, and how words change by what is next to them. <see cref="LexiconPhonemizer"/> runs it.
/// </summary>
internal abstract class PhonemeLanguage
{
    public abstract SpeechNormalizer Normalizer { get; }

    /// <summary>Words said a set way whatever the lexicon says: names of the product and its kin, and words a lexicon
    /// built from words said alone gets wrong in running speech (French <c>y</c>, said alone as the letter).</summary>
    public abstract IReadOnlyDictionary<string, string> Fixed { get; }

    /// <summary>Words said without stress inside a phrase: articles, pronouns, prepositions, auxiliaries.</summary>
    public abstract IReadOnlySet<string> Unstressed { get; }

    /// <summary>A word with an elided clitic (<c>l'ami</c>): the clitic's phonemes and the word it hangs on.</summary>
    public virtual (string Clitic, string Host)? Clitic(string word) => null;

    /// <summary>A word not in the lexicon found as parts that are (a German compound); null when it is not.</summary>
    public virtual IReadOnlyList<string>? Compound(string word, Func<string, string?> find) => null;

    /// <summary>A word nothing else knows, said by rule.</summary>
    public abstract string LetterToSound(string word);

    /// <summary>Parts said as one word: their phonemes run together, with the stress the language gives a compound.</summary>
    public abstract string Joined(IReadOnlyList<string> phonemes);

    /// <summary>What words do to each other in a phrase: French liaison, numbers voiced by what follows.</summary>
    public virtual void Context(IReadOnlyList<SaidWord> words) { }

    /// <summary>Stress marks taken out: a word said without stress.</summary>
    internal static string Destressed(string phonemes) => phonemes.Replace("ˈ", "").Replace("ˌ", "");

    /// <summary>Later parts' primary stress made secondary: how German stresses a compound.</summary>
    protected static string FirstStressed(IReadOnlyList<string> phonemes)
        => phonemes.Count == 0 ? "" : phonemes[0] + string.Concat(phonemes.Skip(1).Select(p => p.Replace('ˈ', 'ˌ')));
}

