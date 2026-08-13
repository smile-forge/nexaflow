using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// One node of a run: which node of the protocol it is, which appearance of it, and what it came to.
///
/// <para>
/// The protocol graph says what a message is; this says what <i>this</i> message is. They are the same
/// shape deliberately — every run node stands for exactly one protocol node — so navigation is the
/// protocol's edges followed to the appearance you are standing in, and there is no second structure
/// describing the same relationships.
/// </para>
///
/// <para>
/// <b>A value lives here and nowhere else.</b> That is the whole point of the run graph existing rather
/// than values being threaded through calls in a scope: anything that links to a node is handed what that
/// node came to, by asking the node.
/// </para>
/// </summary>
public sealed class RunNode
{
    /// <summary>The protocol node this is an appearance of.</summary>
    public required Node Of { get; init; }

    /// <summary>The appearance that encloses this one — a chain's instance, an assortment's component.
    /// Null at the top, where there is exactly one of everything.</summary>
    public RunNode? Within { get; init; }

    /// <summary>Which appearance, where the enclosing structure repeats.</summary>
    public int Index { get; init; }

    private readonly Dictionary<Facet, object?> _settled = [];

    /// <summary>Whether a fact about this node has been worked out yet.</summary>
    public bool Has(Facet facet) => _settled.ContainsKey(facet);

    /// <summary>
    /// What this node came to.
    /// </summary>
    /// <exception cref="ProtoTypeException">
    /// If nothing has settled it. Deliberately an error rather than a null: a value read before it exists
    /// is the failure this whole model is arranged against, and it used to surface as a comparison quietly
    /// going false three layers away.
    /// </exception>
    public object? Settled(Facet facet)
        => _settled.TryGetValue(facet, out var value)
            ? value
            : throw new ProtoTypeException(
                  $"{this} has no {facet} yet. Something asked for it before whatever produces it ran, "
                + "which is a missing edge rather than a missing value.");

    /// <summary>What it came to, where that is a protocol value.</summary>
    public ProtoValue Value => Settled(Facet.Value) as ProtoValue ?? ProtoValue.Nothing;

    /// <summary>
    /// Records what this node came to. Once, because a node that settles twice has been computed twice
    /// and the two answers can differ — which is the back-patching this engine exists without.
    /// </summary>
    public void Settle(Facet facet, object? value)
    {
        if (!_settled.TryAdd(facet, value))
            throw new ProtoTypeException(
                $"{this} already has a {facet}. A node settles once; a second answer means two things "
              + "computed it, and nothing says which of them is right.");
    }

    public override string ToString()
        => Within is null ? Of.Name : $"{Within}/{Of.Name}" + (Index > 0 ? $"[{Index}]" : "");
}

/// <summary>
/// One message being built or read, as nodes that hold what they came to.
///
/// <para>
/// Made from a protocol graph, the values supplied from outside, and what earlier messages left behind.
/// After that, <b>the only two places anything may come from are the protocol graph and this</b> — a
/// value reached any other way is an ambient read wearing a parameter's clothes, and the reason the old
/// codec needed a scope object threaded through every call was that there was nowhere else to put one.
/// </para>
/// </summary>
public sealed class RunGraph
{
    private readonly Dictionary<(Node Of, RunNode? Within, int Index), RunNode> _nodes = [];

    private RunGraph(ProtocolGraph protocol) => Protocol = protocol;

    /// <summary>What is being spoken. Unchanged for the life of the run.</summary>
    public ProtocolGraph Protocol { get; }

    public IEnumerable<RunNode> Nodes => _nodes.Values;

    /// <summary>
    /// Starts a run: the protocol, plus everything known before a single octet is decided.
    /// </summary>
    /// <param name="supplied">Values from outside, by the key their <see cref="Context"/> declares.</param>
    /// <param name="kept">What earlier messages left behind, by slot name.</param>
    /// <remarks>
    /// Inputs and state arrive as <i>settled nodes</i> rather than as a dictionary the walk consults. That
    /// is not tidiness: it is what makes "this field draws on that input" an edge with a value at the end
    /// of it, the same shape as "this field reads that field's extent". One kind of question, one kind of
    /// answer, and no second lookup path that can disagree about what is available.
    /// </remarks>
    public static RunGraph Begin(ProtocolGraph protocol,
                                 IReadOnlyDictionary<string, ProtoValue>? supplied = null,
                                 IReadOnlyDictionary<string, ProtoValue>? kept = null)
    {
        var run = new RunGraph(protocol);

        foreach (var outside in protocol.Nodes.OfType<Context>())
        {
            var node = run.For(outside);

            if (supplied?.TryGetValue(outside.Key, out var given) == true)
                node.Settle(Facet.Value, given);

            // Anything else stays unsettled on purpose. A document that reads an input nobody supplied
            // should say so where the read is, naming the input — not evaluate it as nothing.
        }

        foreach (var (slot, value) in kept ?? new Dictionary<string, ProtoValue>())
            run.Remembered(slot).Settle(Facet.Value, value);

        return run;
    }

    /// <summary>
    /// The appearance of a protocol node inside a given enclosing appearance, made if it is not there yet.
    /// </summary>
    public RunNode For(Node of, RunNode? within = null, int index = 0)
        => _nodes.TryGetValue((of, within, index), out var found)
            ? found
            : _nodes[(of, within, index)] = new RunNode { Of = of, Within = within, Index = index };

    /// <summary>Whether an appearance has been made yet, without making one.</summary>
    public RunNode? Existing(Node of, RunNode? within = null, int index = 0)
        => _nodes.GetValueOrDefault((of, within, index));

    /// <summary>
    /// Which appearance of a node is meant, standing here.
    /// </summary>
    /// <remarks>
    /// Innermost outward, so a structure's own length prefix is its own and the message metadata around it
    /// is still reachable. This is the one piece of naming logic the run graph has, and it is about
    /// appearances rather than names: two instances of a chain are two nodes, and which one an edge means
    /// depends on where the edge is being followed from.
    /// </remarks>
    public RunNode Reach(RunNode from, Node target)
    {
        // Each enclosing appearance in turn, asking only for the parts that belong to it. Falling off the
        // end lands on the message's own, which is the one there is exactly one of.
        for (var level = from; level is not null; level = level.Within)
            if (Existing(target, level) is { } own) return own;

        return For(target);
    }

    /// <summary>
    /// What this run knows about a slot earlier messages wrote.
    /// </summary>
    /// <remarks>
    /// Keyed by name because a state slot belongs to a subject rather than to a message, so the protocol
    /// graph of the message being built does not contain it. The alternative — reaching into a
    /// <c>Standing</c> from inside the walk — is exactly the outside read this graph exists to remove.
    /// </remarks>
    public RunNode Remembered(string slot)
        => For(_slots.TryGetValue(slot, out var known) ? known : _slots[slot] = new Slot(slot));

    private readonly Dictionary<string, Slot> _slots = [];

    private sealed class Slot(string name) : Node
    {
        public override string Name { get; } = name;
    }

    public override string ToString()
        => $"{_nodes.Count} appearances of {Protocol.Nodes.Count} nodes, "
         + $"{_nodes.Values.Count(n => n.Has(Facet.Value))} settled";
}
