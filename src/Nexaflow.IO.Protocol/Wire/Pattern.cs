using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>One named run of bits inside a <see cref="Pattern.Bits"/> group.</summary>
/// <param name="Name">Capture name for this run.</param>
/// <param name="Width">Bits, most-significant first within the group.</param>
public readonly record struct BitSlice(string Name, int Width);

/// <summary>
/// A wire shape. These are <b>notions</b> — a fixed-width number, a run of bits, an opaque span — and
/// never a protocol's mechanism. Anything that can only be described by naming a protocol is not a pattern
/// and belongs in a document, as a composition of these plus a transform.
/// </summary>
public abstract record Pattern
{
    /// <summary>A fixed-width integer.</summary>
    /// <param name="Octets">Width in octets, 1..8.</param>
    /// <param name="BigEndian">Byte order. Never defaulted at the document level — which order is correct
    /// is a property of the protocol.</param>
    /// <param name="Signed">Two's-complement when true.</param>
    public sealed record Scalar(int Octets, bool BigEndian, bool Signed = false) : Pattern;

    /// <summary>
    /// Named bit runs packed most-significant-first. The widths must total a whole number of octets — a
    /// group that does not is a document error rather than something silently padded, because an
    /// accidentally byte-misaligned field reads plausible values from the wrong place.
    /// </summary>
    public sealed record Bits(IReadOnlyList<BitSlice> Slices) : Pattern
    {
        public int TotalBits => Slices.Sum(s => s.Width);
    }

    /// <summary>A span of octets carried without interpretation.</summary>
    public sealed record Opaque(int Octets) : Pattern;

    /// <summary>Octets this pattern occupies, where that is fixed. Null means it depends on the value —
    /// which is exactly the case the resolver's facet ordering exists to handle.</summary>
    public int? StaticWidth => this switch
    {
        Scalar s => s.Octets,
        Bits b => b.TotalBits / 8,
        Opaque o => o.Octets,
        _ => null,
    };

    /// <summary>Document-time checks. Cheap, and they catch the errors that are otherwise invisible until
    /// a capture decodes into plausible nonsense.</summary>
    public IReadOnlyList<string> Validate(string fieldId) => this switch
    {
        Scalar s when s.Octets is < 1 or > 8 =>
            [$"field '{fieldId}': a scalar must be 1..8 octets, got {s.Octets}"],

        Bits b when b.Slices.Count == 0 =>
            [$"field '{fieldId}': a bit group needs at least one slice"],

        Bits b when b.TotalBits % 8 != 0 =>
            [$"field '{fieldId}': bit slices total {b.TotalBits} bits, which is not a whole number of "
           + "octets. A misaligned group reads plausible values from the wrong place, so this is an error "
           + "rather than something padded silently."],

        Bits b when b.Slices.Any(s => s.Width is < 1 or > 32) =>
            [$"field '{fieldId}': each bit slice must be 1..32 bits wide"],

        Opaque o when o.Octets < 0 => [$"field '{fieldId}': an opaque span cannot be negative"],

        _ => [],
    };
}
