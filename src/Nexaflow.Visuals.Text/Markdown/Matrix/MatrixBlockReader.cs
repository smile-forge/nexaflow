using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// The fields a code block is written with: what each one says, by its key — which is what a payload is built from — and the
/// parts of the tree each is written as, so what stops a block being read is said against the line, the key or the value at
/// fault rather than the whole block. A key written twice keeps the last value it was given.
/// </summary>
public sealed class MatrixFields : IReadOnlyDictionary<string, string>
{
    private readonly Dictionary<string, string> _said = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContentPart> _written = new(StringComparer.OrdinalIgnoreCase);

    internal MatrixFields(ContentPart block) => Block = block;

    /// <summary>The whole block — what is blamed where something is missing from it rather than wrong in it.</summary>
    public ContentPart Block { get; }

    internal void Add(ContentPart field)
    {
        var key = field.Part(Roles.Name)!.Text;

        _written[key] = field;
        _said[key] = field.Part(MatrixRoles.Value)?.Text ?? string.Empty;
    }

    /// <summary>A field's key as written, or the block where the field is not written.</summary>
    public ContentPart Key(string key) => _written.TryGetValue(key, out var field) ? field.Part(Roles.Name) ?? field : Block;

    /// <summary>What a field is set to as written — its value, or its line where nothing follows the colon — or the block where it is not written.</summary>
    public ContentPart Value(string key) => _written.TryGetValue(key, out var field) ? field.Part(MatrixRoles.Value) ?? field : Block;

    public string this[string key] => _said[key];
    public IEnumerable<string> Keys => _said.Keys;
    public IEnumerable<string> Values => _said.Values;
    public int Count => _said.Count;
    public bool ContainsKey(string key) => _said.ContainsKey(key);
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _said.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _said.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The part of understanding a 2D-code block that is the same whichever symbology it is for: the fields
/// <see cref="MatrixParser"/> found in it, and the drawing settings every one of them takes.
///
/// <para>
/// A symbology's own reader takes the fields this hands back, checks the keys it knows, and builds its
/// block; none of them walks the tree or re-implements the colour syntax. What stops a block being read is
/// handed back as the part of the tree at fault and why.
/// </para>
/// </summary>
public static class MatrixBlockReader
{
    /// <summary>The keys that configure the drawing rather than the content — valid on any 2D block.</summary>
    public static readonly string[] SettingKeys = ["cellsize", "margin", "dark", "light"];

    /// <summary>What a diagnostic lists when it names the shared settings.</summary>
    public const string SettingNames = "cellSize, margin, dark, light";

    /// <summary>
    /// The fields the parser found in a block — or false with the line that is not a <c>key: value</c> pair, and the
    /// reason the parser gave.
    /// </summary>
    public static bool TryReadFields(ContentPart tree, out MatrixFields fields, out (ContentPart Part, string Reason) wrong)
    {
        fields = new MatrixFields(tree);
        wrong = default;

        foreach (var piece in tree.SelfAndDescendants())
        {
            if (piece.Trouble is { } trouble)
            {
                wrong = (piece, trouble);
                return false;
            }

            if (piece.Kind == MatrixKinds.Field) fields.Add(piece);
        }

        return true;
    }

    /// <summary>Whether <paramref name="key"/> is one of the shared drawing settings.</summary>
    public static bool IsSetting(string key) =>
        SettingKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the shared drawing settings out of the fields, defaults where absent.</summary>
    public static bool TrySettings(MatrixFields fields, out MatrixSettings settings, out (ContentPart Part, string Reason) wrong)
    {
        settings = MatrixSettings.Default;

        if (!TrySize(fields, "cellSize", MatrixSettings.DefaultCellSize, MatrixSettings.MinCellSize,
                     MatrixSettings.MaxCellSize, out int cellSize, out wrong)) return false;
        if (!TrySize(fields, "margin", MatrixSettings.DefaultMargin, 0, MatrixSettings.MaxMargin,
                     out int margin, out wrong)) return false;
        if (!TryColor(fields, "dark",  out var dark,  out wrong)) return false;
        if (!TryColor(fields, "light", out var light, out wrong)) return false;

        settings = new MatrixSettings { CellSize = cellSize, Margin = margin, Dark = dark, Light = light };
        return true;
    }

    /// <summary>A whole number within a range, or its default when the key is absent.</summary>
    public static bool TrySize(MatrixFields fields, string key, int fallback, int min, int max, out int result,
                               out (ContentPart Part, string Reason) wrong)
    {
        result = fallback;
        wrong  = default;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            wrong = (fields.Value(key), $"`{key}: {value}` is not a whole number.");
            return false;
        }

        if (parsed < min || parsed > max)
        {
            wrong = (fields.Value(key), $"`{key}: {value}` is outside the usable range {min}–{max}.");
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>; the leading hash is optional.</summary>
    public static bool TryColor(MatrixFields fields, string key, out HexColor? result, out (ContentPart Part, string Reason) wrong)
    {
        result = null;
        wrong  = default;

        if (!fields.TryGetValue(key, out string? value) || value.Length == 0) return true;

        if (!HexColor.TryParse(value, out var color))
        {
            wrong = (fields.Value(key), $"`{key}: {value}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.");
            return false;
        }

        result = color;
        return true;
    }

    /// <summary>
    /// The first key a symbology does not take, said against the key as written — or false where every key is one it takes.
    /// </summary>
    internal static bool Unknown(MatrixFields fields, Func<string, bool> takes, Func<string, string> says,
                                 out (ContentPart Part, string Reason) wrong)
    {
        wrong = default;

        foreach (var key in fields.Keys)
        {
            if (IsSetting(key) || takes(key)) continue;

            wrong = (fields.Key(key), says(key));
            return true;
        }

        return false;
    }
}
