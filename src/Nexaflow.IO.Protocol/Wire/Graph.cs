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
/// Something that has to hold about this node, and what settles it.
///
/// <para>
/// One edge for what used to be two unrelated shapes. A field drew from a set by pointing at it
/// (<c>field → set</c>, no computation anywhere), and a rule applied to a field by pointing the other way
/// (<c>rule → field</c>) — so "what constrains this node" was a To-query while every other fact about a
/// node is a From-query, and the two could not be asked together at all.
/// </para>
///
/// <para>
/// <b>It points at whatever decides, and that is the point.</b> A set of legal values was a frozen list on
/// a node, which cannot say "the legal values are the ones the handshake advertised" — and negotiated
/// protocols are largely that. Pointing at a producer instead makes the static case a
/// <see cref="Constant"/> and the negotiated case a computation, reached the same way, with the inputs on
/// ordinary <see cref="Requires"/> edges.
/// </para>
/// </summary>
public sealed record Checks : Edge
{
    /// <summary>Why, in the author's words. Carried into the refusal, because the sentence the author
    /// would have written beats "value 3 is not allowed".</summary>
    public string Because { get; init; } = "";

    /// <summary>A named run inside the node, when the check is about one rather than the whole of it.</summary>
    public string? Run { get; init; }

    /// <summary>
    /// Where this sits among the checks on the same node.
    /// </summary>
    /// <remarks>
    /// On the edge rather than on what it points at, and awkward on purpose. Order is a property of
    /// <i>applying</i> a check here, not of the check — one set or one condition can govern several nodes
    /// and be first at one of them.
    /// </remarks>
    public int Order { get; init; }

    public override string Verb => Run is null ? "must satisfy" : $"must satisfy, in {Run},";
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

    /// <summary>
    /// Moves what produces one named fact from one node onto another.
    /// </summary>
    /// <remarks>
    /// For where a node is <i>replaced</i>: a group becomes a set, an alternation becomes a junction, and
    /// a fact about the thing a document declared has to end up on the thing that stands in the message,
    /// because that is what the walk reaches and settles facts on. Left behind, the answer sits where
    /// nobody asks and every reader needs to know to look one hop sideways — which is how a condition on a
    /// group member came to decide nothing at all while appearing to decide something.
    /// <para>
    /// One fact at a time, deliberately. Taking everything moves the discriminator too, and that is found
    /// by asking the declared node what it computes.
    /// </para>
    /// </remarks>
    public void MoveProduction(Node from, Node onto, string facet)
    {
        foreach (var edge in From<Computes>(from).Where(e => e.Facet == facet).ToList())
        {
            _edges.Remove(edge);
            _from[from].Remove(edge);
            _to[edge.To].Remove(edge);

            Add(edge with { From = onto });
        }
    }

