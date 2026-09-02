using System;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Barcode.Encoders;

/// <summary>
/// ISBN, ISSN and ISMN — the numbers printed on books, journals and sheet music.
///
/// <para>
/// None of them is a symbology. Each is a numbering scheme that agreed to be <em>printed</em> as an
/// EAN-13, by reserving a prefix: 978 and 979 for books, 977 for serials, 9790 for printed music. So
/// none of this encodes anything; it works out which thirteen digits the number stands for and hands
/// them to <see cref="EanEncoder"/>.
/// </para>
/// <para>
/// What they add that a bare EAN-13 does not have is the add-on: the little block of extra bars beside
/// the main symbol carrying a price on a book or an issue number on a journal. It is a separate symbol
/// with its own guard, set apart by a gap wide enough that a scanner does not read the two as one.
/// </para>
/// </summary>
internal static class PublicationEncoder
{
    /// <summary>
    /// The light gap between the main symbol and its add-on. The standard allows seven to twelve
    /// modules; twelve is what the generators leave, and the wider the safer — the whole purpose of the
    /// gap is that a scanner sees two symbols rather than one long one.
    /// </summary>
    private const int AddOnGap = 12;

    internal static bool TryEncode(BarcodeSymbology symbology, string value,
                                   out bool[]? modules, out string? text, out string? error)
    {
        modules = null;
        text    = null;
        error   = null;

        // The number, then whatever follows it: an issue variant, an add-on, or both.
        var parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = $"A {Name(symbology)} barcode needs a number.";
            return false;
        }

        string? body = symbology switch
        {
            BarcodeSymbology.Isbn => IsbnBody(parts[0], out error),
            BarcodeSymbology.Ismn => IsmnBody(parts[0], out error),
            _                     => IssnBody(parts, out error),
        };

        if (body is null) return false;

        // Everything after the number that is not the ISSN's two-digit variant is the add-on.
        string? addOn = AddOnFrom(symbology, parts, out error);
        if (error is not null) return false;

        if (!EanEncoder.TryEncodeEan13(body, out var main, out string? mainText, out error)) return false;

        if (addOn is null)
        {
            modules = main;
            text    = mainText;
            return true;
        }

        bool encoded = addOn.Length == 2
            ? EanEncoder.TryEncodeEan2(addOn, out var extra, out _, out error)
            : EanEncoder.TryEncodeEan5(addOn, out extra, out _, out error);

        if (!encoded) return false;

        var joined = new bool[main!.Length + AddOnGap + extra!.Length];
        main.CopyTo(joined, 0);
        extra.CopyTo(joined, main.Length + AddOnGap);

        modules = joined;
        text    = $"{mainText} {addOn}";
        return true;
    }

    // ── Which thirteen digits each number stands for ───────────────────────

    /// <summary>
    /// An ISBN is already the EAN-13 it prints as, once the hyphens come out. A ten-digit one predates
    /// that and is promoted the way the standard says: prefix 978, keep the nine digits that identify
    /// the book, and compute a new check digit — the old one checked a different number.
    /// </summary>
    private static string? IsbnBody(string isbn, out string? error)
    {
        error = null;
        string digits = Compact(isbn);

        switch (digits.Length)
        {
            case 13:
                return digits;

            case 10:
                return "978" + digits[..9];

            default:
                error = $"'{isbn}' is not an ISBN: it has {digits.Length} digits, and an ISBN has 10 or 13.";
                return null;
        }
    }

    /// <summary>An ISMN is a 9790-prefixed EAN-13; the older <c>M</c>-prefixed form means the same 9790.</summary>
    private static string? IsmnBody(string ismn, out string? error)
    {
        error = null;

        string trimmed = ismn.Trim();
        string digits = Compact(trimmed.StartsWith("M", StringComparison.OrdinalIgnoreCase)
            ? "9790" + trimmed[1..]
            : trimmed);

        if (digits.Length == 13 && digits.StartsWith("9790", StringComparison.Ordinal)) return digits;
        if (digits.Length == 12 && digits.StartsWith("9790", StringComparison.Ordinal)) return digits;

        error = $"'{ismn}' is not an ISMN. One is thirteen digits beginning 9790.";
        return null;
    }

    /// <summary>
    /// An ISSN prints as 977, the ISSN without its own check digit, and a two-digit variant saying which
    /// issue of the year this is. The EAN check digit then replaces the ISSN's, which is why the ISSN's
    /// own trailing X — a legal check character there — never reaches the bars.
    /// </summary>
    private static string? IssnBody(string[] parts, out string? error)
    {
        error = null;

        string issn = Compact(parts[0], allowX: true);
        if (issn.Length != 8)
        {
            error = $"'{parts[0]}' is not an ISSN. One is eight characters, the last of them a digit or X.";
            return null;
        }

        // A second field of exactly two digits is the variant; anything else is an add-on.
        string variant = parts.Length > 2 && parts[1].Length == 2 && parts[1].All(char.IsAsciiDigit)
            ? parts[1]
            : "00";

        return "977" + issn[..7] + variant;
    }

    private static string? AddOnFrom(BarcodeSymbology symbology, string[] parts, out string? error)
    {
        error = null;

        // For an ISSN the variant sits between the number and the add-on, so the add-on is the last
        // field when there are three; for the others it is simply whatever follows the number.
        string? candidate = symbology == BarcodeSymbology.Issn
            ? parts.Length >= 3 ? parts[^1] : parts.Length == 2 ? parts[1] : null
            : parts.Length >= 2 ? parts[1] : null;

        if (candidate is null) return null;

        if (!candidate.All(char.IsAsciiDigit) || candidate.Length is not (2 or 5))
        {
            error = $"'{candidate}' is not an add-on. One is two digits, or five for a price.";
            return null;
        }

        return candidate;
    }

    private static string Compact(string value, bool allowX = false)
    {
        var kept = value.Where(c => char.IsAsciiDigit(c) || (allowX && (c is 'X' or 'x')));
        return new string([.. kept]).ToUpperInvariant();
    }

    private static string Name(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Isbn => "ISBN",
        BarcodeSymbology.Ismn => "ISMN",
        _                     => "ISSN",
    };
}
