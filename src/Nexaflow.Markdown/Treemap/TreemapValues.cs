using System.Globalization;
using System.Text;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// What a treemap's values say, and how they are set: read as Mermaid reads a value — digits, with the commas and underscores a
/// reader groups them with — and set as its <c>valueFormat</c> says, which is a d3 format.
///
/// <para>
/// The format is d3's — <c>[sign][$][0][width][,][.precision][~][type]</c> — with Mermaid's own shorthands: a <c>$</c> before it puts
/// a dollar in front of what the rest sets, and <c>$0,0</c> is a dollar and thousands grouped. The types read are <c>f</c> (fixed
/// places), <c>%</c> (a hundred times, with a percent), <c>d</c> (whole), <c>e</c> (exponent), <c>s</c> (an SI prefix) and none
/// (as few places as say it); a width and its fill set nothing in a label, and are passed over.
/// </para>
/// </summary>
public static class TreemapValues
{
    /// <summary>What Mermaid sets a value with where nothing says otherwise: thousands grouped.</summary>
    public const string Grouped = ",";

    /// <summary>What <paramref name="written"/> says as a number — the commas and underscores in it grouping its digits — or null where it says none.</summary>
    public static double? Read(string written)
    {
        var bare = written.Replace(",", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

        return bare.Length > 0 && double.TryParse(bare, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary><paramref name="value"/> set as <paramref name="format"/> says — thousands grouped, where it says nothing.</summary>
    public static string Format(double value, string? format)
    {
        var spec = string.IsNullOrWhiteSpace(format) ? Grouped : format.Trim();

        var dollar = spec.StartsWith('$');
        if (dollar) spec = spec[1..];
        if (spec == "0,0") spec = Grouped;

        var at = 0;
        var sign = at < spec.Length && spec[at] is '+' or '-' or ' ' or '(' ? spec[at++] : '-';
        if (at < spec.Length && spec[at] == '$') { dollar = true; at++; }
        while (at < spec.Length && char.IsAsciiDigit(spec[at])) at++;

        var grouped = at < spec.Length && spec[at] == ',';
        if (grouped) at++;

        int? places = null;
        if (at < spec.Length && spec[at] == '.')
        {
            var from = ++at;
            while (at < spec.Length && char.IsAsciiDigit(spec[at])) at++;
            if (at > from) places = int.Parse(spec[from..at], CultureInfo.InvariantCulture);
        }

        var trimmed = at < spec.Length && spec[at] == '~';
        if (trimmed) at++;

        var type = at < spec.Length ? spec[at] : '\0';
        var said = Typed(Math.Abs(value), type, places, trimmed, out var suffix);

        if (grouped) said = Grouping(said);

        var negative = value < 0 && said.Any(character => character is >= '1' and <= '9');
        var head = negative ? (sign == '(' ? "(" : "-") : sign is '+' ? "+" : sign is ' ' ? " " : string.Empty;
        var tail = negative && sign == '(' ? ")" : string.Empty;

        return head + (dollar ? "$" : string.Empty) + said + suffix + tail;
    }

    /// <summary><paramref name="value"/> — never less than nought — in the digits <paramref name="type"/> sets it with, and what goes after them.</summary>
    private static string Typed(double value, char type, int? places, bool trimmed, out string suffix)
    {
        suffix = string.Empty;

        var said = type switch
        {
            'f' => value.ToString("F" + (places ?? 6), CultureInfo.InvariantCulture),
            '%' => (value * 100).ToString("F" + (places ?? 6), CultureInfo.InvariantCulture),
            'd' => Math.Round(value).ToString("F0", CultureInfo.InvariantCulture),
            'e' => value.ToString((places ?? 6) == 0 ? "0e+0" : "0." + new string('0', places ?? 6) + "e+0", CultureInfo.InvariantCulture),
            's' => Prefixed(value, places, out suffix),
            _ => places is { } fixedPlaces ? Significant(value, Math.Max(1, fixedPlaces)) : Shortest(value),
        };

        if (type == '%') suffix = "%";
        return trimmed && said.Contains('.') && type is not 'e' ? said.TrimEnd('0').TrimEnd('.') : said;
    }

    /// <summary><paramref name="value"/> in as few places as say it, as d3 sets a number with no type.</summary>
    private static string Shortest(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary><paramref name="value"/> to <paramref name="digits"/> significant digits.</summary>
    private static string Significant(double value, int digits)
    {
        if (value == 0) return "0";

        var magnitude = (int)Math.Floor(Math.Log10(value)) + 1;
        var decimals = Math.Max(0, digits - magnitude);
        var rounded = Math.Round(value, Math.Min(decimals, 15));

        return rounded.ToString("F" + Math.Min(decimals, 15), CultureInfo.InvariantCulture);
    }

    /// <summary><paramref name="value"/> scaled to an SI prefix — k, M, G, T — and the prefix it was scaled to.</summary>
    private static string Prefixed(double value, int? places, out string prefix)
    {
        string[] prefixes = ["", "k", "M", "G", "T", "P"];
        var power = value < 1 ? 0 : Math.Min(prefixes.Length - 1, (int)Math.Floor(Math.Log10(value) / 3));

        prefix = prefixes[power];
        var scaled = value / Math.Pow(1000, power);

        return places is { } digits ? Significant(scaled, Math.Max(1, digits)) : Shortest(Math.Round(scaled, 3));
    }

    /// <summary>The digits before the point grouped in threes with commas.</summary>
    private static string Grouping(string said)
    {
        var point = said.IndexOfAny(['.', 'e']);
        var whole = point < 0 ? said : said[..point];
        if (whole.Length <= 3) return said;

        var grouped = new StringBuilder();
        for (var at = 0; at < whole.Length; at++)
        {
            if (at > 0 && (whole.Length - at) % 3 == 0) grouped.Append(',');
            grouped.Append(whole[at]);
        }

        return grouped + (point < 0 ? string.Empty : said[point..]);
    }
}
