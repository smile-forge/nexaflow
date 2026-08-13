using Nexaflow.IO.Protocol.Expressions;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// One node of a value computation.
///
/// <para>
/// Structure is <b>not</b> in here. A term's operands are <see cref="Uses"/> edges, and what it denotes
/// about another node is a <see cref="Reads"/> edge. What it carries is only what it <i>is</i> — an
/// operator, a literal, a call by name — which is why <see cref="Shape"/> is an <see cref="Expr"/> whose
/// own children are never looked at. The string syntax stays because authoring a graph by hand would be
/// miserable; what changes is that parsing now produces nodes.
/// </para>
///
/// <para>
/// A term per <i>occurrence</i>, never shared between two sites that happen to have written the same
/// thing. Identity is the object here as everywhere else: the same text at two sites resolves its names in
/// two scopes and means two different things, and a shared node would have to answer for both.
/// </para>
///
/// <para>
/// This is a class rather than a record on purpose. A record would give two structurally identical terms
/// value equality, and the graph keys its adjacency by node — so <c>a + 1</c> written twice would collapse
/// into one node with both sets of edges hanging off it. That is the defect this model has deleted twice
/// already, and it would arrive here silently.
/// </para>
/// </summary>
public sealed class Term : Node
{
    /// <summary>What this term is. Its children are structure and live on edges instead.</summary>
    public required Expr Shape { get; init; }

    /// <summary>Where it sits, for diagnostics — the owning node and the path down to this operand.</summary>
    public required string Label { get; init; }

    public override string Name => Label;
}

/// <summary>
/// A node's value comes from a term.
///
/// <para>
/// The edge out of the field that starts the requirements path. Arrangement says what follows what and
/// this says how what is there gets its value; they are separate families and they meet on the field,
/// which is the only place either needs to know about the other.
/// </para>
///
/// <para>
/// It carries no role. A role was how one field's several expressions were told apart while they were
/// text — and the term root <i>is</i> that distinction now, so asking for "the value one" is asking for a
/// particular node, which is what the graph is for.
/// </para>
/// </summary>
public sealed record Computes : Edge
{
    public override string Verb => "is computed by";
}

/// <summary>A term takes another term as an operand.</summary>
public sealed record Uses : Edge
{
    /// <summary>Which operand. On the edge for the same reason packing order is: position is a property of
    /// this term using that one, not of either term.</summary>
    public required int Ordinal { get; init; }

    public override string Verb => $"uses[{Ordinal}]";
}
