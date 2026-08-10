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
