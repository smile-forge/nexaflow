using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A converter applied to a field, with the arguments it needs.
///
/// <para>
/// The arguments are the reason this is not just a name. A converter that carries a protocol's constant as
/// a default is a protocol specific hiding in the engine, so every converter that has a choice to make
/// demands it be stated — a group order, a zero rule, a fraction width. A field using one therefore has to
/// be able to state it too, and a bare name cannot.
/// </para>
/// </summary>
public sealed record Conversion(string Name, IReadOnlyList<ProtoValue> Args)
{
    /// <summary>So the common no-argument case still reads as <c>Via = "unascii"</c>. Null in, null out:
    /// a document that computes its converter name and gets nothing means <i>no conversion</i>, not a
    /// conversion with no name.</summary>
    public static implicit operator Conversion?(string? name) => name is null ? null : new(name, []);

    public static Conversion Of(string name, params long[] args)
        => new(name, [.. args.Select(a => ProtoValue.Of(a))]);

    public override string ToString() => Args.Count == 0 ? Name : $"{Name}({string.Join(", ", Args)})";
}

/// <summary>
/// One field of a message: a shape, where its value comes from on encode, and what it binds to on decode.
///
/// <para>
/// The same declaration serves both directions. That is the property the whole design rests on — two
/// descriptions kept in step by hand is how a parser generator ends up unable to serialise, and how a
/// production packet language ends up needing a hand-written second pass for the outbound path.
/// </para>
/// </summary>
public sealed class Field : Node
{
    /// <summary>Referenceable name — <c>fields.&lt;id&gt;.value</c> and <c>fields.&lt;id&gt;.extent</c>.
    /// A name for expressions and for reading, never how anything finds this field.</summary>
    public required string Id { get; init; }

    public override string Name => Id;

    public required Pattern Pattern { get; init; }

    /// <summary>
    /// Encode side. Its referenced fields become resolver dependencies automatically, which is what lets a
    /// length that measures a later region schedule without a placeholder or a back-patch pass.
    /// </summary>
    public Expr? Value { get; init; }

    /// <summary>Decode side: the capture name. Defaults to <see cref="Id"/>; a bit group's slices name
    /// themselves.</summary>
    public string? As { get; init; }

    /// <summary>A converter applied on the way out and inverted on the way in — a fixed-point scale, a
    /// text codec.</summary>
    public Conversion? Via { get; init; }

    /// <summary>
    /// A <b>document-authored</b> transform, applied on the same slot as <see cref="Via"/> but further from
    /// the wire.
    ///
    /// <para>
    /// This is how an encoding rule that belongs to one family reaches a field without living in the
    /// engine. The engine's converters are notions — arithmetic, bit operations, byte sequences; a
    /// composition of them that is one family's rule is written down as a transform, in a document, with
    /// its domain declared. If it were a converter instead, it would be a protocol specific with a general
    /// name, which is the thing the whole model is arranged to prevent.
    /// </para>
    /// </summary>
    public Transform? Through { get; init; }

    /// <summary>
    /// What this field points at, and how the protocol renders the reference into octets.
    ///
    /// <para>
    /// The target is a node. The rendering is an expression evaluated with <c>position</c> bound to where
    /// that node landed — bound <b>there and nowhere else</b>, because an offset is meaningful exactly
    /// where a reference has to be written down and is otherwise an invitation to think in offsets about
    /// things that are relationships.
    /// </para>
    /// </summary>
    public Locating? Points { get; init; }

    /// <summary>
    /// Another protocol, carried in this field's octets.
    ///
    /// <para>
    /// The field stays an ordinary span — something measures it, something bounds it, all the usual
    /// machinery applies — and this says what the octets are. Layering needed no new shape, only somewhere
    /// to say what is inside one.
    /// </para>
    /// </summary>
    public Subprotocol? Carries { get; init; }

    /// <summary>
    /// The set this field's values come from, and the run inside it when only one is governed.
    ///
    /// <para>
    /// Authoring input. It becomes an <see cref="Admits"/> edge when the graph is built, and the edge is
    /// what the engine reads — the same arrangement as a rule's order and an arm's key.
    /// </para>
    /// </summary>
    public (ValueSet Set, string? Run)? Draws { get; init; }

    /// <summary>
    /// What this part is taken to mean when it is not there.
    ///
    /// <para>
    /// Only meaningful on a part that can be absent, which is one belonging to a kind of an assortment.
    /// Authoring input; it becomes an <see cref="Assumes"/> edge when the graph is built.
    /// </para>
    /// </summary>
    public Default? Assumed { get; init; }

    /// <summary>
    /// What kind of thing this part holds, where that changes what comparing two of them means.
    ///
    /// <para>
    /// Not a converter and not a matching rule: a converter would fold the value on its way to the wire and
    /// a header would go out spelled differently than it came in, and a matching rule would have to be
    /// remembered at every site that compares — an arm's key, a set's membership, a distinctness rule, an
    /// ordinary equality in a condition. Saying it once about the part is the only version of this that
    /// stays true everywhere.
    /// </para>
    /// </summary>
    public Held Holds { get; init; } = Held.AsWritten;

    /// <summary>Set when comparing two of these ignores case.</summary>
    internal Held.Caseless? Folded => Holds as Held.Caseless;

    public string CaptureName => As ?? Id;
}

/// <summary>An ordered field list. Ordering is explicit here; free arrangement is a packing concern and
/// is deliberately not expressible at this level.</summary>
public sealed record MessageDef
{
    public required string Id { get; init; }
    public required IReadOnlyList<Field> Fields { get; init; }

    /// <summary>
    /// What makes a structurally-valid message illegal anyway.
    ///
    /// <para>
    /// Checked in both directions, after every field is settled — a rule that only ran on decode would let
    /// the engine emit a message it would itself refuse to read.
    /// </para>
    /// </summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>
    /// The values this message needs from outside itself.
    ///
    /// <para>
    /// Declared, so that what a document requires in order to run is something the graph can be asked
    /// rather than something a reader has to infer by grepping its expressions.
    /// </para>
    /// </summary>
    public IReadOnlyList<Context> Context { get; init; } = [];

    /// <summary>
    /// What this protocol has names for, and which part of the message each one is.
    ///
    /// <para>
    /// Declared, and finite: what a message may <i>carry</i> is unbounded, what it has <i>places for</i> is
    /// not. That is what makes a reference to one of them need no index — identity comes from having been
    /// declared rather than from counting.
    /// </para>
    /// </summary>
    public IReadOnlyList<Concept> Concepts { get; init; } = [];

    /// <summary>
    /// Parts that are in the graph and not on the wire.
    ///
    /// <para>
    /// The message's own field list is the path a walk takes, and everything on it gets written. These are
    /// declared beside it: real nodes with real ids, resolved like any other, and never emitted. Something
    /// points at them.
    /// </para>
    ///
    /// <para>
    /// It is what a digest needs and it needed no new kind of thing. A checksum covering a shape that
    /// appears on no wire — an address pair and a protocol number, assembled only to be summed — is that
    /// shape declared here, with the checksum's requirement reaching it by an ordinary edge. So is the
    /// awkward half of the same problem: a checksum whose span includes the checksum field, computed with
    /// it zeroed, is a zero declared here and covered instead of the field. The document says what goes in
    /// the sum rather than the engine knowing a convention about it.
    /// </para>
    /// </summary>
    public IReadOnlyList<Field> Apart { get; init; } = [];

    /// <summary>Everything declared at message level, on the wire or beside it.</summary>
    internal IReadOnlyList<Field> Declared => [.. Fields, .. Apart];

    /// <summary>
    /// What a person has to be asked before this message can be built, and why — computed from the graph
    /// rather than maintained beside it, so it cannot drift from what the document actually reads.
    /// </summary>
    public IEnumerable<Context> Asked
        => Graph.Of<Requires>().Select(e => e.To).OfType<Context>().Where(c => c.Asked).Distinct();

