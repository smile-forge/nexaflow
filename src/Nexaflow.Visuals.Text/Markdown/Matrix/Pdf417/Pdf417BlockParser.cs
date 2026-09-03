using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>
/// Reads the body of a <c>pdf417</c> fenced block into a <see cref="Pdf417Block"/>.
///
/// <para>
/// The line reader and the drawing settings are <see cref="MatrixBlockReader"/>'s; the <c>type:</c>
/// vocabulary is the <c>qr</c> one, because a URL or a vCard reads the same out of any symbol. What is
/// PDF417's own is its shape: <c>columns:</c>, <c>ec:</c>, <c>rowHeight:</c> and <c>truncated:</c>.
/// </para>
/// </summary>
public static class Pdf417BlockParser
{
    private static readonly string[] OwnKeys = ["type", "columns", "ec", "rowheight", "truncated"];

    public static bool TryParse(string source, out Pdf417Block? block, out string? error)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(source, out var fields, out error)) return false;

        string types = string.Join(", ", QrPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            error = $"An empty pdf417 block. Start with a `type:` line — {types}.";
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            error = $"This pdf417 block has no `type:` line. Supported types: {types}.";
            return false;
        }

        if (!QrPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            error = $"Unknown PDF417 type '{type}'. Supported types: {types}.";
            return false;
        }

        foreach (string key in fields.Keys)
        {
            if (OwnKeys.Contains(key.Replace("-", string.Empty), StringComparer.OrdinalIgnoreCase)) continue;
            if (MatrixBlockReader.IsSetting(key)) continue;
            if (typeFields.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            error = $"'{key}' is not a field of a `{type.ToLowerInvariant()}` PDF417 symbol. "
                  + $"It takes {string.Join(", ", typeFields)}"
                  + $"; and columns, ec, rowHeight, truncated, {MatrixBlockReader.SettingNames}.";
            return false;
        }

        if (!QrPayload.TryBuild(type, fields, out string? payload, out error)) return false;

        int? columns = null;
        if (fields.ContainsKey("columns"))
        {
            if (!MatrixBlockReader.TrySize(fields, "columns", Pdf417Encoder.MinColumns,
                                           Pdf417Encoder.MinColumns, Pdf417Encoder.MaxColumns,
                                           out int c, out error)) return false;
            columns = c;
        }

        int? level = null;
        if (fields.ContainsKey("ec"))
        {
            if (!MatrixBlockReader.TrySize(fields, "ec", Pdf417Encoder.MinErrorLevel,
                                           Pdf417Encoder.MinErrorLevel, Pdf417Encoder.MaxErrorLevel,
                                           out int e, out error)) return false;
            level = e;
        }

        if (!TryRowHeight(fields, out double rowHeight, out error)) return false;
        if (!TryFlag(fields, "truncated", out bool truncated, out error)) return false;
        if (!MatrixBlockReader.TrySettings(fields, out var settings, out error)) return false;

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

    private static bool TryRowHeight(IReadOnlyDictionary<string, string> fields, out double result, out string? error)
    {
        result = Pdf417Block.DefaultRowHeight;
        error  = null;

        if (!fields.TryGetValue("rowHeight", out string? value) || value.Length == 0) return true;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            error = $"`rowHeight: {value}` is not a number.";
            return false;
        }

        if (parsed < Pdf417Block.MinRowHeight || parsed > Pdf417Block.MaxRowHeight)
        {
            error = $"`rowHeight: {value}` is outside the usable range "
                  + $"{Pdf417Block.MinRowHeight}–{Pdf417Block.MaxRowHeight} module widths.";
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryFlag(IReadOnlyDictionary<string, string> fields, string key, out bool result, out string? error)
    {
        result = false;
        error  = null;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "1":  result = true;  return true;
            case "false" or "no" or "0":  result = false; return true;
            default:
                error = $"`{key}: {value}` is not true or false.";
                return false;
        }
    }
}
