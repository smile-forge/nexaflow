using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Reads the body of a <c>qr</c> fenced block ΓÇö a flat list of <c>key: value</c> lines ΓÇö into a
/// <see cref="QrBlock"/>.
///
/// <para>
/// The grammar is deliberately tiny: one field per line, the key before the first colon, everything
/// after it the value. That is what lets a URL sit on the right of a <c>url:</c> without quoting.
/// </para>
///
/// <para>
/// Unrecognised keys are reported rather than ignored. A block is half a dozen lines that produce a
/// picture, so a mistyped <c>cellsize</c> silently doing nothing is the worst outcome available ΓÇö the
/// author would see a code that looks plausible and is not what they asked for.
/// </para>
/// </summary>
public static class QrBlockParser
{
    /// <summary>Keys that configure the drawing rather than the content, valid whatever the type is.</summary>
    private static readonly string[] SettingKeys = ["type", "ec", "cellsize", "margin", "dark", "light"];

    /// <summary>
    /// Parses <paramref name="source"/>. Returns false with a message written for whoever is looking
    /// at the block, not for a log.
    /// </summary>
    public static bool TryParse(string source, out QrBlock? block, out string? error)
    {
        block = null;
        error = null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in (source ?? string.Empty).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;   // blank, or a comment

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                error = $"'{line}' is not a `key: value` line.";
                return false;
            }

            fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (fields.Count == 0)
        {
            error = "An empty qr block. Start with a `type:` line ΓÇö "
                  + string.Join(", ", QrPayload.FieldsByType.Keys) + ".";
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            error = "This qr block has no `type:` line. Supported types: "
                  + string.Join(", ", QrPayload.FieldsByType.Keys) + ".";
            return false;
        }

        if (!QrPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            error = $"Unknown QR type '{type}'. Supported types: "
                  + string.Join(", ", QrPayload.FieldsByType.Keys) + ".";
            return false;
        }

        // Now the type is known, so is the set of keys that mean anything here.
        foreach (string key in fields.Keys)
        {
            if (SettingKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            if (typeFields.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            error = $"'{key}' is not a field of a `{type.ToLowerInvariant()}` QR code. "
                  + $"It takes {string.Join(", ", typeFields)}"
                  + $"; and ec, cellSize, margin, dark, light.";
            return false;
        }

        if (!QrPayload.TryBuild(type, fields, out string? payload, out error))
            return false;

        if (!TryErrorCorrection(fields, out var ecl, out error)) return false;
        if (!TrySize(fields, "cellSize", QrBlock.DefaultCellSize, QrBlock.MinCellSize, QrBlock.MaxCellSize,
                     out int cellSize, out error)) return false;
        if (!TrySize(fields, "margin", QrBlock.DefaultMargin, 0, QrBlock.MaxMargin,
                     out int margin, out error)) return false;
        if (!TryColor(fields, "dark", out var dark, out error)) return false;
        if (!TryColor(fields, "light", out var light, out error)) return false;

        block = new QrBlock
        {
            Type            = type.ToLowerInvariant(),
            Payload         = payload!,
            ErrorCorrection = ecl,
            CellSize        = cellSize,
            Margin          = margin,
            Dark            = dark,
            Light           = light,
        };
        return true;
    }

    // ΓöÇΓöÇ Settings ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private static bool TryErrorCorrection(IReadOnlyDictionary<string, string> fields,
                                           out QrErrorCorrection ecl, out string? error)
    {
        ecl   = QrErrorCorrection.Medium;
        error = null;

        if (!fields.TryGetValue("ec", out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToUpperInvariant())
        {
            case "L": ecl = QrErrorCorrection.Low;      return true;
            case "M": ecl = QrErrorCorrection.Medium;   return true;
            case "Q": ecl = QrErrorCorrection.Quartile; return true;
            case "H": ecl = QrErrorCorrection.High;     return true;
            default:
                error = $"`ec: {value}` is not an error-correction level. Use L, M, Q or H.";
                return false;
        }
    }

    private static bool TrySize(IReadOnlyDictionary<string, string> fields, string key,
                                int fallback, int min, int max, out int result, out string? error)
    {
        result = fallback;
        error  = null;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            error = $"`{key}: {value}` is not a whole number.";
            return false;
        }

        if (parsed < min || parsed > max)
        {
            error = $"`{key}: {value}` is outside the usable range {min}ΓÇô{max}.";
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>; the leading hash is optional.</summary>
    private static bool TryColor(IReadOnlyDictionary<string, string> fields, string key,
                                 out QrColor? result, out string? error)
    {
        result = null;
        error  = null;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        string hex = value.Trim().TrimStart('#');
        bool valid = hex.Length is 3 or 6 or 8
                  && hex.All(char.IsAsciiHexDigit);

        if (!valid)
        {
            error = $"`{key}: {value}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.";
            return false;
        }

        if (hex.Length == 3)   // #ABC is #AABBCC
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        byte Component(int index) =>
            byte.Parse(hex.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        result = hex.Length == 8
            ? new QrColor(Component(0), Component(1), Component(2), Component(3))
            : new QrColor(0xFF, Component(0), Component(1), Component(2));
        return true;
    }
}
