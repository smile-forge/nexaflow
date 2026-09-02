using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// The part of reading a 2D-code block that is the same whichever symbology it is for: the flat
/// <c>key: value</c> body, and the drawing settings every one of them takes.
///
/// <para>
/// The grammar is deliberately tiny — one field per line, the key before the first colon, everything
/// after it the value — which is what lets a URL sit on the right of a <c>url:</c> without quoting.
/// A symbology's own parser reads the fields this hands back, checks the keys it knows, and builds its
/// block; none of them re-implements the line reader or the colour syntax.
/// </para>
/// </summary>
public static class MatrixBlockReader
{
    /// <summary>The keys that configure the drawing rather than the content — valid on any 2D block.</summary>
    public static readonly string[] SettingKeys = ["cellsize", "margin", "dark", "light"];

    /// <summary>What a diagnostic lists when it names the shared settings.</summary>
    public const string SettingNames = "cellSize, margin, dark, light";

    /// <summary>
    /// Reads the body into its fields. False, with a message written for whoever is looking at the block,
    /// when a line is not a <c>key: value</c> pair.
    /// </summary>
    public static bool TryReadFields(string? source, out Dictionary<string, string> fields, out string? error)
    {
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error  = null;

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
