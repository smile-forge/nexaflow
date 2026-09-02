using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

/// <summary>
/// Code 128 — the general-purpose symbology, and the only one here with more than one alphabet.
///
/// <para>
/// Every symbol is eleven modules wide and tabulated as six element widths, alternating ink and paper.
/// Which of the three subsets is in force decides what a symbol value <em>means</em>: in B it is a
/// printable character, in C it is a pair of digits, in A it is upper case and the control codes. That
/// is what makes the format both compact and fiddly — the check character is computed over the symbol
/// values, so it depends on the subset choices as much as on the text.
/// </para>
/// </summary>
internal static class Code128Encoder
{
    /// <summary>Highest character subset B carries: ASCII 32 through 127.</summary>
    private const char LastPrintable = (char)127;

    /// <summary>
    /// The 107 symbol patterns, as element widths starting with ink. Index is the symbol value; 103–105
    /// are the start codes and 106 the stop, which is the one symbol with seven elements rather than six.
    /// </summary>
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int StartA = 103, StartB = 104, StartC = 105, Stop = 106;

    /// <summary>Which subset a block asked for, or none — meaning choose.</summary>
    internal enum Subset { Auto, A, B, C }

    /// <summary>Encodes <paramref name="value"/>, or explains what it cannot carry.</summary>
    internal static bool TryEncode(string value, Subset subset, out bool[]? modules, out string? error)
    {
        modules = null;
        error   = null;

        if (value.Length == 0)
        {
            error = "A Code 128 barcode needs at least one character.";
            return false;
        }

        var codes = subset switch
        {
            Subset.A => EncodeA(value, out error),
            Subset.B => EncodeB(value, out error),
            Subset.C => EncodeC(value, out error),
            _        => EncodeAuto(value, out error),
        };

        if (codes is null) return false;

        // The check character weights each data symbol by its position, so it depends on the subset
        // switches as much as on the text.
        int checksum = codes[0];
        for (int i = 1; i < codes.Count; i++) checksum += codes[i] * i;
        codes.Add(checksum % 103);
        codes.Add(Stop);

        modules = BarcodePattern.FromWidths(codes.Select(c => Patterns[c]));
        return true;
    }

    // ── The three subsets ──────────────────────────────────────────────────

    private static List<int>? EncodeA(string value, out string? error)
    {
        error = null;
        var codes = new List<int> { StartA };

        foreach (char c in value)
        {
            // A runs from space to underscore, then wraps round to the control characters.
            if (c is >= ' ' and <= '_') codes.Add(c - ' ');
            else if (c < ' ')           codes.Add(c + 64);
            else
            {
                error = $"'{c}' is not in Code 128 subset A, which carries upper case, digits, "
                      + "punctuation and control characters — but no lower case.";
                return null;
            }
        }
        return codes;
    }

    private static List<int>? EncodeB(string value, out string? error)
    {
        error = null;
        var codes = new List<int> { StartB };

        foreach (char c in value)
        {
            if (c >= ' ' && c <= LastPrintable) codes.Add(c - ' ');
            else
            {
                error = $"'{c}' is not in Code 128 subset B, which carries printable ASCII only.";
                return null;
            }
        }
        return codes;
    }

    private static List<int>? EncodeC(string value, out string? error)
    {
        error = null;

        if (!value.All(char.IsAsciiDigit))
        {
            error = "Code 128 subset C carries digits only.";
            return null;
        }
        if (value.Length % 2 != 0)
        {
            error = "Code 128 subset C encodes digits two at a time, so it needs an even count — "
                  + $"this is {value.Length}.";
            return null;
        }

        var codes = new List<int> { StartC };
        for (int i = 0; i < value.Length; i += 2)
            codes.Add(int.Parse(value.AsSpan(i, 2)));

        return codes;
    }

    /// <summary>
    /// Picks a subset for the whole value: C when it is an even run of digits, which halves the symbol,
    /// otherwise B, and A only when B cannot hold it.
    /// <para>
    /// Switching subsets mid-value would compress a mixed string further, but only one that is mostly a
    /// long digit run — and a switch code got wrong yields a symbol that scans as something else,
    /// silently. Not worth it for the width.
    /// </para>
    /// </summary>
    private static List<int>? EncodeAuto(string value, out string? error)
    {
        if (value.All(char.IsAsciiDigit) && value.Length % 2 == 0)
            return EncodeC(value, out error);

        if (value.All(c => c >= ' ' && c <= LastPrintable))
            return EncodeB(value, out error);

        return EncodeA(value, out error);
    }
}
