using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Common.Speech;

/// <summary>
/// What a transcript says about a turn that the audio alone cannot, whichever provider heard it: an utterance of only a
/// listener's noises — "mm-hm", "ouais", "ja, genau" — is a backchannel, not a turn. English, French and German; a language not
/// given is judged by all three. Whether a turn is finished is the AI host's to judge, where the listening is.
/// </summary>
public static class SpokenTurns
{
    private static readonly Dictionary<string, HashSet<string>> Backchannels = new(StringComparer.Ordinal)
    {
        ["en"] = Words("mm mmm mhm mm-hm mm-hmm uh-huh hmm yeah yep yes ok okay right sure ah oh"),
        ["fr"] = Words("mm mmm mhm hmm ouais oui ok okay d'accord ah oh bon voilà"),
        ["de"] = Words("mm mmm mhm hmm ja jaja jo ok okay genau ah oh achso stimmt"),
    };

    /// <summary>Whether every word is a listener's noise.</summary>
    public static bool IsBackchannel(string transcript, string? language)
    {
        var words = Normalised(transcript);
        var noises = For(language);
        return words.Count > 0 && words.All(noises.Contains);
    }

    private static readonly HashSet<string> AnyBackchannel = [.. Backchannels.Values.SelectMany(w => w)];

    private static HashSet<string> For(string? language)
        => language is not null && Backchannels.TryGetValue(language, out var words) ? words : AnyBackchannel;

    /// <summary>Lower-case words, punctuation dropped but for the apostrophes and hyphens inside them.</summary>
    private static List<string> Normalised(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (char c in text.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c) || ((c is '\'' or '’' or '-') && word.Length > 0)) word.Append(c == '’' ? '\'' : c);
            else Flush();
        }
        Flush();
        return words;

        void Flush()
        {
            string w = word.ToString().TrimEnd('\'', '-');
            if (w.Length > 0) words.Add(w);
            word.Clear();
        }
    }

    private static HashSet<string> Words(string list) => [.. list.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}
