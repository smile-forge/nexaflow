using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// How Mermaid writes a character that a place in its text cannot hold as itself: as an entity code between <c>#</c> and
/// <c>;</c> — <c>#quot;</c> for a quote, <c>#35;</c> for the character numbered 35 — or HTML's between <c>&amp;</c> and
/// <c>;</c>, which it reads back as that character wherever the text is shown.
/// </summary>
public static partial class MermaidText
{
    /// <summary>
    /// What Mermaid's own named codes stand for: the punctuation that closes what something says, which is why there is a
    /// code for it at all — a colon ends a timeline's period, a semicolon ends a statement. HTML knows a name for each of
    /// these, but not every reader of HTML does, so the ones that matter are named here.
    /// </summary>
    private static readonly Dictionary<string, string> Punctuation = new(StringComparer.Ordinal)
    {
        ["colon"] = ":", ["semi"] = ";", ["num"] = "#", ["hash"] = "#", ["excl"] = "!", ["quest"] = "?",
        ["lpar"] = "(", ["rpar"] = ")", ["lbrace"] = "{", ["rbrace"] = "}", ["lbrack"] = "[", ["rbrack"] = "]",
        ["sol"] = "/", ["bsol"] = "\\", ["equals"] = "=", ["plus"] = "+", ["commat"] = "@", ["dollar"] = "$",
        ["percnt"] = "%", ["ast"] = "*", ["comma"] = ",", ["period"] = ".", ["apos"] = "'", ["grave"] = "`",
        ["verbar"] = "|", ["tilde"] = "~", ["lowbar"] = "_",
    };

    /// <summary>
    /// What text written with entity codes says — Mermaid's own, between <c>#</c> and <c>;</c>, and HTML's, between <c>&amp;</c>
    /// and <c>;</c>, which Mermaid reads as well because what it draws is HTML: <c>&amp;nbsp;</c> is a space that does not break,
    /// <c>&amp;#35;</c> and <c>&amp;#x23;</c> the character numbered 35. A code that stands for nothing is left as it was written.
    /// </summary>
    public static string Decode(string written) =>
        written.Contains('#') || written.Contains('&') ? Entity().Replace(written, Decoded) : written;

    /// <summary>Text as a place in quotes holds it: every quote written as the entity code that stands for it.</summary>
    public static string Quoted(string text) => text.Replace("\"", "#quot;", StringComparison.Ordinal);

    /// <summary>What a value written in quotes says: what is between them, read back from its entity codes — and what is in no quotes as it is.</summary>
    public static string Bare(string? value) =>
        value is null ? string.Empty
        : value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? Decode(value[1..^1])
        : Decode(value);

    [GeneratedRegex(@"#(?:(?<number>\d+)|(?<name>[A-Za-z][A-Za-z0-9]*));|&(?:#(?<number>\d+)|#[xX](?<hex>[0-9A-Fa-f]+)|(?<name>[A-Za-z][A-Za-z0-9]*));")]
    private static partial Regex Entity();

    private static string Decoded(Match match)
    {
        if (match.Groups["number"].Success || match.Groups["hex"].Success)
        {
            var read = match.Groups["hex"].Success
                ? int.TryParse(match.Groups["hex"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
                : int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out code);

            return read && code is > 0 and <= 0x10FFFF and (< 0xD800 or > 0xDFFF)
                ? char.ConvertFromUtf32(code)
                : match.Value;
        }

        if (Punctuation.TryGetValue(match.Groups["name"].Value, out var character)) return character;

        var named = "&" + match.Groups["name"].Value + ";";
        var decoded = WebUtility.HtmlDecode(named);
        return decoded == named ? match.Value : decoded;
    }
}
