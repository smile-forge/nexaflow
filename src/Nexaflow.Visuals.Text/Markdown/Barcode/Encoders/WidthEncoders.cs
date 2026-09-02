using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

/// <summary>
/// The two-width symbologies: every element is either narrow or wide, and the pattern of which is which
/// carries the data.
///
/// <para>
/// They are the older designs, and they share a shape — a table of narrow/wide patterns per character,
/// a start and a stop, and for some a check digit. Grouped into one file because each is a table and a
/// dozen lines; kept as separate types would be five files of ceremony around five tables.
/// </para>
/// <para>
/// A wide element is three modules to a narrow one's one. The ratio is a choice within the standards
/// (2:1 is also legal); 3:1 is the more forgiving of the two to a scanner and the more common in print.
/// </para>
/// </summary>
internal static class WidthEncoders
{
    private const char Narrow = '1', Wide = '3';

    /// <summary>
    /// Codabar draws its wide element at twice the narrow one rather than three times.
    /// <para>
    /// The standard permits anywhere from 2:1 to 3:1 and every format here takes 3:1, which is the more
    /// forgiving to a scanner. Codabar is the exception because the generators that produce it in practice
    /// use 2:1, and a barcode that does not look like the ones beside it on a shelf invites the question of
    /// which is wrong.
    /// </para>
    /// </summary>
    private const char CodabarWide = '2';

    /// <summary>Turns an n/w pattern into the element widths <see cref="BarcodePattern.FromWidths"/> reads.</summary>
    /// <summary>Turns an n/w pattern into element widths, with a caller-chosen width for a wide element.</summary>
private static string Widths(string pattern, char wide = Wide) =>
    new([.. pattern.Select(c => c == 'w' ? wide : Narrow)]);

