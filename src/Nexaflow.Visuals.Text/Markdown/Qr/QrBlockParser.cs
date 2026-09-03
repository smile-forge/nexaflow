using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Reads the body of a <c>qr</c> fenced block into a <see cref="QrBlock"/>.
///
/// <para>
/// The line reader and the drawing settings are <see cref="MatrixBlockReader"/>'s — they are the same
/// for every 2D block. What is QR's own is the <c>type:</c> vocabulary, resolved through
/// <see cref="QrPayload"/>, and the <c>ec:</c> level.
/// </para>
/// <para>
/// Unrecognised keys are reported rather than ignored. A block is half a dozen lines that produce a
/// picture, so a mistyped <c>cellsize</c> silently doing nothing is the worst outcome available — the
/// author would see a code that looks plausible and is not what they asked for.
/// </para>
/// </summary>
public static class QrBlockParser
{
    /// <summary>
    /// Parses <paramref name="source"/>. Returns false with a message written for whoever is looking
    /// at the block, not for a log.
    /// </summary>
    public static bool TryParse(string source, out QrBlock? block, out string? error)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(source, out var fields, out error)) return false;

        if (fields.Count == 0)
        {
            error = "An empty qr block. Start with a `type:` line — "
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
            if (key.Equals("type", StringComparison.OrdinalIgnoreCase) || key.Equals("ec", StringComparison.OrdinalIgnoreCase)) continue;
            if (MatrixBlockReader.IsSetting(key)) continue;
            if (typeFields.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            error = $"'{key}' is not a field of a `{type.ToLowerInvariant()}` QR code. "
                  + $"It takes {string.Join(", ", typeFields)}"
                  + $"; and ec, {MatrixBlockReader.SettingNames}.";
            return false;
        }

        if (!QrPayload.TryBuild(type, fields, out string? payload, out error)) return false;
        if (!TryErrorCorrection(fields, out var ecl, out error)) return false;
        if (!MatrixBlockReader.TrySettings(fields, out var settings, out error)) return false;

        block = new QrBlock
        {
            Type            = type.ToLowerInvariant(),
            Payload         = payload!,
            ErrorCorrection = ecl,
            Settings        = settings,
        };
        return true;
    }

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
}
