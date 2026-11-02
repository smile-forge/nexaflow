using System.Collections.Immutable;

namespace Nexaflow.IO.Common.OpenRaster;

/// <summary>An attribute a reader did not understand, as written: its namespace (empty for none), local name and value.</summary>
public readonly record struct OpenRasterAttribute(string Namespace, string LocalName, string Value);

/// <summary>
/// What a reader of an OpenRaster stack did not understand on one element — attributes and child elements — kept as written
/// so a save writes them back.
/// </summary>
public sealed record OpenRasterKept
{
    public static OpenRasterKept None { get; } = new();

    /// <remarks>Never default: none kept is an empty list.</remarks>
    public ImmutableArray<OpenRasterAttribute> Attributes
    {
        get => _attributes.IsDefault ? [] : _attributes;
        init => _attributes = value;
    }

    private readonly ImmutableArray<OpenRasterAttribute> _attributes = [];

    /// <summary>Child elements, each as its outer XML.</summary>
    /// <remarks>Never default: none kept is an empty list.</remarks>
    public ImmutableArray<string> Elements
    {
        get => _elements.IsDefault ? [] : _elements;
        init => _elements = value;
    }

    private readonly ImmutableArray<string> _elements = [];

    public bool IsEmpty => Attributes.IsEmpty && Elements.IsEmpty;

    public bool Equals(OpenRasterKept? other)
        => other is not null && Attributes.AsSpan().SequenceEqual(other.Attributes.AsSpan()) && Elements.AsSpan().SequenceEqual(other.Elements.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Attributes.Length, Elements.Length);
}
