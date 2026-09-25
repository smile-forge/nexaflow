using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// Reads what <see cref="MatrixParser"/> made of a <c>datamatrix</c> fenced block into a <see cref="DataMatrixBlock"/>.
///
/// <para>
/// The fields and the drawing settings are <see cref="MatrixBlockReader"/>'s. What is Data
/// Matrix's own: the <c>type:</c> vocabulary through <see cref="DataMatrixPayload"/>, and two settings
/// — <c>shape:</c> to keep the symbol square or rectangular, and <c>size:</c> to fix it outright.
/// </para>
/// </summary>
public static class DataMatrixBlockReader
{
    private static readonly string[] OwnKeys = ["type", "shape", "size"];

    public static bool TryRead(ContentPart tree, out DataMatrixBlock? block, out (ContentPart Part, string Reason) wrong)
    {
        block = null;

        if (!MatrixBlockReader.TryReadFields(tree, out var fields, out wrong)) return false;

        string types = string.Join(", ", DataMatrixPayload.FieldsByType.Keys);

        if (fields.Count == 0)
        {
            wrong = (tree, $"An empty datamatrix block. Start with a `type:` line — {types}.");
            return false;
        }

        if (!fields.TryGetValue("type", out string? type) || type.Length == 0)
        {
            wrong = (fields.Value("type"), $"This datamatrix block has no `type:` line. Supported types: {types}.");
            return false;
        }

        if (!DataMatrixPayload.FieldsByType.TryGetValue(type, out string[]? typeFields))
        {
            wrong = (fields.Value("type"), $"Unknown Data Matrix type '{type}'. Supported types: {types}.");
            return false;
        }

        if (MatrixBlockReader.Unknown(fields,
                                      key => OwnKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || typeFields.Contains(key, StringComparer.OrdinalIgnoreCase),
                                      key => $"'{key}' is not a field of a `{type.ToLowerInvariant()}` Data Matrix. It takes {string.Join(", ", typeFields)}"
                                             + $"; and shape, size, {MatrixBlockReader.SettingNames}.",
                                      out wrong))
            return false;

        if (!TryShape(fields, out var shape, out wrong)) return false;
        if (!TrySize(fields, out var size, out wrong)) return false;

        var baseline = new DataMatrixOptions { Shape = shape, Size = size };

        if (!DataMatrixPayload.TryBuild(type, fields, baseline, out string? payload, out var options, out var error))
        {
            wrong = (tree, error ?? "This block could not be read.");
            return false;
        }

        if (!MatrixBlockReader.TrySettings(fields, out var settings, out wrong)) return false;

        block = new DataMatrixBlock
        {
            Type     = type.ToLowerInvariant(),
            Payload  = payload!,
            Options  = options,
            Settings = settings,
        };
        return true;
    }

    private static bool TryShape(MatrixFields fields, out DataMatrixShape shape, out (ContentPart Part, string Reason) wrong)
    {
        shape = DataMatrixShape.Any;
        wrong = default;

        if (!fields.TryGetValue("shape", out string? value) || value.Length == 0) return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "any":                     shape = DataMatrixShape.Any;       return true;
            case "square":                  shape = DataMatrixShape.Square;    return true;
            case "rectangle" or "rect":     shape = DataMatrixShape.Rectangle; return true;
            default:
                wrong = (fields.Value("shape"), $"`shape: {value}` is not a Data Matrix shape. Use square, rectangle or any.");
                return false;
        }
    }

    /// <summary>A <c>size: 32x32</c> — rows by columns, one of the sizes the standard defines.</summary>
    private static bool TrySize(MatrixFields fields, out (int Rows, int Columns)? size, out (ContentPart Part, string Reason) wrong)
    {
        size  = null;
        wrong = default;

        if (!fields.TryGetValue("size", out string? value) || value.Length == 0) return true;

        var parts = value.ToLowerInvariant().Split(['x', '×'], StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int rows) && int.TryParse(parts[1], out int cols)
            && DataMatrixEncoder.TryGetSize(rows, cols, out _))
        {
            size = (rows, cols);
            return true;
        }

        wrong = (fields.Value("size"), $"`size: {value}` is not a Data Matrix size. Write it as rows×columns — 10x10 up to 144x144, or one of the rectangles 8x18, 8x32, 12x26, 12x36, 16x36, 16x48.");
        return false;
    }
}
