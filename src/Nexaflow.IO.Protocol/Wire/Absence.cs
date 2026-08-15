using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// What a part holds, where that decides what comparing two of them means.
///
/// <para>
/// A protocol's own type system, small and declared. A header name, a URI scheme and a DNS label are
/// case-insensitive strings; a SIP method, a MIME boundary and an ETag are not. Which one a part holds is
/// something the specification states and the octets cannot reveal, so the graph carries it.
/// </para>
/// </summary>
public abstract record Held
{
    /// <summary>Two of them are the same when their octets are.</summary>
    public static Held AsWritten { get; } = new Exact();

    /// <summary>Text whose casing is spelling rather than content: compared folded, carried as it
    /// arrived.</summary>
    public static Held Folding { get; } = new Caseless();

    /// <summary>Text that has exactly one legal spelling. Anything else is malformed, not an
    /// alternative.</summary>
    public static Held Cased(Casing only) => new OneCase(only);

    public sealed record Exact : Held;

    public sealed record Caseless : Held;

    /// <summary>
    /// One legal case, and it is not a preference.
    ///
    /// <para>
    /// The near-miss worth keeping separate. HTTP/1.1 folds field names and does not care how they are
    /// written; HTTP/2 requires them lower case and says an upper-case one <i>must be treated as
    /// malformed</i>. Those are opposite rules that look alike: the first is insensitive, and the second is
    /// sensitive with a constraint. Modelling the second as "insensitive, written lower" would accept a
    /// malformed message and quietly fix it, which is the outcome the specification exists to prevent.
    /// </para>
    ///
    /// <para>
    /// So this is a <b>canonicality</b> rule and behaves like the others — <see cref="Pattern.Varint"/>'s
    /// minimality, an absent part that may not also be written explicitly. One legal encoding of a value;
    /// anything else refused in <i>both</i> directions, because an engine that will not read something it
    /// would happily write has two opinions.
    /// </para>
    /// </summary>
    public sealed record OneCase(Casing Only) : Held;
}

/// <summary>The one case a value may be written in.</summary>
public enum Casing
{
    Lower,
    Upper,
}

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
/// <summary>What it means for a part to be missing.</summary>
public enum WhenAbsent
{
    /// <summary>Ordinary. The part was allowed to be missing and is taken to have said its default.</summary>
    Assumed,

    /// <summary>
    /// Malformed. The specification requires the part, so a message without it is not a short message —
    /// it is not this message.
    /// </summary>
    /// <remarks>
    /// Worth stating rather than leaving to the walk to notice. A required part that is simply not reached
    /// makes a decode stop early and report what it managed to bind, which is the failure that reads as
    /// success. Saying so here turns it into a refusal that names the part and cites where the rule comes
    /// from.
    /// </remarks>
    Malformed,
}

public sealed class Default : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    /// <summary>What the part is taken to have said.</summary>
    public required ProtoValue Value { get; init; }

    /// <summary>
    /// Whether the part is still written out when nothing else put it there.
    /// </summary>
    /// <remarks>
    /// Off by default, which is the self-consistent pair: an absent part is assumed coming in and written
    /// going out by not writing it. On is for the protocols that require the field to be present carrying
    /// its default — a reserved octet that must be zero rather than omitted — where leaving it out would
    /// produce a shorter message than the specification allows.
    /// </remarks>
    public bool Written { get; init; }

    /// <summary>
    /// Whether the part is left out when what it would hold is already the default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Real, and common: a protocol that says "omit the field when it is zero" is describing the shortest
    /// legal encoding, not an optimisation a writer may take or leave.
    /// </para>
    /// <para>
    /// <b>It is half a rule, and the other half is on the way in.</b> If a writer omits the default and a
    /// reader also accepts it written out, then two different octet strings mean the same value and
    /// value → octets is no longer injective — the round trip is broken for every message that takes the
    /// long form. So this also makes the long form <i>malformed</i> coming in, which is the same law as a
    /// varint that may not be padded. Saying only the first half is how a decoder ends up quietly
    /// accepting what its own encoder would never produce.
    /// </para>
    /// </remarks>
    public bool Omitted { get; init; }

    /// <summary>Whether being missing is allowed at all.</summary>
    public WhenAbsent Missing { get; init; } = WhenAbsent.Assumed;

    /// <summary>Where the specification says so. Not decoration: an assumption a reader cannot trace is
    /// indistinguishable from a guess the engine made.</summary>
    public string Because { get; init; } = "";

    public override string ToString() => $"{Id} = {Value}";
}
