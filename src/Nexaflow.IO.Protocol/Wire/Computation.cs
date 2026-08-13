using Nexaflow.IO.Protocol.Expressions;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>Where one of a computation's inputs comes from.</summary>
public enum Origin
{
    /// <summary>Another node of the message — a facet of a field or a run of bits.</summary>
    Part,

    /// <summary>A value from outside it.</summary>
    Outside,

    /// <summary>Whether a part that might not be there, is.</summary>
    Presence,

    /// <summary>A value the document states outright. Nothing has to answer it — it is already an answer,
    /// standing as a node so that an argument is something an edge can reach.</summary>
    Stated,
}

/// <summary>
/// One input a computation asks for, in argument order.
///
/// <para>
/// The node's <b>own statement</b> of what it needs, kept beside the edges that say where each one comes
/// from. That looks like saying it twice and is not: an input with no edge is an input nothing answers,
/// and having both is what makes that visible as a gap rather than something a checker has to re-derive by
/// reading the expression again. It is also what lets the number of inputs be a fact about the node, which
/// is what a converter needs in order to be called with its arguments in the right order.
/// </para>
/// </summary>
/// <param name="Name">What the document called it.</param>
/// <param name="Facet">Which fact about it is wanted, where it has more than one.</param>
public readonly record struct Need(string Name, string Facet, Origin From)
{
    public override string ToString()
        => From == Origin.Part ? $"{Name}.{Facet}" : $"{From.ToString().ToLowerInvariant()} {Name}";
}

/// <summary>
/// Something that takes inputs and produces one value.
///
/// <para>
/// Three kinds, and from the graph's point of view they behave identically: inputs arrive on incoming
/// edges in a declared order, one value comes out, and it is held on the node for the run. What differs is
/// only who is called — a converter from the closed set, the expression engine, or code behind an
/// interface — which is exactly the sort of difference that should be a subtype and not a flag.
/// </para>
/// </summary>
public abstract class Computation : Node
{
    /// <summary>What it needs, in the order the call wants them.</summary>
    public required IReadOnlyList<Need> Wants { get; init; }

    /// <summary>Where it sits, for diagnostics — the owning node and what this computes for it.</summary>
    public required string Label { get; init; }

    public override string Name => Label;
}

/// <summary>
/// An expression the AST engine evaluates.
///
/// <para>
/// One node per expression rather than one per sub-term. The finer version was built first and was wrong
/// about the model: an expression is a <i>thing that takes inputs and returns a value</i>, and mirroring
/// its interior into the graph made the graph carry arithmetic it has no use for. What the graph needs to
/// know is what comes in and what comes out.
/// </para>
/// </summary>
public sealed class Evaluated : Computation
{
    public required Expr Source { get; init; }
}

/// <summary>One member of the closed converter set, with its arguments as inputs.</summary>
public sealed class Converted : Computation
{
    public required Conversion Applies { get; init; }
}

/// <summary>
/// Code behind an interface, for the cases neither of the other two can state.
/// </summary>
/// <remarks>
/// Deliberately the same shape as the other two: it takes its inputs from edges like anything else, so it
/// cannot go and fetch what it wants. That restriction is the whole reason it is safe to have — custom
/// code that could read the message directly would be a second path to every value in it, answerable to
/// nothing.
/// </remarks>
public sealed class Coded : Computation
{
    public required string Implementation { get; init; }
}

/// <summary>
/// A value the document states, standing as a node so something can point at it.
///
/// <para>
/// A producer with no inputs. It exists because an argument to a computation has to be <i>somewhere</i> —
/// and an argument frozen into the declaration that names the converter is a value nothing can reach, so
/// two calls needing the same table each carry their own copy of it and no query can tell they are the
/// same. As a node it is pointed at.
/// </para>
/// </summary>
public sealed class Constant(Values.ProtoValue value, string label) : Node
{
    public Values.ProtoValue Value { get; } = value;

    public override string Name { get; } = label;
}

/// <summary>
/// A node's value comes from a computation.
///
/// <para>
/// The edge out of the field that starts the requirements path. Arrangement says what follows what and
/// this says how what is there gets its value; they are separate families and they meet on the field,
/// which is the only place either needs to know about the other.
/// </para>
/// </summary>
public sealed record Computes : Edge
{
    public override string Verb => "is computed by";
}

/// <summary>
/// A computation takes one of its inputs from here.
///
/// <para>
/// <paramref name="Sequence"/> is the argument position, and it is on the edge because that is what it is
/// about: which input this is, of this computation, from that node. The same node can feed two arguments
/// of one call, and two calls at different positions.
/// </para>
///
/// <para>
/// This is one edge where there were three — an operand, a read of another field, a draw on an outside
/// value. They were never three relationships; they were one relationship to three kinds of node, told
/// apart by which of them it happened to reach.
/// </para>
/// </summary>
public sealed record Requires : Edge
{
    public required int Sequence { get; init; }

    /// <summary>Which fact about the target is wanted. A computation has only its value; a field has
    /// several, and a length that means <c>extent</c> is a different edge from one that means
    /// <c>value</c>.</summary>
    public required string Facet { get; init; }

    public override string Verb => $"requires[{Sequence}] the {Facet} of";
}
