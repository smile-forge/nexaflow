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

/// <summary>
/// A container: a header, a body, a pseudo-header. The matching concept to the ones specifications name.
///
/// <para>
/// <b>It produces nothing.</b> That is the whole difference from a field, and it is not a technicality: a
/// field makes octets, and a set only <i>spans</i> the octets its members made. Its extent is a fact about
/// them rather than something it computed, which is exactly why a length can measure a header while the
/// header itself writes nothing at all.
/// </para>
///
/// <para>
/// Modelling a container as a field instead — which is what the engine did, having nothing else to offer —
/// gives every header, sequence and pseudo-header a value and an emission it has no business having, and
/// leaves a document with fields that produce nothing yet answer when asked. The documents in the corpus
/// inherited that from the engine rather than choosing it.
/// </para>
/// </summary>
public sealed class FieldSet(string name, Field? derived = null) : Node
{
    public override string Name { get; } = name;

    /// <summary>
    /// The <c>Pattern.Group</c> field this was made from, while there still is one.
    /// </summary>
    /// <remarks>
    /// Transitional and only that. The codec still walks containment and still wants the field, so the
    /// arrangement names it here rather than the two structures silently disagreeing about which node a
    /// header is. It goes when a document can declare a set outright and <c>Pattern.Group</c> is deleted.
    /// </remarks>
    public Field? Derived { get; } = derived;
}

// There is no edge for "a message offers this arrangement", and none for "an arrangement begins here".
// Both were `Then` wearing another name: the message is a node whose ways on are its arrangements, keyed
// and decided exactly as an alternation's are, and an arrangement is a node whose one way on is the first
// thing in it. Naming them separately made packing selection look like a different mechanism from arm
// selection, and it is the same one at a different scale.

/// <summary>
/// What comes after this, and under what.
///
/// <para>
/// A single unkeyed edge is "and then"; two or more leaving one node are an alternation, and which is
/// taken is decided by the <see cref="Decides"/> node that keys them.
/// </para>
///
/// <para>
/// <b>It never reaches back.</b> A repetition was a back edge for one round of this design and that was
/// wrong: what repeats is inside a span, not along the path, and a cycle here put a containment fact into
/// the arrangement — the walk revisited nodes and "what follows what" stopped being answerable by reading
/// it. A repeated span is one place on the path, however many components turn up in it, and the
/// unrolling happens in the run.
/// </para>
///
/// <para>
/// There is deliberately <b>no sequence number</b>. A node has one successor, or several told apart by
/// their key.
/// </para>
/// </summary>
/// <para>
/// It runs the whole way down: a message's ways on are its arrangements, an arrangement's one way on is
/// the first thing in it, and a field's is whatever follows. One edge, four scales, and the fork
/// mechanism reaches all of them without knowing which it is looking at.
/// </para>
/// </summary>
/// <param name="Key">The value of the deciding node that picks this way on, or null where there is nothing
/// to decide — and null also for the way on that is taken when nothing else matches.</param>
/// <remarks>
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

/// <summary>
/// What may turn up in this span.
///
/// <para>
/// A run of elements is <b>one place</b> on the path holding a list, not a loop. What repeats is inside
/// the field's own reading of itself: it takes the octets it was given and matches each element against
/// the definitions this edge points at, until they run out. So there is no way on that reaches back, no
/// cardinality in the arrangement, and nothing about how many there are anywhere in the description —
/// which is right, because how many there are is a fact about a message and not about a protocol.
/// </para>
///
/// <para>
/// It is also the validation the model was missing. A definition sitting off the path can be pointed at
/// from anywhere, and without this nothing said <i>where</i> it was allowed to appear.
/// </para>
/// </summary>
public sealed record Allowed : Edge
{
    public override string Verb => "may contain";
}

/// <summary>A group's members, in order.</summary>
public sealed record Holds : Edge
{
    /// <summary>Where this one comes among them. Genuinely ordinal, unlike a way on.</summary>
    public required int Order { get; init; }

    public override string Verb => $"holds[{Order}]";
}