    /// <summary>
    /// The message as nodes and relationships.
    ///
    /// <para>
    /// Built once from the declaration, which is a readable way to say a common shape rather than the
    /// model itself. Nesting becomes containment; an expression naming another node becomes a read; a
    /// length and the region it sizes become one edge instead of two expressions that had nothing to do
    /// with each other.
    /// </para>
    /// </summary>
    /// <summary>
    /// The nodes and edges this declaration comes to, built once.
    /// </summary>
    /// <remarks>
    /// Locked, because a document is routinely a <c>static readonly</c> shared by everything that speaks
    /// it, and two callers arriving together would otherwise both build — leaving one of them reading a
    /// graph that is still being assembled. Unnoticed until a check walked <see cref="ProtocolGraph.Nodes"/>
    /// as a whole rather than following edges out of a node it already had.
    /// </remarks>
    /// <summary>
    /// The nodes and edges this declaration comes to.
    /// </summary>
    /// <remarks>
    /// <b>Complete before anything reads it.</b> It used to be built on first touch, which made the
    /// description of a protocol something that came into existence partway through using it — and
    /// promptly produced a race the moment a check walked the node set rather than following edges out of
    /// a node it already held. A protocol graph is the static thing; nothing about it depends on a
    /// message, so there is no reason for any of it to arrive late.
    /// </remarks>
    public ProtocolGraph Graph => _graph ??= Build();

    private ProtocolGraph? _graph;

    /// <summary>Builds the graph now, so that nothing later can be the first to ask for it.</summary>
    internal void Settle() => _ = Graph;

    /// <summary>
    /// A copy starts with no graph.
    /// </summary>
    /// <remarks>
    /// A record's generated copy constructor copies every field, the memoised graph included — so
    /// <c>document with { Fields = … }</c> produced a document describing one field list and answering
    /// graph questions about another. Harmless while the graph only carried containment nobody
    /// interrogated, and a silent wrong answer the moment selection keys moved onto its edges. Putting
    /// facts in the graph means the graph has to be the one for these facts.
    /// </remarks>
    private MessageDef(MessageDef other)
    {
        Id = other.Id;
        Fields = other.Fields;
        Apart = other.Apart;
        Rules = other.Rules;
        Context = other.Context;
        Concepts = other.Concepts;

        // The same roots, not new ones. Field initialisers run for this constructor too, so leaving one
        // out mints a fresh node and every rule pointing at the original's root stops being about
        // anything — identity is the object, which cuts both ways.
        //
        // `Beside` was left out when it was added and nothing noticed, because a document with parts
        // declared beside it only built its graph when something asked a question that reached them. The
        // fix for one of these is the fix for all of them: every node this record owns belongs here.
        Root = other.Root;
        Beside = other.Beside;
    }

    /// <summary>Stands for the message itself, so a rule about the whole thing has something to point at
    /// rather than being attached by convention.</summary>
    public Node Root { get; } = new MessageRoot();

    private sealed class MessageRoot : Node
    {
        public override string Name => "message";
    }

    /// <summary>Stands for what is declared beside the message rather than in it.</summary>
    public Node Beside { get; } = new AsideRoot();

    private sealed class AsideRoot : Node
    {
        public override string Name => "beside";
    }

    private ProtocolGraph Build()
    {
        var graph = new ProtocolGraph();
        graph.Add(Root);

        Wire(graph, Root, Fields);

        // Under a root of their own, so asking what the message contains still answers with what it
        // writes. They are reachable by reference and by nothing else, which is the whole point.
        Wire(graph, Beside, Apart);

        // Every reference an expression makes, materialised in the scope it was written in. Recovering
        // these by scanning text at each encode is what let a reference into an unrealised arm survive
        // until run time.
        foreach (var concept in Concepts)
            foreach (var named in concept.Of)
                graph.Add(new Names { From = concept, To = named });

        LinkReads(graph, ScopeFields(Declared), null);

        // A conversion is a computation like any other: it takes the value coming past it, plus whatever
        // arguments it was given, and produces one value. Its arguments become nodes so that the graph can
        // be asked what a field's octets actually depend on — which, while they were a list frozen into
        // the declaration, it could not be.
        foreach (var field in Descendants(Declared).Concat(Descendants(Apart)))
        {
            if (field.Via is not { } via) continue;

            var applied = new Converted
            {
                Applies = via,
                Label = $"{field.Id} via {via.Name}",

                // Sequence 0 is the value flowing past — on the way out from the field's own computation,
                // on the way in from the octets — so it is never an edge. The arguments start at one.
                Wants = [.. via.Args.Select((_, at) => new Need($"{via.Name}[{at + 1}]", "value",
                                                                Origin.Stated))],
            };

            graph.Add(applied);
            graph.Add(new Computes { From = field, To = applied });

            for (int at = 0; at < via.Args.Count; at++)
                graph.Add(new Requires
                {
                    From = applied,
                    To = new Constant(via.Args[at], $"{via.Name}[{at + 1}]"),
                    Sequence = at + 1,
                    Facet = "value",
                });
        }

        // Order comes from where the rule was written unless it says otherwise, and lands on the EDGE:
        // one rule can constrain several nodes and need not sit in the same place at each of them.
        // The arrangement, beside the containment the codec still walks. After the computations, because
        // a fork points at the one that decides it. One packing for now, because a field list is one
        // arrangement; the shape is here so a second — chosen by state — needs no new machinery.
        var arrangement = new Packing($"{Id} as declared");
        graph.Add(new Packs { From = Root, To = arrangement });
        Lay(graph, arrangement, Fields);

        for (int i = 0; i < Rules.Count; i++)
            foreach (var target in Rules[i].Applies)
                graph.Add(new Constrains
                {
                    From = Rules[i],
                    To = target,
                    Order = Rules[i].Order == int.MaxValue ? i : Rules[i].Order,
                });

        return graph;
    }

