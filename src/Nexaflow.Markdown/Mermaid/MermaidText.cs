using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// How Mermaid writes a character that a place in its text cannot hold as itself: as an entity code between <c>#</c> and
/// <c>;</c> — <c>#quot;</c> for a quote, <c>#35;</c> for the character numbered 35 — which it reads back as that character
/// wherever the text is shown.
/// </summary>
public static partial class MermaidText
{
    /// <summary>What text written with entity codes says. A code that stands for nothing is left as it was written.</summary>
    public static string Decode(string written) =>
        written.Contains('#') ? Entity().Replace(written, Decoded) : written;

    /// <summary>Text as a place in quotes holds it: every quote written as the entity code that stands for it.</summary>
    public static string Quoted(string text) => text.Replace("\"", "#quot;", StringComparison.Ordinal);

    [GeneratedRegex(@"#(?:(?<number>\d+)|(?<name>[A-Za-z][A-Za-z0-9]*));")]
    private static partial Regex Entity();

    private static string Decoded(Match match)
    {
        if (match.Groups["number"].Success)
            return int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                   && code is > 0 and <= 0x10FFFF and (< 0xD800 or > 0xDFFF)
                ? char.ConvertFromUtf32(code)
                : match.Value;

        var named = "&" + match.Groups["name"].Value + ";";
        var decoded = WebUtility.HtmlDecode(named);
        return decoded == named ? match.Value : decoded;
    }
}
