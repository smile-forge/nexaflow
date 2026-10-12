using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Common.Speech;

/// <summary>
/// Whether a spoken answer to a yes-or-no question is yes or no — "yeah, go ahead", "non, attends", "nein, lieber
/// nicht" — from how it starts. English, French and German; a language not given is judged by all three.
/// </summary>
public static class SpokenAnswers
{
    private static readonly Dictionary<string, string[]> Yes = new(StringComparer.Ordinal)
    {
        ["en"] = ["yes", "yeah", "yep", "yup", "sure", "ok", "okay", "go ahead", "do it", "please do", "go on", "fine", "alright", "all right", "absolutely", "of course"],
        ["fr"] = ["oui", "ouais", "d'accord", "ok", "okay", "vas-y", "allez-y", "vas y", "allez y", "bien sûr", "c'est bon", "fais-le", "volontiers"],
        ["de"] = ["ja", "jawohl", "klar", "ok", "okay", "mach", "mach das", "mach es", "gerne", "natürlich", "in ordnung", "los"],
    };

    private static readonly Dictionary<string, string[]> No = new(StringComparer.Ordinal)
    {
        ["en"] = ["no", "nope", "don't", "do not", "stop", "cancel", "wait", "never mind", "not now", "hold on"],
        ["fr"] = ["non", "pas maintenant", "arrête", "attends", "annule", "laisse tomber", "surtout pas"],
        ["de"] = ["nein", "nee", "stopp", "stop", "halt", "warte", "abbrechen", "lieber nicht", "nicht jetzt"],
    };

    /// <returns>True for yes, false for no, null when it is neither — or both.</returns>
    public static bool? Classify(string answer, string? language)
    {
        string text = " " + Normalised(answer) + " ";
        bool yes = For(Yes, language).Any(p => Starts(text, p));
        bool no  = For(No, language).Any(p => Starts(text, p));
        return yes == no ? null : yes;
    }

    /// <summary>An answer counts by how it starts: "go ahead and stop it" is a yes, "no, wait" a no.</summary>
    private static bool Starts(string text, string phrase) => text.StartsWith(" " + phrase + " ", StringComparison.Ordinal);

    private static IEnumerable<string> For(Dictionary<string, string[]> byLanguage, string? language)
        => language is not null && byLanguage.TryGetValue(language, out var phrases) ? phrases : byLanguage.Values.SelectMany(p => p);

    private static string Normalised(string text)
    {
        var words = new StringBuilder();
        foreach (char c in text.ToLower(CultureInfo.InvariantCulture))
            words.Append(char.IsLetterOrDigit(c) || c is '\'' or '-' ? c : c == '’' ? '\'' : ' ');
        return string.Join(' ', words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
