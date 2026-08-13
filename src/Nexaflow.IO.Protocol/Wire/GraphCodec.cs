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
public sealed class GraphCodec(MessageDef message, ConverterTable? converters = null)
{
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;
    private readonly Evaluator _evaluator = new(converters ?? ConverterTable.Default);

    /// <summary>What this codec can walk so far, and what it refuses rather than half-doing.</summary>
    public static bool Handles(Field field) => field.Pattern is Pattern.Scalar or Pattern.Bits
                                                             or Pattern.Opaque { Until: null }
                                                             or Pattern.Group or Pattern.Chain;

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
        var run = RunGraph.Begin(message.Graph, supplied);
        var order = Path(run, reading: false).ToList();
        var resolver = new Resolver();

        foreach (var field in order) resolver.Add(Waiting(run, field));

        resolver.Resolve();

        var wire = new Emission();

        foreach (var field in order)
        {
            var appearance = run.For(field);
            int began = wire.Written;

            Write(wire, field, appearance.Value);
            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(wire.Since(began)));
        }

        return wire.Done(message.Id);
    }

    /// <summary>
    /// One appearance, as a unit of work: what it waits on, and what to do when the wait is over.
    /// </summary>
    private ResolutionNode Waiting(RunGraph run, Field field)
    {
        var appearance = run.For(field);
        var waits = Waits(run, appearance, field);

        return new ResolutionNode
        {
            Id = appearance,

            // Position and emission are a linearisation over settled extents rather than a fixed point,
            // so they are done in one sweep afterwards and are not the worklist's business.
            NotApplicable = new HashSet<Facet>
                { Facet.Realised, Facet.Present, Facet.Position, Facet.Emitted },

            DependenciesFor = facet => facet switch
            {
                Facet.Value => waits,

                // An extent that is not fixed by the declaration is however many octets the value comes
                // to, so it waits for the value — the dependency that runs the other way from what a
                // reader would guess, and the reason this is a worklist and not a sweep.
                Facet.Extent => field.Pattern.StaticWidth is null
                    ? [new FacetRef(appearance, Facet.Value)]
                    : [],

                _ => [],
            },

            // Whatever settles is written onto the appearance, not only handed back to the worklist. The
            // run graph is where a fact lives; the worklist only decides when it can be had, and anything
            // reading this later asks the node.
            Settle = (facet, _) =>
            {
                switch (facet)
                {
                    case Facet.Value: return FacetResult.Of(Settle(run, appearance, field));

                    case Facet.Extent:
                        var width = Sized(appearance, field);
                        appearance.Settle(Facet.Extent, width);
                        return FacetResult.Of(width);

                    default: return FacetResult.Of(null);
                }
            },
        };
    }

    /// <summary>Every fact this appearance's value waits on, taken from the edges that ask for them.</summary>
    private List<FacetRef> Waits(RunGraph run, RunNode appearance, Field field)
    {
        List<FacetRef> waits = [];

        var asking = field.Pattern is Pattern.Bits { Assembled: true } assembled
            ? assembled.Slices.Where(s => s.Value is not null)
                       .Select(s => message.ComputationOf(s, s.Value!)).OfType<Computation>()
            : message.ComputationOf(field, field.Value!) is { } one ? [one] : [];

        foreach (var computation in asking)
            foreach (var wanted in message.InputsOf(computation))
                if (wanted.To is Field part)
                    waits.Add(new FacetRef(run.Reach(appearance, part), Named(wanted.Facet)));

        return [.. waits.Distinct()];
    }

    /// <summary>
    /// How wide this is: what the declaration fixes, or however many octets the value came to.
    /// </summary>
    /// <remarks>
    /// Measured by writing it, which is the only honest way to know — a span is as long as what it was
    /// given. Only reached when the declaration does not say, which is also the only case where the extent
    /// waits on the value.
    /// </remarks>
    private int Sized(RunNode appearance, Field field)
    {
        if (field.Pattern.StaticWidth is { } declared) return declared;

        var measured = new Emission();
        Write(measured, field, appearance.Value);

        return measured.Written / 8;
    }

    private static Facet Named(string facet) => facet switch
    {
        "extent" => Facet.Extent,
        "octets" => Facet.Emitted,
        "position" => Facet.Position,
        _ => Facet.Value,
    };

    /// <summary>
    /// Octets, built out of bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine's unit of emission is a <b>bit</b>, and an octet is what falls out when eight of them
    /// have gone by. That is not only for bit groups: it is the one thing that makes a field which is not
    /// a whole number of octets expressible at all, and there are protocols with them. While a group was
    /// the alignment unit, "these five bits, then those eleven" could be described and not written.
    /// </para>
    /// <para>
    /// A message that ends mid-octet is an error rather than something padded, for the reason padding is
    /// always an error unless a document asked for it: the octets that go out have to be the ones the
    /// document accounted for.
    /// </para>
    /// </remarks>
    private sealed class Emission
    {
        private readonly List<byte> _octets = [];
        private long _held;
        private int _bits;

        /// <summary>How many bits have gone by.</summary>
        public int Written { get; private set; }

        public void Put(long value, int width)
        {
            for (int at = width - 1; at >= 0; at--)
            {
                _held = (_held << 1) | ((value >> at) & 1);

                if (++_bits != 8) continue;

                _octets.Add((byte)_held);
                _held = 0;
                _bits = 0;
            }

            Written += width;
        }

        public void Put(byte[] octets) { foreach (var octet in octets) Put(octet, 8); }

        /// <summary>What has been written since a mark, for a node to hold as its own octets.</summary>
        public byte[] Since(int mark)
            => mark % 8 != 0 || Written % 8 != 0
                ? []
                : [.. _octets.Skip(mark / 8).Take((Written - mark) / 8)];

        public byte[] Done(string id)
            => _bits == 0
                ? [.. _octets]
                : throw new ProtoTypeException(
                      $"message '{id}' comes to {Written} bits, which is not a whole number of octets. "
                    + "What goes out has to be what the document accounted for, so this is an error rather "
                    + "than something padded.");
    }

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
        if (field.Pattern is Pattern.Bits { Assembled: true } assembled)
        {
            Dictionary<string, ProtoValue> runs = new(StringComparer.Ordinal);

            // Asked of the run, not of the group: the run owns its requirements path, which is the whole
            // reason it is a node.
            foreach (var slice in assembled.Slices)
                runs[slice.Name] = _evaluator.Eval(
                    slice.Value!, Given(run, appearance, Computation(slice, slice.Value!)));

            var packed = new ProtoValue.Rec(runs);
            appearance.Settle(Facet.Value, packed);
            return packed;
        }

        var value = _evaluator.Eval(
            field.Value ?? throw new ProtoTypeException(
                $"field '{field.Id}' has nothing to compute its value, so it cannot be written"),
            Given(run, appearance, Computation(field, field.Value)));

        if (field.Via is not null) value = Applied(field, value, forward: true);

        appearance.Settle(Facet.Value, value);
        return value;
    }

    private Computation Computation(Node owner, Expr source)
        => message.ComputationOf(owner, source)
        ?? throw new ProtoTypeException(
               $"'{owner.Name}': nothing in the graph computes `{source.Render()}`");

    /// <summary>
    /// Everything one computation is allowed to see, assembled from the edges that say so.
    /// </summary>
    private EvalScope Given(RunGraph run, RunNode here, Computation computation)
    {
        Dictionary<string, ProtoValue> parts = new(StringComparer.Ordinal);
        Dictionary<string, ProtoValue> outside = new(StringComparer.Ordinal);

        foreach (var wanted in message.InputsOf(computation))
            switch (wanted.To)
            {
                case Field part:
                {
                    var appearance = run.Reach(here, part);

                    parts[part.Id] = EvalScope.Record(
                        ("value", Held(appearance, Facet.Value)),
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

    public RunGraph Decode(ReadOnlySpan<byte> octets,
                           IReadOnlyDictionary<string, ProtoValue>? supplied = null)
    {
        var run = RunGraph.Begin(message.Graph, supplied);
        int at = 0;

        foreach (var field in Path(run, reading: true))
        {
            var appearance = run.For(field);
            int width = Width(run, appearance, field, octets.Length - at);

            if (at + width > octets.Length)
                throw new ProtoTypeException(
                    $"field '{field.Id}' wants {width} octet(s) and {octets.Length - at} remain");

            var taken = octets.Slice(at, width).ToArray();

            appearance.Settle(Facet.Position, at);
            appearance.Settle(Facet.Extent, width);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(taken));
            appearance.Settle(Facet.Value, Read(field, taken));

            at += width;
        }

        return run;
    }

    // ── The walk ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the message goes, decided as it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lazy on purpose. Each field is handed back before the next way on is worked out, so a fork is
    /// decided against values that have actually settled — which is the whole difference between walking
    /// the arrangement and linearising it. Nothing is planned ahead; the path is the run.
    /// </para>
    /// <para>
    /// It starts at the message and not at an arrangement, because a message's ways on <i>are</i> its
    /// arrangements. Choosing between several is the same fork as choosing between packings, one scale up,
    /// and this code cannot tell which it is doing.
    /// </para>
    /// </remarks>
    private IEnumerable<Field> Path(RunGraph run, bool reading)
        => Chain(run, message.Root, reading);

    private IEnumerable<Field> Chain(RunGraph run, Node? place, bool reading)
    {
        while (place is not null)
        {
            var members = message.Graph.From<Holds>(place).OrderBy(h => h.Order).ToList();

            if (members.Count > 0)
            {
                // A container: its members in order, each its own local run of the path. Their ways on
                // stay inside, so this cannot wander out of the thing it is walking.
                foreach (var member in members)
                    foreach (var field in Chain(run, member.To, reading)) yield return field;
            }
            else if (place is Field field)
            {
                if (!Handles(field))
                    throw new ProtoTypeException(
                        $"field '{field.Id}' is a {field.Pattern.GetType().Name.ToLowerInvariant()}, "
                      + "which this walk does not read yet");

                yield return field;
            }

            // Anything else — an arrangement, a fork, an empty packing — stands for part of the message
            // and emits nothing. It is a place, not a thing on the wire.

            place = Onward(run, place, reading);
        }
    }

    /// <summary>
    /// The way on from here: the only one, or the one the deciding node picks.
    /// </summary>
    private Node? Onward(RunGraph run, Node place, bool reading)
    {
        var ways = message.Graph.From<Then>(place).ToList();

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
        var decisions = message.Graph.From<Decides>(place).ToList();

        var deciding = decisions.FirstOrDefault(d => d.Reading == reading)
                    ?? decisions.FirstOrDefault()
                    ?? throw new ProtoTypeException(
                           $"'{place.Name}' offers several ways on and nothing says what decides");

        return deciding.To switch
        {
            Evaluated evaluated => _evaluator.Eval(
                evaluated.Source, Given(run, run.For(place), evaluated)),

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
    private int Width(RunGraph run, RunNode appearance, Field field, int remaining = 0)
    {
        if (field.Pattern.StaticWidth is { } declared) return declared;

        // A span with nothing sizing it takes what is left, which is what "as many as fit" comes to once
        // the count has stopped being something anyone declares.
        if (message.ProducerOf(field, "extent") is null && field.Pattern is Pattern.Chain) return remaining;

        if (message.ProducerOf(field, "extent") is { } measured && measured is Evaluated evaluated)
            return (int)_evaluator.Eval(evaluated.Source, Given(run, appearance, measured)).AsInt();

        throw new ProtoTypeException($"field '{field.Id}' has no width and nothing computes its extent");
    }

    private void Write(Emission wire, Field field, ProtoValue value)
    {
        switch (field.Pattern)
        {
            case Pattern.Scalar scalar:
                wire.Put(scalar.BigEndian
                    ? value.AsInt()
                    : Unfixed(Fixed(value.AsInt(), scalar), scalar with { BigEndian = true }),
                    scalar.Octets * 8);
                break;

            // On the way out a span is however many octets it was given: what measures it reads that back
            // as an extent, which is the other half of one declaration rather than a second one.
            case Pattern.Opaque { Width: var declared }:
                wire.Put(declared is { } fixedWidth
                    ? Sized(value.AsBytes(), fixedWidth, field)
                    : value.AsBytes());
                break;

            // Run by run, in order, and the octet boundary is wherever it happens to land.
            // A run of elements: the field's value is the list, and what it may hold is the shape each
            // item takes. There is no per-item expression and no item to bind — an element definition
            // says how one is written, and the list says how many.
            case Pattern.Chain:
            {
                var shape = Admitted(field);

                foreach (var item in value.AsList()) Write(wire, shape, item);

                break;
            }

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
        ProtoValue value = field.Pattern switch
        {
            Pattern.Scalar scalar => ProtoValue.Of(Unfixed(taken, scalar)),
            Pattern.Opaque => ProtoValue.Of(taken),
            Pattern.Bits bits => Unpacked(bits, taken),
            Pattern.Chain => Elements(field, taken),
            _ => throw new ProtoTypeException($"field '{field.Id}' cannot be read by this walk yet"),
        };

        return field.Via is null ? value : Applied(field, value, forward: false);
    }

    /// <summary>The one shape a span admits, where it admits exactly one.</summary>
    private Field Admitted(Field field)
    {
        var admits = message.Graph.From<Allowed>(field).Select(e => e.To).OfType<Field>().ToList();

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

        var arguments = message.ArgumentsOf(field);

        if (forward) return converter.Apply(value, arguments);

        if (converter.Inverse is null || !_converters.TryGet(converter.Inverse, out var back) || back is null)
            throw new ProtoTypeException($"converter '{via.Name}' declares no inverse");

        return back.Apply(value, arguments);
    }

    private static byte[] Fixed(long value, Pattern.Scalar scalar)
    {
        var octets = new byte[scalar.Octets];

        for (int i = scalar.Octets - 1; i >= 0; i--) { octets[i] = (byte)(value & 0xFF); value >>= 8; }

        return scalar.BigEndian ? octets : [.. octets.Reverse()];
    }

    private static long Unfixed(byte[] octets, Pattern.Scalar scalar)
    {
        long value = 0;

        foreach (var octet in scalar.BigEndian ? octets : octets.Reverse()) value = (value << 8) | octet;

        return scalar.Signed && octets.Length < 8 && (value & (1L << ((octets.Length * 8) - 1))) != 0
            ? value - (1L << (octets.Length * 8))
            : value;
    }

    private static byte[] Sized(byte[] value, int width, Field field)
        => value.Length == width
            ? value
            : throw new ProtoTypeException(
                  $"field '{field.Id}' is {width} octet(s) and was given {value.Length}");

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
