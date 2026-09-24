using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Visuals.Icons;

/// <summary>Where an icon's glyph comes from: the emoji the system font draws, or one face of Microsoft's Fluent UI
/// System Icons, bundled with this assembly.</summary>
public enum IconSet { Emoji, FluentRegular, FluentFilled }

/// <summary>
/// An icon, by the set that draws it and its stable name in that set: the emoji itself, or a Fluent icon's base name
/// (<c>arrow_clockwise</c>). The Fluent font's code points move between upstream releases, so a name is what is kept
/// and <see cref="IconCatalog"/> turns it into a glyph each time.
/// <para>
/// It is saved as its <see cref="ToString"/>: an emoji bare, a Fluent icon as <c>fluent:name</c> or
/// <c>fluent-filled:name</c>. A bare string is therefore always an emoji, which is what every icon saved before the
/// Fluent sets existed was.
/// </para>
/// </summary>
[JsonConverter(typeof(IconRefJsonConverter))]
public readonly record struct IconRef(IconSet Set, string Value)
{
    public const string FluentPrefix = "fluent:";
    public const string FluentFilledPrefix = "fluent-filled:";

    public static IconRef Emoji(string glyph) => new(IconSet.Emoji, glyph);

    public static IconRef Fluent(string name, bool filled = false)
        => new(filled ? IconSet.FluentFilled : IconSet.FluentRegular, name);

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool IsFluent => Set is IconSet.FluentRegular or IconSet.FluentFilled;

    public static IconRef Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        if (text.StartsWith(FluentFilledPrefix, StringComparison.Ordinal)) return Fluent(text[FluentFilledPrefix.Length..], filled: true);
        if (text.StartsWith(FluentPrefix, StringComparison.Ordinal)) return Fluent(text[FluentPrefix.Length..]);
        return Emoji(text);
    }

    public override string ToString() => Set switch
    {
        IconSet.FluentRegular => FluentPrefix + Value,
        IconSet.FluentFilled  => FluentFilledPrefix + Value,
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
