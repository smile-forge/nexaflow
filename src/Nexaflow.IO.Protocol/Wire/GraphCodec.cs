using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Reads and writes a message by walking the arrangement, holding what it finds in a run.
///
/// <para>
/// The rule this exists to keep: <b>once a build starts, information comes from the protocol graph or the
/// run graph, and nowhere else.</b> There is no scope threaded through the calls, no frame stack, no
/// ambient names. A computation's inputs are assembled from <i>its own</i> <see cref="Requires"/> edges
/// immediately before it runs, so what an expression can see is exactly what the graph says it may — and a
/// document that reads something it never declared an edge to gets nothing, which the document checks
/// already refuse at authoring time.
/// </para>
///
/// <para>
/// <b>Beside <see cref="MessageCodec"/>, not instead of it, and not for long.</b> The old codec walks
/// containment and threads a scope; this walks <c>Starts</c>/<c>Then</c>/<c>Holds</c> and threads nothing.
/// Two things describing one protocol is the pattern this whole rewrite is removing, so this is only
/// tolerable while it is growing to replace the other — the corpus test that checks the arrangement
/// against containment is what keeps them honest until then.
/// </para>
/// </summary>
public sealed class GraphCodec(ProtocolGraph graph,
                               ConverterTable? converters = null,
                               Implementations? provided = null)
{
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;
    private readonly Evaluator _evaluator = new(converters ?? ConverterTable.Default);
    private readonly Implementations _provided = provided ?? Implementations.None;

    /// <summary>The description, reachable from the nested walk.</summary>
    private ProtocolGraph Graph => graph;


    /// <summary>
    /// What this codec can walk, which is a consequence rather than a claim.
    /// </summary>
    /// <remarks>
    /// It used to be a hand-kept list of the arms <see cref="Write"/> happened to have, and it was wrong in
    /// both directions at once: it named a shape the writer would have thrown on, and it omitted one the
    /// writer could not have written — so a document with that field <b>encoded silently without it</b>.
    /// Now a field is one this walks if something can lay its value down, which is the same question the
    /// writer asks, so the two cannot disagree.
    /// </remarks>
    public static bool Handles(Field field)
        => field.Pattern.Form is not null || field.Pattern is Pattern.Bits or Pattern.Group or Pattern.Chain;

    // ── Writing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the message, settling each fact when whatever it waits on has settled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order the walk lays fields out in is <b>not</b> the order they can be worked out in. A length
    /// measures a span that comes after it, so it cannot be written until that span has been — and the
    /// span may itself hold something derived from a third field. Writing forwards and hoping works for
    /// well-ordered documents and no others.
    /// </para>
    /// <para>
    /// So the walk says <i>what</i> is in the message and the resolver says <i>when</i> each fact can be
    /// had: every appearance declares what its value waits on, taken from its own edges, and the worklist
    /// settles whatever is ready. Nothing here is a pass, and nothing is back-patched.
    /// </para>
    /// </remarks>
    public byte[] Encode(IReadOnlyDictionary<string, ProtoValue> supplied)
    {
        var run = RunGraph.Begin(graph, supplied);
        var resolver = new Resolver();
        var laying = new Laying(this, run, source: null);

        // One worklist for both. Where the message goes and what is in it are settled by the same
        // machinery, so a fork decided on a field waits for that field exactly as a length waits for the
        // span it measures — and a value fixed before anything ran is simply already settled.
        resolver.Add(laying.Reaching(graph.Root, previous: null));
        resolver.Resolve();

        var wire = new BitWriter();

        foreach (var place in laying.Order)
        {
            var appearance = run.For(place);

            // A part that is not there writes nothing, which is the whole of what absence does on the way
            // out. The walk still reached it — being reached and being present are different facts.
            if (appearance.Has(Facet.Present) && appearance.Settled(Facet.Present) is false) continue;

            int began = wire.Written;

            // A carrier's value is already the octets the inner message came to, so laying it down is
            // putting them there. That is the whole of what a layer costs the outer walk.
            if (place is Subprotocol) wire.Put(appearance.Value.AsBytes());
            else Write(wire, (Field)place, appearance.Value);

            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(wire.Since(began)));
        }

        Vouch(run);

        return wire.Done(graph.Id);
    }

    /// <summary>
    /// The path being laid out, one place at a time, as work the resolver schedules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reaching a place is a <see cref="Facet.Realised"/> like any other fact, and settling it brings the
    /// next place into existence. So the walk is not a thing that happens before values are worked out and
    /// then hands over — the two are the same worklist, and a fork that turns on a field waits for that
    /// field the way anything waits for anything.
    /// </para>
    /// <para>
    /// Reaching a place waits on two things: having reached the one before it, and whatever the decision
    /// <i>at</i> it needs. The second is looked up rather than assumed, because assuming it — waiting for
    /// every decision in the message — invents a cycle wherever a fork turns on a field that is itself
    /// derived from something later.
    /// </para>
    /// </remarks>
    private sealed class Laying(GraphCodec codec, RunGraph run, BitCursor? source)
    {

        /// <summary>Whether this is reading. The octets being read <i>are</i> the direction — there is no
        /// second flag that could disagree with whether there is anything to read from.</summary>
        private bool Reading => source is not null;

        /// <summary>The places that make octets, in the order the path reached them.</summary>
        public List<Node> Order { get; } = [];

        public ResolutionNode Reaching(Node place, Node? previous)
        {
            var appearance = run.For(place);
            var field = place as Field;

            // One refusal, on the one path both directions take. It used to live in the reading walk only,
            // so writing a field nothing could lay down produced a message quietly missing it. A place that
            // is a field is either one this walks or an error — never something to step over.
            if (field is not null && !Handles(field))
                throw new ProtoTypeException(
                    $"field '{field.Id}' is a {field.Pattern.GetType().Name.ToLowerInvariant()}, which "
                  + "this walk does not handle yet");

            // A carrier makes octets the way a field does — the inner message's, rather than one value's.
            // Everything past here treats the two alike, which is the point of the carrier being a place.
            bool carries = field is not null || place is Subprotocol;

            // A set makes no octets and spans the ones its members made. That is not a technicality about
            // where a number comes from: it is what lets a length measure a header while the header writes
            // nothing at all, and it is why the extent is a fact about the members rather than something
            // the set was told. Both directions, because both have members with extents by the time the
            // last one is done — which is exactly when this settles, and not before.
            bool spans = place is FieldSet;

            List<FacetRef> before = previous is null ? [] : [new FacetRef(run.For(previous), Facet.Realised)];
            before.AddRange(Deciding(place));

            // Whether a part is there is decided when the walk reaches it, for the same reason a fork is:
            // it changes where the path goes. An absent set is skipped whole, so the next place cannot be
            // known until presence is — which makes this a prerequisite of arriving, not a fact settled
            // afterwards. It is also what makes the two directions agree without arranging for them to.
            if (Optional(place)) before.AddRange(codec.Awaits(run, appearance, place, "presence"));

            return new ResolutionNode
            {
                Id = appearance,

                // Going out, a fact is scheduled: extent waits on value, value waits on whatever the
                // computation asks for, and the order they settle in is not the order they are laid down.
                // Coming in, there is nothing to schedule — see Intake. So reading declares every facet
                // settled on sight and does the work as the place is reached.
                NotApplicable = spans
                    ? new HashSet<Facet> { Facet.Present, Facet.Value, Facet.Position, Facet.Emitted }
                    : carries && !Reading
                        ? new HashSet<Facet> { Facet.Present, Facet.Position, Facet.Emitted }
                        : new HashSet<Facet>
                            { Facet.Present, Facet.Extent, Facet.Value, Facet.Position, Facet.Emitted },

                DependenciesFor = facet => facet switch
                {
                    Facet.Realised => before,

                    Facet.Value => carries && !Reading && field is not null
                        ? codec.Waits(run, appearance, field)
                        : [],

                    Facet.Extent when spans => codec.Spanned(run, place, Reading),

                    Facet.Extent => carries && !Reading && field?.Pattern.StaticWidth is null
                        ? [new FacetRef(appearance, Facet.Value)]
                        : [],

                    _ => [],
                },

                Settle = (facet, _) =>
                {
                    switch (facet)
                    {
                        case Facet.Realised:
                            bool here = !Optional(place) || codec.Asked(run, appearance, place);

                            if (Optional(place)) appearance.Settle(Facet.Present, here);

                            if (carries && here)
                            {
                                Order.Add(place);

                                if (source is not null) codec.Intake(run, appearance, place, source);
                            }
                            else if (carries)
                            {
                                // It contributed nothing, and says so rather than having no answer. A
                                // length over a region holding it adds a real zero; asking an absent part
                                // for its extent and getting an error would make every such length a
                                // special case about optionality.
                                appearance.Settle(Facet.Extent, 0);
                                appearance.Settle(Facet.Emitted, ProtoValue.Of(Array.Empty<byte>()));
                            }

                            return Next(place, here) is { } next
                                ? FacetResult.Expanding(null, Reaching(next, place))
                                : FacetResult.Of(null);

                        case Facet.Value:
                            // An absent part computes nothing. Not an optimisation: its expression may
                            // well read the very input whose absence is the reason it is not here.
                            if (Missing(appearance)) return FacetResult.Of(null);

                            return FacetResult.Of(field is not null
                                ? codec.Settle(run, appearance, field)
                                : codec.Sealed(run, appearance, (Subprotocol)place));

                        case Facet.Extent when spans:
                            if (Missing(appearance)) return FacetResult.Of(0);

                            var across = codec.Spread(run, appearance, place);

                            if (across % 8 != 0)
                                throw new ProtoTypeException(
                                    $"'{place.Name}' spans {across} bits, which is not a whole number of "
                                  + "octets. A set that ends mid-octet puts everything after it half an "
                                  + "octet out, which reads as plausible values from the wrong place.");

                            appearance.Settle(Facet.Extent, (int)(across / 8));
                            return FacetResult.Of((int)(across / 8));

                        case Facet.Extent:
                            if (Missing(appearance)) return FacetResult.Of(0);

                            var width = codec.Sized(appearance, field);
                            appearance.Settle(Facet.Extent, width);
                            return FacetResult.Of(width);

                        default: return FacetResult.Of(null);
                    }
                },
            };
        }

        /// <summary>Whether the path may arrive here and find nothing — by either packing edge.</summary>
        private bool Optional(Node place) => codec.Graph.MayBeAbsent(place);

        /// <summary>Being here, where that is in question at all.</summary>
        private IEnumerable<FacetRef> Present(RunNode appearance, Node place)
            => Optional(place) ? [new FacetRef(appearance, Facet.Present)] : [];

        /// <summary>Whether this place turned out not to be here.</summary>
        private static bool Missing(RunNode appearance)
            => appearance.Has(Facet.Present) && appearance.Settled(Facet.Present) is false;

        /// <summary>What the decision at this place needs before it can be made.</summary>
        private List<FacetRef> Deciding(Node place)
        {
            if (codec.Graph.From<Then>(place).Count() < 2) return [];

            var deciding = codec.Graph.From<Decides>(place)
                                .FirstOrDefault(d => d.Reading == Reading)
                        ?? codec.Graph.From<Decides>(place).FirstOrDefault();

            return deciding?.To switch
            {
                Evaluated evaluated =>
                    [.. codec.Graph.InputsOf(evaluated).Where(e => e.To is Field or Subprotocol)
                            .Select(e => new FacetRef(run.Reach(run.For(place), e.To),
                                                      Named(e.Facet)))],

                Field announced => [new FacetRef(run.Reach(run.For(place), announced), Facet.Value)],

                _ => [],
            };
        }

        /// <summary>
        /// Where the path goes from here.
        /// </summary>
        /// <remarks>
        /// One question, because a set is on the path rather than beside it. This used to keep a stack of
        /// queues — where to resume once the members of a set ran out — which existed only because the
        /// membership edge was being walked as though it were the path. A set's members are reached by the
        /// ordinary way on, so there is nothing to come back to and nothing to remember being inside.
        /// </remarks>
        private Node? Next(Node place, bool here)
        {
            // A set that is not there takes its members with it. They are on the path, so skipping the set
            // means stepping over all of them — from where the last one would have led. That is the whole
            // difference between an absent part and an absent section, and it is why presence has to be
            // known before the next place is: an optional header is one step, however many fields deep.
            if (!here && place is FieldSet set && codec.Graph.Members(set).LastOrDefault() is { } last)
                return codec.Onward(run, last, Reading);

            return codec.Onward(run, place, Reading);
        }
    }

    /// <summary>
    /// What a set's span waits on, which is a different question in each direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Going out, every member's extent — a set is as wide as what it holds, and what it holds is worked
    /// out before it. Coming in, the members are read <b>after</b> the set is reached, so waiting on their
    /// extents is waiting on facts the walk has not gone and got yet: it waits instead on the last thing
    /// under the set having been read, which is the moment all of them have.
    /// </para>
    /// <para>
    /// The last thing under it, not the last member: a set whose final member is itself a set is realised
    /// the moment the walk arrives at that inner set, which is before any of its contents exist.
    /// </para>
    /// </remarks>
    private List<FacetRef> Spanned(RunGraph run, Node set, bool reading)
        => reading
            ? Last(set) is { } last ? [new FacetRef(run.For(last), Facet.Realised)] : []
            : [.. graph.Members(set).Select(m => new FacetRef(run.For(m), Facet.Extent))];

    /// <summary>The last place under a set that is not itself a set.</summary>
    private Node? Last(Node set)
        => graph.Members(set).LastOrDefault() switch
        {
            FieldSet inner => Last(inner),
            { } member => member,
            _ => null,
        };

    /// <summary>
    /// How many <b>bits</b> a place takes up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Octets are too coarse to add up a set with, and not by a little: a four-bit offset and eight control
    /// bits have an extent of zero octets each, so summing extents makes TCP's header eighteen octets long
    /// rather than twenty. It is not that the extents are wrong — a one-bit field genuinely does not occupy
    /// an octet — it is that the question "how far does this set run" is a question about bits, and only
    /// the answer is about octets.
    /// </para>
    /// <para>
    /// A form that knows its own width is asked; anything else is measured by the extent it settled, which
    /// is what an opaque span or a value-width integer has and a fixed one does not need. A part that
    /// turned out not to be there contributes nothing, rather than contributing the width it would have had.
    /// </para>
    /// </remarks>
    private long Spread(RunGraph run, RunNode appearance, Node place)
    {
        if (appearance.Has(Facet.Present) && appearance.Settled(Facet.Present) is false) return 0;

        if (place is FieldSet set)
            return graph.Members(set).Sum(m => Spread(run, run.For(m), m));

        if (place is Field { Pattern: var pattern } && pattern.Form?.FixedBits is { } fixedBits)
            return fixedBits;

        return appearance.Has(Facet.Extent) ? Convert.ToInt64(appearance.Settled(Facet.Extent)) * 8 : 0;
    }

    /// <summary>What a named facet's computation waits on, taken from the edges that ask for it.</summary>
    private List<FacetRef> Awaits(RunGraph run, RunNode appearance, Node place, string facet)
    {
        if (graph.ProducerOf(place, facet) is not { } producing) return [];

        List<FacetRef> waits = [];

        foreach (var wanted in graph.InputsOf(producing))
            if (wanted.To is Field or Subprotocol)
                waits.Add(new FacetRef(run.Reach(appearance, wanted.To), Named(wanted.Facet)));

        return [.. waits.Distinct()];
    }

    /// <summary>
    /// Whether an optional place is here, by asking what the document said decides it.
    /// </summary>
    /// <remarks>
    /// A place with nothing deciding it is here — but a document cannot get into that state, because an
    /// optional step with no condition is refused at authoring time. So this answering true by default is
    /// about the ordinary case of a part that is simply always present, not a guess about an optional one.
    /// </remarks>
    private bool Asked(RunGraph run, RunNode appearance, Node place)
        => graph.ProducerOf(place, "presence") switch
        {
            null => true,
            Constant stated => stated.Holds.AsBool(),
            Evaluated evaluated => _evaluator.Eval(evaluated.Runs, Given(run, appearance, evaluated)).AsBool(),
            var other => throw new ProtoTypeException(
                             $"'{place.Name}': '{other.Name}' cannot decide whether something is there"),
        };

    /// <summary>Every fact this appearance's value waits on, taken from the edges that ask for them.</summary>
    private List<FacetRef> Waits(RunGraph run, RunNode appearance, Field field)
    {
        List<FacetRef> waits = [];

        var asking = Assembled(field)
            ? Runs(field).Select(s => graph.ProducerOf(s, "value")).OfType<Computation>()
            : graph.ProducerOf(field, "value") is { } one ? [one] : [];

        foreach (var computation in asking)
            foreach (var wanted in graph.InputsOf(computation))
                if (wanted.To is Field or Subprotocol)
                    waits.Add(new FacetRef(run.Reach(appearance, wanted.To), Named(wanted.Facet)));

        return [.. waits.Distinct()];
    }

    /// <summary>
    /// How wide this is: what the declaration fixes, or however many octets the value came to.
    /// </summary>
    private int Sized(RunNode appearance, Field? field)
    {
        // A carrier is however many octets the inner message came to. Nothing measures it twice: the value
        // is already those octets by the time anything asks.
        if (field is null) return appearance.Value.AsBytes().Length;

        if (field.Pattern.StaticWidth is { } declared) return declared;

        var measured = new BitWriter();
        Write(measured, field, appearance.Value);

        return measured.Written / 8;
    }

    /// <summary>
    /// How far a carried layer runs: what measures it, or everything left.
    /// </summary>
    /// <remarks>
    /// The measuring computation hangs off the field the carrier replaced, because that is where a
    /// document writes it. One thing, one extent — reached from whichever of the two nodes is asking.
    /// </remarks>
    private int Reaches(RunGraph run, RunNode appearance, Subprotocol layer, BitCursor source)
        => graph.ProducerOf(layer, "extent") is Evaluated measured
            ? (int)_evaluator.Eval(measured.Runs, Given(run, appearance, measured)).AsInt()
            : source.Remaining / 8;

    private static Facet Named(string facet) => facet switch
    {
        "extent" => Facet.Extent,
        "octets" => Facet.Emitted,
        "position" => Facet.Position,
        _ => Facet.Value,
    };

    /// <summary>
    /// What a field's value comes to, from the computation the graph hangs off it.
    /// </summary>
    /// <remarks>
    /// The scope is built <i>here</i>, from this computation's own edges, and thrown away after. That is
    /// the difference from threading one: an expression cannot see a value merely because the walk
    /// happened to pass it, only because an edge says it may.
    /// </remarks>
    private ProtoValue Settle(RunGraph run, RunNode appearance, Field field)
    {
        // A bit group written from its runs has no expression of its own — each run has one, and the
        // group is what they come to together.
        if (Assembled(field))
        {
            Dictionary<string, ProtoValue> runs = new(StringComparer.Ordinal);

            // Asked of the run, not of the group: the run owns its requirements path, which is the whole
            // reason it is a node.
            foreach (var slice in Runs(field))
                runs[slice.Name] = Produces(slice) switch
                {
                    Constant stated => stated.Holds,
                    Evaluated evaluated => _evaluator.Eval(evaluated.Runs, Given(run, appearance, evaluated)),
                    var other => throw new ProtoTypeException($"'{other.Name}' cannot produce a run"),
                };

            var packed = new ProtoValue.Rec(runs);
            Vet(appearance, field, packed);
            appearance.Settle(Facet.Value, packed);
            return packed;
        }

        // A constant answers without being run. That is what a constant is, and it is why a fixed
        // delimiter needs no expression and can be pointed at by the span that ends before it.
        var value = Produces(field) switch
            {
                Constant stated => stated.Holds,
                Evaluated evaluated => _evaluator.Eval(evaluated.Runs, Given(run, appearance, evaluated)),
                var other => throw new ProtoTypeException($"'{other.Name}' cannot produce a value here"),
            };

        if (field.Via is not null) value = Applied(field, value, forward: true);

        Vet(appearance, field, value);
        appearance.Settle(Facet.Value, value);
        return value;
    }

    // ── Carried protocols ─────────────────────────────────────────────────────

    /// <summary>
    /// What a carrier comes to: the inner message, built, then whatever the seam does to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing crosses the seam yet, and that is a gap rather than a decision.</b> An inner document
    /// that asks for an input is refused here, naming it. The tempting fix — hand the inner run whatever
    /// outside values the outer run happens to hold — is precisely the ambient read this engine exists
    /// without: the outer document would be feeding the inner one by coincidence of naming, with no edge
    /// saying so and nothing able to check it.
    /// </para>
    /// <para>
    /// What it wants is for the carrier to say what feeds the protocol beneath, in the outer document's
    /// terms, as computations on <see cref="Requires"/> edges — the same shape as a converter's arguments.
    /// Then a layer is fed the way everything else is, and a document that does not feed it says so at
    /// authoring time rather than at the first octet.
    /// </para>
    /// </remarks>
    private ProtoValue Sealed(RunGraph run, RunNode appearance, Subprotocol layer)
    {
        var octets = layer.Carries switch
        {
            Carriage.Described inner => ProtoValue.Of(
                new GraphCodec(inner.Message.Graph, _converters, _provided)
                    .Encode(Fed(run, appearance, layer, inner.Message.Graph))),

            Carriage.Provided host => ProtoValue.Of(
                _provided.Get(host.Implementation, layer.Id)
                         .Encode(new ProtoValue.Rec((Dictionary<string, ProtoValue>)
                             Fed(run, appearance, layer, null)))),

            _ => throw new ProtoTypeException($"'{layer.Id}' does not say what it carries"),
        };

        if (layer.Through is { } transform) octets = transform.Apply(octets, null, _evaluator);

        foreach (var via in layer.Via) octets = Through(via, octets, layer.Id);

        appearance.Settle(Facet.Value, octets);
        return octets;
    }

    /// <summary>The same journey backwards: undo the seam, then read the inner message.</summary>
    private ProtoValue Unsealed(Subprotocol layer, byte[] octets)
    {
        ProtoValue value = ProtoValue.Of(octets);

        foreach (var via in layer.Via.Reverse()) value = Through(Backwards(via, layer.Id), value, layer.Id);

        if (layer.Through is { } transform) value = transform.Undo(value, null, _evaluator);

        return layer.Carries switch
        {
            Carriage.Described inner => Gathered(
                new GraphCodec(inner.Message.Graph, _converters, _provided).Decode(value.AsBytes())),

            Carriage.Provided host => _provided.Get(host.Implementation, layer.Id).Decode(value.AsBytes()),

            _ => throw new ProtoTypeException($"'{layer.Id}' does not say what it carries"),
        };
    }

    /// <summary>What an inner run came to, as one value the outer message can hold.</summary>
    private static ProtoValue Gathered(RunGraph inner)
    {
        Dictionary<string, ProtoValue> parts = new(StringComparer.Ordinal);

        foreach (var node in inner.Nodes)
            if (node.Of is Field part && node.Has(Facet.Value)) parts[part.Id] = node.Value;

        return new ProtoValue.Rec(parts);
    }

    /// <summary>
    /// What the protocol beneath is given, from the carrier's own edges.
    /// </summary>
    /// <remarks>
    /// Each fed value is a computation on a <see cref="Requires"/> edge, run here and named by the inner
    /// document's key. So a layer is fed exactly as a converter's arguments are, and what an inner
    /// protocol depends on is a thing the graph records rather than a scope handed down at the seam.
    /// </remarks>
    private IReadOnlyDictionary<string, ProtoValue> Fed(RunGraph run, RunNode here,
                                                        Subprotocol layer, ProtocolGraph? inner)
    {
        Dictionary<string, ProtoValue> given = new(StringComparer.Ordinal);

        foreach (var wanted in graph.InputsOf(layer))
            if (wanted.To is Computation feeding)
                given[feeding.Label] = feeding switch
                {
                    Constant stated => stated.Holds,
                    Evaluated evaluated => _evaluator.Eval(evaluated.Runs, Given(run, here, evaluated)),
                    var other => throw new ProtoTypeException(
                                     $"'{layer.Id}': '{other.Name}' cannot feed a carried protocol"),
                };

        // An inner document asking for something the carrier never named is a hole, and it is the carrier's
        // to fill: the inner protocol is a separate document and cannot know what the outer one calls things.
        var missing = inner?.Nodes.OfType<Context>().Select(c => c.Key)
                           .Where(k => !given.ContainsKey(k)).Order().ToList() ?? [];

        return missing.Count == 0
            ? given
            : throw new ProtoTypeException(
                  $"'{layer.Id}' carries '{inner!.Id}', which asks for {string.Join(", ", missing)} — and "
                + "the carrier does not say what feeds that. Name it among the carrier's feeds, in this "
                + "document's terms, the way a converter names its arguments.");
    }

    /// <summary>The field a carrier replaced on the path, which is where its extent is computed.</summary>
    private Field? Carrier(Subprotocol layer)
        => graph.To<Embeds>(layer).FirstOrDefault()?.From as Field;

    private ProtoValue Through(Conversion via, ProtoValue value, string owner)
        => _converters.TryGet(via.Name, out var converter) && converter is not null
            ? converter.Apply(value, via.Args)
            : throw new ProtoTypeException($"'{owner}': unknown converter '{via.Name}'");

    private Conversion Backwards(Conversion via, string owner)
        => _converters.TryGet(via.Name, out var converter) && converter?.Inverse is { } inverse
            ? via with { Name = inverse }
            : throw new ProtoTypeException($"'{owner}': converter '{via.Name}' declares no inverse");

    /// <summary>
    /// What works out a node's value.
    /// </summary>
    /// <remarks>
    /// Found by the edge that says what it produces, not by matching the identity of the expression it was
    /// written from. The expression-identity version could only ever work while the graph was built in the
    /// same process that parsed the document: written out and read back there is no original object to be
    /// the same as, and every lookup would miss. Asking "what produces this node's value" is the question
    /// anyway — the expression was one way to arrive at an answer, never the answer itself.
    /// </remarks>
    /// <summary>
    /// A bit group's runs, as the graph holds them.
    /// </summary>
    /// <remarks>
    /// Not the ones inside the pattern. A run is a node — it owns its own requirements path, which is the
    /// whole reason it is one — and the pattern carries a second set of objects with the same names and
    /// widths. Asking the pattern gets things that look right and are not the nodes anything computed
    /// against, so every lookup on them returns nothing.
    /// </remarks>
    /// <summary>Whether this group is written from its runs, each computing itself, rather than from one
    /// value that is already a record. A fact about what the graph says produces things, not about the
    /// shape — the shape carries a second copy of the runs and they compute nothing.</summary>
    private bool Assembled(Field field)
        => Runs(field).Any(s => graph.ProducerOf(s, "value") is not null);

    private IEnumerable<BitSlice> Runs(Field field)
        => graph.InputsOf(field).Select(e => e.To).OfType<BitSlice>();

    private Computation Produces(Node owner)
        => graph.ProducerOf(owner, "value")
        ?? throw new ProtoTypeException(
               $"'{owner.Name}' has nothing to compute its value, so it cannot be written");

    /// <summary>
    /// Everything one computation is allowed to see, assembled from the edges that say so.
    /// </summary>
    private EvalScope Given(RunGraph run, RunNode here, Computation computation)
    {
        Dictionary<string, ProtoValue> parts = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> outside = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> spans = new(StringComparer.Ordinal);

        foreach (var wanted in graph.InputsOf(computation))
            switch (wanted.To)
            {
                case Field or Subprotocol:
                {
                    var appearance = run.Reach(here, wanted.To);

                    parts[wanted.To.Name] = EvalScope.Record(
                        ("value", Held(appearance, Facet.Value)),
                        ("extent", Held(appearance, Facet.Extent)),
                        ("octets", Held(appearance, Facet.Emitted)));
                    break;
                }

                // A set is not a field and is asked different questions, so it answers under its own name.
                // It has no value — it produced nothing — and that is the whole distinction: a length that
                // measures a header is reading a fact about what the header holds, not a number the header
                // computed. Putting both under `fields` would give every container a value it has no
                // business having, which is the confusion the set node exists to end.
                case FieldSet set:
                {
                    var appearance = run.For(set);

                    spans[set.Name] = EvalScope.Record(
                        ("extent", Held(appearance, Facet.Extent)),
                        ("octets", Held(appearance, Facet.Emitted)));
                    break;
                }

                case Context source:
                    outside[source.Key] = run.For(source).Value;
                    break;
            }

        return new EvalScope()
            .Set("fields", new ProtoValue.Rec(parts))
            .Set("sets", new ProtoValue.Rec(spans))
            .Set("inputs", new ProtoValue.Rec(outside));
    }

    /// <summary>A fact about an appearance, or nothing where it has not been worked out. Nothing is the
    /// honest answer for a facet a direction never produces — an extent, while reading.</summary>
    private static ProtoValue Held(RunNode appearance, Facet facet)
        => appearance.Has(facet)
            ? appearance.Settled(facet) switch
            {
                ProtoValue value => value,
                int count => ProtoValue.Of((long)count),
                _ => ProtoValue.Nothing,
            }
            : ProtoValue.Nothing;

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a message, by the same walk that writes one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which places are in a message, in what order, and which way a fork goes is <b>one question</b>, and
    /// it used to be answered twice — a resolver-scheduled walk going out, a recursive generator coming in.
    /// They shared only the fork logic, they diverged on what to do with a field neither could handle, and
    /// the divergence was silent in the direction that mattered.
    /// </para>
    /// <para>
    /// What genuinely differs is not the walk but <i>when facts can be had</i>, and the difference is not
    /// symmetric. Going out, a value can wait on a field that has not been laid down yet, so the facts are
    /// scheduled. Coming in there is nothing to schedule: you cannot know an extent without being at the
    /// position, or a value without the extent, and nothing later can inform anything earlier. So reading
    /// settles all of a field's facets at the moment the walk reaches it — not a shortcut, but the actual
    /// shape of reading, and the reason forcing the two directions into one facet graph would have been
    /// inventing a symmetry that is not there.
    /// </para>
    /// </remarks>
    public RunGraph Decode(ReadOnlySpan<byte> octets,
                           IReadOnlyDictionary<string, ProtoValue>? supplied = null)
    {
        var run = RunGraph.Begin(graph, supplied);
        var resolver = new Resolver();

        resolver.Add(new Laying(this, run, new BitCursor(octets.ToArray()))
                         .Reaching(graph.Root, previous: null));
        resolver.Resolve();

        Vouch(run);

        return run;
    }

    /// <summary>
    /// Takes one field off the wire: where it starts, how far it runs, and what it says.
    /// </summary>
    /// <remarks>
    /// A form that delimits itself answers the extent and the value together, from one read — which is the
    /// other half of what splitting <see cref="WireForm"/> out bought. It used to be "work out the width"
    /// and then "read that many octets", two functions of the same marker bits that had to agree by hand
    /// and scanned the same octets twice.
    /// </remarks>
    private void Intake(RunGraph run, RunNode appearance, Node place, BitCursor source)
    {
        int began = source.At;

        // Nothing here asks whether the place is present: the walk settled that before it arrived, and a
        // second answer to a settled question is how two readings of one fact drift apart. An absent place
        // is never handed to this at all.

        // A carrier is told how far it runs, the same as a span is, and for the same reason: what is
        // inside it may be transformed, so there is nothing to look at until the whole of it is in hand.
        if (place is Subprotocol layer)
        {
            int span = Reaches(run, appearance, layer, source);
            var carried = source.Octets(span, new Wiring(_converters, layer.Id));

            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Extent, span);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(carried));
            appearance.Settle(Facet.Value, Unsealed(layer, carried));
            return;
        }

        var field = (Field)place;

        if (field.Pattern.Form is { SelfDelimiting: true } form && form.FixedOctets is null)
        {
            var taken = form.Take(source, null, How(field));

            var got = field.Via is null ? taken.Value : Applied(field, taken.Value, forward: false);

            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Extent, taken.Bits / 8);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(source.Since(began)));

            Vet(appearance, field, got);
            appearance.Settle(Facet.Value, got);
            return;
        }

        int width = Width(run, appearance, field, source.Ahead());

        if (!source.Holds(width * 8))
            throw new ProtoTypeException(
                $"field '{field.Id}' wants {width} octet(s) and {source.Remaining / 8} remain");

        var octets = source.Octets(width, How(field));

        var read = Read(field, octets);

        appearance.Settle(Facet.Position, began / 8);
        appearance.Settle(Facet.Extent, width);
        appearance.Settle(Facet.Emitted, ProtoValue.Of(octets));

        Vet(appearance, field, read);
        appearance.Settle(Facet.Value, read);
    }


    // ── The walk ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The way on from here: the only one, or the one the deciding node picks.
    /// </summary>
    private Node? Onward(RunGraph run, Node place, bool reading)
    {
        var ways = graph.From<Then>(place).ToList();

        if (ways.Count == 0) return null;
        if (ways.Count == 1 && ways[0].Key is null && !ways[0].Otherwise) return ways[0].To;

        var chosen = Decided(run, place, reading);

        return (ways.FirstOrDefault(w => !w.Otherwise && ProtoValue.Alike(w.Key, chosen))
             ?? ways.FirstOrDefault(w => w.Otherwise)
             ?? throw new ProtoTypeException(
                    $"'{place.Name}': {chosen} picks none of the ways on, and none is the one taken when "
                  + $"nothing matches. Offered: {string.Join(", ", ways.Select(w => w.Key?.ToString() ?? "*"))}"))
            .To;
    }

    /// <summary>What the fork was decided on — a computation's answer, or a node's own value.</summary>
    private ProtoValue Decided(RunGraph run, Node place, bool reading)
    {
        var decisions = graph.From<Decides>(place).ToList();

        var deciding = decisions.FirstOrDefault(d => d.Reading == reading)
                    ?? decisions.FirstOrDefault()
                    ?? throw new ProtoTypeException(
                           $"'{place.Name}' offers several ways on and nothing says what decides");

        return deciding.To switch
        {
            Evaluated evaluated => _evaluator.Eval(
                evaluated.Runs, Given(run, run.For(place), evaluated)),

            // A run of unlike components is decided by what its token said, which is a node's value and
            // needs no expression at all.
            Field announced => run.Reach(run.For(place), announced).Value,

            var other => throw new ProtoTypeException($"'{other.Name}' cannot decide a way on"),
        };
    }

    // ── Octets ────────────────────────────────────────────────────────────────

    /// <summary>
    /// How many octets to take, from the declaration or from whatever computes this node's extent.
    /// </summary>
    /// <remarks>
    /// A span sized by another field is not a shape of its own: it is a node whose <i>extent</i> comes
    /// from a computation, exactly as a value does. So there is nothing here about lengths — only the same
    /// question asked of a different facet.
    /// </remarks>
    private int Width(RunGraph run, RunNode appearance, Field field, byte[]? ahead = null)
    {
        if (field.Pattern.StaticWidth is { } declared) return declared;

        // A form that delimits itself is asked where it ends, which is the whole of what a continuation
        // chain, an escaping marker and a marked integer have in common. Going out their extent falls out
        // of the value; coming in it falls out of the octets — the same fact, reached from the side that
        // has it, and neither direction derives it twice.
        if (field.Pattern.Form is { SelfDelimiting: true } form && form.FixedOctets is null
            && ahead is not null)
            return form.Take(new BitCursor(ahead), null, How(field)).Bits / 8;

        // A span that ends at something: not a shape of its own, but a span with no width followed by a
        // node holding a fixed value. It runs up to where that value starts, and the value is a node it
        // can be told about rather than a byte run copied into its declaration.
        if (graph.ProducerOf(field, "extent") is null && Ends(field) is { } ending && ahead is not null)
            return Until(ending, field, ahead);

        // A span with nothing sizing it and nothing after it takes what is left, which is what "as many
        // as fit" comes to once the count has stopped being something anyone declares.
        if (graph.ProducerOf(field, "extent") is null && field.Pattern is Pattern.Chain && ahead is not null)
            return ahead.Length;

        if (graph.ProducerOf(field, "extent") is { } measured && measured is Evaluated evaluated)
            return (int)_evaluator.Eval(evaluated.Runs, Given(run, appearance, measured)).AsInt();

        throw new ProtoTypeException($"field '{field.Id}' has no width and nothing computes its extent");
    }

    /// <summary>
    /// Lays a field down: its own form, or the shape it is made of.
    /// </summary>
    /// <remarks>
    /// Two arms, and only one of them is the walk's business. A form is asked to lay one value down and
    /// nothing here knows which form it got — that is what stops a new encoding being an edit to the
    /// engine. The shapes are the cases where a field is written out of <i>several</i> values, which no
    /// form can be handed.
    /// </remarks>
    private void Write(BitWriter wire, Field field, ProtoValue value)
    {
        if (field.Pattern.Form is { } form) { form.Lay(wire, value, How(field)); return; }

        switch (field.Pattern)
        {
            // A run of elements: the field's value is the list, and what it may hold is the shape each item
            // takes. There is no per-item expression and no item to bind — an element definition says how
            // one is written, and the list says how many.
            case Pattern.Chain:
            {
                var shape = Admitted(field);

                foreach (var item in value.AsList()) Write(wire, shape, item);

                break;
            }

            // Run by run, in order, and the octet boundary is wherever it happens to land.
            case Pattern.Bits bits:
            {
                var runs = value as ProtoValue.Rec
                    ?? throw new ProtoTypeException(
                           $"field '{field.Id}' is written from a record of its runs");

                foreach (var run in bits.Slices)
                    wire.Put(runs.Members.TryGetValue(run.Name, out var held) ? held.AsInt() : 0, run.Width);

                break;
            }

            default:
                throw new ProtoTypeException($"field '{field.Id}' cannot be written by this walk yet");
        }
    }

    private ProtoValue Read(Field field, byte[] taken)
    {
        ProtoValue value = field.Pattern.Form is { } form
            ? form.Take(new BitCursor(taken), taken.Length * 8, How(field)).Value
            : field.Pattern switch
            {
                Pattern.Bits bits => Unpacked(bits, taken),
                Pattern.Chain => Elements(field, taken),
                _ => throw new ProtoTypeException($"field '{field.Id}' cannot be read by this walk yet"),
            };

        return field.Via is null ? value : Applied(field, value, forward: false);
    }

    /// <summary>What a form may reach for, and whose field it is when something goes wrong.</summary>
    private Wiring How(Field field) => new(_converters, field.Id);

    // ── What has to hold ──────────────────────────────────────────────────────

    /// <summary>
    /// Everything the document says has to hold about a value, checked where the value settles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both directions, deliberately.</b> An engine that will not read something it would happily write
    /// has two opinions about the protocol. So a confinement refuses on the way out as well — a caller
    /// handing in a value the document calls illegal finds out here, rather than on a wire.
    /// </para>
    /// <para>
    /// What the check points at is whatever settles it, which is the reason it is an edge. A written list
    /// of legal values is a set node; a negotiated one will be a computation producing a set, reached the
    /// same way with its inputs on ordinary edges. Nothing here has to know which it got until it looks.
    /// </para>
    /// </remarks>
    private void Vet(RunNode appearance, Node place, ProtoValue value)
    {
        foreach (var check in graph.Checking(place))
        {
            if (check.To is not Validator checker) continue;

            var held = check.Run is { } run && value is ProtoValue.Rec runs
                ? runs.Members.GetValueOrDefault(run, ProtoValue.Nothing)
                : value;

            var verdict = checker.Judge(held, [.. Supplying(checker)]);

            // Passing while unrecognised is the answer a boolean cannot give: a value an open set has not
            // heard of is legal, and refusing it would call tomorrow's assignments malformed.
            if (verdict.Passed) continue;

            throw new ProtoTypeException(
                $"'{check.Run ?? place.Name}': {verdict.Why}"
              + (check.Because.Length > 0 ? $" — {check.Because}" : ""));
        }
    }

    /// <summary>
    /// Every condition the document says has to hold about the message, once it is all there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After, and not during. These are the rules a wire shape cannot express — one field obliging
    /// another, two that must never combine, something that must always be true — and every one of them is
    /// about fields that need not have settled in any particular order. Checking them as each value lands
    /// would mean checking some of them against a message that is not finished yet.
    /// </para>
    /// <para>
    /// Both directions, for the reason confinement is: an engine that will not read what it would happily
    /// write has two opinions about the protocol. A caller building a message that contradicts itself finds
    /// out here, not from the peer.
    /// </para>
    /// </remarks>
    private void Vouch(RunGraph run)
    {
        foreach (var check in graph.Checking(graph.Root).Concat(
                     graph.Nodes.OfType<Field>().SelectMany(f => graph.Checking(f))))
        {
            if (check.To is not Evaluated asserted) continue;

            var here = run.Existing(check.From) ?? run.For(check.From);

            if (_evaluator.Eval(asserted.Runs, Given(run, here, asserted)).AsBool()) continue;

            throw new ProtoTypeException(
                $"message '{graph.Id}' does not satisfy: {asserted.Source.Render()}"
              + (check.Because.Length > 0 ? $" — {check.Because}" : ""));
        }
    }

    /// <summary>
    /// What a validator checks against, from its own requirement edges, in order.
    /// </summary>
    /// <remarks>
    /// A set node is handed over as itself; anything else is run and its value handed over. So a written
    /// list and a negotiated limit reach the check by the same path, and the check cannot tell which it
    /// got until it looks at what it was given.
    /// </remarks>
    private IEnumerable<object?> Supplying(Validator checker)
    {
        foreach (var wanted in graph.InputsOf(checker))
            yield return wanted.To switch
            {
                ValueSet set => set,
                Constant stated => stated.Holds,
                Evaluated evaluated => _evaluator.Eval(evaluated.Runs, new EvalScope()),
                var other => throw new ProtoTypeException(
                                 $"'{checker.Id}' cannot check against '{other.Name}'"),
            };
    }

    /// <summary>The fixed value that follows this node, where one does.</summary>
    private ProtoValue? Ends(Field field)
        => graph.From<Then>(field).Count() == 1
        && graph.From<Then>(field).Single().To is Field after
        && graph.ProducerOf(after, "value") is Constant stated
            ? stated.Holds
            : null;

    /// <summary>
    /// How far a span runs when what ends it is the next node's value.
    /// </summary>
    /// <remarks>
    /// The delimiter is not consumed and is not copied into this node's declaration: it stays the next
    /// node's value, so it is written back out by the thing that owns it and a document can fix it, name
    /// it and constrain it like anything else.
    /// </remarks>
    private static int Until(ProtoValue ending, Field field, byte[] ahead)
    {
        var wanted = ending is ProtoValue.Bytes octets
            ? octets.Value
            : System.Text.Encoding.ASCII.GetBytes(ending.AsText());

        if (wanted.Length == 0)
            throw new ProtoTypeException($"field '{field.Id}' runs up to nothing, which ends it at once");

        for (int at = 0; at + wanted.Length <= ahead.Length; at++)
            if (ahead.AsSpan(at, wanted.Length).SequenceEqual(wanted)) return at;

        throw new ProtoTypeException(
            $"field '{field.Id}' runs up to {ending}, which is not in the {ahead.Length} octet(s) left");
    }

    /// <summary>The one shape a span admits, where it admits exactly one.</summary>
    private Field Admitted(Field field)
    {
        var admits = graph.From<Allowed>(field).Select(e => e.To).OfType<Field>().ToList();

        return admits.Count == 1
            ? admits[0]
            : throw new ProtoTypeException(
                  $"field '{field.Id}' admits {admits.Count} shapes, and telling one element from another "
                + "by what it announces is not something this walk does yet");
    }

    /// <summary>
    /// The elements of a span, read until its octets run out.
    /// </summary>
    /// <remarks>
    /// How many there are is never asked and never declared: the span is as long as something else said,
    /// and the elements are however many fit. That is the whole of what used to be a repetition.
    /// </remarks>
    private ProtoValue Elements(Field field, byte[] taken)
    {
        var shape = Admitted(field);
        int width = shape.Pattern.StaticWidth
            ?? throw new ProtoTypeException(
                   $"field '{field.Id}' holds '{shape.Id}', whose width this walk cannot work out");

        if (taken.Length % width != 0)
            throw new ProtoTypeException(
                $"field '{field.Id}' is {taken.Length} octets and holds {width}-octet elements, so the "
              + "last one is incomplete");

        List<ProtoValue> items = [];

        for (int at = 0; at < taken.Length; at += width)
            items.Add(Read(shape, taken[at..(at + width)]));

        return new ProtoValue.List(items);
    }

    private ProtoValue Applied(Field field, ProtoValue value, bool forward)
    {
        var via = field.Via!;

        if (!_converters.TryGet(via.Name, out var converter) || converter is null)
            throw new ProtoTypeException($"field '{field.Id}': unknown converter '{via.Name}'");

        var arguments = graph.ArgumentsOf(field);

        if (forward) return converter.Apply(value, arguments);

        if (converter.Inverse is null || !_converters.TryGet(converter.Inverse, out var back) || back is null)
            throw new ProtoTypeException($"converter '{via.Name}' declares no inverse");

        return back.Apply(value, arguments);
    }

    private static ProtoValue Unpacked(Pattern.Bits bits, byte[] octets)
    {
        long accumulated = 0;
        foreach (var octet in octets) accumulated = (accumulated << 8) | octet;

        Dictionary<string, ProtoValue> runs = new(StringComparer.Ordinal);
        int left = bits.TotalBits;

        foreach (var run in bits.Slices)
        {
            left -= run.Width;
            runs[run.Name] = ProtoValue.Of((accumulated >> left) & ((1L << run.Width) - 1));
        }

        return new ProtoValue.Rec(runs);
    }
}
