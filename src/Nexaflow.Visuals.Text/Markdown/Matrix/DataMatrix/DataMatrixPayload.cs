using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// What a <c>datamatrix</c> block's <c>type:</c> means — the shared vocabulary a <c>qr</c> block has,
/// because a <c>WIFI:</c> descriptor or a vCard decodes the same from any symbol, plus the types that
/// exist only as Data Matrix symbols.
///
/// <para>
/// Those last are not text conventions but industry message formats with the symbol prescribed: a
/// pharmacy pack's PPN under Macro 06, a GS1 item under FNC1, a Royal Mail Mailmark at the exact size
/// its format demands. What they have in common with <c>wifi:</c> is the reason they are here — the
/// author writes fields and the block writes the wire format, check digits and separators included,
/// which is precisely the part nobody gets right by hand.
/// </para>
/// </summary>
internal static class DataMatrixPayload
{
    /// <summary>The fields each type reads. Doubles as the spell-check for a block.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> FieldsByType = Build();

    private static IReadOnlyDictionary<string, string[]> Build()
    {
        var all = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gs1"]      = ["data"],
            ["ppn"]      = ["pzn", "lot", "expiry", "serial"],
            ["ntin"]     = ["pzn", "gtin", "expiry", "lot", "serial"],
            ["mailmark"] = ["format", "message"],
        };
        foreach (var (type, fields) in QrPayload.FieldsByType) all.TryAdd(type, fields);
        return all;
    }

    /// <summary>
    /// Builds the encodable string and whatever the encoder must be told alongside it — a GS1 symbol
    /// starts with FNC1, a PPN is wrapped in Macro 06, a Mailmark is a fixed size.
    /// </summary>
    internal static bool TryBuild(string type, IReadOnlyDictionary<string, string> fields,
                                  DataMatrixOptions baseline,
                                  out string? payload, out DataMatrixOptions options, out string? error)
    {
        payload = null;
        options = baseline;
        error   = null;

        switch (type.ToLowerInvariant())
        {
            case "gs1":      return TryGs1(fields, baseline, out payload, out options, out error);
            case "ppn":      return TryPpn(fields, baseline, out payload, out options, out error);
            case "ntin":     return TryNtin(fields, baseline, out payload, out options, out error);
            case "mailmark": return TryMailmark(fields, baseline, out payload, out options, out error);
            default:         return QrPayload.TryBuild(type, fields, out payload, out error);
        }
    }

    // ── GS1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Application identifiers whose data is a fixed length, and so takes no separator after it. Every
    /// other AI is variable and is closed by FNC1 when another follows.
    /// </summary>
    private static readonly Dictionary<string, int> FixedLengthAis = new()
    {
        ["00"] = 18, ["01"] = 14, ["02"] = 14, ["03"] = 14,
        ["11"] = 6,  ["12"] = 6,  ["13"] = 6,  ["15"] = 6, ["16"] = 6, ["17"] = 6,
        ["20"] = 2,
        ["410"] = 13, ["411"] = 13, ["412"] = 13, ["413"] = 13, ["414"] = 13, ["415"] = 13, ["416"] = 13, ["417"] = 13,
    };

    /// <summary>
    /// A GS1 element string written the human way — <c>(01)04150123456782(17)261231(10)LOT7</c> — turned
    /// into the wire form: brackets off, FNC1 first, and a separator after each variable-length element
    /// that is not the last.
    /// </summary>
    private static bool TryGs1(IReadOnlyDictionary<string, string> fields, DataMatrixOptions baseline,
                               out string? payload, out DataMatrixOptions options, out string? error)
    {
        payload = null;
        options = baseline;
        error   = null;

        if (!fields.TryGetValue("data", out string? data) || data.Length == 0)
        {
            error = "A `gs1` symbol needs `data:` — the element string, with each AI in brackets: (01)…(17)…(10)….";
            return false;
        }

        var elements = ParseElements(data, out error);
        if (elements is null) return false;

        payload = JoinElements(elements);
        options = baseline with { Gs1 = true };
        return true;
    }

    /// <summary>Splits <c>(ai)value(ai)value…</c> into pairs, or explains where it stopped being that.</summary>
    private static List<(string Ai, string Value)>? ParseElements(string data, out string? error)
    {
        error = null;
        var elements = new List<(string, string)>();
        int at = 0;

        while (at < data.Length)
        {
            if (data[at] != '(')
            {
                error = $"GS1 data must be a run of (AI)value pairs; at '{data[at..]}' there is no opening bracket.";
                return null;
            }

            int close = data.IndexOf(')', at);
            if (close < 0)
            {
                error = "A GS1 application identifier's bracket is never closed.";
                return null;
            }

            string ai = data[(at + 1)..close];
            if (ai.Length is < 2 or > 4 || !ai.All(char.IsAsciiDigit))
            {
                error = $"'{ai}' is not a GS1 application identifier — two to four digits.";
                return null;
            }

            int next  = data.IndexOf('(', close);
            string value = next < 0 ? data[(close + 1)..] : data[(close + 1)..next];

            if (value.Length == 0)
            {
                error = $"AI ({ai}) has no value.";
                return null;
            }

            if (FixedLengthAis.TryGetValue(ai, out int fixedLength) && value.Length != fixedLength)
            {
                error = $"AI ({ai}) takes exactly {fixedLength} characters; '{value}' is {value.Length}.";
                return null;
            }

            elements.Add((ai, value));
            at = next < 0 ? data.Length : next;
        }

        return elements;
    }

    private static string JoinElements(List<(string Ai, string Value)> elements)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < elements.Count; i++)
        {
            var (ai, value) = elements[i];
            sb.Append(ai).Append(value);

            bool last = i == elements.Count - 1;
            if (!last && !FixedLengthAis.ContainsKey(ai)) sb.Append('');
        }
        return sb.ToString();
    }

    // ── PPN — the Pharmacy Product Number ──────────────────────────────────

    /// <summary>
    /// A German pharmacy pack's PPN symbol: the product number with its two check characters, then batch,
    /// expiry and serial as MH10.8.2 data identifiers, separated by GS and wrapped in Macro 06.
    /// <para>
    /// The PPN is derived from the pack's PZN rather than typed, because its check is the part people
    /// get wrong: <c>11</c>, the eight PZN digits, then the ASCII values of those ten characters
    /// weighted 2 through 11, modulo 97, as two digits. The PZN's own check digit is verified first.
    /// </para>
    /// </summary>
    private static bool TryPpn(IReadOnlyDictionary<string, string> fields, DataMatrixOptions baseline,
                               out string? payload, out DataMatrixOptions options, out string? error)
    {
        payload = null;
        options = baseline;

        if (!TryPzn(fields, out string? pzn, out error)) return false;

        string ppn = "11" + pzn;
        int sum = 0;
        for (int i = 0; i < ppn.Length; i++) sum += ppn[i] * (i + 2);
        ppn += (sum % 97).ToString("00");

        var parts = new List<string> { "9N" + ppn };
        if (Optional(fields, "lot")    is { } lot)    parts.Add("1T" + lot);
        if (Optional(fields, "expiry") is { } expiry)
        {
            if (!IsYyMmDd(expiry)) { error = $"`expiry: {expiry}` must be six digits, YYMMDD."; return false; }
            parts.Add("D" + expiry);
        }
        if (Optional(fields, "serial") is { } serial) parts.Add("S" + serial);

        payload = string.Join('', parts);
        options = baseline with { Macro = DataMatrixMacro.Macro06 };
        return true;
    }

    /// <summary>The PZN, verified: eight digits whose last is the weighted sum of the first seven modulo 11.</summary>
    private static bool TryPzn(IReadOnlyDictionary<string, string> fields, out string? pzn, out string? error)
    {
        pzn   = null;
        error = null;

        if (!fields.TryGetValue("pzn", out string? text) || text.Length == 0)
        {
            error = "This symbol needs a `pzn:` — the eight-digit Pharmazentralnummer.";
            return false;
        }

        string digits = text.Replace("-", string.Empty).Replace(" ", string.Empty);
        if (digits.Length == 7) digits = "0" + digits;   // the older seven-digit form, promoted

        if (digits.Length != 8 || !digits.All(char.IsAsciiDigit))
        {
            error = $"`pzn: {text}` is not a PZN — eight digits.";
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 7; i++) sum += (digits[i] - '0') * (i + 1);
        int check = sum % 11;

        if (check == 10 || check != digits[7] - '0')
        {
            error = $"`pzn: {text}` fails its check digit.";
            return false;
        }

        pzn = digits;
        return true;
    }

    // ── NTIN — a national trade item number, carried as GS1 ────────────────

    /// <summary>
    /// The pharmacy pack as GS1 sees it: a GTIN under AI 01 — the German NTIN is <c>0 4150</c> then the
    /// PZN and a mod-10 check — with expiry, lot and serial under 17, 10 and 21.
    /// </summary>
    private static bool TryNtin(IReadOnlyDictionary<string, string> fields, DataMatrixOptions baseline,
                                out string? payload, out DataMatrixOptions options, out string? error)
    {
        payload = null;
        options = baseline;
        error   = null;

        string gtin;
        if (fields.TryGetValue("gtin", out string? given) && given.Length > 0)
        {
            if (!TryGtin(given, out gtin, out error)) return false;
        }
        else
        {
            if (!TryPzn(fields, out string? pzn, out error))
            {
                error = "An `ntin` symbol needs a `pzn:` to derive the number from, or a `gtin:` to use as it is.";
                return false;
            }
            gtin = WithGtinCheck("04150" + pzn);
        }

        var elements = new List<(string, string)> { ("01", gtin) };
        if (Optional(fields, "expiry") is { } expiry)
        {
            if (!IsYyMmDd(expiry)) { error = $"`expiry: {expiry}` must be six digits, YYMMDD."; return false; }
            elements.Add(("17", expiry));
        }
        if (Optional(fields, "lot")    is { } lot)    elements.Add(("10", lot));
        if (Optional(fields, "serial") is { } serial) elements.Add(("21", serial));

        payload = JoinElements(elements);
        options = baseline with { Gs1 = true };
        return true;
    }

    private static bool TryGtin(string text, out string gtin, out string? error)
    {
        gtin  = string.Empty;
        error = null;

        string digits = text.Replace(" ", string.Empty);
        if (digits.Length is not (13 or 14) || !digits.All(char.IsAsciiDigit))
        {
            error = $"`gtin: {text}` is not a GTIN — thirteen or fourteen digits.";
            return false;
        }

        if (digits.Length == 13) digits = "0" + digits;

        if (WithGtinCheck(digits[..13]) != digits)
        {
            error = $"`gtin: {text}` fails its check digit.";
            return false;
        }

        gtin = digits;
        return true;
    }

    /// <summary>Thirteen digits plus the GS1 mod-10 check: weights 3 and 1 alternating from the right.</summary>
    private static string WithGtinCheck(string thirteen)
    {
        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            int weight = (12 - i) % 2 == 0 ? 3 : 1;
            sum += (thirteen[i] - '0') * weight;
        }
        return thirteen + (10 - sum % 10) % 10;
    }

    // ── Royal Mail Mailmark 2D ─────────────────────────────────────────────

    /// <summary>
    /// The three Mailmark 2D formats and the symbol each one is: format 7 is 51 characters in 24×24,
    /// format 9 is 90 in 32×32, format 29 is 70 in 16×48. The size is not a choice — Royal Mail's readers
    /// expect the format to be in the symbol they specified for it.
    /// </summary>
    private static readonly Dictionary<string, (int Length, int Rows, int Columns)> MailmarkFormats = new()
    {
        ["7"]  = (51, 24, 24),
        ["9"]  = (90, 32, 32),
        ["29"] = (70, 16, 48),
    };

    /// <summary>
    /// A Mailmark 2D symbol from its complete message. The message is the author's: Royal Mail's field
    /// layout — country, format, version, class, supply-chain and item ids, postcode, service, customer
    /// content — is defined by their barcode specification and is validated by their systems at
    /// induction; what the block guarantees is that it is the right length, the right characters, and in
    /// the right symbol.
    /// </summary>
    private static bool TryMailmark(IReadOnlyDictionary<string, string> fields, DataMatrixOptions baseline,
                                    out string? payload, out DataMatrixOptions options, out string? error)
    {
        payload = null;
        options = baseline;
        error   = null;

        if (!fields.TryGetValue("format", out string? format) || !MailmarkFormats.TryGetValue(format.Trim(), out var spec))
        {
            error = "A `mailmark` symbol needs `format:` 7, 9 or 29.";
            return false;
        }

        if (!fields.TryGetValue("message", out string? message))
        {
            error = $"A `mailmark` symbol needs `message:` — the {spec.Length}-character content for format {format}.";
            return false;
        }

        if (message.Length != spec.Length)
        {
            error = $"A format {format} Mailmark message is exactly {spec.Length} characters; this one is {message.Length}.";
            return false;
        }

        if (!message.All(c => c == ' ' || char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
        {
            error = "A Mailmark message is upper-case letters, digits and spaces only.";
            return false;
        }

        payload = message;
        options = baseline with { Size = (spec.Rows, spec.Columns) };
        return true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? Optional(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

    private static bool IsYyMmDd(string text) => text.Length == 6 && text.All(char.IsAsciiDigit);
}
