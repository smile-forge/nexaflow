using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Reads what <see cref="MatrixParser"/> made of an <c>aztec</c> fenced block into an <see cref="AztecBlock"/>.
///
/// <para>
/// The fields and the drawing settings are <see cref="MatrixBlockReader"/>'s. What is Aztec's own
/// is the shape of the symbol: <c>format:</c> to choose compact or full range, <c>layers:</c> to fix
/// the size outright, <c>ecc:</c> for how much of it is error correction, and <c>eci:</c> to declare
/// the character set.
/// </para>
/// </summary>
public static class AztecBlockReader
{
    private static readonly string[] OwnKeys = ["type", "format", "layers", "ecc", "eci"];

    public static bool TryRead(ContentPart tree, out AztecBlock? block, out (ContentPart Part, string Reason) wrong)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(tree, out var fields, out wrong)) return false;

        string types = string.Join(", ", AztecPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            wrong = (tree, $"An empty aztec block. Start with a `type:` line — {types}.");
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            wrong = (fields.Value("type"), $"This aztec block has no `type:` line. Supported types: {types}.");
            return false;
        }

        if (!AztecPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            wrong = (fields.Value("type"), $"Unknown Aztec type '{type}'. Supported types: {types}.");
            return false;
        }

        if (MatrixBlockReader.Unknown(fields,
                                      key => OwnKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || typeFields.Contains(key, StringComparer.OrdinalIgnoreCase),
                                      key => $"'{key}' is not a field of an `{type.ToLowerInvariant()}` Aztec code. It takes {string.Join(", ", typeFields)}"
                                             + $"; and format, layers, ecc, eci, {MatrixBlockReader.SettingNames}.",
                                      out wrong))
            return false;

        if (!TryFormat(fields, out var format, out wrong)) return false;
        if (!TryLayers(fields, format, out int? layers, out wrong)) return false;
        if (!MatrixBlockReader.TrySize(fields, "ecc", AztecOptions.DefaultErrorCorrectionPercent,
                                       AztecOptions.MinErrorCorrectionPercent,
                                       AztecOptions.MaxErrorCorrectionPercent, out int ecc, out wrong))
            return false;
        if (!TryEci(fields, out int? eci, out wrong)) return false;

        var baseline = new AztecOptions
        {
            Format                 = format,
            Layers                 = layers,
            ErrorCorrectionPercent = ecc,
            Eci                    = eci,
        };

        if (!AztecPayload.TryBuild(type, fields, baseline, out string? payload, out var options, out var error))
        {
            wrong = (tree, error ?? "This block could not be read.");
            return false;
        }

        if (!MatrixBlockReader.TrySettings(fields, out var settings, out wrong)) return false;

        block = new AztecBlock
        {
            Type     = type.ToLowerInvariant(),
            Payload  = payload!,
            Options  = options,
            Settings = settings,
        };
        return true;
    }

    private static bool TryFormat(MatrixFields fields, out AztecFormat format, out (ContentPart Part, string Reason) wrong)
    {
        format = AztecFormat.Auto;
        wrong  = default;

        if (!fields.TryGetValue("format", out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":                    format = AztecFormat.Auto;    return true;
            case "compact":                 format = AztecFormat.Compact; return true;
            case "full" or "full-range":    format = AztecFormat.Full;    return true;
            default:
                wrong = (fields.Value("format"), $"`format: {value}` is not an Aztec format. Use compact, full or auto.");
                return false;
        }
    }

    /// <summary>
    /// A forced layer count. The ceiling depends on the family — four compact, thirty-two full — and a
    /// count above four with <c>format: compact</c> is a contradiction rather than a number to clamp.
    /// </summary>
    private static bool TryLayers(MatrixFields fields, AztecFormat format, out int? layers, out (ContentPart Part, string Reason) wrong)
    {
        layers = null;

        int ceiling = format == AztecFormat.Compact
            ? AztecOptions.MaxCompactLayers
            : AztecOptions.MaxFullLayers;

        if (!MatrixBlockReader.TrySize(fields, "layers", 0, 1, ceiling, out int value, out wrong))
        {
            if (format == AztecFormat.Compact)
                wrong = (wrong.Part, wrong.Reason + $" A compact Aztec symbol has one to {AztecOptions.MaxCompactLayers} layers; "
                                                  + "use `format: full` for more.");
            return false;
        }

        if (value > 0) layers = value;
        return true;
    }

    /// <summary>An ECI number, which FLG(n) writes as up to six digits.</summary>
    private static bool TryEci(MatrixFields fields, out int? eci, out (ContentPart Part, string Reason) wrong)
    {
        eci = null;

        if (!MatrixBlockReader.TrySize(fields, "eci", -1, 0, AztecOptions.MaxEci, out int value, out wrong))
            return false;

        if (value >= 0) eci = value;
        return true;
    }
}
