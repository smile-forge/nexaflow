using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Theming;

public enum ColorSpecKind { Default, Swatch, Custom }

/// <summary>
/// A colour a user chose, in one of three forms: the theme's own (<see cref="Default"/>), a token from the
/// <c>Swatch.*</c> bank that the active theme retunes (<see cref="FromSwatch"/>), or a fixed colour
/// (<see cref="Custom"/>). It is saved as its <see cref="ToString"/> — empty, <c>swatch:Blue</c> or
/// <c>#AARRGGBB</c> — so a plain hex string written before swatches were tokens still reads back as a custom colour.
/// </summary>
[JsonConverter(typeof(ColorSpecJsonConverter))]
public readonly record struct ColorSpec
{
    public const string SwatchPrefix = "swatch:";

    private ColorSpec(ColorSpecKind kind, string? swatchKey, Color color)
    {
        Kind      = kind;
        SwatchKey = swatchKey;
        Color     = color;
    }

    public ColorSpecKind Kind { get; }

    /// <summary>The full resource key (<c>Swatch.Blue</c>) when <see cref="Kind"/> is <see cref="ColorSpecKind.Swatch"/>.</summary>
    public string? SwatchKey { get; }

    /// <summary>The fixed colour when <see cref="Kind"/> is <see cref="ColorSpecKind.Custom"/>.</summary>
    public Color Color { get; }

    public bool IsDefault => Kind == ColorSpecKind.Default;

    public static ColorSpec Default => default;

    /// <summary>A swatch token, by its resource key (<c>Swatch.Blue</c>) or its short name (<c>Blue</c>).</summary>
    public static ColorSpec FromSwatch(string key)
        => new(ColorSpecKind.Swatch, key.StartsWith("Swatch.", StringComparison.Ordinal) ? key : "Swatch." + key, default);

    public static ColorSpec Custom(Color color) => new(ColorSpecKind.Custom, null, color);

    /// <summary>Reads a saved spec. Empty is the theme default; anything unreadable is too, so a hand-edited file
    /// degrades to the theme rather than failing to load.</summary>
    public static ColorSpec Parse(string? text) => TryParse(text, out var spec) ? spec : Default;

    public static bool TryParse(string? text, out ColorSpec spec)
    {
        spec = Default;
        if (string.IsNullOrWhiteSpace(text)) return true;

        text = text.Trim();
        if (text.StartsWith(SwatchPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = text[SwatchPrefix.Length..];
            if (name.Length == 0) return false;
            spec = FromSwatch(name);
            return true;
        }

        if (!text.StartsWith('#')) return false;
        try
        {
            if (ColorConverter.ConvertFromString(text) is not Color color) return false;
            spec = Custom(color);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public override string ToString() => Kind switch
    {
        ColorSpecKind.Swatch => SwatchPrefix + SwatchKey!["Swatch.".Length..],
        ColorSpecKind.Custom => Color.ToString(),
        _                    => string.Empty,
    };

    /// <summary>The brush to paint with, or null for the theme default — the caller's own style supplies that.</summary>
    public Brush? ToBrush() => Kind switch
    {
        ColorSpecKind.Swatch => SwatchPalette.Resolve(SwatchKey),
        ColorSpecKind.Custom => Frozen(new SolidColorBrush(Color)),
        _                    => null,
    };

    /// <summary>The colour this spec paints right now, or null for the theme default.</summary>
    public Color? Resolve() => Kind switch
    {
        ColorSpecKind.Swatch => SwatchPalette.Resolve(SwatchKey) is SolidColorBrush b ? b.Color : null,
        ColorSpecKind.Custom => Color,
        _                    => null,
    };

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>Writes a <see cref="ColorSpec"/> as its string, the theme default as null.</summary>
public sealed class ColorSpecJsonConverter : JsonConverter<ColorSpec>
{
    public override bool HandleNull => true;

    public override ColorSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? ColorSpec.Parse(reader.GetString()) : ColorSpec.Default;

    public override void Write(Utf8JsonWriter writer, ColorSpec value, JsonSerializerOptions options)
    {
        if (value.IsDefault) writer.WriteNullValue();
        else writer.WriteStringValue(value.ToString());
    }
}
