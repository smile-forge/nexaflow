using System.Globalization;
using System.Text;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// What a transcript says about whether the speaker has finished their turn, that the audio alone cannot. A turn that trails off —
/// on a comma, an ellipsis or a filler ("um", "euh", "ähm") — is not finished, however final it sounded; nor is one that stops on a
/// word that opens a phrase ("and", "parce que", "die"), unless the transcriber closed the sentence: "Nein, nicht das." ends on an
/// article and is complete. English, French and German; a language not given is judged by all three.
/// </summary>
internal static class TurnWords
{
    private static readonly Dictionary<string, HashSet<string>> Fillers = new(StringComparer.Ordinal)
    {
        ["en"] = Words("um uh er erm hmm"),
        ["fr"] = Words("euh heu ben bah hum"),
        ["de"] = Words("äh ähm öh hm"),
    };

    private static readonly Dictionary<string, HashSet<string>> Openers = new(StringComparer.Ordinal)
    {
        ["en"] = Words("and or but because so if when that which who the a an to of in on at for with from like than as my your our their"),
        ["fr"] = Words("et ou mais parce que qui quand si donc car le la les un une des du de au aux à avec pour dans sur sans chez mon ma mes ton ta tes son sa ses notre votre leur ce cette ces"),
        ["de"] = Words("und oder aber weil dass ob wenn als denn sondern der die das den dem des ein eine einen einem einer mit für zu von bei aus nach über unter vor auf an in mein meine dein deine sein seine ihr ihre unser unsere"),
    };

    private static readonly HashSet<string> AnyFiller = [.. Fillers.Values.SelectMany(w => w)];
    private static readonly HashSet<string> AnyOpener = [.. Openers.Values.SelectMany(w => w)];

    /// <summary>Whether the transcript reads as a finished turn. An empty one — nothing was made of the sound — does.</summary>
    public static bool LooksFinished(string transcript, string? language)
    {
        string text = transcript.TrimEnd();
        if (text.Length == 0) return true;
        if (text[^1] is ',' or ';' or ':' or '-' or '—' or '–' or '…' || text.EndsWith("...", StringComparison.Ordinal)) return false;

        var words = Normalised(text);
        if (words.Count == 0) return true;
        if (For(Fillers, AnyFiller, language).Contains(words[^1])) return false;
        if (text[^1] is '.' or '?' or '!' or '。') return true;
        return !For(Openers, AnyOpener, language).Contains(words[^1]);
    }

    private static HashSet<string> For(Dictionary<string, HashSet<string>> byLanguage, HashSet<string> any, string? language)
        => language is not null && byLanguage.TryGetValue(language, out var words) ? words : any;

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
