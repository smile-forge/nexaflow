using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// One arrangement of a message — what follows what.
///
/// <para>
/// A message has one or more, and which applies is a decision like any other. That is the whole reason
/// this is a node rather than the field list being the arrangement: an arrangement the state chooses
/// cannot be a property of the message, because then the message would have two.
/// </para>
/// </summary>
public sealed class Packing(string name) : Node
{
    public override string Name { get; } = name;
}

// There is deliberately no separate FieldSet node. A group is expanded by having `Holds` edges, and
// today every group also occupies octets — it is a field. Making a second node kind for the same notion
// would mean two ways to be a group, and the walk would have to know both. A set that is *not* a field
// arrives when a packing is authored directly rather than derived from a field list; it will be a node
// with `Holds` edges and no pattern, and the walk already handles it.

/// <summary>A message offers an arrangement.</summary>
public sealed record Packs : Edge
{
    public override string Verb => "can be packed as";
}

/// <summary>Where an arrangement begins.</summary>
public sealed record Starts : Edge
{
    public override string Verb => "starts at";
}

/// <summary>
/// What comes after this, and under what.
///
/// <para>
/// <b>One edge doing three jobs, which is the point of it.</b> A single unkeyed edge is "and then". Two or
/// more leaving one node are an alternation, and which is taken is decided by the
/// <see cref="Decides"/> node that keys them. An edge reaching back to somewhere the walk has already been
/// is a repetition. Sequence, choice and repeat stop being three shapes with three sets of rules and
/// become one relationship read three ways.
/// </para>
///
/// <para>
/// There is deliberately <b>no sequence number</b>. A node has one successor, or several told apart by
/// their key; numbering them would only matter if they fanned out from the arrangement as a flat list,
/// and in that shape a repetition cannot be expressed at all.
/// </para>
/// </summary>
/// <param name="Key">The value of the deciding node that picks this way on, or null where there is nothing
/// to decide — and null also for the way on that is taken when nothing else matches.</param>
public sealed record Then : Edge
{
    public ProtoValue? Key { get; init; }

    /// <summary>Whether this is the way on when no key matched. Distinct from carrying no key at all,
    /// which is what an unbranched step does.</summary>
    public bool Otherwise { get; init; }

    public override string Verb
        => Otherwise ? "failing everything else, then" : Key is null ? "then" : $"on {Key}, then";
}

/// <summary>
/// What decides which way on is taken.
///
/// <para>
/// Points at a computation, so the decision takes its inputs from edges and is scheduled like anything
/// else. A <i>value</i> that keys the branches rather than a condition per branch: sibling conditions
/// cannot be checked for cover or overlap by anything, which is how a message with an unanticipated
/// discriminator binds no fields and reports nothing. Keyed ways on can be proved exhaustive, and are.
/// </para>
/// </summary>
public sealed record Decides : Edge
{
    /// <summary>The reader's question, where the two directions ask different ones. Same asymmetry as a
    /// recovered length: on the way in you look at the octets, on the way out at what you were given.</summary>
    public bool Reading { get; init; }

    public override string Verb => Reading ? "on the way in, decided by" : "decided by";
}

/// <summary>A group's members, in order.</summary>
public sealed record Holds : Edge
{
    /// <summary>Where this one comes among them. Genuinely ordinal, unlike a way on.</summary>
    public required int Order { get; init; }

    public override string Verb => $"holds[{Order}]";
}