    /// <summary>
    /// Points everything that asked for a node at the one standing in its place.
    /// </summary>
    /// <remarks>
    /// The other half of a replacement. Moving what a node <i>produces</i> is not enough when something
    /// else replaces it on the path: an expression naming the declaration built an edge to it, and the
    /// facts land on whatever the walk actually reached. Left alone, every reader has to redirect the
    /// reference itself at the moment it follows it — which is a redirection repeated at six call sites
    /// instead of one edge pointing where it means.
    /// </remarks>
    public void Redirect(Node from, Node onto)
    {
        foreach (var edge in To<Requires>(from).ToList())
        {
            _edges.Remove(edge);
            _from[edge.From].Remove(edge);
            _to[from].Remove(edge);

            Add(edge with { To = onto });
        }
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

    // ── What the graph knows about itself ─────────────────────────────────────

    /// <summary>
    /// What this describes, and where a walk of it begins.
    /// </summary>
    /// <remarks>
    /// On the graph because they are facts about the message, and the graph is the message. They lived on
    /// the declaration for as long as a declaration was the only way to get one — which is also the reason
    /// everything below lived there, and none of it ever needed to.
    /// </remarks>
    public string Id { get; set; } = "";

    public Node Root { get; set; } = Nowhere;

    private static readonly Node Nowhere = new Unplaced();

    private sealed class Unplaced : Node
    {
        public override string Name => "message";
    }

    // ── Questions about a node, answered from its edges ───────────────────────
    //
    // Every one of these was a method on the declaration, and every one of them was already a query over
    // this and nothing else — they sat there because a declaration was where you happened to be holding
    // when you needed to ask. Moving them is what lets a codec take a graph and never learn whether a
    // document produced it.

    /// <summary>What computes a named fact about a node — its value, its extent, whether it is there.</summary>
    /// <param name="when">
    /// Which direction is asking, where the two get different answers. Null asks for whichever producer
    /// there is, which is the ordinary case: a value is computed the same way whoever wants it.
    ///
    /// <para>
    /// Presence is the case that needs it, and it is the same asymmetry a fork already has. Whether TCP's
    /// options are there is answered on the way out by whether the caller asked for any, and on the way in
    /// by what Data Offset said — and neither question can be asked in the other direction, because on the
    /// way out Data Offset is derived from how long the header turned out to be.
    /// </para>
    /// </param>
    public Computation? ProducerOf(Node owner, string facet, bool? when = null)
        => From<Computes>(owner)
               .Where(e => e.Facet == facet && (e.Reading is null || when is null || e.Reading == when))
               .OrderBy(e => e.Reading is null)
               .Select(e => e.To).OfType<Computation>().FirstOrDefault();

    /// <summary>The computation a particular expression became, found by that expression's identity.</summary>
    public Computation? ComputationOf(Node owner, Expressions.Expr expression)
        => From<Computes>(owner).Select(e => e.To).OfType<Computation>()
               .FirstOrDefault(c => ReferenceEquals(c.Source, expression));

    /// <summary>The conversion applied to a node, as a node, if it has one.</summary>
    public Converted? ConversionOf(Node owner)
        => From<Computes>(owner).Select(e => e.To).OfType<Converted>().FirstOrDefault();

    /// <summary>A conversion's fixed arguments, in order.</summary>
    public IReadOnlyList<Values.ProtoValue> ArgumentsOf(Node owner)
        => ConversionOf(owner) is not { } applied
            ? []
            : [.. InputsOf(applied).Select(e => e.To).OfType<Constant>().Select(c => c.Holds)];

    /// <summary>
    /// What a set repeats over, where it repeats at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A requirement rather than anything structural, and deliberately: the description says this set is
    /// written once per item of that list, and says nothing about how many items there are. That number
    /// belongs to a message, not to a protocol — and it may itself be computed, or read off a field, or
    /// handed in, none of which a count sitting in the graph could be.
    /// </para>
    /// <para>
    /// So the unrolling happens in the run, when the requirement is resolved and the list turns out to have
    /// a length. Which is what this design has claimed since before anything could do it.
    /// </para>
    /// </remarks>
    public Requires? Repeating(Node set)
        => From<Requires>(set).FirstOrDefault(e => e.Facet == "each");

    /// <summary>What a part is taken to have said when it is not there.</summary>
    public Default? Assumed(Node place)
        => From<Assumes>(place).Select(e => e.To).OfType<Default>().FirstOrDefault();

    /// <summary>Where something's inputs come from, in argument order. A computation is the usual asker;
    /// a carrier declares what the protocol beneath is given the same way.</summary>
    public IEnumerable<Requires> InputsOf(Node taker) => From<Requires>(taker).OrderBy(e => e.Sequence);

    /// <summary>Everything that has to hold about a node, in the order the document put them in.</summary>
    public IEnumerable<Checks> Checking(Node node) => From<Checks>(node).OrderBy(e => e.Order);

    /// <summary>What a set is packed from, in order.</summary>
    public IEnumerable<Node> Members(Node set) => From<Holds>(set).OrderBy(h => h.Order).Select(h => h.To);

    /// <summary>
    /// The repeating sets this place is inside, outermost first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What tells the third label of the second record from the second label of the third. An appearance is
    /// keyed by which repetition encloses it as well as by which time round, and this is where that chain
    /// comes from — so a run of names inside a run of records is two indices rather than one shared counter
    /// that neither of them owns.
    /// </para>
    /// <para>
    /// <b>Membership, not the path.</b> A repetition is left and re-entered by edges from junctions that
    /// belong to no set at all, so asking the walk where it has been says a loop's own fork is outside the
    /// loop. Asking what holds this place is a fact about the protocol and gives the same answer whichever
    /// way the walk got here.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Node> Enclosing(Node place)
    {
        List<Node> within = [];
        HashSet<Node> been = [place];

        for (var at = place; To<Holds>(at).FirstOrDefault() is { } held && been.Add(held.From); at = held.From)
            if (Repeating(held.From) is not null) within.Add(held.From);

        within.Reverse();
        return within;
    }

    /// <summary>Whether the path may arrive at this place and find nothing.</summary>
    public bool MayBeAbsent(Node place) => To<Then>(place).Any(w => w.Optional);

    /// <summary>Every place a path arrives at, and whether that step may not be taken.</summary>
    public IEnumerable<(Node Place, bool Optional)> Arrivals()
        => Of<Then>().Select(w => (w.To, w.Optional));

    // There is deliberately nothing here for "the node this one stands for". A set and a junction replace
    // the thing a document declared, and what was hung off that declaration is moved onto them when they
    // are made — so a fact about a place is on that place, and asking is asking. The version with a
    // sideways hop existed because the edge had been left on a node the walk never reaches, and every
    // reader then had to know to look for it; one of them did not, and a condition on a group member
    // decided nothing at all while appearing to decide something.

    public override string ToString() => $"{_nodes.Count} nodes, {_edges.Count} edges";
}