    // ── Code 39 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Nine elements per character — five bars and four spaces — of which exactly three are wide. Hence
    /// the name, and hence how little it carries per millimetre.
    /// </summary>
    private static readonly Dictionary<char, string> Code39 = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw",
        ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn",
        ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn", ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww",
        ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww", ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn",
        ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn", ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw",
        ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn",
    };

    internal static bool TryEncodeCode39(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        // Lower case is not a different character in Code 39, it is simply absent; folding it up is what
        // every reader of this format expects, and refusing would be pedantry.
        string upper = value.ToUpperInvariant();

        foreach (char c in upper)
        {
            if (c == '*')
            {
                error = "'*' is Code 39's start and stop mark and cannot appear in the value.";
                return false;
            }
            if (!Code39.ContainsKey(c))
            {
                error = $"'{c}' is not in Code 39, which carries A–Z, digits, space and - . $ / + %.";
                return false;
            }
        }

        // One continuous run of elements, not a symbol each. The alternation between ink and paper never
        // restarts, so the narrow gap between characters lands as a space; emitted as its own symbol it would
        // start on ink again and separate the characters with a bar.
        //
        // It works out because the counts are odd: nine elements to a character and one to a gap leaves every
        // character starting on ink, which is what its pattern is written for.
        var elements = new StringBuilder(Widths(Code39['*']));
        foreach (char c in upper) elements.Append('1').Append(Widths(Code39[c]));
        elements.Append('1').Append(Widths(Code39['*']));

        modules = BarcodePattern.FromWidths([elements.ToString()]);
        text    = upper;
        return true;
    }

    // ── Interleaved 2 of 5 ─────────────────────────────────────────────────

    /// <summary>Five elements per digit, two of them wide.</summary>
    private static readonly string[] Itf =
    [
        "nnwwn", "wnnnw", "nwnnw", "wwnnn", "nnwnw",
        "wnwnn", "nwwnn", "nnnww", "wnnwn", "nwnwn",
    ];

    internal static bool TryEncodeItf(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!value.All(char.IsAsciiDigit))
        {
            error = "Interleaved 2 of 5 carries digits only.";
            return false;
        }
        if (value.Length == 0 || value.Length % 2 != 0)
        {
            error = "Interleaved 2 of 5 encodes digits in pairs — one digit in the bars, the next in the "
                  + $"spaces between them — so it needs an even count. This is {value.Length}.";
            return false;
        }

        // The interleaving proper: a pair becomes one five-bar, five-space run, taken alternately from
        // the two digits' patterns. It is what makes the format dense and what makes it need pairs.
        var widths = new StringBuilder("1111");   // start: four narrow elements
        for (int i = 0; i < value.Length; i += 2)
        {
            string bars   = Itf[value[i] - '0'];
            string spaces = Itf[value[i + 1] - '0'];
            for (int e = 0; e < 5; e++)
            {
                widths.Append(bars[e]   == 'w' ? Wide : Narrow);
                widths.Append(spaces[e] == 'w' ? Wide : Narrow);
            }
        }
        widths.Append("311");   // stop: wide bar, narrow space, narrow bar

        modules = BarcodePattern.FromWidths([widths.ToString()]);
        text    = value;
        return true;
    }

    /// <summary>ITF-14 is an ITF of exactly fourteen digits, the last of them the EAN check digit.</summary>
    internal static bool TryEncodeItf14(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!value.All(char.IsAsciiDigit))
        {
            error = "ITF-14 carries digits only.";
            return false;
        }

        string digits;
        if (value.Length == 13) digits = value + EanEncoder.CheckDigit(value);
        else if (value.Length == 14)
        {
            int expected = EanEncoder.CheckDigit(value[..13]);
            if (value[13] - '0' != expected)
            {
                error = $"ITF-14 check digit should be {expected}, not {value[13]}. "
                      + "Leave the last digit off and it is worked out for you.";
                return false;
            }
            digits = value;
        }
        else
        {
            error = $"ITF-14 takes 13 digits, or 14 with the check digit — this is {value.Length}.";
            return false;
        }

        if (!TryEncodeItf(digits, out modules, out _, out error)) return false;

        text = digits;
        return true;
    }

    // ── MSI ────────────────────────────────────────────────────────────────

    /// <summary>
    /// MSI writes each digit as four bits, and each bit as a pair of elements — a long bar for a one, a
    /// short one for a zero. Four check-digit schemes exist and the block chooses between them by format
    /// name, because nothing in the symbol says which was used.
    /// </summary>
    internal static bool TryEncodeMsi(string value, bool mod10, bool mod11, bool twice,
                                      out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!value.All(char.IsAsciiDigit) || value.Length == 0)
        {
            error = "MSI carries digits only.";
            return false;
        }

        string digits = value;
        if (mod11) digits += Mod11(digits);
        if (mod10) digits += Mod10(digits);
        if (twice) digits += Mod10(digits);

        var widths = new StringBuilder("21");   // start
        foreach (char digit in digits)
        {
            for (int bit = 3; bit >= 0; bit--)
                widths.Append(((digit - '0') >> bit & 1) == 1 ? "21" : "12");
        }
        widths.Append("121");   // stop

        modules = BarcodePattern.FromWidths([widths.ToString()]);
        text    = digits;
        return true;
    }

    /// <summary>
    /// MSI's mod 10: the alternate digits from the right make a number, which is doubled and its digits
    /// summed, and the rest are added on.
    /// </summary>
    private static int Mod10(string digits)
    {
        var odd = new StringBuilder();
        int evenSum = 0;

        for (int i = digits.Length - 1, place = 0; i >= 0; i--, place++)
        {
            if (place % 2 == 0) odd.Insert(0, digits[i]);
            else evenSum += digits[i] - '0';
        }

        int doubled = 0;
        foreach (char c in (long.Parse(odd.ToString()) * 2).ToString()) doubled += c - '0';

        return (10 - (doubled + evenSum) % 10) % 10;
    }

    /// <summary>MSI's mod 11, with weights running 2 to 7 from the right.</summary>
    private static int Mod11(string digits)
    {
        int sum = 0;
        for (int i = digits.Length - 1, weight = 2; i >= 0; i--, weight = weight == 7 ? 2 : weight + 1)
            sum += (digits[i] - '0') * weight;

        int check = (11 - sum % 11) % 11;
        return check == 10 ? 0 : check;
    }

    // ── Pharmacode ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pharmacode carries a number, not text, and carries it backwards: the bars are read right to left,
    /// and a wide bar is worth twice a narrow one plus one. It has no check digit — its safety comes from
    /// the small range, so a misread is usually not a valid code at all.
    /// </summary>
    internal static bool TryEncodePharmacode(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!int.TryParse(value, out int number) || number is < 3 or > 131070)
        {
            error = $"Pharmacode carries a whole number from 3 to 131070 — '{value}' is not one.";
            return false;
        }

        var bars = new List<string>();
        for (int n = number; n > 0;)
        {
            if (n % 2 == 0) { bars.Add("3"); n = (n - 2) / 2; }
            else            { bars.Add("1"); n = (n - 1) / 2; }
        }
        bars.Reverse();

        // Laetus fixes the three widths at 0.5mm, 1.5mm and 1.0mm - a narrow bar, a wide bar, and the gap
        // between them - so the space is two modules where a narrow bar is one. It is the only format here
        // whose gap is not simply the narrow element.
        var widths = new StringBuilder();
        for (int i = 0; i < bars.Count; i++)
        {
            if (i > 0) widths.Append('2');
            widths.Append(bars[i]);
        }

        modules = BarcodePattern.FromWidths([widths.ToString()]);
        text    = number.ToString();
        return true;
    }

    // ── Codabar ────────────────────────────────────────────────────────────

    /// <summary>Seven elements per character — four bars and three spaces.</summary>
    private static readonly Dictionary<char, string> Codabar = new()
    {
        ['0'] = "nnnnnww", ['1'] = "nnnnwwn", ['2'] = "nnnwnnw", ['3'] = "wwnnnnn",
        ['4'] = "nnwnnwn", ['5'] = "wnnnnwn", ['6'] = "nwnnnnw", ['7'] = "nwnnwnn",
        ['8'] = "nwwnnnn", ['9'] = "wnnwnnn",
        ['-'] = "nnnwwnn", ['$'] = "nnwwnnn", [':'] = "wnnnwnw", ['/'] = "wnwnnnw",
        ['.'] = "wnwnwnn", ['+'] = "nnwnwnw",
        ['A'] = "nnwwnwn", ['B'] = "nwnwnnw", ['C'] = "nnnwnww", ['D'] = "nnnwwwn",
    };

    internal static bool TryEncodeCodabar(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (value.Length == 0)
        {
            error = "A Codabar barcode needs at least one character.";
            return false;
        }

        // A–D are the start and stop marks, and a value that brought its own pair is used as written.
        //
        // One that did not is wrapped in A…B. Codabar allows any of the four at either end and attaches no
        // meaning to the choice, so there is no right answer — but A…B is what the generators produce, and a
        // code that differs from the ones beside it invites the question of which is wrong. An author who
        // cares writes the marks into the value.
        string body = value.ToUpperInvariant();
        bool bracketed = body.Length >= 2 && body[0] is >= 'A' and <= 'D' && body[^1] is >= 'A' and <= 'D';
        string full = bracketed ? body : $"A{body}B";

        for (int i = 0; i < full.Length; i++)
        {
            char c = full[i];
            bool isMark = c is >= 'A' and <= 'D';
            bool atEnd  = i == 0 || i == full.Length - 1;

            if (isMark && !atEnd)
            {
                error = $"'{c}' is a Codabar start/stop mark and can only appear at either end.";
                return false;
            }
            if (!Codabar.ContainsKey(c))
            {
                error = $"'{c}' is not in Codabar, which carries digits and - $ : / . + between "
                      + "start and stop marks A to D.";
                return false;
            }
        }

        // One continuous run of elements, as in Code 39: the alternation must not restart, or the narrow
        // gap between characters would be drawn as a bar. Seven elements to a character and one to a gap
        // again leaves every character starting on ink.
        var elements = new StringBuilder();
        for (int i = 0; i < full.Length; i++)
        {
            if (i > 0) elements.Append('1');
            elements.Append(Widths(Codabar[full[i]], CodabarWide));
        }

        modules = BarcodePattern.FromWidths([elements.ToString()]);
        text    = full;
        return true;
    }
}
