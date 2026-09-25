using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// Reads a <c>barcode</c> block's tree (<see cref="BarcodeParser"/>) into a <see cref="BarcodeBlock"/>: which format, what value,
/// and how it is drawn.
///
/// <para>
/// What stops it being read is said against the piece of the tree it is about — the line that is not a field, the key that is
/// not a setting, the width that is not a number — and against the whole block where what is wrong is something missing from it.
/// A value the format cannot carry is not one of them: the block reads, and whether its value encodes is the builder's to find.
/// </para>
/// </summary>
public static class BarcodeBlockReader
{
    private static readonly string[] Keys =
    [
        "format", "value", "width", "height", "displayvalue",
        "fontsize", "textalign", "linecolor", "background", "margin",
    ];

    /// <summary>The block a tree says, or the piece of it that stops it being read and why.</summary>
    public static bool TryRead(ContentPart tree, out BarcodeBlock? block, out (ContentPart Part, string Reason) wrong)
    {
        block = null;
        wrong = default;

        // A hole says something still has to go where it stands, which is not something wrong with what is written.
        if (tree.SelfAndDescendants().FirstOrDefault(part => part.Trouble is not null && !part.Derived) is { } troubled)
        {
            wrong = (troubled, troubled.Trouble!);
            return false;
        }

        // A key written twice keeps the last value it was given, and one written with hyphens is the same key without them.
        var fields = new Dictionary<string, ContentPart>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in tree.SelfAndDescendants().Where(part => part.Kind == MatrixKinds.Field))
        {
            var key = field.Part(Roles.Name)!;
            var named = key.Text.Replace("-", string.Empty);

            if (!Keys.Contains(named, StringComparer.OrdinalIgnoreCase))
            {
                wrong = (key, $"'{key.Text}' is not a barcode setting. It takes format, value, width, height, "
                            + "displayValue, fontSize, textAlign, lineColor, background, margin.");
                return false;
            }

            fields[named] = field;
        }

        if (fields.Count == 0)
        {
            wrong = (tree, "An empty barcode block. It needs a `format:` and a `value:`.");
            return false;
        }

        if (Said(fields, "format") is not { } formatName)
        {
            wrong = (fields.GetValueOrDefault("format") ?? tree,
                     "This barcode block has no `format:`. Supported formats: " + string.Join(", ", BarcodeEncoder.FormatNames) + ".");
            return false;
        }

        if (!BarcodeEncoder.TryParseSymbology(formatName.Text, out var format))
        {
            wrong = (formatName, $"Unknown barcode format '{formatName.Text}'. Supported formats: "
                               + string.Join(", ", BarcodeEncoder.FormatNames) + ".");
            return false;
        }

        // A missing value line is something missing from the block; an empty value is not — it is where somebody starts
        // from, and the builder says it will not encode.
        if (!fields.TryGetValue("value", out var value))
        {
            wrong = (tree, "This barcode block has no `value:` line.");
            return false;
        }

        if (!TryNumber(fields, "width", BarcodeBlock.DefaultBarWidth, BarcodeBlock.MinBarWidth, BarcodeBlock.MaxBarWidth,
                       out var width, out wrong)) return false;
        if (!TryNumber(fields, "height", BarcodeBlock.DefaultBarHeight, BarcodeBlock.MinBarHeight, BarcodeBlock.MaxBarHeight,
                       out var height, out wrong)) return false;
        if (!TryNumber(fields, "fontSize", BarcodeBlock.DefaultFontSize, BarcodeBlock.MinFontSize, BarcodeBlock.MaxFontSize,
                       out var fontSize, out wrong)) return false;
        if (!TryNumber(fields, "margin", BarcodeBlock.DefaultMargin, 0, BarcodeBlock.MaxMargin, out var margin, out wrong)) return false;

        if (!TryBool(fields, "displayValue", true, out var displayValue, out wrong)) return false;
        if (!TryAlign(fields, out var align, out wrong)) return false;
        if (!TryColor(fields, "lineColor", out var lineColor, out wrong)) return false;
        if (!TryColor(fields, "background", out var background, out wrong)) return false;

        block = new BarcodeBlock
        {
            Format       = format,
            Field        = value,
            BarWidth     = width,
            BarHeight    = height,
            FontSize     = fontSize,
            Margin       = margin,
            DisplayValue = displayValue,
            TextAlign    = align,
            LineColor    = lineColor,
            Background   = background,
        };
        return true;
    }

    /// <summary>What a field says, or null where it is not written or says nothing.</summary>
    private static ContentPart? Said(IReadOnlyDictionary<string, ContentPart> fields, string key) =>
        fields.TryGetValue(key, out var field) ? field.Part(MatrixRoles.Value) : null;

    // ── Settings ───────────────────────────────────────────────────────────

    private static bool TryNumber(IReadOnlyDictionary<string, ContentPart> fields, string key, double fallback,
                                  double min, double max, out double result, out (ContentPart, string) wrong)
    {
        result = fallback;
        wrong = default;

        if (Said(fields, key) is not { } said) return true;

        if (!double.TryParse(said.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            wrong = (said, $"`{key}: {said.Text}` is not a number.");
            return false;
        }

        if (parsed < min || parsed > max)
        {
            wrong = (said, $"`{key}: {said.Text}` is outside the usable range {min}–{max}.");
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryBool(IReadOnlyDictionary<string, ContentPart> fields, string key, bool fallback,
                                out bool result, out (ContentPart, string) wrong)
    {
        result = fallback;
        wrong = default;

        if (Said(fields, key) is not { } said) return true;

        switch (said.Text.Trim().ToLowerInvariant())
        {
            case "true"  or "yes" or "1": result = true;  return true;
            case "false" or "no"  or "0": result = false; return true;
            default:
                wrong = (said, $"`{key}: {said.Text}` is not true or false.");
                return false;
        }
    }

    private static bool TryAlign(IReadOnlyDictionary<string, ContentPart> fields, out BarcodeTextAlign align,
                                 out (ContentPart, string) wrong)
    {
        align = BarcodeTextAlign.Center;
        wrong = default;

        if (Said(fields, "textAlign") is not { } said) return true;

        switch (said.Text.Trim().ToLowerInvariant())
        {
            case "left":               align = BarcodeTextAlign.Left;   return true;
            case "center" or "centre": align = BarcodeTextAlign.Center; return true;
            case "right":              align = BarcodeTextAlign.Right;  return true;
            default:
                wrong = (said, $"`textAlign: {said.Text}` is not left, center or right.");
                return false;
        }
    }

    private static bool TryColor(IReadOnlyDictionary<string, ContentPart> fields, string key,
                                 out HexColor? color, out (ContentPart, string) wrong)
    {
        color = null;
        wrong = default;

        if (Said(fields, key) is not { } said) return true;

        if (!HexColor.TryParse(said.Text, out var parsed))
        {
            wrong = (said, $"`{key}: {said.Text}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.");
            return false;
        }

        color = parsed;
        return true;
    }
}