    private static void Wire(ProtocolGraph graph, Node parent, IReadOnlyList<Field> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            graph.Add(new Contains { From = parent, To = field, Ordinal = i });

            if (field.Draws is { } drawn)
                graph.Add(new Admits { From = field, To = drawn.Set, Run = drawn.Run });

            if (field.Assumed is { } assumed)
                graph.Add(new Assumes { From = field, To = assumed });

            if (field.Points is { } points)
                graph.Add(new Locates { From = field, To = points.Target });

            if (field.Carries is { } carried)
            {
                graph.Add(new Embeds { From = field, To = carried });

                if (carried.Carries is Carriage.Described described)
                    graph.Add(new Speaks { From = carried, To = described.Message.Root });
            }

            switch (field.Pattern)
            {
                case Pattern.Group group:
                    Wire(graph, field, group.Fields);
                    break;

                case Pattern.Choice choice:
                    foreach (var arm in choice.Arms)
                    {
                        graph.Add(new Offers { From = field, To = arm, Key = arm.DeclaredKey });

                        // Two different relationships and two edges. What value picks this packing is not
                        // the same fact as whether picking it is available.
                        if (arm.Condition is not null) graph.Add(new Enables { From = arm, To = field });

                        Wire(graph, arm, arm.Fields);
                    }
                    break;

                case Pattern.Chain chain:
                    graph.Add(new Repeats { From = field, To = chain.Element });
                    Wire(graph, field, [chain.Element]);
                    break;

                // The token and whatever always follows it are the assortment's own children; each kind
                // hangs off its offer, so "which token picks this kind" and "may it come twice" are facts
                // about the offering rather than about the packing.
                case Pattern.Assorted assorted:
                    Wire(graph, field, [assorted.Token]);

                    foreach (var sort in assorted.Sorts)
                    {
                        graph.Add(new Offers
                        {
                            From = field, To = sort, Key = sort.DeclaredKey, Repeats = sort.Repeats,
                        });

                        Wire(graph, sort, sort.Fields);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// <summary>Lays a run of fields out as a path, from wherever the walk currently is.</summary>
    /// <returns>The nodes the path can leave from — several, where the run ended in an alternation.</returns>
    private IReadOnlyList<Node> Lay(ProtocolGraph graph, Packing? arrangement,
                                    IReadOnlyList<Field> fields, IReadOnlyList<Node>? from = null)
    {
        var loose = from ?? [];

        foreach (var field in fields)
        {
            var (entry, exits) = Place(graph, field);

            if (arrangement is not null && loose.Count == 0)
                graph.Add(new Starts { From = arrangement, To = entry });
            else
                foreach (var end in loose) graph.Add(new Then { From = end, To = entry });

            loose = exits;
        }

        return loose;
    }

    /// <summary>
    /// What stands on the path here, and where the path leaves it.
    /// </summary>
    /// <remarks>
    /// A container produces nothing — its members do — so it is not a field, whatever the declaration had
    /// to call it for want of anywhere else to say so. An alternation is the same: a place on the path
    /// that makes no octets, offering several ways on rather than holding members in order. One node kind
    /// serves both, told apart by which edges leave it, because "stands for part of the message and emits
    /// nothing" is one notion.
    /// </remarks>
    private (Node Entry, IReadOnlyList<Node> Exits) Place(ProtocolGraph graph, Field field)
    {
        switch (field.Pattern)
        {
            case Pattern.Group group:
            {
                var set = new FieldSet(field.Id, field);
                graph.Add(set);

                for (int at = 0; at < group.Fields.Count; at++)
                    graph.Add(new Holds
                    {
                        From = set, To = Place(graph, group.Fields[at]).Entry, Order = at,
                    });

                return (set, [set]);
            }

            case Pattern.Choice choice:
            {
                var fork = new FieldSet(field.Id, field);
                graph.Add(fork);

                // What decides, as a node. A value that keys the ways on rather than a condition per way:
                // sibling conditions cannot be checked for cover by anything, and keyed ones can.
                // Queried off the graph being built, never off the property: reaching for that while it
                // is still being assembled re-enters the build, and the recursion has no bottom.
                foreach (var deciding in new[] { choice.Key, choice.Selects })
                    if (deciding is not null
                        && graph.From<Computes>(field).Select(e => e.To).OfType<Evaluated>()
                                .FirstOrDefault(c => ReferenceEquals(c.Source, deciding)) is { } decides)
                        graph.Add(new Decides
                        {
                            From = fork, To = decides, Reading = ReferenceEquals(deciding, choice.Key),
                        });

                List<Node> exits = [];

                foreach (var offer in graph.From<Offers>(field))
                {
                    var arm = (Arm)offer.To;

                    // An arm with nothing in it still went somewhere: the path leaves the fork directly,
                    // so whatever follows the alternation follows it.
                    if (arm.Fields.Count == 0) { exits.Add(fork); continue; }

                    var (entry, ends) = Place(graph, arm.Fields[0]);

                    graph.Add(new Then
                    {
                        From = fork, To = entry, Key = offer.Key, Otherwise = offer.IsFallback,
                    });

                    exits.AddRange(Lay(graph, null, [.. arm.Fields.Skip(1)], ends));
                }

                return (fork, exits);
            }

            default:
                return (field, [field]);
        }
    }

    /// <summary>The arrangements this message offers.</summary>
    public IEnumerable<Packing> Arrangements
        => Graph.From<Packs>(Root).Select(e => e.To).OfType<Packing>();

    /// <summary>
    /// What an arrangement lays out, in order, groups expanded.
    /// </summary>
    /// <remarks>
    /// The walk the engine will do: start where the arrangement says, follow what comes next, and expand
    /// anything that holds members. It yields leaves — a group is a place on the path rather than a thing
    /// on the wire of its own.
    /// </remarks>
    /// <remarks>
    /// It follows the <b>unbranched</b> path and stops at a fork, which is not a limitation but the
    /// truth: past an alternation there is no one path, and which way it goes is a value nothing has yet.
    /// A fork is yielded as a single place, exactly as a container is expanded into its members — the
    /// engine's walk will carry a decision, and this one is what the arrangement can be checked with
    /// before there is one.
    /// </remarks>
    public IEnumerable<Node> Walk(Packing arrangement)
    {
        var at = Graph.From<Starts>(arrangement).FirstOrDefault()?.To;

        while (at is not null)
        {
            foreach (var node in Expand(at)) yield return node;

            var ways = Graph.From<Then>(at).ToList();

            at = ways.Count == 1 && ways[0].Key is null && !ways[0].Otherwise ? ways[0].To : null;
        }
    }

    private IEnumerable<Node> Expand(Node node)
    {
        var members = Graph.From<Holds>(node).OrderBy(h => h.Order).Select(h => h.To).ToList();

        if (members.Count == 0) { yield return node; yield break; }

        foreach (var member in members)
            foreach (var deeper in Expand(member)) yield return deeper;
    }

    /// Resolves every expression's references once, in the scope the expression was written in.
    ///
    /// <para>
    /// A chain's <i>carry</i> is the one that reads outside its own scope, and inward rather than outward:
    /// it is written on the chain but evaluated inside the structure, so it names the structure's fields.
    /// Nothing about the text says that; the graph has to.
    /// </para>
    /// </summary>
    private void LinkReads(ProtocolGraph graph, IReadOnlyList<Field> here, IReadOnlyList<Field>? outer)
    {
        Field? Visible(string name)
            => here.FirstOrDefault(f => string.Equals(f.Id, name, StringComparison.Ordinal))
            ?? outer?.FirstOrDefault(f => string.Equals(f.Id, name, StringComparison.Ordinal));

        foreach (var field in here)
        {
            void Link(Expr expression, string what, Func<string, Field?> lookup)
                => Compute(graph, field, expression, $"{field.Id}.{what}", lookup);

            if (field.Value is not null) Link(field.Value, "value", Visible);

            // A group written from its runs depends on whatever each run reads, exactly as it would have
            // depended on them through the one record it used to be written from.
            foreach (var run in (field.Pattern as Pattern.Bits)?.Slices ?? [])
                if (run.Value is { } contents) Link(contents, run.Name, Visible);

            switch (field.Pattern)
            {
                case Pattern.Choice choice:
                    if (choice.Key is { } key) Link(key, "discriminator", Visible);

                    foreach (var arm in choice.Arms.Where(a => a.Condition is not null))
                        Link(arm.Condition!, $"when '{arm.Name}'", Visible);

                    if (choice.Selects is { } selects) Link(selects, "selection", Visible);
                    break;

                case Pattern.Opaque { Length: { } length }:
                    Link(length, "length", Visible);
                    break;

                case Pattern.Group { Extent: { } extent }:
                    Link(extent, "bound", Visible);
                    break;

                case Pattern.Chain chain:
                {
                    Link(chain.Continues, "continuation", Visible);
                    if (chain.Seed is not null) Link(chain.Seed, "seed", Visible);

                    var inside = ScopeFields([chain.Element]);

                    if (chain.Carry is not null)
                        Link(chain.Carry, "carry",
                             n => inside.FirstOrDefault(f => string.Equals(f.Id, n, StringComparison.Ordinal)));

                    LinkReads(graph, inside, [.. here, .. outer ?? []]);
                    break;
                }

                case Pattern.Assorted assorted:
                    Link(assorted.Continues, "continuation", Visible);
                    if (assorted.Seed is { } seeded) Link(seeded, "seed", Visible);

                    // A kind's carry is written on the kind and read inside it, so it names that kind's
                    // fields — the same inward resolution a chain's carry has.
                    foreach (var sort in assorted.Sorts.Where(a => a.Carry is not null))
                    {
                        var within = ScopeFields(sort.Fields);
                        Link(sort.Carry!, $"carry on '{sort.Name}'",
                             n => within.FirstOrDefault(f => string.Equals(f.Id, n, StringComparison.Ordinal))
                               ?? Visible(n));
                    }


                    // A kind that repeats gets its own scope, so its expressions resolve there first and
                    // outward after — the same arrangement a chain's instance has, for the same reason.
                    foreach (var (_, fields) in assorted.Scoped)
                        LinkReads(graph, ScopeFields(fields), [.. here, .. outer ?? []]);
                    break;
            }
        }
    }

    /// <summary>
    /// Turns one expression into a computation node, and hangs the requirements path off its owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One node for the whole expression. The interior is the AST engine's business — what the graph needs
    /// to know is what comes in and what goes out, and an expression is a thing that takes inputs and
    /// returns a value like any other computation.
    /// </para>
    /// <para>
    /// The references are read out of the expression once, here, and become <see cref="Requires"/> edges
    /// in the order they were found. That order is the argument order, so the same machinery serves a
    /// converter called with three arguments and an expression naming three fields.
    /// </para>
    /// </remarks>
    private Computation Compute(ProtocolGraph graph, Node owner, Expr expression, string label,
                                Func<string, Field?> lookup)
    {
        List<Need> wants = [];

        foreach (var term in expression.Descendants().OfType<Expr.Member>())
        {
            Need? need = term switch
            {
                { Target: Expr.Member { Target: Expr.Root { Name: "fields" } } of }
                    => new Need(of.Name, term.Name, Origin.Part),
                { Target: Expr.Root { Name: "inputs" } }
                    => new Need(term.Name, "value", Origin.Outside),
                { Target: Expr.Root { Name: Vocabulary.Present } }
                    => new Need(term.Name, "value", Origin.Presence),
                _ => null,
            };

            if (need is { } wanted && !wants.Contains(wanted)) wants.Add(wanted);
        }

        var computation = new Evaluated { Source = expression, Label = label, Wants = wants };
        graph.Add(computation);
        graph.Add(new Computes { From = owner, To = computation });

        for (int at = 0; at < wants.Count; at++)
        {
            var want = wants[at];

            Node? from = want.From switch
            {
                Origin.Part => lookup(want.Name),
                Origin.Outside => Context.FirstOrDefault(
                    c => string.Equals(c.Key, want.Name, StringComparison.Ordinal)),

                // Asking whether a part turned up depends on the run that would have carried it, and
                // nothing in the name says so — what has to settle first is the assortment.
                _ => Optionals.FirstOrDefault(
                         o => string.Equals(o.Key.CaptureName, want.Name, StringComparison.Ordinal))
                     is { Key: not null } known ? known.Value.Assortment : null,
            };

            if (from is not null)
                graph.Add(new Requires
                {
                    From = computation, To = from, Sequence = at, Facet = want.Facet,
                });
        }

        return computation;
    }

    /// <summary>The computation a node's named requirements path is rooted at, found by the expression it
    /// was built from — by object identity, which is why a <c>Reads</c> edge needs no role.</summary>
    public Computation? ComputationOf(Node owner, Expr expression)
        => Graph.From<Computes>(owner).Select(e => e.To).OfType<Evaluated>()
                .FirstOrDefault(c => ReferenceEquals(c.Source, expression));

    /// <summary>The computations rooted at a node.</summary>
    public IEnumerable<Computation> Computations(Node owner)
        => Graph.From<Computes>(owner).Select(e => e.To).OfType<Computation>();

    /// <summary>The conversion applied to a field, as a node, if it has one.</summary>
    public Converted? ConversionOf(Field field)
        => Graph.From<Computes>(field).Select(e => e.To).OfType<Converted>().FirstOrDefault();

    /// <summary>A conversion's arguments, taken from the graph rather than from the declaration.</summary>
    public IReadOnlyList<Values.ProtoValue> ArgumentsOf(Field field)
        => ConversionOf(field) is not { } applied
            ? []
            : [.. InputsOf(applied).Select(e => e.To).OfType<Constant>().Select(c => c.Value)];

    /// <summary>Where a computation's inputs come from, in argument order.</summary>
    public IEnumerable<Requires> InputsOf(Computation computation)
        => Graph.From<Requires>(computation).OrderBy(e => e.Sequence);

    /// <summary>Where one of a node's computations takes its inputs from.</summary>
    public IEnumerable<Requires> InputsOf(Node owner, Expr? expression)
        => expression is null || ComputationOf(owner, expression) is not { } path ? [] : InputsOf(path);

    /// <summary>
    /// The computation a node belongs to, and the expression it was rooted at.
    /// </summary>
    public (Node Owner, Expr Root)? Belongs(Computation computation)
        => Graph.To<Computes>(computation).FirstOrDefault() is { } rooted
        && computation is Evaluated evaluated
            ? (rooted.From, evaluated.Source)
            : null;


    /// <summary>Every field in the message, nested ones included, in declaration order.</summary>
    public IEnumerable<Field> AllFields => Descendants(Declared);

    /// <summary>The rules that apply to one node. A reference lookup, so a rule on a segment inside a
    /// repeated structure is found every time that structure is realised.</summary>
    public IEnumerable<Rule> RulesOn(Node node)
        => Graph.To<Constrains>(node).OrderBy(e => e.Order).Select(e => (Rule)e.From);

    /// <summary>What a choice offers, and on what. Read from the edges, because that is where the key
    /// lives — the arm knows what shape it is, the edge knows what picks it.</summary>
    public IReadOnlyList<Offers> Offered(Node choice) => [.. Graph.From<Offers>(choice)];

    /// <summary>
    /// The node a concept names <b>in this message</b>. A concept can name a part of several messages, so
    /// the lookup is scoped: what "the invoke id" is depends on which message you are holding.
    /// </summary>
    public Node? Named(Concept concept) => NamedAll(concept).FirstOrDefault();

    /// <summary>
    /// Every part of this message the concept names.
    ///
    /// <para>
    /// More than one, with only one present at a time, is the ordinary case rather than an oddity:
    /// BACnet's invoke id is called something different in each packing, and which of them arrived depends
    /// on which arm was taken. So the concept names all four and the message answers with whichever it
    /// actually bound.
    /// </para>
    /// </summary>
    public IReadOnlyList<Node> NamedAll(Concept concept)
    {
        var mine = AllFields.ToHashSet();

        return [.. Graph.From<Names>(concept).Select(e => e.To).Where(n => n is Field f && mine.Contains(f))];
    }

    /// <summary>Every place this message carries another protocol. What a reviewer asks first about a
    /// document that stacks: what else is in here, and is any of it something we already trust.</summary>
    public IReadOnlyList<Subprotocol> Layers => [.. Graph.Of<Embeds>().Select(e => (Subprotocol)e.To)];

    /// <summary>
    /// The component a field belongs to, where it belongs to one that might not turn up.
    ///
    /// <para>
    /// This is what makes absence a fact rather than an accident. Every other field of a message is there
    /// because the walk reached it; a field of an assorted kind is there only if that kind arrived, so a
    /// read of one has an answer the rest do not have — <i>nobody sent it</i>. Saying that is the whole
    /// difference between a diagnostic naming the header and one naming a type three layers away.
    /// </para>
    /// </summary>
    public (Field Assortment, Arm Sort)? Optional(Field field)
        => Optionals.TryGetValue(field, out var owner) ? owner : null;

    /// <summary>
    /// Every part that might not be there, and the component it belongs to.
    ///
    /// <para>
    /// Computed from the declaration rather than the graph, because the graph is built from it and needs
    /// the answer while it is being built — the read a presence condition makes is an edge, and it cannot
    /// be drawn without knowing which assortment settles it.
    /// </para>
    /// </summary>
    internal IReadOnlyDictionary<Field, (Field Assortment, Arm Sort)> Optionals => _optionals ??= Optionality();
    private IReadOnlyDictionary<Field, (Field, Arm)>? _optionals;

    private Dictionary<Field, (Field, Arm)> Optionality()
    {
        Dictionary<Field, (Field, Arm)> found = [];

        foreach (var field in Descendants(Fields))
        {
            if (field.Pattern is not Pattern.Assorted assorted) continue;

            foreach (var sort in assorted.Sorts)
                foreach (var part in Descendants(sort.Fields))
                    found[part] = (field, sort);
        }

        return found;
    }

    /// <summary>The set a field draws from, if it declares one.</summary>
    public Admits? DrawnFrom(Node field) => Graph.From<Admits>(field).FirstOrDefault();

    /// <summary>
    /// The packing a discriminator selects, or a refusal naming what arrived and what was declared.
    /// </summary>
    public Arm Choose(Field field, ProtoValue key)
    {
        var offered = Offered(field);

        var taken = offered.FirstOrDefault(o => !o.IsFallback && ProtoValue.Alike(o.Key, key))
                 ?? offered.FirstOrDefault(o => o.IsFallback)
                 ?? throw new ProtoTypeException(
                        $"field '{field.Id}': {Shown(key)} matches no arm, and none is declared as the "
                      + "fallback. Declared: "
                      + string.Join(", ", offered.Select(o => o.IsFallback ? $"{o.To.Name}=*"
                                                                           : $"{o.To.Name}={o.Key}")));

        return (Arm)taken.To;
    }

    /// <summary>
    /// The packing whose condition holds, or a refusal saying what was true when none did.
    /// </summary>
    /// <remarks>
    /// Two holding is refused rather than resolved by declaration order, the same as two transitions
    /// matching one message. Which one applies would otherwise depend on how a list happened to be sorted,
    /// and it is checked at document time anyway — this is the runtime half of the same rule.
    /// </remarks>
    public Arm Choose(Field field, Func<Expr, bool> holds, string what)
    {
        var offered = Offered(field);

        List<Offers> taken = [.. offered.Where(o => ((Arm)o.To).Condition is { } c && holds(c))];

        if (taken.Count > 1)
            throw new ProtoTypeException(
                $"field '{field.Id}': {taken.Count} packings are options at once — "
              + string.Join(", ", taken.Select(o => $"'{o.To.Name}'"))
              + $". Which applies is not decided. {what}");

        var one = taken.FirstOrDefault()
               ?? offered.FirstOrDefault(o => ((Arm)o.To).Condition is null)
               ?? throw new ProtoTypeException(
                      $"field '{field.Id}': no packing is an option. {what}");

        return (Arm)one.To;
    }

    /// <summary>A key as a diagnostic names it. Hex alongside a number because a discriminator is almost
    /// always written that way in a specification, and not for a token that was never a number.</summary>
    private static string Shown(ProtoValue key)
        => key is ProtoValue.Int i ? $"discriminator {i.Value} (0x{i.Value:x})" : $"the token '{key}'";

    /// <summary>The same key inside a list of them, where the sentence around it has already said what
    /// they are.</summary>
    private static string Listed(ProtoValue key)
        => key is ProtoValue.Int i ? $"0x{i.Value:x}" : $"'{key}'";

    internal static IEnumerable<Field> Descendants(IReadOnlyList<Field> fields)
    {
        foreach (var field in fields)
        {
            yield return field;
            foreach (var nested in Descendants(field.Pattern.Nested)) yield return nested;
        }
    }

    /// <summary>Document-time checks over the whole message.</summary>
    public IReadOnlyList<string> Validate() => Validate([]);

    /// <summary>
    /// The same check, following what this message carries.
    /// </summary>
    /// <remarks>
    /// A carried document was validated by nothing until something tried to encode through it, so a
    /// document that stacks on a broken one passed and failed at run time. Checking the graph's own
    /// structure is this engine's job wherever the graph reaches, and an <see cref="Embeds"/> edge is
    /// somewhere it reaches — the same reasoning that puts a rule's targets and an arm's cover under the
    /// same pass.
    ///
    /// <para>
    /// The seen set is not paranoia: a document may legitimately carry one that carries it back, and a
    /// tunnelling protocol describing its own encapsulation is a real shape rather than an error.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> Validate(HashSet<Node> seen)
    {
        List<string> issues = [];

        if (!seen.Add(Root)) return issues;

        Check(Declared, new HashSet<string>(StringComparer.Ordinal), issues);
        CheckApart(issues);

        foreach (var layer in Layers)
            if (layer.Carries is Carriage.Described described)
                foreach (var inside in described.Message.Validate(seen))
                    issues.Add($"in '{layer.Id}', which '{Id}' carries: {inside}");

        return issues;
    }

    /// <summary>
    /// One field scope: the ids declared here plus everything visible from outside it.
    ///
    /// <para>
    /// Ids are flat within a scope, which is what lets an expression say <c>fields.byteCount.extent</c>
    /// without spelling a path through the nesting. A chain opens a new one, because its instances are
    /// separate structures — two instances each declaring a length is not a duplicate, and instance 2's
    /// length must not resolve to instance 1's.
    /// </para>
    /// </summary>
    private void Check(IReadOnlyList<Field> fields, IReadOnlySet<string> outer, List<string> issues)
    {
        // Everything declared at this level, nested scopes excluded — those get their own pass.
        List<Field> here = [];
        Gather(fields, here);

        // Patterns are checked with their scope in hand, so a discriminator reading a bit run can be
        // measured against how wide that run actually is.
        var patterns = here.GroupBy(f => f.Id, StringComparer.Ordinal)
                           .ToDictionary(g => g.Key, g => g.First().Pattern, StringComparer.Ordinal);

        foreach (var field in here)
        {
            issues.AddRange(field.Pattern.Validate(field.Id, patterns));

            // A transform is document-authored, so it is checked here rather than trusted — containment,
            // totality and a declared domain, the same bar a transform faces anywhere else.
            if (field.Through is { } transform)
                issues.AddRange(transform.Validate().Select(i => $"field '{field.Id}': {i}"));
        }

        foreach (var duplicate in here.GroupBy(f => f.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"message '{Id}': duplicate field id '{duplicate.Key}' in one scope — ids are how "
                     + "fields reference each other, and they are flat across the regions and arms of a scope");

        // Bit runs bind into the same flat namespace as fields, so two groups each naming a run `seconds`
        // would overwrite one another and the second would look like the first had moved. Found by an
        // audit against a specification, which is exactly the sort of thing a hand-built corpus misses.
        var taken = new HashSet<string>(here.Select(f => f.Id), StringComparer.Ordinal);

        foreach (var field in here)
            foreach (var slice in (field.Pattern as Pattern.Bits)?.Slices ?? [])
                if (!taken.Add(slice.Name))
                    issues.Add($"message '{Id}': the bit run '{slice.Name}' in '{field.Id}' collides with "
                             + "another name in this scope — runs bind alongside fields, not underneath them");

        var visible = new HashSet<string>(outer, StringComparer.Ordinal);
        visible.UnionWith(here.Select(f => f.Id));

        // Rules are checked once, against the graph, because a reference does not need a scope to be
        // resolved in — which is the point of it being a reference.
        if (outer.Count == 0)
        {
            CheckRules(issues);
            CheckContext(issues);
            CheckVocabulary(issues);
            CheckDistinctness(issues);
            CheckReferences(issues);
            CheckAddressability(issues);
            CheckNames(issues);
        }

        CheckChoices(here, issues);

        // A carry is written on the chain and evaluated INSIDE the structure, so it names the structure's
        // fields rather than these. The graph has always known that; this check did not, because until
        // the vocabulary check made the walk enumerate every expression, a carry was never scope-checked
        // at all. Skipped here and checked below against the scope it is actually read in.
        // Each scope of its own is checked against itself, with everything out here still visible: a
        // structure may read the message metadata around it.
        foreach (var field in here)
            foreach (var (_, scoped) in field.Pattern.Scoped) Check(scoped, visible, issues);
    }

    /// <summary>
    /// A name nothing answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The graph resolves every reference once, in the scope it was written in, and a reference it could
    /// not resolve leaves a term shaped like one with no edge out of it. So this is a question about the
    /// graph rather than a second reading of the text — which is what the two scope checks it replaced
    /// were, each with its own idea of what was visible where.
    /// </para>
    /// <para>
    /// It has to exist. Without it an unresolvable name is simply a term with no <c>Reads</c>, the
    /// expression quietly evaluates it as nothing, and every comparison against it is false — the exact
    /// failure the vocabulary table was built for, one level down.
    /// </para>
    /// </remarks>
    private void CheckNames(List<string> issues)
    {
        foreach (var computation in Graph.Nodes.OfType<Computation>())
        {
            var answered = InputsOf(computation).Select(e => e.Sequence).ToHashSet();

            for (int at = 0; at < computation.Wants.Count; at++)
            {
                if (answered.Contains(at) || computation.Wants[at].From != Origin.Part) continue;

                // A carry runs inside the structure the chain repeats, so the scope it missed is that one
                // and saying "in scope there" would send the author looking in the wrong place.
                bool carried = Belongs(computation) is { } owned
                            && owned.Owner is Field { Pattern: Pattern.Chain chain }
                            && ReferenceEquals(owned.Root, chain.Carry);

                issues.Add($"message '{Id}': {computation.Label} references "
                         + $"'{computation.Wants[at].Name}', which is not a field "
                         + (carried ? "of the structure it runs in" : "in scope there"));
            }
        }
    }

    /// <summary>
    /// What may be named from outside an assortment, and what may not.
    ///
    /// <para>
    /// This is the rule that makes a component addressable at all, and it is worth being strict about
    /// because the failure is silent. A kind declared once has exactly one appearance, so an edge to its
    /// field means something. A kind that repeats has several, and an edge would resolve to whichever one
    /// settled last — the same defect that made a chain's instances need their own scope, arriving by a
    /// different door. The token is the same: it is read once per component, so from out here it names the
    /// last one, which is nobody's intent.
    /// </para>
    /// </summary>
    private void CheckAddressability(List<string> issues)
    {
        Dictionary<Field, (Field Assortment, Arm? Sort, string Why)> unreachable = [];

        foreach (var field in AllFields)
        {
            if (field.Pattern is not Pattern.Assorted assorted) continue;

            foreach (var part in Descendants([assorted.Token]))
                unreachable[part] = (field, null,
                    $"'{part.Id}' is read once per component of '{field.Id}', so from out here it names "
                  + "the last one that happened to arrive");

            foreach (var sort in assorted.Sorts.Where(s => s.Repeats))
                foreach (var part in Descendants(sort.Fields))
                    unreachable[part] = (field, sort,
                        $"'{sort.Name}' may appear more than once in '{field.Id}', so '{part.Id}' is not "
                      + "one field but several. Something with several appearances cannot be pointed at; "
                      + "if this one really comes at most once, say so and it becomes nameable");
        }

        if (unreachable.Count == 0) return;

        foreach (var read in Graph.Of<Requires>())
        {
            if (read.To is not Field target || !unreachable.TryGetValue(target, out var why)) continue;
            if (Belongs((Computation)read.From) is not { Owner: Field reader } owned) continue;

            // A kind's own carry is written on the assortment and read inside the kind, so it names parts
            // that are unaddressable from anywhere else — which is correct, and not a violation here.
            if (Alongside(reader, why.Assortment, why.Sort)) continue;
            if (why.Sort is { } sort && ReferenceEquals(owned.Root, sort.Carry)) continue;

            issues.Add($"message '{Id}': '{reader.Id}' reads '{target.Id}' — {why.Why}.");
        }
    }

    /// <summary>
    /// Whether a reader is close enough to a part for the read to mean one thing.
    ///
    /// <para>
    /// Close enough means <i>in the same component</i>, and that is not containment: an arm hangs off its
    /// assortment by an <see cref="Offers"/> edge, so walking parents up from a field inside one arrives
    /// nowhere. The first real use of this check refused a length reading the span it measures — two
    /// fields of one option, the pairing this engine is built around, called unaddressable because the
    /// walk could not see they were siblings.
    /// </para>
    ///
    /// <para>
    /// A part of a repeating kind is nameable from that same kind and nowhere else, because within one
    /// component there is exactly one of it. The token is nameable from any kind of its assortment, since
    /// every component has one and it is that component's own.
    /// </para>
    ///
    /// <para>
    /// A kind's carry is exempt, and hangs off the assortment only because that is where the edge was
    /// drawn from: it is written on the kind and read inside it, so it names that kind's fields for the
    /// same reason a chain's carry names its element's. The exemption is by role rather than by reader.
    /// </para>
    /// </summary>
    private bool Alongside(Node reader, Field assortment, Arm? sort)
        => reader is Field part
           && Optional(part) is { } from
           && ReferenceEquals(from.Assortment, assortment)
           && (sort is null || ReferenceEquals(from.Sort, sort));

    /// <summary>
    /// What a part declared beside the message may read.
    ///
    /// <para>
    /// Order matters here and nowhere else on this side. Everything on the wire is settled by the time
    /// these are worked out, so they may read any of it; but they are worked out among themselves in the
    /// order they were written, so one reading a later one would read nothing and every comparison against
    /// it would go quietly false. Checked rather than ordered for the author, because the fix is to move a
    /// line and the diagnostic can say which.
    /// </para>
    /// </summary>
    private void CheckApart(List<string> issues)
    {
        HashSet<string> settled = new(Descendants(Fields).Select(f => f.Id), StringComparer.Ordinal);

        foreach (var field in Apart)
        {
            foreach (var part in Descendants([field])) settled.Add(part.Id);

            if (field.Value is null && field.Pattern.Nested.Count == 0)
                issues.Add($"message '{Id}': '{field.Id}' is declared beside the message and says nothing "
                         + "about what it holds. Nothing on the wire will fill it in.");

            foreach (var read in Descendants([field]).SelectMany(f => InputsOf(f, f.Value)))
                if (read.To is Field earlier && !settled.Contains(earlier.Id))
                    issues.Add($"message '{Id}': '{field.Id}' is declared beside the message and reads "
                             + $"'{earlier.Id}', which is worked out after it. Move it earlier.");
        }
    }

    private void CheckContext(List<string> issues)
    {
        var declared = Context.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        // A term shaped like an outside value with no edge leaving it is one nothing declared — the same
        // question CheckNames asks about fields, and the same answer: the graph resolved it or it did not.
        foreach (var computation in Graph.Nodes.OfType<Computation>())
            foreach (var want in computation.Wants.Where(w => w.From == Origin.Outside))
                if (!declared.Contains(want.Name))
                    issues.Add($"message '{Id}': {computation.Label} reads 'inputs.{want.Name}', which "
                             + "the document never says it needs. An undeclared outside value resolves "
                             + "to nothing and surfaces much later as a type error somewhere unrelated.");

        foreach (var duplicate in Context.GroupBy(c => c.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"message '{Id}': 'inputs.{duplicate.Key}' is declared {duplicate.Count()} times");

        foreach (var source in Context.Where(c => c.Asked && string.IsNullOrWhiteSpace(c.Purpose)))
            issues.Add($"message '{Id}': '{source.Key}' is asked of a person and does not say what for. "
                     + "That sentence is the question they get shown.");

        // Declaring a need nothing reads is how a prompt list grows things nobody wants any more.
        var read = Graph.Of<Requires>().Select(e => e.To).OfType<Context>()
                        .Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var source in Context.Where(c => !read.Contains(c.Key)))
            issues.Add($"message '{Id}': '{source.Key}' is declared and never read");
    }

    private void CheckRules(List<string> issues)
    {
        var known = AllFields.ToHashSet();

        foreach (var rule in Rules)
        {
            foreach (var target in rule.Applies)
            {
                if (target is Field field && !known.Contains(field))
                    issues.Add($"message '{Id}': the rule {rule} is about '{field.Name}', which is not a "
                             + "field of this message. A reference to a field of some OTHER message is the "
                             + "one mistake naming could not make and pointing can.");

                if (target is not Field && !ReferenceEquals(target, Root))
                    issues.Add($"message '{Id}': the rule {rule} is about something that is not part of it");
            }

            if (rule is Rule.Domain domain)
            {
                if (domain.Allowed.Count == 0)
                    issues.Add($"message '{Id}': the value rule on '{domain.What}' allows nothing, so no "
                             + "message can satisfy it");

                if (domain.Run is { } run
                    && (domain.Field.Pattern is not Pattern.Bits bits
                        || !bits.Slices.Any(s => string.Equals(s.Name, run, StringComparison.Ordinal))))
                    issues.Add($"message '{Id}': the value rule names the run '{run}', which "
                             + $"'{domain.Field.Name}' does not have");
            }

            if (string.IsNullOrWhiteSpace(rule.Because))
                issues.Add($"message '{Id}': the rule {rule} does not say why. A refusal a reader cannot "
                         + "act on is barely better than none.");
        }
    }

    /// <summary>The ids declared in one scope. Both directions resolve names against this — the validator
    /// to decide what is in scope, the encoder to decide what to qualify.</summary>
    internal static IReadOnlyList<Field> ScopeFields(IReadOnlyList<Field> fields)
    {
        List<Field> here = [];
        Gather(fields, here);
        return here;
    }

    /// <summary>Fields declared in this scope — descending through regions and arms, stopping at anything
    /// that opens a scope of its own.</summary>
    private static void Gather(IReadOnlyList<Field> fields, List<Field> into)
    {
        foreach (var field in fields)
        {
            into.Add(field);
            Gather(field.Pattern.InScope, into);
        }
    }

    /// <summary>Every expression the message carries, with enough context to name it in a diagnostic and
    /// the site that decides which of the walk's roots can answer it.</summary>
    private static IEnumerable<(Field Owner, Expr Expression, string What, ExprSite Site)> Expressions(
        IEnumerable<Field> all)
    {
        foreach (var field in all)
        {
            if (field.Value is not null) yield return (field, field.Value, "the value", ExprSite.Value);

            foreach (var run in (field.Pattern as Pattern.Bits)?.Slices ?? [])
                if (run.Value is { } contents)
                    yield return (field, contents, $"the run '{run.Name}'", ExprSite.Value);

            switch (field.Pattern)
            {
                case Pattern.Choice choice:
                    // Paired with an encode-side reading, the key is the reader's question alone and may
                    // use the reader's vocabulary. Alone, it has to answer in both directions.
                    if (choice.Key is { } key)
                        yield return (field, key, "the discriminator",
                                      choice.Selects is null ? ExprSite.Discriminator : ExprSite.Recognition);

                    foreach (var arm in choice.Arms.Where(a => a.Condition is not null))
                        yield return (field, arm.Condition!, $"the condition on '{arm.Name}'",
                                      ExprSite.Discriminator);

                    if (choice.Selects is { } selects)
                        yield return (field, selects, "the selection", ExprSite.Selection);
                    break;

                case Pattern.Chain chain:
                    yield return (field, chain.Continues, "the continuation", ExprSite.Continuation);
                    if (chain.Seed is { } seed) yield return (field, seed, "the seed", ExprSite.Seeding);
                    if (chain.Carry is { } carry) yield return (field, carry, "the carry", ExprSite.Carry);
                    break;

                case Pattern.Assorted assorted:
                    yield return (field, assorted.Continues, "the continuation", ExprSite.Continuation);
                    if (assorted.Seed is { } start) yield return (field, start, "the seed", ExprSite.Seeding);

                    foreach (var sort in assorted.Sorts.Where(a => a.Carry is not null))
                        yield return (field, sort.Carry!, $"the carry on '{sort.Name}'", ExprSite.Carry);
                    break;

                case Pattern.Opaque { Length: { } length }:
                    yield return (field, length, "the length", ExprSite.Length);
                    break;
            }

            if (field.Points is { } points)
                yield return (field, points.Render, "the reference", ExprSite.Locating);

            switch (field.Pattern)
            {

                case Pattern.Group { Extent: { } extent }:
                    yield return (field, extent, "the region bound", ExprSite.Bound);
                    break;
            }
        }
    }

    /// <summary>
    /// Every expression checked against what its site can answer.
    ///
    /// <para>
    /// The check that would have caught three defects in a row, each of which was a name bound at one
    /// scope-construction site and not another — evaluating to nothing and turning every comparison
    /// against it quietly false. See <see cref="Vocabulary"/> for why availability is a property of the
    /// site rather than of the expression.
    /// </para>
    /// </summary>
    /// <summary>
    /// Every choice, checked against the edges that carry its keys.
    ///
    /// <para>
    /// Moved here from the pattern when the keys moved onto the <see cref="Offers"/> edges: a shape cannot
    /// see the graph, and it should not be the authority on a relationship between two nodes anyway. The
    /// substance is unchanged — the arms must be distinct, exhaustive over what the discriminator can
    /// produce, and a fallback must be needed if one is declared.
    /// </para>
    /// </summary>
    private void CheckChoices(IReadOnlyList<Field> here, List<string> issues)
    {
        var patterns = here.GroupBy(f => f.Id, StringComparer.Ordinal)
                           .ToDictionary(g => g.Key, g => g.First().Pattern, StringComparer.Ordinal);

        foreach (var field in here)
        {
            // An assortment offers packings on the same edge and under the same rules; what differs is only
            // where the deciding value comes from — a token the component carries rather than an
            // expression over its siblings — and there is no second, encode-side reading, because the
            // value says which kinds to write and in what order.
            if (field.Pattern is Pattern.Choice { Conditioned: true } conditioned)
            {
                CheckConditions(field, conditioned, issues);
                continue;
            }

            List<(string Reading, Expr? Deciding)> readings = field.Pattern switch
            {
                Pattern.Choice { Arms.Count: > 0, Key: { } k } c => c.Selects is { } s
                    ? [("discriminator", k), ("selection", s)]
                    : [("discriminator", k)],

                Pattern.Assorted { Sorts.Count: > 0 } => [("token", null)],

                _ => [],
            };

            if (readings.Count == 0) continue;

            var offered = Offered(field);
            string packing = field.Pattern is Pattern.Assorted ? "kind" : "arm";

            foreach (var duplicate in offered.GroupBy(o => o.To.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
                issues.Add($"field '{field.Id}': two {packing}s are both named '{duplicate.Key}' — the name "
                         + "is what a later step branches on, so it has to identify one shape");

            foreach (var duplicate in offered.Where(o => !o.IsFallback).GroupBy(o => o.Key!).Where(g => g.Count() > 1))
                issues.Add($"field '{field.Id}': {duplicate.Key} selects {duplicate.Count()} {packing}s — "
                         + "which one applies is not decidable");

            if (offered.Count(o => o.IsFallback) > 1)
                issues.Add($"field '{field.Id}': only one {packing} may be the fallback");

            var fallback = offered.FirstOrDefault(o => o.IsFallback);

            // Both readings must land on the same arms, so both are proved. A pair that disagreed about
            // its own keyset would be two discriminators wearing one name.
            foreach (var (reading, deciding) in readings)
            {
                var reachable = deciding is null ? null : Pattern.ReachableKeys(deciding, patterns);

                if (reachable is null)
                {
                    if (fallback is null)
                        issues.Add($"field '{field.Id}': the engine cannot compute which values this "
                                 + $"{reading} can take, so it cannot prove the {packing}s are exhaustive. "
                                 + $"Declare a fallback {packing} (key null). An unanticipated {reading} "
                                 + "otherwise binds no fields and reports no error, which is worse than "
                                 + "either outcome you would have chosen.");
                    continue;
                }

                foreach (var arm in offered.Where(o => !o.IsFallback))
                    if (!reachable.Contains(arm.Key!))
                        issues.Add($"field '{field.Id}': {packing} '{arm.To.Name}' is keyed {arm.Key}, "
                                 + $"which this {reading} can never produce — a dead {packing} is a mistake "
                                 + "about the mask, not a harmless extra");

                var covered = offered.Where(o => !o.IsFallback).Select(o => o.Key!).ToHashSet();
                var missing = reachable.Where(k => !covered.Contains(k)).OrderBy(k => k.ToString(),
                                                                                 StringComparer.Ordinal).ToList();

                if (missing.Count > 0 && fallback is null)
                    issues.Add($"field '{field.Id}': the {packing}s are not exhaustive — nothing handles "
                             + string.Join(", ", missing.Select(Listed))
                             + $". Add the {packing}s, or declare a fallback.");

                if (missing.Count == 0 && fallback is not null)
                    issues.Add($"field '{field.Id}': {packing} '{fallback.To.Name}' is the fallback, but the "
                             + $"other {packing}s already cover every value this {reading} can take, so it "
                             + "can never be selected");
            }
        }
    }

    /// <summary>
    /// Conditioned packings, proved rather than trusted.
    ///
    /// <para>
    /// This is what makes guards admissible here when they were refused everywhere else. Sibling guards
    /// over arbitrary expressions cannot be checked for cover or overlap by anything — which is how an
    /// unanticipated message binds no fields and reports nothing. A condition over <c>present.*</c> is
    /// different in kind: the parts are declared and finite, so every combination of them can be tried and
    /// the two questions actually answered. Exactly one packing must be an option in every world, or the
    /// engine says which world breaks it and how.
    /// </para>
    ///
    /// <para>
    /// The same enumeration a mask gets, and the same ceiling: past a handful of parts the worlds outrun
    /// any real arm list, and demanding an unconditioned packing to fall back on is both cheaper and more
    /// honest than trying 2^20 of them.
    /// </para>
    /// </summary>
    private void CheckConditions(Field field, Pattern.Choice choice, List<string> issues)
    {
        var conditions = choice.Arms.Where(a => a.Condition is not null).ToList();
        var fallbacks = choice.Arms.Where(a => a.Condition is null).ToList();

        if (conditions.Count == 0)
        {
            issues.Add($"field '{field.Id}': it has no discriminator, so its packings have to say when each "
                     + "of them is an option. None of them does.");
            return;
        }

        if (fallbacks.Count > 1)
            issues.Add($"field '{field.Id}': {fallbacks.Count} packings are options whatever happens, so "
                     + "which one applies is never decided");

        var parts = conditions
            .Select(a => ComputationOf(field, a.Condition!))
            .Where(c => c is not null)
            .SelectMany(c => c!.Wants)
            .Where(w => w.From == Origin.Presence)
            .Select(w => w.Name)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        foreach (var part in parts)
            if (!Optionals.Any(o => string.Equals(o.Key.CaptureName, part, StringComparison.Ordinal)))
                issues.Add($"field '{field.Id}': a packing asks whether '{part}' is present, and no "
                         + "component of this message has a part by that name. Something that is always "
                         + "there or never declared has no presence to ask about.");

        if (parts.Count > 12)
        {
            if (fallbacks.Count == 0)
                issues.Add($"field '{field.Id}': its packings turn on {parts.Count} parts, which is too "
                         + "many combinations to prove one always applies. Declare a packing with no "
                         + "condition to fall back on.");
            return;
        }

        var evaluator = new Evaluator();

        for (int world = 0; world < 1 << parts.Count; world++)
        {
            Dictionary<string, ProtoValue> which = new(StringComparer.Ordinal);
            for (int p = 0; p < parts.Count; p++) which[parts[p]] = ProtoValue.Of((world & (1 << p)) != 0);

            var scope = new EvalScope().Set(Vocabulary.Present, new ProtoValue.Rec(which));

            List<string> options = [.. conditions
                .Where(a => evaluator.Eval(a.Condition!, scope) is ProtoValue.Bool { Value: true })
                .Select(a => a.Name)];

            string when = which.Count == 0 ? "always"
                : string.Join(" and ", which.Select(kv => kv.Value.AsBool() ? kv.Key : $"no {kv.Key}"));

            if (options.Count > 1)
                issues.Add($"field '{field.Id}': with {when}, {options.Count} packings are options at once "
                         + $"({string.Join(", ", options)}) — which applies would come down to the order "
                         + "they were written in");

            if (options.Count == 0 && fallbacks.Count == 0 && !Forbidden(scope, evaluator))
                issues.Add($"field '{field.Id}': with {when}, no packing is an option. Add one, declare a "
                         + "packing with no condition to fall back on, or say that this combination may "
                         + "never happen — a rule that excludes it is a document meaning it, and nothing "
                         + "otherwise separates that from having forgotten the case.");
        }
    }

    /// <summary>
    /// Whether the document says this combination may never happen.
    ///
    /// <para>
    /// Leaving a world with no packing is either a case somebody forgot or a case the protocol calls
    /// malformed, and those need to look different or the check is worthless in one direction and wrong in
    /// the other. A rule saying the two may never combine is a document <i>meaning</i> it — and it carries
    /// the reason, which the absence of a packing never could.
    /// </para>
    ///
    /// <para>
    /// Only rules answerable from presence alone are consulted. One that also reads a field is about this
    /// message rather than about which combinations exist, and has no answer in a hypothetical world.
    /// </para>
    /// </summary>
    private bool Forbidden(EvalScope world, Evaluator evaluator)
        => Rules.OfType<Rule.Excludes>()
                .Where(r => r.Expressions.All(
                    e => Vocabulary.RootsOf(e).All(root => root == Vocabulary.Present)))
                .Any(r => evaluator.Eval(r.One, world) is ProtoValue.Bool { Value: true }
                       && evaluator.Eval(r.Other, world) is ProtoValue.Bool { Value: true });

    /// <summary>
    /// Distinctness, checked against the set it is really about.
    ///
    /// <para>
    /// This is where exclusivity stops being a label. Asking for two structures to carry different values
    /// is only coherent when a value settles which thing is meant; over an indicative set the same number
    /// twice is not a contradiction, and a rule saying it is would refuse well-formed messages. So the
    /// engine refuses the <i>rule</i>, at document time, and says which of the set's three answers made it
    /// incoherent.
    /// </para>
    /// </summary>
    private void CheckDistinctness(List<string> issues)
    {
        foreach (var rule in Rules.OfType<Rule.Distinct>())
        {
            if (rule.Chain.Pattern is not Pattern.Chain chain)
            {
                issues.Add($"message '{Id}': {rule} — but '{rule.Chain.Name}' is not a chain, so there are "
                         + "no structures to be distinct from each other");
                continue;
            }

            if (!ScopeFields([chain.Element]).Contains(rule.Of))
            {
                issues.Add($"message '{Id}': {rule} — but '{rule.Of.Name}' is not a field of the structure "
                         + $"'{rule.Chain.Name}' repeats, so no structure has one to compare");
                continue;
            }

            if (DrawnFrom(rule.Of) is not { } edge)
            {
                issues.Add($"message '{Id}': {rule} — but '{rule.Of.Name}' does not say what set its values "
                         + "come from, and whether repeating a value means anything is a property of the "
                         + "set rather than of the field. Declare one.");
                continue;
            }

            if (((ValueSet)edge.To).Exclusivity == Exclusivity.Indicative)
                issues.Add($"message '{Id}': {rule} — but '{edge.To.Name}' is indicative, so a value points "
                         + "at a member without settling it and the same value twice need not be about the "
                         + "same thing. Requiring distinctness over it would refuse messages that are well "
                         + "formed. Either the set is definitive and should say so, or this rule is not "
                         + "the one you want.");
        }
    }

    /// <summary>
    /// References, checked against the order they will be written in.
    ///
    /// <para>
    /// Forwards is the interesting one. It is not a resolver problem — a pointer's extent is fixed by its
    /// declaration, so the chain of positions never runs through its value and a forward reference would
    /// settle perfectly well. It is a <i>protocol</i> problem: a reader meeting a forward pointer has not
    /// read the target yet, and two pointing at each other is a loop no reader escapes. So the engine says
    /// so, rather than emitting something only it can read.
    /// </para>
    /// </summary>
    private void CheckReferences(List<string> issues)
    {
        var order = AllFields.ToList();

        foreach (var field in order)
        {
            if (field.Points is not { } points) continue;

            if (field.Value is not null)
                issues.Add($"message '{Id}': '{field.Id}' both points at '{points.Target.Name}' and has a "
                         + "value of its own. A reference is written from what it refers to; a second "
                         + "answer to the same question is one of them being ignored.");

            if (Named(points.Target) is not Field named)
            {
                issues.Add($"message '{Id}': '{field.Id}' points at '{points.Target.Name}', which names "
                         + "nothing this message contains. A concept is a lookup; one that resolves to "
                         + "nothing is a reference to an idea rather than to a part.");
                continue;
            }

            if (!Concepts.Contains(points.Target))
                issues.Add($"message '{Id}': '{field.Id}' points at '{points.Target.Name}', which this "
                         + "document does not declare");

            int here = order.IndexOf(field);
            int there = order.IndexOf(named);

            if (there < 0)
                issues.Add($"message '{Id}': '{field.Id}' points at '{points.Target.Name}', which names "
                         + $"'{named.Name}' — not a node of this message");

            else if (there >= here)
                issues.Add($"message '{Id}': '{field.Id}' points forwards at '{points.Target.Name}'. A "
                         + "reader meeting it has not read the target yet, and two references pointing at "
                         + "each other are a loop nothing escapes — which is why this is refused even "
                         + "though the offset itself would resolve.");
        }
    }

    private void CheckVocabulary(List<string> issues)
    {
        IEnumerable<(Node Owner, Expr Expression, string What, ExprSite Site)> everywhere =
        [
            .. Expressions(AllFields).Select(e => ((Node)e.Owner, e.Expression, e.What, e.Site)),
            .. Rules.SelectMany(rule => rule.Expressions.Select(
                   e => ((Node)rule, e, "a condition",
                         rule is Rule.Arrangement ? ExprSite.Pairing : ExprSite.Condition))),
        ];

        foreach (var (owner, expression, what, site) in everywhere)
        {
            var answerable = Vocabulary.Available(site);

            foreach (var root in Vocabulary.RootsOf(expression))
            {
                if (answerable.Contains(root)) continue;

                issues.Add(Vocabulary.All.Contains(root)
                    ? $"message '{Id}': {what} of '{owner.Name}' names `{root}`, and "
                    + $"{Vocabulary.Why(root, site)}."
                    : $"message '{Id}': {what} of '{owner.Name}' names `{root}`, which is not part of the "
                    + "walk's vocabulary at all. A root nothing binds reads as nothing and makes every "
                    + $"comparison against it false. The ones that exist are: {string.Join(", ", Vocabulary.All.Order())}.");
            }
        }
    }

}
