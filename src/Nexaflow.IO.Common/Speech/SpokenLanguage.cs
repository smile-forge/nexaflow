using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Common.Speech;

/// <summary>
/// Which of the spoken languages — English, French, German — a piece of text is in, from the short words every
/// sentence of a language is made of. A reply's language picks the voice that reads it, so a German sentence in an
/// English conversation is read by the German voice.
/// </summary>
public static class SpokenLanguage
{
    private static readonly Dictionary<string, HashSet<string>> Common = new(StringComparer.Ordinal)
    {
        ["en"] = WordSet("the a an and or but is are was were be been it this that these those to of in on at for with from by as not you your we our they their i my he she his her what which who how do does did have has had will would can could should there here yes no"),
        ["fr"] = WordSet("le la les un une des du de et ou mais est sont était été ce cette ces que qui quoi dans sur pour avec par pas ne vous votre nous notre ils elles leur je mon ma il elle son sa au aux y en a ont fait être avoir oui non très plus aussi c'est j'ai qu'il"),
        ["de"] = WordSet("der die das den dem des ein eine einen einem einer und oder aber ist sind war waren sein es dies diese dieser zu von in im auf für mit aus bei nicht kein keine sie ihr ihre wir unser ich mein er sein was wer wie wo hat haben wird werden kann können soll ja nein auch noch schon"),
    };

    /// <summary>The language the text is most clearly in, or null when it is too short or too mixed to tell.</summary>
    public static string? Detect(string text)
    {
        var scores = Common.Keys.ToDictionary(k => k, _ => 0);
        int words = 0;
        foreach (var word in Tokens(text.ToLower(CultureInfo.InvariantCulture)))
        {
            words++;
            foreach (var (language, common) in Common)
                if (common.Contains(word)) scores[language]++;
        }

        // Letters only one language uses settle a near tie: ß and umlauts, French accents.
        foreach (char c in text)
        {
            if (c is 'ß' or 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü') scores["de"]++;
            else if (c is 'é' or 'è' or 'ê' or 'à' or 'ç' or 'ù' or 'œ' or 'É' or 'È' or 'À' or 'Ç') scores["fr"]++;
        }

        var ranked = scores.OrderByDescending(s => s.Value).ToList();
        if (words == 0 || ranked[0].Value == 0 || ranked[0].Value == ranked[1].Value) return null;
        return ranked[0].Key;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var word = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetter(c) || ((c is '\'' or '’') && word.Length > 0)) word.Append(c == '’' ? '\'' : c);
            else if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
        }
        if (word.Length > 0) yield return word.ToString();
    }

    private static HashSet<string> WordSet(string list) => [.. list.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}
