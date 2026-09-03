using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Reads the body of an <c>aztec</c> fenced block into an <see cref="AztecBlock"/>.
///
/// <para>
/// The line reader and the drawing settings are <see cref="MatrixBlockReader"/>'s. What is Aztec's own
/// is the shape of the symbol: <c>format:</c> to choose compact or full range, <c>layers:</c> to fix
/// the size outright, <c>ecc:</c> for how much of it is error correction, and <c>eci:</c> to declare
/// the character set.
/// </para>
/// </summary>
public static class AztecBlockParser
{
    private static readonly string[] OwnKeys = ["type", "format", "layers", "ecc", "eci"];

    public static bool TryParse(string source, out AztecBlock? block, out string? error)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(source, out var fields, out error)) return false;

        string types = string.Join(", ", AztecPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            error = $"An empty aztec block. Start with a `type:` line — {types}.";
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            error = $"This aztec block has no `type:` line. Supported types: {types}.";
            return false;
        }

        if (!AztecPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            error = $"Unknown Aztec type '{type}'. Supported types: {types}.";
            return false;
        }

        foreach (string key in fields.Keys)
        {
            if (OwnKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            if (MatrixBlockReader.IsSetting(key)) continue;
            if (typeFields.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            error = $"'{key}' is not a field of an `{type.ToLowerInvariant()}` Aztec code. "
                  + $"It takes {string.Join(", ", typeFields)}"
                  + $"; and format, layers, ecc, eci, {MatrixBlockReader.SettingNames}.";
            return false;
        }

        if (!TryFormat(fields, out var format, out error)) return false;
        if (!TryLayers(fields, format, out int? layers, out error)) return false;
        if (!MatrixBlockReader.TrySize(fields, "ecc", AztecOptions.DefaultErrorCorrectionPercent,
                                       AztecOptions.MinErrorCorrectionPercent,
                                       AztecOptions.MaxErrorCorrectionPercent, out int ecc, out error))
            return false;
        if (!TryEci(fields, out int? eci, out error)) return false;

        var baseline = new AztecOptions
        {
            Format                 = format,
            Layers                 = layers,
            ErrorCorrectionPercent = ecc,
            Eci                    = eci,
        };

        if (!AztecPayload.TryBuild(type, fields, baseline, out string? payload, out var options, out error))
            return false;

        if (!MatrixBlockReader.TrySettings(fields, out var settings, out error)) return false;

        block = new AztecBlock
        {
            Type     = type.ToLowerInvariant(),
            Payload  = payload!,
            Options  = options,
            Settings = settings,
        };
        return true;
    }

    private static bool TryFormat(IReadOnlyDictionary<string, string> fields,
                                  out AztecFormat format, out string? error)
    {
        format = AztecFormat.Auto;
        error  = null;

        if (!fields.TryGetValue("format", out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":                    format = AztecFormat.Auto;    return true;
            case "compact":                 format = AztecFormat.Compact; return true;
            case "full" or "full-range":    format = AztecFormat.Full;    return true;
            default:
                error = $"`format: {value}` is not an Aztec format. Use compact, full or auto.";
                return false;
        }
    }

    /// <summary>
    /// A forced layer count. The ceiling depends on the family — four compact, thirty-two full — and a
    /// count above four with <c>format: compact</c> is a contradiction rather than a number to clamp.
    /// </summary>
    private static bool TryLayers(IReadOnlyDictionary<string, string> fields, AztecFormat format,
                                  out int? layers, out string? error)
    {
        layers = null;

        int ceiling = format == AztecFormat.Compact
            ? AztecOptions.MaxCompactLayers
            : AztecOptions.MaxFullLayers;

        if (!MatrixBlockReader.TrySize(fields, "layers", 0, 1, ceiling, out int value, out error))
        {
            if (format == AztecFormat.Compact)
                error += $" A compact Aztec symbol has one to {AztecOptions.MaxCompactLayers} layers; "
                       + "use `format: full` for more.";
            return false;
        }

        if (value > 0) layers = value;
        return true;
    }

    /// <summary>An ECI number, which FLG(n) writes as up to six digits.</summary>
    private static bool TryEci(IReadOnlyDictionary<string, string> fields, out int? eci, out string? error)
    {
        eci = null;

        if (!MatrixBlockReader.TrySize(fields, "eci", -1, 0, AztecOptions.MaxEci, out int value, out error))
            return false;

        if (value >= 0) eci = value;
        return true;
    }
}
