using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// The part of understanding a 2D-code block that is the same whichever symbology it is for: the fields
/// <see cref="MatrixParser"/> found in it, and the drawing settings every one of them takes.
///
/// <para>
/// A symbology's own reader takes the fields this hands back, checks the keys it knows, and builds its
/// block; none of them walks the tree or re-implements the colour syntax.
/// </para>
/// </summary>
public static class MatrixBlockReader
{
    /// <summary>The keys that configure the drawing rather than the content — valid on any 2D block.</summary>
    public static readonly string[] SettingKeys = ["cellsize", "margin", "dark", "light"];

    /// <summary>What a diagnostic lists when it names the shared settings.</summary>
    public const string SettingNames = "cellSize, margin, dark, light";

    /// <summary>
    /// The fields the parser found in a block. False, with the reason the parser gave, when a line of it is not a
    /// <c>key: value</c> pair. A key written twice keeps the last value it was given.
    /// </summary>
    public static bool TryReadFields(ContentNode tree, out Dictionary<string, string> fields, out string? error)
    {
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error  = null;

        foreach (var piece in tree.SelfAndDescendants())
        {
            if (piece.Trouble is { } trouble)
            {
                error = trouble;
                return false;
            }

            if (piece.Kind == MatrixKinds.Field)
                fields[piece.Part(Roles.Name)!.Text] = piece.Part(MatrixRoles.Value)?.Text ?? string.Empty;
        }

        return true;
    }

    /// <summary>Whether <paramref name="key"/> is one of the shared drawing settings.</summary>
    public static bool IsSetting(string key) =>
        SettingKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the shared drawing settings out of the fields, defaults where absent.</summary>
    public static bool TrySettings(IReadOnlyDictionary<string, string> fields,
                                   out MatrixSettings settings, out string? error)
    {
        settings = MatrixSettings.Default;

        if (!TrySize(fields, "cellSize", MatrixSettings.DefaultCellSize, MatrixSettings.MinCellSize,
                     MatrixSettings.MaxCellSize, out int cellSize, out error)) return false;
        if (!TrySize(fields, "margin", MatrixSettings.DefaultMargin, 0, MatrixSettings.MaxMargin,
                     out int margin, out error)) return false;
        if (!TryColor(fields, "dark",  out var dark,  out error)) return false;
        if (!TryColor(fields, "light", out var light, out error)) return false;

        settings = new MatrixSettings { CellSize = cellSize, Margin = margin, Dark = dark, Light = light };
        return true;
    }

    /// <summary>A whole number within a range, or its default when the key is absent.</summary>
    public static bool TrySize(IReadOnlyDictionary<string, string> fields, string key,
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
            error = $"`{key}: {value}` is outside the usable range {min}–{max}.";
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>; the leading hash is optional.</summary>
    public static bool TryColor(IReadOnlyDictionary<string, string> fields, string key,
                                out HexColor? result, out string? error)
    {
        result = null;
        error  = null;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        if (!HexColor.TryParse(value, out var color))
        {
            error = $"`{key}: {value}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.";
            return false;
        }

        result = color;
        return true;
    }
}
