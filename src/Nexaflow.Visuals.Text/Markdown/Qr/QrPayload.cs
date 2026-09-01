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
        if (phone is not null)   Line("TEL;TYPE=CELL:" + CompactNumber(phone));
        if (email is not null)   Line("EMAIL;TYPE=INTERNET:" + EscapeVCard(email));
        if (url is not null)     Line("URL:"   + EscapeVCard(url));
        // ADR's seven components are box;extended;street;locality;region;postcode;country. A single
        // written address cannot be split reliably, so it all goes in the street field.
        if (address is not null) Line($"ADR;TYPE=WORK:;;{EscapeVCard(address)};;;;");

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

        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n");

        Line("BEGIN:VEVENT");
        Line("SUMMARY:" + EscapeVCard(summary));
        if (location is not null)   Line("LOCATION:" + EscapeVCard(location));
        if (startStamp is not null) Line("DTSTART:" + startStamp);
        if (endStamp is not null)   Line("DTEND:"   + endStamp);
        Line("END:VEVENT");

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
