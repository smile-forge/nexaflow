using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// What a part means when it is not there.
///
/// <para>
/// A node rather than a value on the field, for the same reasons a <see cref="ValueSet"/> is one: it comes
/// from the specification and can say so, several parts can share one, and something reviewing a document
/// wants to ask what it assumes rather than read every field to find out. "No <c>Content-Type</c> means
/// <c>application/octet-stream</c>" is a sentence from a standard, not a convenience the engine invented.
/// </para>
///
/// <para>
/// <b>It is read-side only, and deliberately.</b> An absent part is assumed on the way in and written on
/// the way out by not writing it — which is the only pair of behaviours that is self-consistent. The two
/// crossings were considered and left out: a part that is optional and should still be emitted is a
/// caller's choice rather than a rule, and it already works, because a caller that supplies the value gets
/// it written. A part that is required and should be <i>dropped</i> when it equals its default is a real
/// rule, and it is not this one — it is a <b>canonicality</b> rule, the same law as
/// <see cref="Pattern.Varint.Minimal"/>: if both absent and explicitly-default decode to the same value
/// then value → octets is no longer injective and the round trip is already broken. When a protocol needs
/// it, it belongs next to the minimality checks and has to refuse on the way in, not merely omit on the
/// way out.
/// </para>
/// </summary>
public sealed class Default : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    /// <summary>What the part is taken to have said.</summary>
    public required ProtoValue Value { get; init; }

    /// <summary>Where the specification says so. Not decoration: an assumption a reader cannot trace is
    /// indistinguishable from a guess the engine made.</summary>
    public string Because { get; init; } = "";

    public override string ToString() => $"{Id} = {Value}";
}
