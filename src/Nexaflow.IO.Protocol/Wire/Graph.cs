namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Something the model can point at.
///
/// <para>
/// Identity is the object, not a name. That distinction is the whole reason this exists: a model whose
/// relationships are stored as names has to resolve them somewhere, and wherever it resolves them is a
/// scope, and a scope is a fence. Constraints ended up fenced out of everything inside a repetition —
/// not because anyone decided they should be, but because a name has to be looked up and the lookup only
/// knew one place to look.
/// </para>
///
/// <para>
/// Names survive for expressions and diagnostics, where a human is reading. They are not how anything
/// finds anything.
/// </para>
/// </summary>
public abstract class Node
{
    public abstract string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// A typed, directed relationship.
///
/// <para>
/// Each kind is separate for the same reason the kinds of illegal are: the relationship is the meaning.
/// "This segment measures that region" and "this choice reads that segment" are both a reference from one
/// node to another, and collapsing them into one would leave the engine unable to say which it was
/// looking at — which is exactly what an expression holding <c>fields.body.extent</c> did.
/// </para>
/// </summary>
public abstract record Edge
{
    public required Node From { get; init; }
    public required Node To { get; init; }

    public abstract string Verb { get; }

    public override string ToString() => $"{From} —{Verb}→ {To}";
}

/// <summary>
/// Ordered containment: the packing relation.
///
/// <para>
/// An edge rather than list position, because order is a thing a constraint needs to be able to point at.
/// "This may not follow that when X holds" has no home in an array index.
/// </para>
/// </summary>
public sealed record Contains : Edge
{
    public required int Ordinal { get; init; }
    public override string Verb => $"contains[{Ordinal}]";
}

/// <summary>
/// A segment carries the extent of another node.
///
/// <para>
/// <b>One edge, both directions.</b> A length field measures a region on the way out and bounds it on the
/// way in — the same relationship read from either end. It used to be two unrelated expressions sitting on
/// two different nodes with nothing connecting them, which is why nothing could check that a region's
/// declared bound and the field that wrote it were talking about each other.
/// </para>
/// </summary>
public sealed record Measures : Edge
{
    public override string Verb => "measures";
}

/// <summary>A choice reads a segment to decide which packing applies.</summary>
public sealed record Discriminates : Edge
{
    public override string Verb => "discriminates on";
}

/// <summary>
/// A choice offers a packing, under the discriminator value that selects it.
///
/// <para>
/// On the edge rather than on the arm, for the same reason ordering is: <i>which value selects this
/// packing</i> is a fact about this choice offering it, not about the packing. An arm knows what shape it
/// is; it does not know what number some other field has to hold for it to be the one. Reading it off the
/// edge also puts every question about selection in one place — and it is where a state-scoped offer will
/// have to hang when a packing becomes legal only while the conversation is in some state.
/// </para>
/// </summary>
/// <param name="Key">The value selecting this arm, or null for the fallback.</param>
public sealed record Offers : Edge
{
    public Values.ProtoValue? Key { get; init; }

    /// <summary>Whether this packing may appear more than once where it is offered — and therefore whether
    /// anything may name its fields from outside. One of something is addressable; several are not.</summary>
    public bool Repeats { get; init; }

    public bool IsFallback => Key is null;

    public override string Verb => Key is null ? "offers, failing over to"
                                 : Repeats ? $"offers, on {Key}, any number of "
                                 : $"offers, on {Key}, ";
}

/// <summary>A chain repeats a structure.</summary>
public sealed record Repeats : Edge
{
    public override string Verb => "repeats";
}

/// <summary>A concept names the part of the message that is it. A lookup and nothing else — the edge
/// carries no computation, so it cannot become a second opinion about the shape.</summary>
public sealed record Names : Edge
{
    public override string Verb => "names";
}

/// <summary>
/// A field says that what it stands for continues at another node.
///
/// <para>
/// The <b>intent</b>, and the only thing a document declares. Where that node lands in the octets is not a
/// property of it — it falls out of walking the containment order and summing extents, and it exists only
/// because a wire is a byte sequence. So the document points at the node and the engine renders the
/// offset, the same division as between an object identifier's bits and what they denote.
/// </para>
///
/// <para>
/// Naming the target instead — <c>fields.thatName.position</c> — would be the defect this model has
/// deleted twice: a name cannot say <i>which</i> appearance it means, so inside a repeated structure it is
/// a dictionary key wearing a node's clothes.
/// </para>
/// </summary>
public sealed record Locates : Edge
{
    public override string Verb => "continues at";
}

/// <summary>A span carries another protocol.</summary>
public sealed record Embeds : Edge
{
    public override string Verb => "carries";
}

/// <summary>A carrier resolves to a described message. Absent when it resolves to an implementation the
/// host provides, which is the point of the carrier being a node in between.</summary>
public sealed record Speaks : Edge
{
    public override string Verb => "speaks";
}

/// <summary>
/// A field takes its values from a set.
///
/// <para>
/// An edge rather than a list on the field, because the set is a thing in its own right: several fields
/// draw from one registry, a rule needs to ask whether that registry settles meaning, and a choice needs
/// to ask whether it is closed. A copy of the values inlined at each site answers none of those and drifts
/// between them.
/// </para>
/// </summary>
public sealed record Admits : Edge
{
    /// <summary>A named run inside the field, when the set governs one rather than the whole group.</summary>
    public string? Run { get; init; }

