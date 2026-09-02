using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// Reads the body of a <c>barcode</c> fenced block — a flat list of <c>key: value</c> lines — into a
/// <see cref="BarcodeBlock"/>.
///
/// <para>
/// It draws a line the QR parser does not have to. A <em>structural</em> fault — an unknown key, a
/// format that does not exist, a width that is not a number — means the block cannot be understood at
/// all, and the reader gets the source back with the reason. A value the chosen format cannot carry is
/// a different thing entirely: the block is perfectly well formed, and the value is the one part of it
/// the reader edits in place, so it must keep rendering while they type through the invalid states that
/// lie between one good value and the next.
/// </para>
/// <para>
/// So this parser does not encode. It reports where the value sits and leaves whether it encodes to the
/// element, which can then mark it and carry on.
/// </para>
/// </summary>
public static class BarcodeBlockParser
{
    private static readonly string[] Keys =
    [
        "format", "value", "width", "height", "displayvalue",
        "fontsize", "textalign", "linecolor", "background", "margin",
    ];

    /// <summary>Parses <paramref name="source"/>, or explains which line stops it being a block at all.</summary>
    public static bool TryParse(string source, out BarcodeBlock? block, out string? error)
    {
        block = null;
        error = null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int valueStart = 0;

        int offset = 0;
        foreach (string raw in (source ?? string.Empty).Split('\n'))
        {
            string line = raw.Trim();
            int lineStart = offset;
            offset += raw.Length + 1;   // the newline the split took off

            if (line.Length == 0 || line[0] == '#') continue;

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                error = $"'{line}' is not a `key: value` line.";
                return false;
            }

            string key = line[..colon].Trim();
            string text = line[(colon + 1)..].Trim();

            if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                // Where the value's first character sits in the whole block, so an edit can be put back
                // exactly where it came from. Found by looking in the untrimmed line, since the leading
                // space after the colon is real.
                int inRaw = raw.IndexOf(text, StringComparison.Ordinal);
                valueStart = lineStart + (inRaw < 0 ? raw.Length : inRaw);
            }

            fields[key] = text;
        }

        if (fields.Count == 0)
        {
            error = "An empty barcode block. It needs a `format:` and a `value:`.";
            return false;
        }

        foreach (string key in fields.Keys)
        {
            if (!Keys.Contains(key.Replace("-", string.Empty), StringComparer.OrdinalIgnoreCase))
            {
                error = $"'{key}' is not a barcode setting. It takes format, value, width, height, "
                      + "displayValue, fontSize, textAlign, lineColor, background, margin.";
                return false;
            }
        }

        if (!fields.TryGetValue("format", out string? formatName) || formatName.Length == 0)
        {
            error = "This barcode block has no `format:` line. Supported formats: "
                  + string.Join(", ", BarcodeEncoder.FormatNames) + ".";
            return false;
        }

        if (!BarcodeEncoder.TryParseSymbology(formatName, out var format))
        {
            error = $"Unknown barcode format '{formatName}'. Supported formats: "
                  + string.Join(", ", BarcodeEncoder.FormatNames) + ".";
            return false;
        }

        // A missing value is structural — there is nothing to draw and nothing to edit. An empty one is
        // not: it is where a reader starts from, and the element shows it as an unreadable value.
        if (!fields.TryGetValue("value", out string? value))
        {
            error = "This barcode block has no `value:` line.";
            return false;
        }

        if (!TryNumber(fields, "width", BarcodeBlock.DefaultBarWidth,
                       BarcodeBlock.MinBarWidth, BarcodeBlock.MaxBarWidth, out double width, out error)) return false;
        if (!TryNumber(fields, "height", BarcodeBlock.DefaultBarHeight,
                       BarcodeBlock.MinBarHeight, BarcodeBlock.MaxBarHeight, out double height, out error)) return false;
        if (!TryNumber(fields, "fontSize", BarcodeBlock.DefaultFontSize,
                       BarcodeBlock.MinFontSize, BarcodeBlock.MaxFontSize, out double fontSize, out error)) return false;
        if (!TryNumber(fields, "margin", BarcodeBlock.DefaultMargin,
                       0, BarcodeBlock.MaxMargin, out double margin, out error)) return false;

        if (!TryBool(fields, "displayValue", true, out bool displayValue, out error)) return false;
        if (!TryAlign(fields, out var align, out error)) return false;
        if (!TryColor(fields, "lineColor", out var lineColor, out error)) return false;
        if (!TryColor(fields, "background", out var background, out error)) return false;

        block = new BarcodeBlock
        {
            Format       = format,
            Value        = value,
            ValueStart   = valueStart,
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

    // ── Settings ───────────────────────────────────────────────────────────

    private static bool TryNumber(IReadOnlyDictionary<string, string> fields, string key, double fallback,
                                  double min, double max, out double result, out string? error)
    {
        result = fallback;
        error  = null;

        if (!fields.TryGetValue(key, out string? text) || text.Length == 0) return true;

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            error = $"`{key}: {text}` is not a number.";
            return false;
        }

        if (parsed < min || parsed > max)
        {
            error = $"`{key}: {text}` is outside the usable range {min}–{max}.";
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryBool(IReadOnlyDictionary<string, string> fields, string key, bool fallback,
                                out bool result, out string? error)
    {
        result = fallback;
        error  = null;

        if (!fields.TryGetValue(key, out string? text) || text.Length == 0) return true;

        switch (text.Trim().ToLowerInvariant())
        {
            case "true"  or "yes" or "1": result = true;  return true;
            case "false" or "no"  or "0": result = false; return true;
            default:
                error = $"`{key}: {text}` is not true or false.";
                return false;
        }
    }

    private static bool TryAlign(IReadOnlyDictionary<string, string> fields, out BarcodeTextAlign align,
                                 out string? error)
    {
        align = BarcodeTextAlign.Center;
        error = null;

        if (!fields.TryGetValue("textAlign", out string? text) || text.Length == 0) return true;

        switch (text.Trim().ToLowerInvariant())
        {
            case "left":              align = BarcodeTextAlign.Left;   return true;
            case "center" or "centre": align = BarcodeTextAlign.Center; return true;
            case "right":             align = BarcodeTextAlign.Right;  return true;
            default:
                error = $"`textAlign: {text}` is not left, center or right.";
                return false;
        }
    }

    private static bool TryColor(IReadOnlyDictionary<string, string> fields, string key,
                                 out HexColor? color, out string? error)
    {
        color = null;
        error = null;

        if (!fields.TryGetValue(key, out string? text) || text.Length == 0) return true;

        if (!HexColor.TryParse(text, out var parsed))
        {
            error = $"`{key}: {text}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.";
            return false;
        }

        color = parsed;
        return true;
    }
}
