using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>A point in oriented-source pixels.</summary>
public readonly record struct MarkupPoint(double X, double Y)
{
    public MarkupPoint Offset(double dx, double dy) => new(X + dx, Y + dy);
}

/// <summary>
/// One annotation drawn over the photo. Positions are oriented-source pixels, so an annotation stays on what it
/// points at when the crop changes, and turns with the picture when it is rotated or flipped.
/// </summary>
/// <remarks>Colours are 32-bit ARGB. Sizes are source pixels, so an annotation keeps its weight at any zoom.</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MarkupArrow), "arrow")]
[JsonDerivedType(typeof(MarkupBox), "box")]
[JsonDerivedType(typeof(MarkupText), "text")]
[JsonDerivedType(typeof(MarkupStroke), "stroke")]
public abstract record MarkupElement
{
    /// <summary>Identity across edits, so a selection survives undo and redo.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public uint Color { get; init; } = 0xFFE53935;

    /// <summary>Line weight in source pixels (the text size is its own).</summary>
    public double StrokeWidth { get; init; } = 6;

    /// <summary>The same element moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public abstract MarkupElement Offset(double dx, double dy);
}

/// <summary>A straight arrow, its head at <see cref="To"/>.</summary>
public sealed record MarkupArrow : MarkupElement
{
    public MarkupPoint From { get; init; }
    public MarkupPoint To { get; init; }

    public override MarkupElement Offset(double dx, double dy) => this with { From = From.Offset(dx, dy), To = To.Offset(dx, dy) };
}

/// <summary>A rectangle outline, with an optional fill.</summary>
public sealed record MarkupBox : MarkupElement
{
    public MarkupPoint Corner { get; init; }
    public MarkupPoint OppositeCorner { get; init; }

    /// <summary>ARGB fill; 0 leaves the inside clear.</summary>
    public uint Fill { get; init; }

    public override MarkupElement Offset(double dx, double dy) =>
        this with { Corner = Corner.Offset(dx, dy), OppositeCorner = OppositeCorner.Offset(dx, dy) };
}

/// <summary>A line or more of text whose top-left is <see cref="Anchor"/>, turned about it by <see cref="RotationDegrees"/>.</summary>
public sealed record MarkupText : MarkupElement
{
    public MarkupPoint Anchor { get; init; }
    public string Text { get; init; } = string.Empty;
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 48;

    /// <summary>Clockwise, so text placed on a turned picture still reads upright where it was typed.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>ARGB plate behind the text; 0 draws none.</summary>
    public uint Backing { get; init; }

    public override MarkupElement Offset(double dx, double dy) => this with { Anchor = Anchor.Offset(dx, dy) };
}

/// <summary>How a freehand stroke is laid down.</summary>
public enum StrokeStyle
{
    /// <summary>Opaque ink.</summary>
    Pen,

    /// <summary>Wide, translucent and multiplied, so the picture shows through and overlaps never darken twice.</summary>
    Highlighter,
}

/// <summary>A freehand line through <see cref="Points"/>, smoothed when drawn.</summary>
public sealed record MarkupStroke : MarkupElement
{
    public ImmutableArray<MarkupPoint> Points { get; init; } = [];
    public StrokeStyle Style { get; init; } = StrokeStyle.Pen;

    public override MarkupElement Offset(double dx, double dy) =>
        this with { Points = [.. Points.Select(p => p.Offset(dx, dy))] };

    public bool Equals(MarkupStroke? other) =>
        other is not null
        && base.Equals(other)
        && Style == other.Style
        && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Style, Points.Length);
}