    public override string Verb => Run is null ? "admits" : $"admits, in {Run},";
}

/// <summary>
/// One node reads a facet of another — the dependency the resolver schedules on.
///
/// <para>
/// Derived from an expression, but <i>materialised</i>: it exists in the graph rather than being recovered
/// by scanning expression text at every encode. That is what makes a reference into an arm that was not
/// taken something a document check can see, instead of a run-time surprise.
/// </para>
/// </summary>
public sealed record Reads : Edge
{
    public required string Facet { get; init; }

    /// <summary>Which of the reader's expressions this read belongs to.
    ///
    /// <para>
    /// A field can hold several — a value and a discriminator, a value and a continuation — and they are
    /// not interchangeable. A chain's continuation reads a count that is only meaningful on the way in;
    /// treating that as a dependency of the chain's value would make the chain wait on a field that waits
    /// on the chain, and report a cycle in a document that has none.
    /// </para>
    /// </summary>
    public required string Role { get; init; }

    public override string Verb => $"reads {Facet} of";
}

/// <summary>
/// A message can cause a move, going this way.
///
/// <para>
/// The edge the whole state layer hangs off. It is what makes "what does sending this do?" a question the
/// graph answers, and it is where the direction belongs: the same message sent and received are two
/// different events, and a protocol that treats them alike is describing a broadcast.
/// </para>
/// </summary>
public sealed record Triggers : Edge
{
    /// <summary>Sent or received. On the edge, because it is a property of this message causing this
    /// move rather than of either end.</summary>
    public required object Way { get; init; }

    public override string Verb => $"on being {Way.ToString()!.ToLowerInvariant()}, causes";
}

/// <summary>A move's ends. <c>Entering</c> tells the destination from the origin.</summary>
public sealed record Moves : Edge
{
    public bool Entering { get; init; }

    public override string Verb => Entering ? "enters" : "leaves";
}

/// <summary>Whose view a move changes.</summary>
public sealed record Viewed : Edge
{
    public override string Verb => "is the view of";
}

/// <summary>A part takes a value when it is not there. Read-side only — the way to write an absent part is
/// not to write it.</summary>
public sealed record Assumes : Edge
{
    public override string Verb => "when absent, is";
}

/// <summary>
/// A packing is only an option while its condition holds.
///
/// <para>
/// Distinct from the key on an <see cref="Offers"/> edge, and the distinction is the point: a key says
/// <i>which value picks this packing</i>, and this says <i>whether picking it is available at all</i>. A
/// body is counted because a length arrived, not because some field holds a number meaning "counted".
/// </para>
/// </summary>
public sealed record Enables : Edge
{
    public override string Verb => "is only an option while";
}

/// <summary>A move keeps something in a slot. What it means is the protocol's business.</summary>
public sealed record Remembers : Edge
{
    public override string Verb => "keeps something in";
}

/// <summary>The expressions a node can hold, and therefore the roles a read can have.</summary>
public static class Roles
{
    public const string Value = "the value";
    public const string Discriminator = "the discriminator";

    /// <summary>The encode-side discriminator, where the two directions decide differently — a reader
    /// asks the region whether a section is there, a writer asks whether it was given one.</summary>
    public const string Selection = "the selection";
    public const string Continuation = "the continuation";
    public const string Seed = "the seed";
    public const string Carry = "the carry";

