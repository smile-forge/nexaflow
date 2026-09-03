using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// Reads the body of a <c>datamatrix</c> fenced block into a <see cref="DataMatrixBlock"/>.
///
/// <para>
/// The line reader and the drawing settings are <see cref="MatrixBlockReader"/>'s. What is Data
/// Matrix's own: the <c>type:</c> vocabulary through <see cref="DataMatrixPayload"/>, and two settings
/// — <c>shape:</c> to keep the symbol square or rectangular, and <c>size:</c> to fix it outright.
/// </para>
/// </summary>
public static class DataMatrixBlockParser
{
    private static readonly string[] OwnKeys = ["type", "shape", "size"];

    public static bool TryParse(string source, out DataMatrixBlock? block, out string? error)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(source, out var fields, out error)) return false;

        string types = string.Join(", ", DataMatrixPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            error = $"An empty datamatrix block. Start with a `type:` line — {types}.";
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            error = $"This datamatrix block has no `type:` line. Supported types: {types}.";
            return false;
        }

        if (!DataMatrixPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            error = $"Unknown Data Matrix type '{type}'. Supported types: {types}.";
            return false;
        }

        foreach (string key in fields.Keys)
        {
            if (OwnKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            if (MatrixBlockReader.IsSetting(key)) continue;
            if (typeFields.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            error = $"'{key}' is not a field of a `{type.ToLowerInvariant()}` Data Matrix. "
                  + $"It takes {string.Join(", ", typeFields)}"
                  + $"; and shape, size, {MatrixBlockReader.SettingNames}.";
            return false;
        }

        if (!TryShape(fields, out var shape, out error)) return false;
        if (!TrySize(fields, out var size, out error)) return false;

        var baseline = new DataMatrixOptions { Shape = shape, Size = size };

        if (!DataMatrixPayload.TryBuild(type, fields, baseline, out string? payload, out var options, out error))
            return false;

        if (!MatrixBlockReader.TrySettings(fields, out var settings, out error)) return false;

        block = new DataMatrixBlock
        {
            Type     = type.ToLowerInvariant(),
            Payload  = payload!,
            Options  = options,
            Settings = settings,
        };
        return true;
    }

    private static bool TryShape(IReadOnlyDictionary<string, string> fields,
                                 out DataMatrixShape shape, out string? error)
    {
        shape = DataMatrixShape.Any;
        error = null;

        if (!fields.TryGetValue("shape", out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "any":                     shape = DataMatrixShape.Any;       return true;
            case "square":                  shape = DataMatrixShape.Square;    return true;
            case "rectangle" or "rect":     shape = DataMatrixShape.Rectangle; return true;
            default:
                error = $"`shape: {value}` is not a Data Matrix shape. Use square, rectangle or any.";
                return false;
        }
    }

    /// <summary>A <c>size: 32x32</c> — rows by columns, one of the sizes the standard defines.</summary>
    private static bool TrySize(IReadOnlyDictionary<string, string> fields,
                                out (int Rows, int Columns)? size, out string? error)
    {
        size  = null;
        error = null;

        if (!fields.TryGetValue("size", out string? value) || value.Length == 0) return true;

        var parts = value.ToLowerInvariant().Split(['x', '×'], StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int rows) && int.TryParse(parts[1], out int cols)
            && DataMatrixEncoder.TryGetSize(rows, cols, out _))
        {
            size = (rows, cols);
            return true;
        }

        error = $"`size: {value}` is not a Data Matrix size. Write it as rows×columns — 10x10 up to 144x144, or one of the rectangles 8x18, 8x32, 12x26, 12x36, 16x36, 16x48.";
        return false;
    }
}
