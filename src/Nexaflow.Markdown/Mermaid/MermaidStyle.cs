using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What a <c>style</c> line asks of whatever it styles — the words as written, a later line winning over an earlier one.
///
/// <para>
/// Mermaid writes a style the same way in every diagram that has one, so it is read once: the properties by
/// <see cref="MermaidLine.Properties"/>, and what they add up to here. What <c>#ff6b6b</c> or <c>red</c> comes to on the
/// page is the builder's.
/// </para>
/// </summary>
public sealed record MermaidStyle
{
    /// <summary>Nothing asked for.</summary>
    public static MermaidStyle None { get; } = new();

    public string? Fill { get; init; }

    /// <summary>The ink of what is written on it.</summary>
    public string? Colour { get; init; }

    public string? Stroke { get; init; }

    /// <summary>How thick its outline is, in pixels.</summary>
    public double? StrokeWidth { get; init; }

    /// <summary>How solid its fill is, from nought to one.</summary>
    public double? FillOpacity { get; init; }

    /// <summary>The dashes its outline is drawn with, as written — the lengths drawn and left, over and over.</summary>
    public string? Dashes { get; init; }

    /// <summary>
    /// This style over <paramref name="under"/>: what this one asks for wins, and what it says nothing about stays whatever was
    /// already asked — a <c>style</c> line over the classes a block is given, over the class every block starts from.
    /// </summary>
    public MermaidStyle Over(MermaidStyle under) => new()
    {
        Fill = Fill ?? under.Fill,
        Colour = Colour ?? under.Colour,
        Stroke = Stroke ?? under.Stroke,
        StrokeWidth = StrokeWidth ?? under.StrokeWidth,
        FillOpacity = FillOpacity ?? under.FillOpacity,
        Dashes = Dashes ?? under.Dashes,
    };

    /// <summary>The same style with one more property set — or unchanged, for one no style sets.</summary>
    public MermaidStyle With(string property, string value) => property.ToLowerInvariant() switch
    {
        "fill" => this with { Fill = value },
        "color" => this with { Colour = value },
        "stroke" => this with { Stroke = value },
        "stroke-width" => this with { StrokeWidth = MermaidNumber.Pixels(value) },
        "fill-opacity" when MermaidNumber.Read(value) is { } opacity => this with { FillOpacity = Math.Clamp(opacity, 0, 1) },
        "stroke-dasharray" => this with { Dashes = value },
        _ => this,
    };

    /// <summary>The same style with every property a style's <see cref="MermaidKinds.Properties"/> set, in order — those with nothing wrong with them.</summary>
    public MermaidStyle With(ContentPart? properties) => With(properties?.Node);

    /// <summary>This style with what some properties set laid over it, where what they set reads.</summary>
    public MermaidStyle With(ContentNode? properties)
    {
        var style = this;

        foreach (var property in properties?.Children.Where(child => child.Kind == MermaidKinds.Property) ?? [])
        {
            if (property.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Key) is { Trouble: null } name
                && property.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Setting) is { Trouble: null } value)
                style = style.With(name.Text, value.Text);
        }

        return style;
    }

    /// <summary>What is wrong with what a property is set to, where anything is. A colour is the builder's to make sense of.</summary>
    public static string? Trouble(string name, string value)
    {
        if (value.Length == 0)
            return $"{name} is set to nothing: {name}:{(name.StartsWith("fill-", StringComparison.OrdinalIgnoreCase) ? "0.5" : "#ff6b6b")}.";

        if (name.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase))
            return MermaidNumber.Read(value) is >= 0 and <= 1 ? null : "fill-opacity is a number from 0 to 1.";

        if (name.Equals("stroke-width", StringComparison.OrdinalIgnoreCase))
            return MermaidNumber.Pixels(value) is >= 0 ? null : "stroke-width is a number of pixels.";

        return null;
    }
}
