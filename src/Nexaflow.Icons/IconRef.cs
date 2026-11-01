using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Icons;

/// <summary>Where an icon's glyph comes from: the emoji the system font draws, one face of Microsoft's Fluent UI
/// System Icons (<see cref="FluentGlyphs"/>), or an icon pack shipped with the app, drawn as outlines (<see cref="IconPacks"/>).</summary>
public enum IconSet { Emoji, FluentRegular, FluentFilled, Iconify }

/// <summary>
/// An icon, by the set that draws it and its stable name in that set: the emoji itself, or a Fluent icon's base name
/// (<c>arrow_clockwise</c>). The Fluent font's code points move between upstream releases, so a name is what is kept
/// and <see cref="FluentGlyphs"/> turns it into a glyph each time.
/// <para>
/// It is saved as its <see cref="ToString"/>: an emoji bare, a Fluent icon as <c>fluent:name</c> or
/// <c>fluent-filled:name</c>, and a pack's icon as <c>iconify:</c> before the <c>pack:name</c> Iconify writes it as. A bare
/// string is therefore always an emoji, which is what every icon saved before the other sets existed was.
/// </para>
/// </summary>
[JsonConverter(typeof(IconRefJsonConverter))]
[TypeConverter(typeof(IconRefTypeConverter))]
public readonly record struct IconRef(IconSet Set, string Value)
{
    public const string FluentPrefix = "fluent:";
    public const string FluentFilledPrefix = "fluent-filled:";
    public const string IconifyPrefix = "iconify:";

    public static IconRef Emoji(string glyph) => new(IconSet.Emoji, glyph);

    public static IconRef Fluent(string name, bool filled = false)
        => new(filled ? IconSet.FluentFilled : IconSet.FluentRegular, name);

    /// <summary>The icon <paramref name="name"/> of the pack Iconify calls <paramref name="pack"/> — <c>material-icon-theme</c>,
    /// <c>rust</c> — kept as Iconify writes it, <c>pack:name</c>.</summary>
    public static IconRef Iconify(string pack, string name) => new(IconSet.Iconify, pack + ":" + name);

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool IsFluent => Set is IconSet.FluentRegular or IconSet.FluentFilled;

    public bool IsIconify => Set == IconSet.Iconify;

    public static IconRef Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        if (text.StartsWith(FluentFilledPrefix, StringComparison.Ordinal)) return Fluent(text[FluentFilledPrefix.Length..], filled: true);
        if (text.StartsWith(FluentPrefix, StringComparison.Ordinal)) return Fluent(text[FluentPrefix.Length..]);
        if (text.StartsWith(IconifyPrefix, StringComparison.Ordinal)) return new(IconSet.Iconify, text[IconifyPrefix.Length..]);
        return Emoji(text);
    }

    public override string ToString() => Set switch
    {
        IconSet.FluentRegular => FluentPrefix + Value,
        IconSet.FluentFilled  => FluentFilledPrefix + Value,
        IconSet.Iconify       => IconifyPrefix + Value,
        _                     => Value ?? string.Empty,
    };
}

/// <summary>Writes an <see cref="IconRef"/> as its string, so an emoji saved before the Fluent sets reads back as one.</summary>
public sealed class IconRefJsonConverter : JsonConverter<IconRef>
{
    public override IconRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? IconRef.Parse(reader.GetString()) : default;

    public override void Write(Utf8JsonWriter writer, IconRef value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>Reads an <see cref="IconRef"/> from its string, so markup can name one as it is saved:
/// <c>Icon="fluent:arrow_up"</c>.</summary>
public sealed class IconRefTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string text ? IconRef.Parse(text) : base.ConvertFrom(context, culture, value);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is IconRef icon ? icon.ToString() : base.ConvertTo(context, culture, value, destinationType);
}
