using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>
/// Reads what <see cref="MatrixParser"/> made of a <c>pdf417</c> fenced block into a <see cref="Pdf417Block"/>.
///
/// <para>
/// The fields and the drawing settings are <see cref="MatrixBlockReader"/>'s; the <c>type:</c>
/// vocabulary is the <c>qr</c> one, because a URL or a vCard reads the same out of any symbol. What is
/// PDF417's own is its shape: <c>columns:</c>, <c>ec:</c>, <c>rowHeight:</c> and <c>truncated:</c>.
/// </para>
/// </summary>
public static class Pdf417BlockReader
{
    private static readonly string[] OwnKeys = ["type", "columns", "ec", "rowheight", "truncated"];

    public static bool TryRead(ContentPart tree, out Pdf417Block? block, out (ContentPart Part, string Reason) wrong)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(tree, out var fields, out wrong)) return false;

        string types = string.Join(", ", QrPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            wrong = (tree, $"An empty pdf417 block. Start with a `type:` line — {types}.");
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            wrong = (fields.Value("type"), $"This pdf417 block has no `type:` line. Supported types: {types}.");
            return false;
        }

        if (!QrPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            wrong = (fields.Value("type"), $"Unknown PDF417 type '{type}'. Supported types: {types}.");
            return false;
        }

        if (MatrixBlockReader.Unknown(fields,
                                      key => OwnKeys.Contains(key.Replace("-", string.Empty), StringComparer.OrdinalIgnoreCase)
                                             || typeFields.Contains(key, StringComparer.OrdinalIgnoreCase),
                                      key => $"'{key}' is not a field of a `{type.ToLowerInvariant()}` PDF417 symbol. It takes {string.Join(", ", typeFields)}"
                                             + $"; and columns, ec, rowHeight, truncated, {MatrixBlockReader.SettingNames}.",
                                      out wrong))
            return false;

        if (!QrPayload.TryBuild(type, fields, out string? payload, out var error))
        {
            wrong = (tree, error ?? "This block could not be read.");
            return false;
        }

        int? columns = null;
        if (fields.ContainsKey("columns"))
        {
            if (!MatrixBlockReader.TrySize(fields, "columns", Pdf417Encoder.MinColumns,
                                           Pdf417Encoder.MinColumns, Pdf417Encoder.MaxColumns,
                                           out int c, out wrong)) return false;
            columns = c;
        }

        int? level = null;
        if (fields.ContainsKey("ec"))
        {
            if (!MatrixBlockReader.TrySize(fields, "ec", Pdf417Encoder.MinErrorLevel,
                                           Pdf417Encoder.MinErrorLevel, Pdf417Encoder.MaxErrorLevel,
                                           out int e, out wrong)) return false;
            level = e;
        }

        if (!TryRowHeight(fields, out double rowHeight, out wrong)) return false;
        if (!TryFlag(fields, "truncated", out bool truncated, out wrong)) return false;
        if (!MatrixBlockReader.TrySettings(fields, out var settings, out wrong)) return false;

        block = new Pdf417Block
        {
            Type      = type.ToLowerInvariant(),
            Payload   = payload!,
            Options   = new Pdf417Options { Columns = columns, ErrorCorrectionLevel = level, Truncated = truncated },
            Settings  = settings,
            RowHeight = rowHeight,
        };
        return true;
    }

    private static bool TryRowHeight(MatrixFields fields, out double result, out (ContentPart Part, string Reason) wrong)
    {
        result = Pdf417Block.DefaultRowHeight;
        wrong  = default;

        if (!fields.TryGetValue("rowHeight", out string? value) || value.Length == 0) return true;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            wrong = (fields.Value("rowHeight"), $"`rowHeight: {value}` is not a number.");
            return false;
        }

        if (parsed < Pdf417Block.MinRowHeight || parsed > Pdf417Block.MaxRowHeight)
        {
            wrong = (fields.Value("rowHeight"), $"`rowHeight: {value}` is outside the usable range "
                                                + $"{Pdf417Block.MinRowHeight}–{Pdf417Block.MaxRowHeight} module widths.");
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryFlag(MatrixFields fields, string key, out bool result, out (ContentPart Part, string Reason) wrong)
    {
        result = false;
        wrong  = default;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "1":  result = true;  return true;
            case "false" or "no" or "0":  result = false; return true;
            default:
                wrong = (fields.Value(key), $"`{key}: {value}` is not true or false.");
                return false;
        }
    }
}
