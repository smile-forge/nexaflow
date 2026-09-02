using System;
using System.Linq;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

/// <summary>
/// The EAN/UPC family — the codes on retail packaging.
///
/// <para>
/// Every digit is seven modules, and there are three ways to draw one: L on the left half, R on the
/// right, and G, which is R reversed. The trick the family turns on is that the <em>first</em> digit of
/// an EAN-13 is never drawn at all. It is carried by which of L and G each of the next six digits uses,
/// which is how thirteen digits fit in a symbol built for twelve — and why an EAN-13 and a UPC-A are
/// the same 95 modules.
/// </para>
/// </summary>
internal static class EanEncoder
{
    private static readonly string[] LeftOdd =
    [
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011",
    ];

    private static readonly string[] LeftEven =
    [
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111",
    ];

    private static readonly string[] Right =
    [
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100",
    ];

    /// <summary>Which of L and G the six left-hand digits use, chosen by the undrawn first digit.</summary>
    private static readonly string[] Ean13Parity =
    [
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL",
    ];

    /// <summary>UPC-E's parity, chosen by the check digit. Inverted for number system 1.</summary>
    private static readonly string[] UpcEParity =
    [
        "EEEOOO", "EEOEOO", "EEOOEO", "EEOOOE", "EOEEOO",
        "EOOEEO", "EOOOEE", "EOEOEO", "EOEOOE", "EOOEOE",
    ];

    /// <summary>The five-digit add-on's parity, chosen by its own weighted checksum.</summary>
    private static readonly string[] Ean5Parity =
    [
        "GGLLL", "GLGLL", "GLLGL", "GLLLG", "LGGLL",
        "LLGGL", "LLLGG", "LGLGL", "LGLLG", "LLGLG",
    ];

    private const string Guard = "101", Centre = "01010", AddOnStart = "01011", AddOnSeparator = "01";

    // ── Check digits ───────────────────────────────────────────────────────

    /// <summary>
    /// The EAN/UPC check digit: weights alternating 3 and 1 from the right, so the position a digit sits
    /// in decides what a mistyped one costs.
    /// </summary>
    internal static int CheckDigit(string digits)
    {
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            // Weight 3 falls on the digit immediately left of the check digit, and alternates leftwards.
            int weight = (digits.Length - i) % 2 == 1 ? 3 : 1;
            sum += (digits[i] - '0') * weight;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>
    /// Normalises a value to exactly <paramref name="total"/> digits: computing the check digit when it
    /// was left off, and verifying it when it was written out.
    /// </summary>
    private static bool TryNormalise(string value, int total, string name, out string? digits, out string? error)
    {
        digits = null;
        error  = null;

        if (!value.All(char.IsAsciiDigit))
        {
            error = $"{name} carries digits only.";
            return false;
        }

        if (value.Length == total - 1)
        {
            digits = value + CheckDigit(value);
            return true;
        }

        if (value.Length == total)
        {
            int expected = CheckDigit(value[..^1]);
            if (value[^1] - '0' != expected)
            {
                error = $"{name} check digit should be {expected}, not {value[^1]}. "
                      + $"Leave the last digit off and it is worked out for you.";
                return false;
            }
            digits = value;
            return true;
        }

        error = $"{name} takes {total - 1} digits, or {total} with the check digit — this is {value.Length}.";
        return false;
    }

    // ── The symbologies ────────────────────────────────────────────────────

    internal static bool TryEncodeEan13(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;

        if (!TryNormalise(value, 13, "EAN-13", out string? digits, out error)) return false;

        var bits = new StringBuilder(Guard);
        string parity = Ean13Parity[digits![0] - '0'];

        for (int i = 0; i < 6; i++)
        {
            int digit = digits[i + 1] - '0';
            bits.Append(parity[i] == 'L' ? LeftOdd[digit] : LeftEven[digit]);
        }

        bits.Append(Centre);
        for (int i = 7; i < 13; i++) bits.Append(Right[digits[i] - '0']);
        bits.Append(Guard);

        modules = BarcodePattern.FromBits(bits.ToString());
        text    = digits;
        return true;
    }

    internal static bool TryEncodeEan8(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;

        if (!TryNormalise(value, 8, "EAN-8", out string? digits, out error)) return false;

        var bits = new StringBuilder(Guard);
        for (int i = 0; i < 4; i++) bits.Append(LeftOdd[digits![i] - '0']);
        bits.Append(Centre);
        for (int i = 4; i < 8; i++) bits.Append(Right[digits![i] - '0']);
        bits.Append(Guard);

        modules = BarcodePattern.FromBits(bits.ToString());
        text    = digits;
        return true;
    }

    /// <summary>UPC-A is an EAN-13 whose undrawn first digit is zero — the same 95 modules.</summary>
    internal static bool TryEncodeUpc(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;

        if (!TryNormalise(value, 12, "UPC-A", out string? digits, out error)) return false;
        if (!TryEncodeEan13("0" + digits, out modules, out _, out error)) return false;

        text = digits;   // the leading zero is EAN's way of holding a UPC, not part of the number
        return true;
    }

    internal static bool TryEncodeUpcE(string value, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!value.All(char.IsAsciiDigit))
        {
            error = "UPC-E carries digits only.";
            return false;
        }
        if (value.Length is not (6 or 7 or 8))
        {
            error = $"UPC-E takes 6 digits, optionally with a number system and a check digit — "
                  + $"this is {value.Length}.";
            return false;
        }

        // Written as 6, it is number system 0 and the check digit has to be worked out from the UPC-A
        // this expands to; written as 8, it carries both already.
        string body   = value.Length == 8 ? value[1..7] : value.Length == 7 ? value[..6] : value;
        int    system = value.Length == 8 ? value[0] - '0' : 0;

        if (system is not (0 or 1))
        {
            error = $"UPC-E number system must be 0 or 1, not {system}.";
            return false;
        }

        int check = value.Length == 8
            ? value[7] - '0'
            : value.Length == 7 ? value[6] - '0' : CheckDigit(ExpandToUpcA(system, body));

        var bits = new StringBuilder(Guard);
        string parity = UpcEParity[check];

        for (int i = 0; i < 6; i++)
        {
            int digit = body[i] - '0';
            // Number system 1 flips every parity — the two systems share one table.
            bool even = (parity[i] == 'E') ^ (system == 1);
            bits.Append(even ? LeftEven[digit] : LeftOdd[digit]);
        }

        bits.Append("010101");   // the six-module end guard UPC-E uses in place of a centre and a guard

        modules = BarcodePattern.FromBits(bits.ToString());
        text    = $"{system}{body}{check}";
        return true;
    }

