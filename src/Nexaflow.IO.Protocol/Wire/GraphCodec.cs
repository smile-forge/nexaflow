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

        // A set something requires but nothing reaches is built on a walk of its own, and that walk's
        // order is thrown away: it says what those octets WOULD be, which is exactly what a pseudo-header
        // is for and exactly why it must not end up in the message.
        var aside = new Laying(this, run, source: null);
        foreach (var set in Aside()) resolver.Add(aside.Reaching(set, previous: null));

        resolver.Resolve();

        var wire = new BitWriter();

        foreach (var appearance in laying.Order)
        {
            // A part that is not there writes nothing, which is the whole of what absence does on the way
            // out. The walk still reached it — being reached and being present are different facts.
            if (appearance.Has(Facet.Present) && appearance.Settled(Facet.Present) is false) continue;

            int began = wire.Written;

            // A carrier's value is already the octets the inner message came to, so laying it down is
            // putting them there. That is the whole of what a layer costs the outer walk.
            if (appearance.Of is Subprotocol) wire.Put(appearance.Value.AsBytes());
            else Write(wire, (Field)appearance.Of, appearance.Value);

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

        /// <summary>
        /// The appearances that make octets, in the order the path reached them.
        /// </summary>
        /// <remarks>
        /// Appearances rather than places, because a place that repeats is several of them and each holds
        /// its own value. Recording the node instead writes the first pass over and over — and settles the
        /// same position twice, which at least refuses rather than emitting it.
        /// </remarks>
        public List<RunNode> Order { get; } = [];

        /// <summary>The last place the walk got to, which is where it stopped.</summary>
        public Node? Stopped { get; private set; }

        /// <summary>
        /// How many times the walk has arrived at each place, which is what tells one pass of a loop from
        /// the next.
        /// </summary>
        /// <remarks>
        /// A reading may go round, and the second time round is not the first: the same field holds a
        /// different value and both have to survive. The run graph already keys an appearance by an index
        /// for exactly this; nothing was ever counting. Without it a loop overwrites its own previous pass
        /// and a list of four options decodes to whichever one happened to be last.
        /// </remarks>
        private readonly Dictionary<Node, int> _at = [];

        /// <summary>Which time round the walk is on.</summary>
        private int _round;

        public ResolutionNode Reaching(Node place, Node? previous)
        {
            // Read before this arrival is recorded, because on a loop back to the same place they are the
            // same node: recording first makes the second pass wait on itself.
            int came = previous is null ? 0 : _at.GetValueOrDefault(previous, 0);

            // The round, not a count of visits to this place. An arm of a fork is only reached on the
            // rounds it is chosen, so counting its own visits drifts from the round everything around it
            // is on — and the second time round it would be handed an appearance the first round had
            // already decided was absent.
            if (_at.TryGetValue(place, out var last) && last == _round) _round++;

            int pass = _round;
            _at[place] = pass;

            var appearance = run.For(place, within: null, index: pass);
            var field = place as Field;

            // A carrier makes octets the way a field does — the inner message's, rather than one value's.
            // Everything past here treats the two alike, which is the point of the carrier being a place.
            bool carries = field is not null || place is Subprotocol;

            // A set makes no octets and spans the ones its members made. That is not a technicality about
            // where a number comes from: it is what lets a length measure a header while the header writes
            // nothing at all, and it is why the extent is a fact about the members rather than something
            // the set was told. Both directions, because both have members with extents by the time the
            // last one is done — which is exactly when this settles, and not before.
            bool spans = place is FieldSet;

            // The pass of the previous place that actually led here, which on a second time round is its
            // second appearance and not its first.
            List<FacetRef> before = previous is null
                ? []
                : [new FacetRef(run.For(previous, null, came), Facet.Realised)];
            before.AddRange(Deciding(place));

            // Whether a part is there is decided when the walk reaches it, for the same reason a fork is:
            // it changes where the path goes. An absent set is skipped whole, so the next place cannot be
            // known until presence is — which makes this a prerequisite of arriving, not a fact settled
            // afterwards. It is also what makes the two directions agree without arranging for them to.
            if (Optional(place)) before.AddRange(codec.Awaits(run, appearance, place, "presence", Reading));

            return new ResolutionNode
            {
                Id = appearance,

                // Going out, a fact is scheduled: extent waits on value, value waits on whatever the
                // computation asks for, and the order they settle in is not the order they are laid down.
                // Coming in, there is nothing to schedule — see Intake. So reading declares every facet
                // settled on sight and does the work as the place is reached.
                NotApplicable = spans && Reading
                    ? new HashSet<Facet>
                        { Facet.Present, Facet.Value, Facet.Position, Facet.Extent, Facet.Emitted }
                    : spans
                    ? codec.Asks(place)
                        ? new HashSet<Facet> { Facet.Present, Facet.Value, Facet.Position }
                        : new HashSet<Facet> { Facet.Present, Facet.Value, Facet.Position, Facet.Emitted }
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

                    // A set that turned out not to be there waits for nothing. Its members were stepped
                    // over — that is what skipping a set means — so they are never reached and never
                    // settle, and waiting on them is waiting on somewhere the walk did not go.
                    Facet.Extent when spans => Missing(appearance)
                        ? [] : codec.Spanned(run, place, Reading, Facet.Extent, appearance.Index),

                    Facet.Emitted when spans => Missing(appearance)
                        ? [] : codec.Spanned(run, place, Reading, Facet.Value, appearance.Index),

                    // Fixed BITS, not fixed octets. A four-bit field has no fixed octet width — it does not
                    // occupy an octet — so asking in octets says its width depends on its value, and TCP's
                    // Data Offset is then a cycle: how wide it is waits on what it holds, which is how wide
                    // the header is, which includes it.
                    Facet.Extent => carries && !Reading && field?.Form.FixedBits is null
                        ? [new FacetRef(appearance, Facet.Value)]
                        : [],

                    _ => [],
                },

                Settle = (facet, _) =>
                {
                    switch (facet)
                    {
                        case Facet.Realised:
                            bool here = !Optional(place) || codec.Asked(run, appearance, place, Reading, source);

                            if (Optional(place)) appearance.Settle(Facet.Present, here);

                            if (carries && here)
                            {
                                Order.Add(appearance);

                                if (source is not null) codec.Intake(run, appearance, place, source);
                            }
                            else if (carries)
                            {
                                // Read-side only, and that asymmetry is the whole of it: an absent part is
                                // ASSUMED coming in and written going out by not writing it. Anything else
                                // makes absent and explicitly-default the same octets, and value → octets
                                // stops being injective.
                                if (codec.Graph.Assumed(place) is { } assumed)
                                {
                                    if (Reading && assumed.Missing == WhenAbsent.Malformed)
                                        throw new ProtoTypeException(
                                            $"'{place.Name}' is not there and has to be"
                                          + (assumed.Because.Length == 0 ? "" : $" — {assumed.Because}"));

                                    if (Reading) appearance.Settle(Facet.Value, assumed.Value);
                                }

                                // It contributed nothing, and says so rather than having no answer. A
                                // length over a region holding it adds a real zero; asking an absent part
                                // for its extent and getting an error would make every such length a
                                // special case about optionality.
                                appearance.Settle(Facet.Extent, 0);
                                appearance.Settle(Facet.Emitted, ProtoValue.Of(Array.Empty<byte>()));
                            }

                            if (spans && !here)
                            {
                                appearance.Settle(Facet.Extent, 0);
                                appearance.Settle(Facet.Emitted, ProtoValue.Of(Array.Empty<byte>()));
                            }

                            // Coming in, a set is settled the moment everything under it has been read —
                            // pushed from the last member rather than waited for by the set. Waiting is
                            // what does not work: whether a set is there is decided as the walk arrives,
                            // and by then its dependencies have long been declared.
                            if (Reading) codec.Closing(run);

                            return Next(place, here) is { } next
                                ? FacetResult.Expanding(null, Reaching(next, place))
                                : Ending(place);

                        case Facet.Value:
                            // An absent part computes nothing. Not an optimisation: its expression may
                            // well read the very input whose absence is the reason it is not here.
                            if (Missing(appearance)) return FacetResult.Of(null);

                            return FacetResult.Of(field is not null
                                ? codec.Settle(run, appearance, field)
                                : codec.Sealed(run, appearance, (Subprotocol)place));

                        case Facet.Emitted when spans:
                            if (Missing(appearance)) return FacetResult.Of(Array.Empty<byte>());

                            var laid = codec.Laid(run, appearance, place);
                            appearance.Settle(Facet.Emitted, ProtoValue.Of(laid));
                            return FacetResult.Of(laid);

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

        /// <summary>Nowhere left to go, which is where the reading stopped.</summary>
        private FacetResult Ending(Node place)
        {
            Stopped = place;
            return FacetResult.Of(null);
        }

        /// <summary>
        /// The next place, with the round ended if the walk has left what was going round.
        /// </summary>
        /// <remarks>
        /// Rounds belong to the repetition, not to the walk. Letting one run on past the end means every
        /// place after a set that went round three times is asked for on its fourth appearance — while
        /// everything that waits on it declared the first, so the two never meet and the message quietly
        /// lacks everything after the repetition.
        /// </remarks>
        private Node? Leaving(Node? next)
        {
            // Writing only. A reading goes round on its own edge and its rounds end when the edge stops
            // pointing back — there is no set driving them, so there is nothing here to be finished with.
            if (Reading || _round == 0 || next is null) return next;

            foreach (var set in codec.Graph.Nodes.OfType<FieldSet>())
                if (codec.Graph.Repeating(set) is not null && codec.Under(set, next))
                    return next;

            _round = 0;
            return next;
        }

        /// <summary>
        /// The set to go round again, where leaving this place means one item of it has been written.
        /// </summary>
        /// <remarks>
        /// Asked of the place the walk is leaving rather than of the set, because that is where the
        /// question arises: a set is entered once per item and the only moment anyone can tell whether
        /// another is due is on the way out of the last thing in it.
        /// </remarks>
        private Node? Again(Node place)
        {
            foreach (var set in codec.Graph.Nodes.OfType<FieldSet>())
            {
                if (codec.Graph.Repeating(set) is null) continue;
                if (!ReferenceEquals(codec.Last(set), place)) continue;

                if (codec.Graph.Members(set).FirstOrDefault() is not { } first) continue;

                // Back to the first MEMBER, not to the set. A repeated span is one place on the path
                // however many turn up in it — so the set keeps a single appearance to be measured and
                // pointed at, and what repeats is what it holds.
                if (_round + 1 < codec.Items(run, run.For(set), set).Count) return first;
            }

            return null;
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

            // A reading that looks ahead waits for nothing, and must not: what it is about to read has not
            // been read, so waiting for it is waiting for the walk to arrive somewhere it cannot go until
            // this very decision is made. The value comes off the octets at the moment of deciding.
            if (Reading && codec.Graph.From<Identifies>(place).Any()) return [];

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
            // The last thing UNDER the set, not its last member. A member that is itself a set is left by
            // stepping into it, so leaving from there walks into the very thing being skipped — and reads
            // the first field of a part the walk had just decided was not there.
            if (!here && place is FieldSet set && codec.Last(set) is { } last)
                return codec.Onward(run, last, Reading, _at.GetValueOrDefault(last, 0), source);

            // Leaving the last thing under a set that repeats, with items still to go, goes back to the
            // set. The reading has no need of this — it goes round on its own edge and finds out how many
            // there were — so this is the writing's answer to the same question, taken from the length of
            // the list rather than from anything on the wire.
            if (!Reading && here && Again(place) is { } again) return again;

            return Leaving(codec.Onward(run, place, Reading, _at.GetValueOrDefault(place, 0), source));
        }
    }

    /// <summary>A node's name without the kind that prefixes it, where it carries one.</summary>
    private static string Plainly(string name, string kind)
        => name.StartsWith(kind, StringComparison.Ordinal) ? name[kind.Length..] : name;

    /// <summary>
    /// The appearance of something a computation wants, from where the computation is standing.
    /// </summary>
    /// <remarks>
    /// Itself first, then whatever shares this pass, then the ordinary outward search. Without the first
    /// two a fork inside a loop reads the value from the first time round on every time round, so a run
    /// ended by a sentinel never sees its sentinel and reads until the octets run out.
    /// </remarks>
    private static RunNode Toward(RunGraph run, RunNode here, Node target)
        => ReferenceEquals(here.Of, target) ? here
         : run.Existing(target, here.Within, here.Index) ?? run.Reach(here, target);

    /// <summary>
    /// Runs an expression, saying whose it was when it will not run.
    /// </summary>
    /// <remarks>
    /// "expected Int, got Null" is true and useless: a protocol has dozens of expressions and the message
    /// names none of them. What a reader needs is which one asked, and what it was trying to work out.
    /// </remarks>
    private ProtoValue Ran(Evaluated evaluated, EvalScope scope)
    {
        try
        {
            return _evaluator.Eval(evaluated.Runs, scope);
        }
        catch (ProtoTypeException why)
        {
            throw new ProtoTypeException(
                $"'{evaluated.Name}' could not work out `{evaluated.Runs.Render()}`: {why.Message}");
        }
    }

    /// <summary>What a computation answers, whichever kind it is.</summary>
    private ProtoValue Produced(RunGraph run, RunNode appearance, Computation computation) => computation switch
    {
        // A constant answers without being run. That is what a constant is, and it is why a fixed
        // delimiter needs no expression and can be pointed at by the span that ends before it.
        Constant stated => stated.Holds,
        Context outside => run.For(outside).Value,
        Evaluated evaluated => Ran(evaluated, Given(run, appearance, evaluated)),
        Converted applied => Through(applied.Applies, Gathering(run, appearance, applied), applied.Name),
        var other => throw new ProtoTypeException($"'{other.Name}' cannot produce a value here"),
    };

    /// <summary>
    /// What a conversion is handed: the things it requires, in the order the edges number them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One input is handed over as itself; several are handed over as a list. That is not a special case —
    /// a converter over octets either takes a run or takes several and joins them, and both are ordinary
    /// enough that <c>concat</c> declared it long before this needed it.
    /// </para>
    /// <para>
    /// This is what lets a checksum be described without a field ever holding two values. The sum requires
    /// three things — the pseudo-header, the header with a zero where the checksum goes, and the payload —
    /// and the middle one is itself a join of the fields before it, a constant zero, and the fields after.
    /// So the octets that get summed contain a zero in that position, and the field whose value the sum
    /// becomes never held anything else.
    /// </para>
    /// </remarks>
    private ProtoValue Gathering(RunGraph run, RunNode appearance, Computation applied)
    {
        List<ProtoValue> given = [];

        foreach (var wanted in graph.InputsOf(applied).OrderBy(e => e.Sequence))
            given.Add(wanted.To switch
            {
                // A computation may require another. The join of a header's two halves around a zero is
                // one, and it is nobody's field — inventing a field to hold it would put a value on the
                // wire that the protocol does not have.
                Computation inner => Produced(run, appearance, inner),
                var other => Held(Toward(run, appearance, other), Named(wanted.Facet)),
            });

        return given.Count switch
        {
            0 => throw new ProtoTypeException(
                     $"'{applied.Name}' converts something and nothing says what"),
            1 => given[0],
            _ => new ProtoValue.List(given),
        };
    }

    /// <summary>
    /// The octets a set comes to: each member's value, laid down by its own form, in the order held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From the <b>values</b> rather than from what was written, and that is what makes it work at all for
    /// a set nothing walks to. A pseudo-header is never transmitted, so there is no stretch of wire to
    /// point at — but its fields have values and forms, and that is all "what would these octets be" ever
    /// needed.
    /// </para>
    /// <para>
    /// Laying them down rather than joining what each member emitted is also the only thing that works
    /// through bit fields: eight one-bit flags emit nothing individually and one octet together, and only
    /// a writer keeping the bit position knows that.
    /// </para>
    /// </remarks>
    private byte[] Laid(RunGraph run, RunNode appearance, Node set)
    {
        var wire = new BitWriter();
        Lay(run, appearance, set, wire);
        return wire.Done(graph.Id);
    }

    private void Lay(RunGraph run, RunNode appearance, Node set, BitWriter wire)
    {
        if (graph.Repeating(set) is not null)
        {
            for (int pass = 0; pass < Passes(run, set); pass++)
                foreach (var member in graph.Members(set))
                    LayOne(run, run.For(member, null, pass), member, wire);

            return;
        }

        foreach (var member in graph.Members(set))
        {
            // The same appearance the dependency was declared against. Reaching for it by scope instead
            // finds a different one, whose facets nothing ever settles.
            LayOne(run, run.For(member, null, appearance.Index), member, wire);
        }
    }

    private void LayOne(RunGraph run, RunNode held, Node member, BitWriter wire)
    {
        // The same rule that decided not to wait on it. Anything else lays down a part that was ignored
        // when the octets were counted, so the length says one thing and the message is another.
        if (Skipped(run, member, held.Index)) return;

        if (member is FieldSet nested) Lay(run, held, nested, wire);
        else if (member is Field field) Write(wire, field, held.Value);
    }

    /// <summary>
    /// The sets laid out because something requires them, rather than because the path arrives at them.
    /// </summary>
    /// <remarks>
    /// A pseudo-header is the case that needs this, and it is not a peculiarity of one protocol: the family
    /// TCP belongs to checksums over fields belonging to the layer underneath, which are genuinely part of
    /// what is summed and genuinely not part of what is sent. Saying so needs a set with no step on the
    /// path reaching it — and then something has to notice it is wanted, or it is a set nobody ever builds.
    /// </remarks>
    private IEnumerable<FieldSet> Aside()
        => graph.Nodes.OfType<FieldSet>()
                .Where(set => graph.To<Requires>(set).Any() && !graph.To<Then>(set).Any());

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
    private List<FacetRef> Spanned(RunGraph run, Node set, bool reading, Facet wanted, int pass = 0)
    {
        if (Skipped(run, set)) return [];

        // Coming in, one thing: the last place under the set having been read. Everything about the set is
        // known then and nothing about it is known before, so there is no finer answer to give.
        if (reading)
            return Last(set) is { } last ? [new FacetRef(run.For(last), Facet.Realised)] : [];

        // Going out, it depends on which question. How WIDE a set is falls out of its members' extents, and
        // a fixed-width field has one before it has a value. What its OCTETS are needs the values — asking
        // for extents there settles the moment the widths are known, which is before anything has been
        // computed, and lays down a pseudo-header full of nothing.
        List<FacetRef> waits = [];

        // A set that repeats waits on every pass of what it holds. The number of them is known the moment
        // the list is — it is the list's length — so this can be declared up front like any other
        // dependency, rather than discovered as the walk goes and declared too late to be waited on.
        if (graph.Repeating(set) is not null)
        {
            for (int round = 0; round < Passes(run, set); round++)
                foreach (var member in graph.Members(set))
                    Wanted(run, member, round, wanted, waits);

            return waits;
        }

        // Only what makes octets. A set may hold a junction — a fork, a place the arms meet — and a
        // junction has no extent and no value by construction, so waiting on one is waiting for something
        // that will never be settled by anybody.
        foreach (var member in graph.Members(set))
            Wanted(run, member, pass, wanted, waits);

        return waits;
    }

    /// <summary>What one member of a set contributes to what that set waits on.</summary>
    /// <remarks>
    /// A nested set has no value of its own, so asking one for a value waits on something nothing will ever
    /// settle. Its members are what the octets came from, at whatever depth — which is why this descends
    /// for a value and does not for an extent, where a set does have an answer.
    /// </remarks>
    private void Wanted(RunGraph run, Node member, int pass, Facet wanted, List<FacetRef> waits)
    {
        // Absent at any depth means stepped over at any depth. A set inside a set that is not there is not
        // there either, and its members are places the walk never went.
        if (Skipped(run, member, pass)) return;

        switch (member)
        {
            case FieldSet nested when wanted == Facet.Value:
                waits.AddRange(Spanned(run, nested, false, wanted, pass));
                break;

            // Only what makes octets. A junction has no extent and no value by construction, so waiting on
            // one waits for something nothing will ever settle.
            case Field or Subprotocol or FieldSet:
                waits.Add(new FacetRef(run.For(member, null, pass), wanted));
                break;
        }
    }

    /// <summary>
    /// Settles every set whose contents are all now accounted for, innermost first.
    /// </summary>
    /// <remarks>
    /// Repeated until nothing more can be settled, which is what carries it through nesting: the options
    /// close, so the half of the header past the checksum closes, so the header closes. A member counts as
    /// accounted for when it has an extent or when the walk stepped over it — an absent part contributes a
    /// real zero rather than leaving the set unanswerable.
    /// </remarks>
    private void Closing(RunGraph run)
    {
        bool moved = true;

        while (moved)
        {
            moved = false;

            foreach (var set in graph.Nodes.OfType<FieldSet>())
            {
                var appearance = run.For(set);

                if (appearance.Has(Facet.Extent)) continue;

                if (!graph.Members(set).All(m => m is Junction || Skipped(run, m) || run.For(m).Has(Facet.Extent)))
                    continue;

                long across = Spread(run, appearance, set);

                appearance.Settle(Facet.Extent, (int)(across / 8));
                appearance.Settle(Facet.Emitted, ProtoValue.Of(Laid(run, appearance, set)));
                moved = true;
            }
        }
    }

    /// <summary>
    /// Whether anything actually wants this set's octets.
    /// </summary>
    /// <remarks>
    /// Working them out regardless looks harmless and is not: a set's octets are its members' values laid
    /// down, so a set holding an optional part that is not there has nothing to lay down for it and the
    /// question has no answer. Asking it anyway makes every container of an absent part unanswerable — for
    /// a reason nobody wanted the answer for.
    /// </remarks>
    private bool Asks(Node set)
        => graph.To<Requires>(set).Any(e => e.Facet is "octets" or "emitted");

    /// <summary>
    /// Whether this place is not there — asking, where the walk has not been to settle it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An arm whose condition says no is ignored as though it were not written down: nothing waits on it,
    /// nothing computes it. Reading a settled answer is not enough, because dependencies are declared as
    /// the walk arrives at the SET and the arm is decided later — so by the time anything had settled, the
    /// waiting would already have been declared on a part the walk was about to step past.
    /// </para>
    /// <para>
    /// A condition that cannot be answered yet is not an answer of no. It means the question was asked too
    /// early, and the honest response is to keep waiting rather than to quietly drop a part that was going
    /// to be there.
    /// </para>
    /// </remarks>
    private bool Skipped(RunGraph run, Node place, int pass = 0)
    {
        var appearance = run.For(place, null, pass);

        if (appearance.Has(Facet.Present)) return appearance.Settled(Facet.Present) is false;
        if (!graph.MayBeAbsent(place)) return false;
        if (graph.ProducerOf(place, "presence", false) is not { } asks) return false;

        try
        {
            return Produced(run, appearance, asks).AsBool() is false;
        }
        catch (ProtoTypeException)
        {
            return false;
        }
    }

    /// <summary>
    /// What this time round is about: the item of the enclosing repeating set that belongs to this pass.
    /// </summary>
    /// <remarks>
    /// Found by asking which repeating set holds the node being computed, then taking the element at this
    /// appearance's index. Nothing when there is no such set, which is the ordinary case and why every
    /// description that does not repeat never mentions it.
    /// </remarks>
    private ProtoValue Item(RunGraph run, RunNode here)
    {
        foreach (var set in graph.Nodes.OfType<FieldSet>())
        {
            if (graph.Repeating(set) is null || !Under(set, here.Of)) continue;

            var items = Items(run, run.For(set, null, here.Index), set);

            return here.Index < items.Count ? items[here.Index] : ProtoValue.Nothing;
        }

        return ProtoValue.Nothing;
    }

    /// <summary>Whether a place is somewhere under a set, however deeply.</summary>
    internal bool Under(Node set, Node place)
        => graph.Members(set).Any(m => ReferenceEquals(m, place) || (m is FieldSet inner && Under(inner, place)));

    /// <summary>
    /// The items a repeating set is written once for.
    /// </summary>
    /// <remarks>
    /// Resolved here and now rather than looked up, because the thing producing the list may be an input,
    /// a computation over one, or something derived from a field that was read a moment ago — and which of
    /// those it is makes no difference to anybody except the node that produces it.
    /// </remarks>
    internal IReadOnlyList<ProtoValue> Items(RunGraph run, RunNode appearance, Node set)
    {
        if (graph.Repeating(set)?.To is not Computation over) return [];

        // Nothing to write once per, which is the ordinary case coming IN: a reading finds out how many
        // there were by looking, so the list nobody supplied is not a missing value, it is a question that
        // was never asked.
        if (over is Context outside && !run.For(outside).Has(Facet.Value)) return [];

        return Produced(run, appearance, over).AsList();
    }

    /// <summary>
    /// How many times a repeating set turned out to go round.
    /// </summary>
    /// <remarks>
    /// The list says so going out; coming in nobody supplied one, so the answer is however many
    /// appearances the walk made. Both are the same question asked of whichever side knows.
    /// </remarks>
    internal int Passes(RunGraph run, Node set)
    {
        int told = Items(run, run.For(set), set).Count;

        if (told > 0) return told;
        if (graph.Members(set).FirstOrDefault() is not { } first) return 0;

        int made = 0;
        while (run.Existing(first, null, made) is not null) made++;

        return made;
    }

    /// <summary>The last place under a set that is not itself a set.</summary>
    internal Node? Last(Node set)
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
        if (Skipped(run, place, appearance.Index)) return 0;

        if (place is FieldSet set)
        {
            // A set that repeats is as wide as ALL of its passes. Measuring one of them is how a header
            // carrying five options comes out the width of one.
            if (graph.Repeating(set) is not null)
            {
                long across = 0;

                for (int pass = 0; pass < Passes(run, set); pass++)
                    foreach (var member in graph.Members(set))
                        across += Spread(run, run.For(member, null, pass), member);

                return across;
            }

            return graph.Members(set).Sum(m => Spread(run, run.For(m, null, appearance.Index), m));
        }

        if (place is Field carried && carried.Form.FixedBits is { } fixedBits)
            return fixedBits;

        return appearance.Has(Facet.Extent) ? Convert.ToInt64(appearance.Settled(Facet.Extent)) * 8 : 0;
    }

    /// <summary>What a named facet's computation waits on, taken from the edges that ask for it.</summary>
    private List<FacetRef> Awaits(RunGraph run, RunNode appearance, Node place, string facet, bool reading)
    {
        if (graph.ProducerOf(place, facet, reading) is not { } producing) return [];

        // The same rule a value waits by, and it had a different and smaller one: sets did not count, so a
        // part whose presence turns on how wide something is waited on nothing at all and was asked before
        // the answer existed. Two ways of saying "what does this computation need" is one of them being
        // wrong, and it was this one.
        List<FacetRef> waits = [];
        Wanting(run, appearance, producing, waits);

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
    /// <param name="ahead">
    /// Where the reading has got to, so that a part which is there only when there is room for it can say
    /// so. Null going out, and that is not a gap — on the way out nothing is left over, and whether a
    /// trailer is written is something the caller said rather than something the octets imply.
    /// </param>
    private bool Asked(RunGraph run, RunNode appearance, Node place, bool reading, BitCursor? ahead)
        => graph.ProducerOf(place, "presence", reading) switch
        {
            // A default that insists on being written makes the part present, however the question was
            // going to be answered. It is the reserved-octet case: leaving it out is a shorter message
            // than the specification allows, so "absent" is not one of the answers available.
            _ when !reading && graph.Assumed(place) is { Written: true } => true,

            // Going out, a part whose value is already the default is left out — the shortest legal
            // encoding, where a protocol says so. What it would have held has to be worked out to answer
            // that, which is the one place presence asks about a value rather than the other way round.
            _ when !reading && graph.Assumed(place) is { Omitted: true } omits && place is Field field
                => !ProtoValue.Alike(Produced(run, appearance, Produces(field)), omits.Value),

            null => true,
            Constant stated => stated.Holds.AsBool(),
            Evaluated evaluated => Ran(evaluated, Given(run, appearance, evaluated, ahead)).AsBool(),
            var other => throw new ProtoTypeException(
                             $"'{place.Name}': '{other.Name}' cannot decide whether something is there"),
        };

    /// <summary>Every fact this appearance's value waits on, taken from the edges that ask for them.</summary>
    /// <summary>
    /// Everything a computation needs settled before it can run, following through the ones it requires.
    /// </summary>
    /// <remarks>
    /// A set counts, which it did not before: a checksum waits on the octets of the header halves either
    /// side of it, and those are facts about sets. Without this the sum is scheduled against nothing and
    /// runs while the values it is summing are still unsettled — which does not fail, it produces a
    /// plausible wrong number.
    /// </remarks>
    private void Wanting(RunGraph run, RunNode appearance, Computation computation, List<FacetRef> waits)
    {
        foreach (var wanted in graph.InputsOf(computation))
            switch (wanted.To)
            {
                case Field or Subprotocol or FieldSet:
                    waits.Add(new FacetRef(Toward(run, appearance, wanted.To), Named(wanted.Facet)));
                    break;

                case Computation inner:
                    Wanting(run, appearance, inner, waits);
                    break;
            }
    }

    private List<FacetRef> Waits(RunGraph run, RunNode appearance, Field field)
    {
        List<FacetRef> waits = [];

        IEnumerable<Computation> asking =
            graph.ProducerOf(field, "value") is { } one ? [one] : [];

        foreach (var computation in asking) Wanting(run, appearance, computation, waits);

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

        // A form that knows its own width does not need its value to be measured — and must not ask for
        // one, because a field whose extent waits on its value cannot be part of what its value is computed
        // from. Sub-octet widths come out as zero, which is right: a four-bit field occupies no octets, it
        // occupies four bits of one, and the set holding it is what occupies octets.
        if (field.Form.FixedBits is { } bits) return bits / 8;

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
        // A constant answers without being run. That is what a constant is, and it is why a fixed
        // delimiter needs no expression and can be pointed at by the span that ends before it.
        var value = Produced(run, appearance, Produces(field));

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
                new GraphCodec(inner.Protocol, _converters, _provided)
                    .Encode(Fed(run, appearance, layer, inner.Protocol))),

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
                new GraphCodec(inner.Protocol, _converters, _provided).Decode(value.AsBytes())),

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
        var missing = inner?.Nodes.OfType<Context>().Select(c => c.Name)
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
    private Computation Produces(Node owner)
        => graph.ProducerOf(owner, "value")
        ?? throw new ProtoTypeException(
               $"'{owner.Name}' has nothing to compute its value, so it cannot be written");

    /// <summary>
    /// Everything one computation is allowed to see, assembled from the edges that say so.
    /// </summary>
    private EvalScope Given(RunGraph run, RunNode here, Computation computation, BitCursor? ahead = null)
    {
        Dictionary<string, ProtoValue> parts = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> outside = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> spans = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> kept = new(StringComparer.Ordinal);

        foreach (var wanted in graph.InputsOf(computation))
            switch (wanted.To)
            {
                case Field or Subprotocol:
                {
                    var appearance = Toward(run, here, wanted.To);

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
                    var appearance = Toward(run, here, set);

                    spans[set.Name] = EvalScope.Record(
                        ("extent", Held(appearance, Facet.Extent)),
                        ("octets", Held(appearance, Facet.Emitted)));
                    break;
                }

                // The two kinds of outside value answer under different names, because they fail
                // differently. A missing input is a caller that did not say something it had to; a missing
                // state is a conversation starting, which is ordinary.
                // Under its own name, less the kind that prefixes it: the root an expression reaches it
                // through already says which kind it is, so `inputs.input.syn` would be saying it twice.
                case Context.Input given:
                    outside[Plainly(given.Name, "input.")] = run.For(given).Value;
                    break;

                case Context.State carried:
                    kept[Plainly(carried.Name, "state.")] = run.For(carried).Value;
                    break;
            }

        return new EvalScope()
            .Set("fields", new ProtoValue.Rec(parts))
            .Set("sets", new ProtoValue.Rec(spans))
            .Set("inputs", new ProtoValue.Rec(outside))
            .Set("state", new ProtoValue.Rec(kept))

            // How many octets are left to read, and only while reading. It is bound rather than reachable
            // by an edge because it is not a fact about any node — it is where the reading has got to. A
            // trailer that is there when there is room for it is the one honest way to say "is there
            // more", and it has no counterpart going out: nothing is left over when you are the one
            // writing.
            .Set("remaining", ahead is null ? ProtoValue.Nothing : ProtoValue.Of(ahead.Remaining / 8))

            // Where the reading has got to, counted from the start of the message. The companion of
            // `remaining`, and needed for the same kind of question asked from the other end: a run of
            // options ends where the header ends, and the header's end is an offset rather than an amount
            // left over — what follows the options is the payload, which has no length of its own.
            .Set("position", ahead is null ? ProtoValue.Nothing : ProtoValue.Of(ahead.At / 8))

            // Which time round this is, and what this time round is about. A set written once per item of
            // a list needs both: the ordinal to say where it has got to, and the item so that the fields
            // inside can read what they hold by NAME rather than by digging the same index out of the same
            // list in every one of them.
            .Set("ordinal", ProtoValue.Of(here.Index))
            .Set("item", Item(run, here));
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
        var source = new BitCursor(octets.ToArray());

        var reading = new Laying(this, run, source);

        resolver.Add(reading.Reaching(graph.Root, previous: null));
        resolver.Resolve();

        // Where a protocol says where ending is legal, ending anywhere else is not a short message — it is
        // a message that was cut, or one this description does not cover. A walk that merely runs out of
        // edges cannot tell those apart from finishing, which is the whole reason for saying so.
        // Only where a reading was declared. A protocol read by walking what it writes ends wherever the
        // written path ends, and that is not somewhere it was ever going to name.
        if (graph.Of<Decode>().Any() && graph.Nodes.OfType<EndParse>().Any()
            && reading.Stopped is not EndParse)
            throw new ProtoTypeException(
                $"'{graph.Id}': the reading stopped at '{reading.Stopped?.Name ?? "nowhere"}', which is not "
              + "somewhere it is allowed to stop. Every way on from there was refused, so this is either "
              + "cut short or not this message.");

        // Everything it was given has to be accounted for. A walk that stops with octets still in hand has
        // not read this message — it has read a prefix of it and found that prefix well-formed, which is
        // the failure that looks like success: every field it did bind holds a plausible value.
        if (source.Remaining > 0)
            throw new ProtoTypeException(
                $"'{graph.Id}': {source.Remaining / 8} octet(s) were left over. Reading stopped before the "
              + "end of what arrived, so this is a prefix that happens to parse rather than the message.");

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

        if (field.Form is { SelfDelimiting: true } form && form.FixedOctets is null)
        {
            var taken = form.Take(source, null, How(field));

            var got = field.Via is null ? taken.Value : Applied(field, taken.Value, forward: false);

            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Extent, taken.Bits / 8);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(source.Since(began)));

            Vet(appearance, field, got);
            Canonical(field, got);
            appearance.Settle(Facet.Value, got);
            return;
        }

        int width = Width(run, appearance, field, source.Ahead(), source);

        if (!source.Holds(width * 8))
            throw new ProtoTypeException(
                $"field '{field.Id}' wants {width} octet(s) and {source.Remaining / 8} remain");

        var octets = source.Octets(width, How(field));

        var read = Read(field, octets);

        appearance.Settle(Facet.Position, began / 8);
        appearance.Settle(Facet.Extent, width);
        appearance.Settle(Facet.Emitted, ProtoValue.Of(octets));

        Vet(appearance, field, read);
        Canonical(field, read);
        appearance.Settle(Facet.Value, read);
    }

    /// <summary>
    /// Refuses the long form of something the protocol says to omit.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="Default.Omitted"/>, and it is not optional politeness: without it a
    /// value has two encodings, and every message taking the longer one comes back different from how it
    /// arrived. Same law as a padded varint, refused for the same reason.
    /// </remarks>
    private void Canonical(Field field, ProtoValue value)
    {
        if (graph.Assumed(field) is not { Omitted: true } omits) return;
        if (!ProtoValue.Alike(value, omits.Value)) return;

        throw new ProtoTypeException(
            $"'{field.Id}' is written out holding {value}, which is what it means when it is absent — so "
          + "it should not be here at all. EndParse both would give one value two encodings"
          + (omits.Because.Length == 0 ? "." : $": {omits.Because}"));
    }


    // ── The walk ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The way on from here: the only one, or the one the deciding node picks.
    /// </summary>
    private Node? Onward(RunGraph run, Node place, bool reading, int pass = 0, BitCursor? ahead = null)
    {
        // Where a reading says how it goes, it goes that way; where it says nothing, it goes the way the
        // writing does. Most of a protocol is the second case, and there the two directions share one
        // description and cannot drift apart.
        List<(Node To, ProtoValue? Key, bool Otherwise, bool Optional)> ways =
            reading && graph.From<Decode>(place).Any()
                ? [.. graph.From<Decode>(place).Select(e => (e.To, e.Key, e.Otherwise, false))]
                : [.. graph.From<Then>(place).Select(e => (e.To, e.Key, e.Otherwise, e.Optional))];

        if (ways.Count == 0) return null;
        if (ways.Count == 1 && ways[0].Key is null && !ways[0].Otherwise) return ways[0].To;

        var chosen = Decided(run, place, reading, pass, ahead);

        foreach (var way in ways)
            if (!way.Otherwise && ProtoValue.Alike(way.Key, chosen)) return Taking(run, ways, way.To, pass);

        foreach (var way in ways)
            if (way.Otherwise) return Taking(run, ways, way.To, pass);

        throw new ProtoTypeException(
            $"'{place.Name}': {chosen} picks none of the ways on, and none is the one taken when nothing "
          + $"matches. Offered: {string.Join(", ", ways.Select(w => w.Key?.ToString() ?? "*"))}");
    }

    /// <summary>
    /// Takes one way on, and says of the others that they are not there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only of the ones declared optional, and that distinction is the whole safety of it. An alternation
    /// of optional arms is exactly one of them: picking a Modbus exception body means the normal body is
    /// not in this frame, which something measuring the PDU has to be told or it waits forever on a part
    /// the walk stepped past.
    /// </para>
    /// <para>
    /// A way on that is <b>not</b> optional says nothing of the kind. A loop leaves by one of two edges
    /// every time round and takes the other one eventually — marking the road not taken as absent there
    /// would say a segment has no payload because this pass went back for another option.
    /// </para>
    /// </remarks>
    private Node Taking(RunGraph run, List<(Node To, ProtoValue? Key, bool Otherwise, bool Optional)> ways,
                               Node taken, int pass)
    {
        foreach (var way in ways)
        {
            if (ReferenceEquals(way.To, taken) || !way.Optional) continue;

            // At this round. The arms of a fork inside a repetition are per-round like everything else, and
            // marking round zero's arm absent again on round one is two answers to one question.
            Absent(run, way.To, pass);
        }

        return taken;
    }

    /// <summary>
    /// Says of a place and everything under it that it is not here, and what that comes to.
    /// </summary>
    /// <remarks>
    /// Saying only that it is absent is not enough. Anything measuring the set this arm belongs to is
    /// waiting on the arm's extent, and anything taking that set's octets is waiting on the values inside
    /// it — none of which the walk will ever go and settle, because it went the other way. So an absent
    /// part answers, and what it answers is nothing: no octets, no width, no value.
    /// </remarks>
    private void Absent(RunGraph run, Node place, int pass)
    {
        var missed = run.For(place, null, pass);

        if (missed.Has(Facet.Present)) return;

        missed.Settle(Facet.Present, false);
    }

    /// <summary>What the fork was decided on — a computation's answer, or a node's own value.</summary>
    /// <param name="pass">
    /// Which time round this is. A fork inside a loop is decided by what THIS pass read — reading the
    /// first pass's value every time is how a loop never reaches its own terminator and runs off the end
    /// of the octets.
    /// </param>
    private ProtoValue Decided(RunGraph run, Node place, bool reading, int pass, BitCursor? ahead)
    {
        // Coming in, a place may find out which way to go by looking. That is the only answer available at
        // the top of a protocol, where which message this is, is a thing the message says: there is no
        // caller to ask and nothing has been read yet.
        if (reading && ahead is not null && graph.From<Identifies>(place).Any())
            return Probed(place, ahead);

        var decisions = graph.From<Decides>(place).ToList();

        var deciding = decisions.FirstOrDefault(d => d.Reading == reading)
                    ?? decisions.FirstOrDefault()
                    ?? throw new ProtoTypeException(
                           $"'{place.Name}' offers several ways on and nothing says what decides");

        return deciding.To switch
        {
            Evaluated evaluated => Ran(evaluated, Given(run, run.For(place, null, pass), evaluated, ahead)),

            // A run of unlike components is decided by what its token said, which is a node's value and
            // needs no expression at all.
            Field announced => Toward(run, run.For(place, null, pass), announced).Value,

            var other => throw new ProtoTypeException($"'{other.Name}' cannot decide a way on"),
        };
    }

    /// <summary>
    /// What the octets ahead say, read along the path that identifies which way on to take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From a <b>copy</b> of the position, so nothing is consumed: whichever way on this picks starts
    /// reading from where the walk already is. A message therefore never has to know that something looked
    /// at its first octets before choosing it — it reads its own from its own beginning, and its
    /// description is the same one it would have if it were the only message in the protocol.
    /// </para>
    /// <para>
    /// The path may be several fields long, and the last one is the discriminator. Everything before it is
    /// there to get past — a length, a version, whatever a specification put in front of the thing that
    /// says which message this is.
    /// </para>
    /// </remarks>
    private ProtoValue Probed(Node place, BitCursor ahead)
    {
        var apart = new BitCursor(ahead.Ahead());
        var answer = ProtoValue.Nothing;
        HashSet<Node> been = [];

        for (var at = place; graph.From<Identifies>(at).FirstOrDefault() is { } step; at = step.To)
        {
            if (!been.Add(step.To))
                throw new ProtoTypeException(
                    $"'{place.Name}' reads ahead in a circle, back to '{step.To.Name}'. The way to a "
                  + "discriminator is a path and arrives somewhere.");

            if (step.To is not Field field)
                throw new ProtoTypeException(
                    $"'{place.Name}' reads ahead through '{step.To.Name}', which is not a field. Only "
                  + "something that takes octets off the wire can be on the way to a discriminator.");

            // Sized by its own declaration or by its own octets, because there is nothing else to size it
            // by: this runs before the walk has arrived, so no field it might have measured against has a
            // value yet.
            if (field.Form is { SelfDelimiting: false })
                throw new ProtoTypeException(
                    $"'{field.Id}' is on the way to a discriminator and nothing about it says how far it "
                  + "runs. Reading ahead happens before anything has been read, so a width that comes from "
                  + "another field is not available to it.");

            var taken = field.Form.Take(apart, field.Form.FixedBits, How(field));

            answer = field.Via is null ? taken.Value : Applied(field, taken.Value, forward: false);
        }

        return answer;
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
    private int Width(RunGraph run, RunNode appearance, Field field, byte[]? ahead = null,
                      BitCursor? at = null)
    {
        if (field.Form.FixedOctets is { } declared) return declared;

        // A form that delimits itself is asked where it ends, which is the whole of what a continuation
        // chain, an escaping marker and a marked integer have in common. Going out their extent falls out
        // of the value; coming in it falls out of the octets — the same fact, reached from the side that
        // has it, and neither direction derives it twice.
        if (field.Form is { SelfDelimiting: true } form && form.FixedOctets is null
            && ahead is not null)
            return form.Take(new BitCursor(ahead), null, How(field)).Bits / 8;

        // A span that ends at something: not a shape of its own, but a span with no width followed by a
        // node holding a fixed value. It runs up to where that value starts, and the value is a node it
        // can be told about rather than a byte run copied into its declaration.
        if (graph.ProducerOf(field, "extent") is null && Ends(field) is { } ending && ahead is not null)
            return Until(ending, field, ahead);

        if (graph.ProducerOf(field, "extent") is { } measured && measured is Evaluated evaluated)
            return (int)Ran(evaluated, Given(run, appearance, measured, at)).AsInt();

        // A span with no width, nothing measuring it and nothing after it takes what is left of whatever
        // contains it. That is what "the rest of the segment" is, and it is only ever answerable coming in
        // — going out the value is the octets and its own length is the extent.
        if (ahead is not null && field.Form is WireForm.Opaque { Octets: null }) return ahead.Length;

        throw new ProtoTypeException($"field '{field.Id}' has no width and nothing computes its extent");
    }

    /// <summary>
    /// Lays a field down.
    /// </summary>
    /// <remarks>
    /// One arm, and nothing here knows which form it got — that is what stops a new encoding being an edit
    /// to the engine. It used to have a second arm for the cases where a field was written out of SEVERAL
    /// values, which is what a shape was; those are sets of fields now, and a set is walked rather than
    /// laid down.
    /// </remarks>
    private void Write(BitWriter wire, Field field, ProtoValue value)
        => field.Form.Lay(wire, value, How(field));

    private ProtoValue Read(Field field, byte[] taken)
    {
        var value = field.Form.Take(new BitCursor(taken), taken.Length * 8, How(field)).Value;

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

}
