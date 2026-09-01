using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Turns a <c>qr</c> block's <c>type:</c> and its fields into the one string that actually gets
/// encoded.
///
/// <para>
/// This is where nearly all the value of the block lives. A QR code carries text and nothing else; a
/// phone knows to offer "join this network" or "add this contact" only because the text follows a
/// convention ΓÇö <c>WIFI:</c>, a vCard, <c>SMSTO:</c>, a BIP-21 URI. Getting the escaping and the field
/// order right is the difference between a code that does something and one that shows a wall of
/// punctuation, so each builder here is written against the convention its scanners expect rather
/// than being glued together from the fields in source order.
/// </para>
/// </summary>
internal static class QrPayload
{
    /// <summary>
    /// The fields each type reads. Doubles as the spell-check for a block: anything else in the
    /// source is a typo the author wants told about, not a line to skip in silence.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> FieldsByType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"]   = ["text"],
            ["url"]    = ["url"],
            ["email"]  = ["email", "subject", "body"],
            ["phone"]  = ["phone"],
            ["sms"]    = ["number", "message"],
            ["wifi"]   = ["ssid", "password", "security", "hidden"],
            ["vcard"]  = ["name", "org", "title", "phone", "email", "url", "address"],
            ["geo"]    = ["lat", "lng"],
            ["event"]  = ["title", "location", "start", "end"],
            ["crypto"] = ["coin", "address", "amount"],
            ["epc"]    = ["name", "iban", "bic", "amount", "purpose", "reference", "message"],
            ["mecard"] = ["name", "phone", "email", "url", "address", "note"],
        };

    /// <summary>Builds the encodable string, or explains what the block is missing.</summary>
    internal static bool TryBuild(
        string type,
        IReadOnlyDictionary<string, string> fields,
        out string? payload,
        out string? error)
    {
        payload = null;
        error   = null;

        string? Field(string name) =>
            fields.TryGetValue(name, out var v) && v.Length > 0 ? v : null;

        // The message for the one field a type cannot do without.
        string Missing(string name) =>
            $"A `{type.ToLowerInvariant()}` QR code needs a `{name}:` line.";

        switch (type.ToLowerInvariant())
        {
            case "text":
                if (Field("text") is not { } text) { error = Missing("text"); return false; }
                payload = text;
                return true;

            case "url":
                if (Field("url") is not { } url) { error = Missing("url"); return false; }
                // A bare host scans as text and does nothing; a scheme is what makes it a link.
                payload = HasScheme(url) ? url : "https://" + url;
                return true;

            case "email":
                if (Field("email") is not { } address) { error = Missing("email"); return false; }
                payload = BuildMailto(address, Field("subject"), Field("body"));
                return true;

            case "phone":
                if (Field("phone") is not { } phone) { error = Missing("phone"); return false; }
                payload = "tel:" + CompactNumber(phone);
                return true;

            case "sms":
                if (Field("number") is not { } smsNumber) { error = Missing("number"); return false; }
                // SMSTO: is the form Android and iOS both act on; the older SMS: is not.
                payload = $"SMSTO:{CompactNumber(smsNumber)}:{Field("message") ?? string.Empty}";
                return true;

            case "wifi":
                if (Field("ssid") is not { } ssid) { error = Missing("ssid"); return false; }
                payload = BuildWifi(ssid, Field("password"), Field("security"), Field("hidden"));
                return true;

            case "vcard":
                if (Field("name") is not { } name) { error = Missing("name"); return false; }
                payload = BuildVCard(name, Field("org"), Field("title"), Field("phone"),
                                     Field("email"), Field("url"), Field("address"));
                return true;

            case "geo":
                if (Field("lat") is not { } lat) { error = Missing("lat"); return false; }
                if (Field("lng") is not { } lng) { error = Missing("lng"); return false; }
                if (!TryCoordinate(lat, out double latValue, -90, 90))
                {
                    error = $"`lat: {lat}` is not a latitude between -90 and 90.";
                    return false;
                }
                if (!TryCoordinate(lng, out double lngValue, -180, 180))
                {
                    error = $"`lng: {lng}` is not a longitude between -180 and 180.";
                    return false;
                }
                payload = string.Create(CultureInfo.InvariantCulture, $"geo:{latValue},{lngValue}");
                return true;

            case "event":
                if (Field("title") is not { } summary) { error = Missing("title"); return false; }
                return TryBuildEvent(summary, Field("location"), Field("start"), Field("end"),
                                     out payload, out error);

            case "epc":
            if (Field("name") is not { } payee)   { error = Missing("name"); return false; }
            if (Field("iban") is not { } iban)    { error = Missing("iban"); return false; }
            return TryBuildEpc(payee, iban, Field("bic"), Field("amount"), Field("purpose"),
            Field("reference"), Field("message"), out payload, out error);

            case "mecard":
            if (Field("name") is not { } mecardName) { error = Missing("name"); return false; }
            payload = BuildMecard(mecardName, Field("phone"), Field("email"),
            Field("url"), Field("address"), Field("note"));
            return true;

            case "crypto":
                if (Field("address") is not { } cryptoAddress) { error = Missing("address"); return false; }
                payload = BuildCrypto(Field("coin") ?? "bitcoin", cryptoAddress, Field("amount"));
                return true;

            default:
                error = $"Unknown QR type '{type}'. Supported types: "
                      + string.Join(", ", FieldsByType.Keys) + ".";
                return false;
        }
    }

    // ΓöÇΓöÇ Builders ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private static string BuildMailto(string address, string? subject, string? body)
    {
        var query = new List<string>(2);
        if (subject is not null) query.Add("subject=" + Uri.EscapeDataString(subject));
        if (body is not null)    query.Add("body="    + Uri.EscapeDataString(body));

        return query.Count == 0
            ? "mailto:" + address
            : "mailto:" + address + "?" + string.Join("&", query);
    }

    /// <summary>
    /// True when the value already names a scheme.
    /// <para>
    /// A colon on its own is not enough to tell: <c>example.com:8080</c> is a host and a port, and
    /// treating it as a scheme would leave a code that scans as plain text and opens nothing. So a colon
    /// followed by a digit reads as a port, and anything else after a well-formed scheme name ΓÇö the
    /// <c>mailto:</c>, <c>tel:</c> and <c>bitcoin:</c> a URL block may reasonably carry ΓÇö reads as a scheme.
    /// </para>
    /// </summary>
    private static bool HasScheme(string url)
    {
        if (url.Contains("://", StringComparison.Ordinal)) return true;

        int colon = url.IndexOf(':');
        if (colon <= 0 || colon + 1 >= url.Length || char.IsAsciiDigit(url[colon + 1])) return false;

        return char.IsAsciiLetter(url[0])
            && url.AsSpan(1, colon - 1).ContainsAnyExcept(SchemeCharacters) is false;
    }

    /// <summary>The characters a scheme name may use after its first letter.</summary>
    private static readonly System.Buffers.SearchValues<char> SchemeCharacters =
        System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-.");

    private static string BuildWifi(string ssid, string? password, string? security, string? hidden)
    {
        // No password means an open network however the block labelled its security.
        string type = password is null
            ? "nopass"
            : (security ?? "WPA").Trim().ToUpperInvariant() switch
            {
                "WEP"                            => "WEP",
                "NONE" or "NOPASS" or "OPEN" or "" => "nopass",
                _                                => "WPA",   // WPA, WPA2, WPA3 all join as WPA
            };

        var sb = new StringBuilder("WIFI:");
        sb.Append("T:").Append(type).Append(';');
        sb.Append("S:").Append(EscapeWifi(ssid)).Append(';');
        if (type != "nopass" && password is not null)
            sb.Append("P:").Append(EscapeWifi(password)).Append(';');
        if (IsTrue(hidden))
            sb.Append("H:true;");

        return sb.Append(';').ToString();
    }

    private static string BuildVCard(string name, string? org, string? title, string? phone,
                                     string? email, string? url, string? address)
    {
        // vCard is CRLF-delimited by specification, whatever the host file uses.
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n");

        Line("BEGIN:VCARD");
        Line("VERSION:3.0");

        // N wants the name in parts. Splitting on the last space is a guess, but a structured name is
        // what lets a phone file the contact under a surname, so it beats leaving N empty.
        int split = name.LastIndexOf(' ');
        string family = split > 0 ? name[(split + 1)..] : name;
        string given  = split > 0 ? name[..split] : string.Empty;
        Line($"N:{EscapeVCard(family)};{EscapeVCard(given)};;;");
        Line("FN:" + EscapeVCard(name));

        if (org is not null)     Line("ORG:"   + EscapeVCard(org));
        if (title is not null)   Line("TITLE:" + EscapeVCard(title));
        // No TYPE= parameters on these. The block gives one phone and one email with nothing said about
        // what kind they are, so labelling them CELL and INTERNET would be inventing detail — and the
        // parameter syntax is the part of vCard whose handling varies most between the things that read it.
        if (phone is not null)   Line("TEL:"   + CompactNumber(phone));
        if (email is not null)   Line("EMAIL:" + EscapeVCard(email));
        if (url is not null)     Line("URL:"   + EscapeVCard(url));
        // ADR's seven components are box;extended;street;locality;region;postcode;country. A single
        // written address cannot be split reliably, so it all goes in the street field.
        if (address is not null) Line($"ADR:;;{EscapeVCard(address)};;;;");

        Line("END:VCARD");
        return sb.ToString();
    }

    private static bool TryBuildEvent(string summary, string? location, string? start, string? end,
                                      out string? payload, out string? error)
    {
        payload = null;
        error   = null;

        string? startStamp = null, endStamp = null;
        if (start is not null && !TryStamp(start, out startStamp))
        {
            error = $"`start: {start}` is not a date/time. Use a form like 2026-08-01T18:00.";
            return false;
        }
        if (end is not null && !TryStamp(end, out endStamp))
        {
            error = $"`end: {end}` is not a date/time. Use a form like 2026-08-01T21:00.";
            return false;
        }

        // Wrapped in a VCALENDAR rather than emitted as a bare VEVENT. Some readers take the bare form,
        // but the wrapper is what every calendar app accepts ΓÇö and PRODID and VERSION are what make this
        // a valid iCalendar object rather than one that happens to be tolerated.
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Nexaflow//Markdown QR//EN");
        Line("BEGIN:VEVENT");
        Line("SUMMARY:" + EscapeVCard(summary));
        if (location is not null)   Line("LOCATION:" + EscapeVCard(location));
        if (startStamp is not null) Line("DTSTART:" + startStamp);
        if (endStamp is not null)   Line("DTEND:"   + endStamp);
        Line("END:VEVENT");
        Line("END:VCALENDAR");

        payload = sb.ToString();
        return true;
    }

    private static string BuildCrypto(string coin, string address, string? amount)
    {
        // BIP-21 and the schemes modelled on it: <coin>:<address>[?amount=ΓÇª].
        string scheme = coin.Trim().ToLowerInvariant() switch
        {
            "btc"  => "bitcoin",
            "eth"  => "ethereum",
            "ltc"  => "litecoin",
            "bch"  => "bitcoincash",
            "xmr"  => "monero",
            var other => other,
        };

        return amount is null
            ? $"{scheme}:{address}"
            : $"{scheme}:{address}?amount={Uri.EscapeDataString(amount)}";
    }

    /// <summary>
    /// An EPC069-12 credit transfer ΓÇö the "GiroCode" printed on European invoices. Twelve line-separated
    /// elements in a fixed order, of which the trailing empty ones may be dropped.
    /// <para>
    /// Version 002 is emitted rather than 001 because it makes the BIC optional, which for an IBAN inside
    /// the EEA is what banks actually want; 001 would force every block to carry one.
    /// </para>
    /// </summary>
    private static bool TryBuildEpc(string name, string iban, string? bic, string? amount,
                                    string? purpose, string? reference, string? message,
                                    out string? payload, out string? error)
    {
        payload = null;
        error   = null;

        string account = iban.Replace(" ", string.Empty).ToUpperInvariant();
        if (!IsValidIban(account))
        {
            error = $"`iban: {iban}` is not a valid IBAN ΓÇö the check digits do not match. "
                  + "A typo here produces a code the bank rejects, so it is caught before the code is drawn.";
            return false;
        }

        if (name.Length > 70)
        {
            error = $"`name:` is {name.Length} characters; an EPC payment carries at most 70.";
            return false;
        }

        if (bic is not null && (bic.Length is not (8 or 11) || !bic.All(char.IsAsciiLetterOrDigit)))
        {
            error = $"`bic: {bic}` is not a BIC. It is 8 or 11 letters and digits.";
            return false;
        }

        // Structured and unstructured remittance occupy different lines and the standard allows only one.
        if (reference is not null && message is not null)
        {
            error = "An EPC payment takes either `reference:` (a structured creditor reference) or "
                  + "`message:` (free text), not both.";
            return false;
        }

        string amountLine = string.Empty;
        if (amount is not null)
        {
            if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                || value < 0.01m || value > 999999999.99m)
            {
                error = $"`amount: {amount}` is not a euro amount between 0.01 and 999999999.99.";
                return false;
            }
            amountLine = "EUR" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        if (purpose is not null && purpose.Length > 4)
        {
            error = $"`purpose: {purpose}` is longer than the 4-character purpose code the standard allows.";
            return false;
        }

        string[] lines =
        [
            "BCD",                      // service tag
            "002",                      // version ΓÇö 002 makes the BIC optional
            "1",                        // character set: UTF-8
            "SCT",                      // SEPA credit transfer
            bic       ?? string.Empty,
            name,
            account,
            amountLine,
            purpose   ?? string.Empty,
            reference ?? string.Empty,  // structured creditor reference
            message   ?? string.Empty,  // unstructured remittance information
            string.Empty,               // beneficiary-to-originator information
        ];

        // Trailing empties carry no meaning and the standard lets them go; dropping them keeps the symbol
        // smaller, which for a code printed on an invoice is the difference worth having.
        int last = lines.Length;
        while (last > 0 && lines[last - 1].Length == 0) last--;

        // EPC069-12 fixes the separator as a line feed, whatever the host document uses.
        string result = string.Join("\n", lines[..last]);

        const int maxBytes = 331;
        int bytes = Encoding.UTF8.GetByteCount(result);
        if (bytes > maxBytes)
        {
            error = $"This payment is {bytes} bytes; an EPC code holds {maxBytes}. Shorten `message:` or `name:`.";
            return false;
        }

        payload = result;
        return true;
    }

    /// <summary>
    /// Checks an IBAN by its own check digits: move the first four characters to the end, read letters as
    /// two-digit numbers, and the whole thing must be 1 mod 97.
    /// </summary>
    private static bool IsValidIban(string iban)
    {
        if (iban.Length is < 15 or > 34) return false;
        if (!char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1])) return false;
        if (!char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3])) return false;
        if (!iban.All(char.IsAsciiLetterOrDigit)) return false;

        int remainder = 0;
        foreach (char c in iban[4..] + iban[..4])
        {
            int value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value > 9 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }
        return remainder == 1;
    }

    /// <summary>
    /// DENSO Wave's MECARD ΓÇö the compact contact format, one line, fewer fields than a vCard and a much
    /// smaller symbol for it. Kept beside <c>vcard</c> rather than replacing it because the two are a real
    /// trade: vCard carries the organisation and job title, MECARD scans faster and older readers know it.
    /// </summary>
    private static string BuildMecard(string name, string? phone, string? email,
                                      string? url, string? address, string? note)
    {
        var sb = new StringBuilder("MECARD:");

        // N takes "last,first" ΓÇö the comma is a separator, so each half is escaped on its own and joined
        // with a raw one.
        int split = name.LastIndexOf(' ');
        string family = split > 0 ? name[(split + 1)..] : name;
        string given  = split > 0 ? name[..split] : string.Empty;

        sb.Append("N:").Append(EscapeMecard(family));
        if (given.Length > 0) sb.Append(',').Append(EscapeMecard(given));
        sb.Append(';');

        if (phone is not null)   sb.Append("TEL:").Append(CompactNumber(phone)).Append(';');
        if (email is not null)   sb.Append("EMAIL:").Append(EscapeMecard(email)).Append(';');
        if (url is not null)     sb.Append("URL:").Append(EscapeMecard(url)).Append(';');
        if (address is not null) sb.Append("ADR:").Append(EscapeMecard(address)).Append(';');
        if (note is not null)    sb.Append("NOTE:").Append(EscapeMecard(note)).Append(';');

        return sb.Append(';').ToString();
    }

    /// <summary>Backslash-escapes the characters MECARD uses as separators.</summary>
    private static string EscapeMecard(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or ';' or ':' or ',') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ΓöÇΓöÇ Escaping and parsing helpers ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>Backslash-escapes the characters that would otherwise end a WIFI: field.</summary>
    private static string EscapeWifi(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Escapes a vCard / iCalendar text value, whose separators are the comma and semicolon.</summary>
    private static string EscapeVCard(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';':  sb.Append("\\;");  break;
                case ',':  sb.Append("\\,");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Strips the spacing a written phone number carries; a dialler wants the digits and the +.</summary>
    private static string CompactNumber(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            if (!char.IsWhiteSpace(c) && c is not ('(' or ')' or '-' or '.'))
                sb.Append(c);
        return sb.ToString();
    }

    private static bool IsTrue(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "true" or "yes" or "1";

    private static bool TryCoordinate(string value, out double result, double min, double max) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && result >= min && result <= max;

    /// <summary>Formats a date/time as iCalendar's basic form; a trailing Z is kept as UTC.</summary>
    private static bool TryStamp(string value, out string? stamp)
    {
        stamp = null;

        bool utc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        var styles = utc
            ? DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            : DateTimeStyles.None;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed))
            return false;

        stamp = parsed.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + (utc ? "Z" : string.Empty);
        return true;
    }
}