    /// <summary>
    /// The UPC-A an eight-digit UPC-E stands for. The last body digit says which of the six expansions
    /// applies — that is the whole of the zero-suppression trick.
    /// </summary>
    private static string ExpandToUpcA(int system, string body)
    {
        char last = body[5];
        string manufacturer, product;

        switch (last)
        {
            case '0' or '1' or '2':
                manufacturer = $"{body[0]}{body[1]}{last}00";
                product      = $"00{body[2]}{body[3]}{body[4]}";
                break;
            case '3':
                manufacturer = $"{body[0]}{body[1]}{body[2]}00";
                product      = $"000{body[3]}{body[4]}";
                break;
            case '4':
                manufacturer = $"{body[0]}{body[1]}{body[2]}{body[3]}0";
                product      = $"0000{body[4]}";
                break;
            default:
                manufacturer = $"{body[0]}{body[1]}{body[2]}{body[3]}{body[4]}";
                product      = $"0000{last}";
                break;
        }

        return $"{system}{manufacturer}{product}";
    }

    internal static bool TryEncodeEan5(string value, out bool[]? modules, out string? text, out string? error)
        => TryEncodeAddOn(value, 5, out modules, out text, out error);

    internal static bool TryEncodeEan2(string value, out bool[]? modules, out string? text, out string? error)
        => TryEncodeAddOn(value, 2, out modules, out text, out error);

    /// <summary>
    /// The add-ons printed beside a main code. They carry no guard pair; a start bar and a separator
    /// between each digit is the whole structure, and the parity carries their own checksum.
    /// </summary>
    private static bool TryEncodeAddOn(string value, int length, out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        if (!value.All(char.IsAsciiDigit) || value.Length != length)
        {
            error = $"An EAN-{length} add-on takes exactly {length} digits.";
            return false;
        }

        string parity;
        if (length == 5)
        {
            // Weights 3 and 9 alternating, which is the add-on's own rule rather than the family's.
            int sum = 0;
            for (int i = 0; i < 5; i++) sum += (value[i] - '0') * (i % 2 == 0 ? 3 : 9);
            parity = Ean5Parity[sum % 10];
        }
        else
        {
            // Two digits read as a number, and its remainder mod 4 picks one of four parities.
            parity = (int.Parse(value) % 4) switch { 0 => "LL", 1 => "LG", 2 => "GL", _ => "GG" };
        }

        var bits = new StringBuilder(AddOnStart);
        for (int i = 0; i < length; i++)
        {
            if (i > 0) bits.Append(AddOnSeparator);
            int digit = value[i] - '0';
            bits.Append(parity[i] == 'L' ? LeftOdd[digit] : LeftEven[digit]);
        }

        modules = BarcodePattern.FromBits(bits.ToString());
        text    = value;
        return true;
    }
}
