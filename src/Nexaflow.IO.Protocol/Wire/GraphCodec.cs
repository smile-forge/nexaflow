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
                                                             or Pattern.Opaque { Width: not null }
                                                             or Pattern.Group;

    // ── Writing ───────────────────────────────────────────────────────────────

    public byte[] Encode(IReadOnlyDictionary<string, ProtoValue> supplied)
    {
        var run = RunGraph.Begin(message.Graph, supplied);
        var wire = new Emission();

        foreach (var node in Path())
        {
            var appearance = run.For(node);
            var field = (Field)node;
            int began = wire.Written;

            var value = Settle(run, appearance, field);
            Write(wire, field, value);

            appearance.Settle(Facet.Position, began / 8);
            appearance.Settle(Facet.Extent, (wire.Written - began) / 8);
            appearance.Settle(Facet.Emitted, ProtoValue.Of(wire.Since(began)));
        }

        return wire.Done(message.Id);
    }

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

        foreach (var node in Path())
        {
            var field = (Field)node;
            var appearance = run.For(node);
            int width = Width(field);

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
    /// The nodes this message lays out, from the arrangement rather than from a field list.
    /// </summary>
    /// <remarks>
    /// One arrangement so far. Choosing between several is the same question the forks ask, one scale up,
    /// and it lands here when a document can declare more than one.
    /// </remarks>
    private IEnumerable<Node> Path()
    {
        var arrangement = message.Arrangements.SingleOrDefault()
            ?? throw new ProtoTypeException($"message '{message.Id}' offers no arrangement to walk");

        foreach (var node in message.Walk(arrangement))
        {
            if (node is not Field field)
                throw new ProtoTypeException($"'{node.Name}' is on the path and is not a field");

            if (!Handles(field))
                throw new ProtoTypeException(
                    $"field '{field.Id}' is a {field.Pattern.GetType().Name.ToLowerInvariant()}, which "
                  + "this walk does not read yet");

            yield return field;
        }
    }

    // ── Octets ────────────────────────────────────────────────────────────────

    private static int Width(Field field)
        => field.Pattern.StaticWidth
        ?? throw new ProtoTypeException($"field '{field.Id}' has no width this walk can work out yet");

    private static void Write(Emission wire, Field field, ProtoValue value)
    {
        switch (field.Pattern)
        {
            case Pattern.Scalar scalar:
                wire.Put(scalar.BigEndian
                    ? value.AsInt()
                    : Unfixed(Fixed(value.AsInt(), scalar), scalar with { BigEndian = true }),
                    scalar.Octets * 8);
                break;

            case Pattern.Opaque { Width: { } width }:
                wire.Put(Sized(value.AsBytes(), width, field));
                break;

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
        ProtoValue value = field.Pattern switch
        {
            Pattern.Scalar scalar => ProtoValue.Of(Unfixed(taken, scalar)),
            Pattern.Opaque => ProtoValue.Of(taken),
            Pattern.Bits bits => Unpacked(bits, taken),
            _ => throw new ProtoTypeException($"field '{field.Id}' cannot be read by this walk yet"),
        };

        return field.Via is null ? value : Applied(field, value, forward: false);
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