    /// <summary>One kind's carry, told apart from its neighbours'. An assortment has a carry per kind, and
    /// a dependency gathered under one role would make every kind wait on every other kind's fields —
    /// including ones no component of this message realised.</summary>
    public static string Carrying(Node sort) => $"the carry on '{sort.Name}'";
    public const string Length = "the length";
    public const string Bound = "the region bound";
}

/// <summary>
/// A node needs a value from outside the message.
///
/// <para>
/// The same shape as a read, and separate from it on purpose: what a document requires <i>in order to
/// run</i> is a different question from what one of its fields is computed from, and the first is the one
/// somebody has to answer before anything can happen.
/// </para>
/// </summary>
public sealed record Draws : Edge
{
    /// <summary>Which of the reader's expressions wants it.</summary>
    public required string Role { get; init; }

    public override string Verb => "draws";
}

/// <summary>A constraint applies to a node. Wherever that node is realised — once, or once per structure
/// of a chain — the constraint applies there too.</summary>
public sealed record Constrains : Edge
{
    /// <summary>
    /// Where this constraint sits among the others on the same node.
    ///
    /// <para>
    /// On the edge rather than on the rule, and awkward on purpose. Order is a property of <i>applying</i>
    /// this rule <i>here</i>: one rule can constrain several nodes and need not come in the same place at
    /// each, and once rules are scoped by state the same rule will apply in several scopes with different
    /// neighbours. A number on the rule would be one answer to a question that has several.
    /// </para>
    ///
    /// <para>
    /// It matters because the first failure is the one anyone reads. A rule that says "this is not a
    /// version we know" should be reached before one that says the packet is internally inconsistent —
    /// the second is true and the first is why.
    /// </para>
    /// </summary>
    public required int Order { get; init; }

    public override string Verb => $"constrains[{Order}]";
}

/// <summary>
/// The nodes and the relationships between them.
///
/// <para>
/// Built from a declaration rather than typed out: nesting in the source produces containment edges, and
/// an expression naming another node produces a read. The declaration is a convenient way to say a common
/// shape; this is what the engine actually works on.
/// </para>
/// </summary>
public sealed class ProtocolGraph
{
    private readonly List<Node> _nodes = [];
    private readonly List<Edge> _edges = [];
    private readonly Dictionary<Node, List<Edge>> _from = [];
    private readonly Dictionary<Node, List<Edge>> _to = [];

    public IReadOnlyList<Node> Nodes => _nodes;
    public IReadOnlyList<Edge> Edges => _edges;

    public void Add(Node node)
    {
        if (_from.ContainsKey(node)) return;
        _nodes.Add(node);
        _from[node] = [];
        _to[node] = [];
    }

    public void Add(Edge edge)
    {
        Add(edge.From);
        Add(edge.To);
        _edges.Add(edge);
        _from[edge.From].Add(edge);
        _to[edge.To].Add(edge);
    }

    /// <summary>Edges leaving a node.</summary>
    public IEnumerable<Edge> From(Node node) => _from.TryGetValue(node, out var e) ? e : [];

    /// <summary>Edges arriving at a node.</summary>
    public IEnumerable<Edge> To(Node node) => _to.TryGetValue(node, out var e) ? e : [];

    /// <summary>Edges of one kind leaving a node — the query almost everything actually wants.</summary>
    public IEnumerable<T> From<T>(Node node) where T : Edge => From(node).OfType<T>();

    public IEnumerable<T> To<T>(Node node) where T : Edge => To(node).OfType<T>();

    public IEnumerable<T> Of<T>() where T : Edge => _edges.OfType<T>();

    /// <summary>What a node contains, in order.</summary>
    public IEnumerable<Node> Children(Node node)
        => From<Contains>(node).OrderBy(e => e.Ordinal).Select(e => e.To);

    /// <summary>What contains a node, or null at the root.</summary>
    public Node? Parent(Node node) => To<Contains>(node).FirstOrDefault()?.From;

    /// <summary>Every node from here down, this one first.</summary>
    public IEnumerable<Node> Under(Node node)
    {
        yield return node;

        foreach (var child in Children(node))
            foreach (var deeper in Under(child)) yield return deeper;
    }

    public override string ToString() => $"{_nodes.Count} nodes, {_edges.Count} edges";
}
