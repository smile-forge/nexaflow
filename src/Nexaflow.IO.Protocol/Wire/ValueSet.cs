using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Whether the listed members are all the values that will ever exist.
/// </summary>
public enum Bounding
{
    /// <summary>These are all of them. A value outside is malformed, and a choice over this set can be
    /// proved exhaustive without a fallback.</summary>
    Closed,

    /// <summary>More exist, or will. A value outside is <b>not yet known to this document</b> — which is
    /// a different thing from illegal, and has to produce a different answer. A registry that a standards
    /// body keeps adding to is open however complete today's list looks.</summary>
    Open,
}

/// <summary>
/// Whether a value settles which concept is meant.
/// </summary>
public enum Exclusivity
{
    /// <summary>A value denotes one member and always that one, so meaning can be derived from the value
    /// alone and two occurrences of a value are two references to one thing.</summary>
    Definitive,

    /// <summary>A value points at a member without settling it — the same number means something else in
    /// another context, or is locally reassigned, or is one of several that fit. Meaning needs more than
    /// the value, and two occurrences of a value are not necessarily about the same thing.</summary>
    Indicative,
}

/// <summary>One value or run of them, and what it denotes.</summary>
/// <param name="Range">The value, or a run for a block assignment.</param>
/// <param name="Denotes">What it stands for, where the set names its members. Null for a range that is
/// merely legal — a reserved block is membership without denotation.</param>
public readonly record struct SetMember(ValueRange Range, string? Denotes = null)
{
    public static SetMember Of(long value, string denotes) => new(ValueRange.Exactly(value), denotes);

    public static SetMember Spanning(long low, long high, string? denotes = null)
        => new(ValueRange.Between(low, high), denotes);

    public override string ToString() => Denotes is null ? Range.ToString() : $"{Range} ({Denotes})";
}

/// <summary>
/// The values a field may carry, as a node other nodes point at.
///
/// <para>
/// Three independent questions, deliberately not collapsed into one. <b>Membership</b> is what is in it.
/// <b>Bounding</b> is whether that is the whole story. <b>Exclusivity</b> is whether a value settles which
/// member is meant. Every combination occurs, and each answer changes what the engine may do:
/// </para>
///
/// <list type="bullet">
/// <item>Closed decides whether an unlisted value is a <i>refusal</i> or an <i>unrecognised member</i>,
///   and whether a choice over the set needs a fallback at all.</item>
/// <item>Definitive decides whether meaning may be derived from the value — and therefore whether asking
///   for two structures to carry distinct values is a coherent thing to ask.</item>
/// </list>
///
/// <para>
/// The case that forces the split: IP protocol numbers are an <b>open</b>, <b>indicative</b> set. Open,
/// because the registry keeps growing and a number nobody has heard of is a number, not a malformed
/// packet. Indicative, because a number points at a protocol without settling it — the value is a hint
/// about content, not a definition of it. A model with only "the legal values are…" says neither, and
/// cannot tell "this packet is corrupt" from "this packet is newer than my table".
/// </para>
/// </summary>
public sealed class ValueSet : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    public required IReadOnlyList<SetMember> Members { get; init; }

    /// <summary>Required. There is no defensible default: guessing closed calls tomorrow's assignments
    /// malformed, and guessing open throws away every refusal a truly fixed set is there to make.</summary>
    public required Bounding Bounding { get; init; }

    /// <summary>Required, for the same reason. A registry with one meaning per number and a registry of
    /// hints look identical as a list of numbers and behave differently everywhere else.</summary>
    public required Exclusivity Exclusivity { get; init; }

    /// <summary>Why this set is what it is, carried into any refusal it causes.</summary>
    public string Because { get; init; } = "";

    public bool Admits(ProtoValue value) => Members.Any(m => m.Range.Admits(value));

    /// <summary>What a value stands for, where the set says and the value settles it. Null when the value
    /// is unlisted, when the member has no denotation, or when the set is
    /// <see cref="Exclusivity.Indicative"/> — in which case the value does not settle the question and
    /// answering anyway would be the engine inventing a meaning.</summary>
    public string? Denotation(ProtoValue value)
        => Exclusivity == Exclusivity.Indicative
            ? null
            : Members.FirstOrDefault(m => m.Range.Admits(value)).Denotes;

    public override string ToString()
        => $"{Id} ({Bounding.ToString().ToLowerInvariant()}, {Exclusivity.ToString().ToLowerInvariant()})";
}
