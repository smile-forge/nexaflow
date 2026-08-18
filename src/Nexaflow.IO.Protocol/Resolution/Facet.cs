namespace Nexaflow.IO.Protocol.Resolution;

/// <summary>
/// An independently-settleable fact about a node.
///
/// <para>
/// These are deliberately <b>not</b> a fixed chain. The first resolver design ordered them
/// <c>Sized → Positioned → Valued → Emitted</c> and every one of ten stress protocols failed on pass one,
/// because for self-delimiting, minimal-width, escaping and arm-bearing shapes <b>extent is a function of
/// value</b>, not a precondition for it. Each pattern now declares its own dependencies between facets and
/// the engine stops owning the order.
/// </para>
/// </summary>
public enum Facet
{
    /// <summary>
    /// The node exists at all.
    ///
    /// <para>
    /// Repetition elements, choice branches and optional instances must be <i>materialised</i> before they
    /// can even be unresolved, and its absence was the worst defect in the first design: the terminal
    /// condition "every node emitted" is <b>vacuously satisfied by a node set that is too small</b>. A
    /// large repeated structure would emit one short, structurally valid, wrong message and report success.
    /// A cycle was detected; under-expansion was not.
    /// </para>
    /// </summary>
    Realised,

    /// <summary>Whether the node contributes octets at all. A length covering an optional body cannot be
    /// sized until presence resolves, and there was previously nowhere to say so.</summary>
    Present,

    /// <summary>How many octets it occupies. For many shapes this depends on <see cref="Value"/>.</summary>
    Extent,

    /// <summary>What it encodes to.</summary>
    Value,

    /// <summary>
    /// Where it starts, in octets from the beginning of the message.
    ///
    /// <para>
    /// A facet rather than a sweep, because a value can depend on one: a pointer carries the offset of the
    /// thing it points at, so its value cannot settle until that thing's position has. Doing it as a pass
    /// after everything else means a pointer's value has to be back-patched, which is the shape this
    /// resolver exists to avoid.
    /// </para>
    ///
    /// <para>
    /// It needs no fixed point for the reason an extent does not: position is the previous sibling's
    /// position plus its extent, and a pointer's own extent is fixed by its declaration whatever value it
    /// ends up holding. So a pointer that precedes its target still resolves — the chain of positions never
    /// passes through the pointer's value.
    /// </para>
    /// </summary>
    Position,

    /// <summary>Committed to the output. Terminal.</summary>
    Emitted,
}

/// <summary>
/// One fact about one node — the unit the resolver schedules.
///
/// <para>
/// <paramref name="Node"/> is deliberately untyped, and the resolver never looks inside it. That is what
/// keeps this a general worklist, exercisable with nothing but synthetic nodes; it also means <b>identity
/// is whatever object the caller says it is</b>. It used to be a string, and the caller ended up building
/// them by concatenation — <c>options[0].deltaOne</c> — which is a dictionary key impersonating a node.
/// </para>
/// </summary>
public readonly record struct FacetRef(object Node, Facet Facet)
{
    public override string ToString() => $"{Node}.{Facet}";
}
