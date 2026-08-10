using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>What one octet run contributed, for the dry-run breakdown.</summary>
/// <param name="Offset">Absolute offset in the message.</param>
/// <param name="Length">Octets.</param>
/// <param name="Name">Field or bit-slice name.</param>
/// <param name="Value">The value it carried.</param>
public readonly record struct WireSpan(int Offset, int Length, string Name, ProtoValue Value)
{
    public override string ToString() => $"{Offset}:{Length}:{Name}:{Value}";
}

/// <summary>The result of decoding: named captures plus the span breakdown.</summary>
public sealed record DecodeResult(IReadOnlyDictionary<string, ProtoValue> Captures, IReadOnlyList<WireSpan> Spans)
{
    public ProtoValue this[string name] => Captures.TryGetValue(name, out var v) ? v : ProtoValue.Nothing;

    /// <summary>The breakdown, in the same shape the corpus fixtures use — which is what makes a decode
    /// comparable against a capture's own field listing rather than merely plausible.</summary>
    public string Breakdown => string.Join("\n", Spans);
}

/// <summary>
/// Decode and encode a message against one <see cref="MessageDef"/>.
///
/// <para>
/// Encode runs through the <see cref="Resolver"/>, with dependencies derived from each field's expression,
/// so a value computed from another field's extent schedules itself — no placeholder, no back-patch pass,
/// and a genuine cycle reported as one before any octet is produced. Regions, choices and repetitions come
/// into existence through <see cref="Facet.Realised"/>, so a branch that never expanded is a named failure
/// rather than a short message that looks structurally valid.
/// </para>
///
/// <para>
/// Both directions read the same <c>fields.&lt;id&gt;.value</c> vocabulary. On decode it means "what was
/// just read"; on encode, "what is about to be written". That is what lets one discriminator expression
/// select the arm in both directions instead of a decode guard paired with a hand-maintained encode-side
/// variant selector.
/// </para>
/// </summary>
public sealed class MessageCodec(MessageDef message, ConverterTable? converters = null)
{
    private readonly MessageDef _message = message;
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;

    /// <summary>Validates the definition. Always call before use; the issues are authoring errors.</summary>
    public IReadOnlyList<string> Validate() => _message.Validate();

    // ── Decode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the message. <paramref name="scope"/> supplies anything a count or discriminator expression
    /// reads besides <c>fields.*</c>; the field vocabulary is built up as the walk proceeds, so a
    /// discriminator sees every field ahead of it and none behind.
    /// </summary>
    public DecodeResult Decode(ReadOnlySpan<byte> bytes, EvalScope? scope = null)
    {
        var reading = new Reading(bytes.ToArray(), new Evaluator(_converters), scope ?? new EvalScope());

        foreach (var field in _message.Fields) Read(field, reading, field.CaptureName, exposed: true);

        if (reading.Offset != reading.Bytes.Length)
            throw new ProtoTypeException(
                $"decoded {reading.Offset} octet(s) but the message is {reading.Bytes.Length} — trailing "
              + "data is an error rather than something ignored, because ignoring it accepts a malformed "
              + "capture as valid");

        return new DecodeResult(reading.Captures, reading.Spans);
    }

    /// <summary>The live state of one decode: where we are, what has been bound, what it cost.</summary>
    private sealed class Reading(byte[] bytes, Evaluator evaluator, EvalScope scope)
    {
        public readonly byte[] Bytes = bytes;
        public int Offset;
        public readonly Dictionary<string, ProtoValue> Captures = new(StringComparer.Ordinal);
        public readonly List<WireSpan> Spans = [];

        /// <summary>Field id → what it read and what it cost. Deliberately keyed by <i>id</i> rather than
        /// capture name: this is what expressions address, and a repeated element overwrites its own entry
        /// each iteration, which is exactly loop-variable behaviour.</summary>
        private readonly Dictionary<string, (ProtoValue Value, int Extent)> _facets = new(StringComparer.Ordinal);

        public void Note(string fieldId, ProtoValue value, int extent) => _facets[fieldId] = (value, extent);

        public ProtoValue Eval(Expr expression)
        {
            Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

            foreach (var (id, facet) in _facets)
                byField[id] = EvalScope.Record(("value", facet.Value),
                                               ("extent", ProtoValue.Of((long)facet.Extent)));

            return evaluator.Eval(expression, scope.Child().Set("fields", new ProtoValue.Rec(byField)));
        }
    }

    /// <summary>
    /// Reads one field, composites included.
    /// </summary>
    /// <param name="path">Name used for spans. Qualified inside a repetition, because
    /// <c>registers[1]</c> and <c>registers[2]</c> are not the same octets and a breakdown that calls them
    /// both <c>register</c> cannot be checked against a capture.</param>
    /// <param name="exposed">Whether this field's binding is a message-level capture. A repeated element's
    /// members are not: they belong to the element, and flattening them leaves only whichever iteration
    /// happened to run last.</param>
    private ProtoValue Read(Field field, Reading r, string path, bool exposed)
    {
        switch (field.Pattern)
        {
            case Pattern.Group group:
            {
                int start = r.Offset;
                Dictionary<string, ProtoValue> members = new(StringComparer.Ordinal);

                foreach (var child in group.Fields)
                    members[child.CaptureName] = Read(child, r, ChildPath(path, child, exposed), exposed);

                return Bind(field, r, new ProtoValue.Rec(members), r.Offset - start, exposed);
            }

            case Pattern.Choice choice:
            {
                long key = r.Eval(choice.Key).AsInt();
                var arm = choice.Select(key, field.Id);
                int start = r.Offset;

                foreach (var child in arm.Fields)
                    Read(child, r, ChildPath(path, child, exposed), exposed);

                // The arm NAME is the value. A later step then branches on which shape arrived, rather
                // than re-deriving it from the raw discriminator and hoping the two rules stay in step.
                return Bind(field, r, ProtoValue.Of(arm.Name), r.Offset - start, exposed);
            }

            case Pattern.Repeat repeat:
            {
                long count = r.Eval(repeat.Count).AsInt();

                if (count < 0 || count > ProtoLimits.MaxRepetitions)
                    throw new ProtoTypeException(
                        $"field '{field.Id}': the count resolved to {count}, which is outside 0.."
                      + $"{ProtoLimits.MaxRepetitions}. A corrupt or hostile length must not be able to buy "
                      + "an unbounded allocation.");

                int start = r.Offset;
                List<ProtoValue> items = [];

                for (int i = 0; i < count; i++)
                    items.Add(Read(repeat.Element, r, $"{path}[{i}]", exposed: false));

                return Bind(field, r, new ProtoValue.List(items), r.Offset - start, exposed);
            }

            default:
                return ReadLeaf(field, r, path, exposed);
        }
    }

    private static string ChildPath(string path, Field child, bool exposed)
        => exposed ? child.CaptureName : $"{path}.{child.CaptureName}";

    private static ProtoValue Bind(Field field, Reading r, ProtoValue value, int extent, bool exposed)
    {
        r.Note(field.Id, value, extent);
        if (exposed) r.Captures[field.CaptureName] = value;
        return value;
    }

    private ProtoValue ReadLeaf(Field field, Reading r, string path, bool exposed)
    {
        // The two shapes whose width is not in the declaration. A continuation chain measures itself; an
        // octet run is measured by something already read.
        int width = field.Pattern switch
        {
            Pattern.Varint varint => ScanContinuation(field, varint, r),
            Pattern.Opaque { Length: { } length } => Recovered(field, length, r),
            _ => field.Pattern.StaticWidth
                 ?? throw new ProtoTypeException($"field '{field.Id}' has no way to determine its width"),
        };

        if (r.Offset + width > r.Bytes.Length)
            throw new ProtoTypeException(
                $"field '{field.Id}' needs {width} octet(s) at offset {r.Offset}, but the message is "
              + $"{r.Bytes.Length} octets — the definition and the data disagree");

        var run = r.Bytes.AsSpan(r.Offset, width);
        int at = r.Offset;
        ProtoValue value;

        switch (field.Pattern)
        {
            case Pattern.Bits bits:
            {
                long packed = ReadUnsigned(run);
                int remaining = bits.TotalBits;
                Dictionary<string, ProtoValue> slices = new(StringComparer.Ordinal);

                foreach (var slice in bits.Slices)
                {
                    remaining -= slice.Width;
                    var sliced = ProtoValue.Of((packed >> remaining) & ((1L << slice.Width) - 1));
                    slices[slice.Name] = sliced;

                    r.Spans.Add(new WireSpan(at, width, exposed ? slice.Name : $"{path}.{slice.Name}", sliced));
                    if (exposed) r.Captures[slice.Name] = sliced;
                }

                value = new ProtoValue.Rec(slices);
                break;
            }

            case Pattern.Scalar scalar:
            {
                long raw = scalar.Signed ? ReadSigned(run, scalar.BigEndian)
                                         : ReadUnsigned(run, scalar.BigEndian);
                value = Convert(ProtoValue.Of(raw), field.Via, forward: false);
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }

            case Pattern.Varint varint:
            {
                value = Convert(Unpack(run.ToArray(), varint), field.Via, forward: false);

                // Minimality checked by re-encoding, which is the backward round-trip law applied at
                // decode time. A padded chain would otherwise decode to a value that re-encodes shorter,
                // making encode(decode(b)) != b for input the protocol calls malformed anyway.
                if (varint.Minimal)
                {
                    var shortest = Pack(value.AsInt(), varint);
                    if (!shortest.AsSpan().SequenceEqual(run))
                        throw new ProtoTypeException(
                            $"field '{field.Id}': {Hex(run)} is not the shortest encoding of {value} — that "
                          + $"is {Hex(shortest)}. A non-shortest chain is rejected rather than carried, "
                          + "because remembering the padding in order to reproduce it means preserving "
                          + "malformed input instead of refusing it.");
                }
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }

            default:
            {
                value = Convert(ProtoValue.Of(run.ToArray()), field.Via, forward: false);
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }
        }

        r.Offset += width;
        return Bind(field, r, value, width, exposed);
    }

    /// <summary>
    /// How many octets the continuation chain at the cursor occupies. Bounded, because the alternative is
    /// letting the data decide how much of it there is.
    /// </summary>
    private static int ScanContinuation(Field field, Pattern.Varint varint, Reading r)
    {
        for (int n = 0; n < varint.MaxOctets; n++)
        {
            if (r.Offset + n >= r.Bytes.Length)
                throw new ProtoTypeException(
                    $"field '{field.Id}': the continuation chain at offset {r.Offset} runs off the end of "
                  + $"a {r.Bytes.Length}-octet message");

            if ((r.Bytes[r.Offset + n] & 0x80) == 0) return n + 1;
        }

        throw new ProtoTypeException(
            $"field '{field.Id}': the continuation chain at offset {r.Offset} is still continuing after "
          + $"{varint.MaxOctets} octet(s), which is its declared bound");
    }

    private static int Recovered(Field field, Expr declared, Reading r)
    {
        long length = r.Eval(declared).AsInt();

        if (length < 0 || length > r.Bytes.Length)
            throw new ProtoTypeException(
                $"field '{field.Id}': the length resolved to {length}, which cannot be an extent inside a "
              + $"{r.Bytes.Length}-octet message");

        return (int)length;
    }

    /// <summary>
    /// The continuation codec, borrowed from the converter table rather than written again here.
    ///
    /// <para>
    /// One implementation, and it is the one the inverse laws already test — a second copy inside the
    /// codec would be a second thing to keep true, and the group-order parameter is exactly where a
    /// duplicate quietly picks one family's answer.
    /// </para>
    /// </summary>
    private byte[] Pack(long value, Pattern.Varint varint) => Through("base128", ProtoValue.Of(value), varint).AsBytes();

    private ProtoValue Unpack(byte[] octets, Pattern.Varint varint) => Through("unbase128", ProtoValue.Of(octets), varint);

    private ProtoValue Through(string converter, ProtoValue value, Pattern.Varint varint)
    {
        if (!_converters.TryGet(converter, out var c) || c is null)
            throw new ProtoTypeException($"the converter table has no '{converter}'");

        return c.Apply(value, [ProtoValue.Of(varint.Order == GroupOrder.MostSignificantFirst
            ? "msbFirst" : "lsbFirst")]);
    }

    // ── Encode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces the octets. <paramref name="scope"/> supplies <c>inputs.*</c> and anything else the field
    /// expressions read; <c>fields.&lt;id&gt;.value</c> and <c>.extent</c> resolve through the worklist.
    /// </summary>
    public byte[] Encode(EvalScope scope)
    {
        var encoder = new Encoder(this, scope, new Evaluator(_converters));
        var resolver = new Resolver();

        foreach (var field in _message.Fields)
            foreach (var node in encoder.Nodes(field, field.Id, literal: null))
                resolver.Add(node);

        var settled = resolver.Resolve();

        // Position is a linearisation, not a fixpoint: one ordered sweep once extents have settled, now
        // through the realised shape rather than a flat list.
        var output = new List<byte>();
        foreach (var field in _message.Fields) Emit(field, field.Id, encoder, settled, output);
        return [.. output];
    }

    private void Emit(Field field, string nodeId, Encoder encoder,
                      IReadOnlyDictionary<FacetRef, object?> settled, List<byte> output)
    {
        switch (field.Pattern)
        {
            case Pattern.Group group:
                foreach (var child in group.Fields) Emit(child, child.Id, encoder, settled, output);
                break;

            case Pattern.Choice:
                foreach (var child in encoder.Chosen[nodeId].Fields) Emit(child, child.Id, encoder, settled, output);
                break;

            case Pattern.Repeat repeat:
                for (int i = 0; i < encoder.Counts[nodeId]; i++)
                    Emit(repeat.Element, $"{nodeId}[{i}]", encoder, settled, output);
                break;

            default:
                Write(output, field, settled[new FacetRef(nodeId, Facet.Value)] as ProtoValue ?? ProtoValue.Nothing);
                break;
        }
    }

    /// <summary>
    /// Builds the resolution nodes for one encode, and records which shape each composite settled on so the
    /// emission sweep walks the same tree the resolver did.
    /// </summary>
    private sealed class Encoder(MessageCodec codec, EvalScope scope, Evaluator evaluator)
    {
        /// <summary>Choice field id → the arm that was selected.</summary>
        public readonly Dictionary<string, Arm> Chosen = new(StringComparer.Ordinal);

        /// <summary>Repetition field id → how many elements were realised.</summary>
        public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);

        private static readonly HashSet<Facet> LeafNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Emitted];

        private static readonly HashSet<Facet> RegionNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Value, Facet.Emitted];

        private static readonly HashSet<Facet> ExpandingNotApplicable = [Facet.Present, Facet.Emitted];

        public List<ResolutionNode> Nodes(Field field, string nodeId, ProtoValue? literal)
        {
            List<ResolutionNode> nodes = [];

            switch (field.Pattern)
            {
                case Pattern.Group group:
                {
                    Composite(field, literal);

                    foreach (var child in group.Fields) nodes.AddRange(Nodes(child, child.Id, null));
                    var extents = group.Fields.Select(c => new FacetRef(c.Id, Facet.Extent)).ToList();

                    nodes.Add(new ResolutionNode
                    {
                        Id = nodeId,
                        NotApplicable = RegionNotApplicable,
                        DependenciesFor = f => f == Facet.Extent ? extents : [],
                        Settle = (f, inputs) => FacetResult.Of(f == Facet.Extent ? Sum(extents, inputs) : null),
                    });
                    break;
                }

                case Pattern.Choice choice:
                {
                    Composite(field, literal);

                    var keyNeeds = Refs(choice.Key);
                    List<FacetRef>? armExtents = null;

                    nodes.Add(new ResolutionNode
                    {
                        Id = nodeId,
                        NotApplicable = ExpandingNotApplicable,

                        DependenciesFor = f => f switch
                        {
                            Facet.Realised => keyNeeds,
                            Facet.Value => [new FacetRef(nodeId, Facet.Realised)],
                            Facet.Extent => armExtents is null
                                ? [new FacetRef(nodeId, Facet.Realised)]
                                : [new FacetRef(nodeId, Facet.Realised), .. armExtents],
                            _ => [],
                        },

                        Settle = (f, inputs) =>
                        {
                            switch (f)
                            {
                                case Facet.Realised:
                                {
                                    long key = evaluator.Eval(choice.Key, FieldsScope(inputs)).AsInt();
                                    var arm = choice.Select(key, field.Id);

                                    Chosen[nodeId] = arm;
                                    armExtents = arm.Fields.Select(c => new FacetRef(c.Id, Facet.Extent)).ToList();

                                    return new FacetResult(arm.Name,
                                        [.. arm.Fields.SelectMany(c => Nodes(c, c.Id, null))]);
                                }

                                case Facet.Value: return FacetResult.Of(ProtoValue.Of(Chosen[nodeId].Name));
                                case Facet.Extent: return FacetResult.Of(Sum(armExtents!, inputs));
                                default: return FacetResult.Of(null);
                            }
                        },
                    });
                    break;
                }

                case Pattern.Repeat repeat:
                {
                    Composite(field, literal);

                    List<FacetRef> valueNeeds = field.Value is null ? [] : Refs(field.Value);
                    List<FacetRef>? elementExtents = null;
                    ProtoValue? sequence = null;

                    // Realising the elements is what publishes their extents back to this node, which is
                    // why the count cannot be a dependency of the extent: it is a result of it.
                    FacetResult Expand()
                    {
                        if (sequence is not ProtoValue.List list)
                            throw new ProtoTypeException(
                                $"field '{field.Id}' repeats, so its value must be a list, got "
                              + $"{sequence?.Kind ?? "Null"}");

                        if (list.Items.Count > ProtoLimits.MaxRepetitions)
                            throw new ProtoTypeException(
                                $"field '{field.Id}': {list.Items.Count} elements exceeds the "
                              + $"{ProtoLimits.MaxRepetitions} ceiling");

                        Counts[nodeId] = list.Items.Count;
                        List<ResolutionNode> children = [];
                        List<FacetRef> extents = [];

                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            var elementId = $"{nodeId}[{i}]";
                            children.AddRange(Nodes(repeat.Element, elementId, list.Items[i]));
                            extents.Add(new FacetRef(elementId, Facet.Extent));
                        }

                        elementExtents = extents;
                        return new FacetResult(list.Items.Count, children);
                    }

                    nodes.Add(new ResolutionNode
                    {
                        Id = nodeId,
                        NotApplicable = ExpandingNotApplicable,

                        DependenciesFor = f => f switch
                        {
                            Facet.Value => valueNeeds,
                            Facet.Realised => [new FacetRef(nodeId, Facet.Value)],
                            Facet.Extent => elementExtents is null
                                ? [new FacetRef(nodeId, Facet.Realised)]
                                : [new FacetRef(nodeId, Facet.Realised), .. elementExtents],
                            _ => [],
                        },

                        Settle = (f, inputs) => f switch
                        {
                            Facet.Value => FacetResult.Of(sequence = codec.Evaluate(field, scope, evaluator, inputs)),
                            Facet.Realised => Expand(),
                            Facet.Extent => FacetResult.Of(Sum(elementExtents!, inputs)),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }

                default:
                {
                    List<FacetRef> valueNeeds = literal is not null || field.Value is null
                        ? [] : Refs(field.Value);

                    // The one edge step 4 never drew. A fixed width is axiomatic and settles with no
                    // prerequisites; a continuation chain or a recovered octet run cannot be measured
                    // until it is known, so `Extent` declares a dependency on `Value` and the worklist
                    // does the rest. No pass, no placeholder, no widen-and-retry.
                    int? fixedWidth = field.Pattern.StaticWidth;
                    ProtoValue? settledValue = null;

                    nodes.Add(new ResolutionNode
                    {
                        Id = nodeId,
                        NotApplicable = LeafNotApplicable,

                        DependenciesFor = f => f switch
                        {
                            Facet.Value => valueNeeds,
                            Facet.Extent when fixedWidth is null => [new FacetRef(nodeId, Facet.Value)],
                            _ => [],
                        },

                        Settle = (f, inputs) => f switch
                        {
                            Facet.Extent => FacetResult.Of(fixedWidth ?? codec.Measure(field, settledValue)),
                            Facet.Value => FacetResult.Of(settledValue = literal is not null
                                ? codec.Convert(literal, field.Via, forward: true)
                                : codec.Evaluate(field, scope, evaluator, inputs)),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }
            }

            return nodes;
        }

        /// <summary>A composite reached as a repeated element would need its own field namespace, which is
        /// the piece that does not exist yet. Refused loudly rather than silently resolving against
        /// whichever iteration ran last.</summary>
        private static void Composite(Field field, ProtoValue? literal)
        {
            if (literal is not null)
                throw new ProtoTypeException(
                    $"field '{field.Id}' is a composite supplied with an element value — a repeated "
                  + "composite needs per-element field naming, which is not built");
        }

        private EvalScope FieldsScope(IReadOnlyDictionary<FacetRef, object?> resolved)
            => scope.Child().Set("fields", codec.FieldsRecord(resolved));

        private static int Sum(IReadOnlyList<FacetRef> extents, IReadOnlyDictionary<FacetRef, object?> inputs)
            => extents.Sum(r => Extent(inputs[r]));

        private static int Extent(object? settled) => settled switch
        {
            int i => i,
            long l => (int)l,
            ProtoValue v => (int)v.AsInt(),
            _ => throw new ProtoTypeException($"expected an extent, got {settled?.GetType().Name ?? "nothing"}"),
        };

        private static List<FacetRef> Refs(Expr expression)
            => MessageDef.FieldReferences(expression)
                .Select(r => new FacetRef(r.Field,
                    r.Facet.Equals("extent", StringComparison.OrdinalIgnoreCase) ? Facet.Extent : Facet.Value))
                .Distinct()
                .ToList();
    }

    private ProtoValue Evaluate(Field field, EvalScope scope, Evaluator evaluator,
                                IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        if (field.Value is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' has no value expression, so it cannot be encoded. A decode-only field "
              + "must be declared as such rather than left silent.");

        var child = scope.Child().Set("fields", FieldsRecord(resolved));
        return Convert(evaluator.Eval(field.Value, child), field.Via, forward: true);
    }

    /// <summary>
    /// Settled facets as <c>fields.&lt;id&gt;.value</c> / <c>.extent</c>, so an expression reads a
    /// dependency back by the same name it used to declare it.
    /// </summary>
    private ProtoValue FieldsRecord(IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

        foreach (var (reference, value) in resolved)
        {
            if (reference.Facet is not (Facet.Value or Facet.Extent)) continue;

            var slot = byField.TryGetValue(reference.NodeId, out var existing) && existing is ProtoValue.Rec r
                ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
                : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

            slot[reference.Facet == Facet.Extent ? "extent" : "value"] = value switch
            {
                ProtoValue pv => pv,
                int i => ProtoValue.Of((long)i),
                long l => ProtoValue.Of(l),
                _ => ProtoValue.Nothing,
            };

            byField[reference.NodeId] = new ProtoValue.Rec(slot);
        }

        return new ProtoValue.Rec(byField);
    }

    /// <summary>How many octets a value-dependent shape will occupy. Measured by producing the octets, so
    /// the extent and the emission cannot disagree.</summary>
    private int Measure(Field field, ProtoValue? value) => field.Pattern switch
    {
        Pattern.Varint varint => Pack(Settled(field, value).AsInt(), varint).Length,
        Pattern.Opaque { Length: not null } => Settled(field, value).AsBytes().Length,
        _ => throw new ProtoTypeException($"field '{field.Id}' has no way to determine its width"),
    };

    private static ProtoValue Settled(Field field, ProtoValue? value)
        => value ?? throw new ProtoTypeException(
            $"field '{field.Id}': its extent was asked for before its value settled");

    private void Write(List<byte> output, Field field, ProtoValue value)
    {
        switch (field.Pattern)
        {
            case Pattern.Bits bits:
            {
                // A bit group's value is a record of its slice names, so the same declaration reads and
                // writes without a second description of the packing.
                long packed = 0;
                int remaining = bits.TotalBits;

                foreach (var slice in bits.Slices)
                {
                    remaining -= slice.Width;
                    long v = value is ProtoValue.Rec rec && rec.Members.TryGetValue(slice.Name, out var m)
                        ? m.AsInt() : 0;

                    long max = (1L << slice.Width) - 1;
                    if (v < 0 || v > max)
                        throw new ProtoTypeException(
                            $"'{slice.Name}' = {v} does not fit in {slice.Width} bit(s) (max {max})");

                    packed |= v << remaining;
                }
                WriteUnsigned(output, packed, bits.TotalBits / 8, bigEndian: true);
                break;
            }

            case Pattern.Scalar scalar:
                WriteUnsigned(output, value.AsInt(), scalar.Octets, scalar.BigEndian);
                break;

            case Pattern.Opaque opaque:
            {
                var bytes = value.AsBytes();

                // A declared width is asserted rather than trimmed or padded: a span that is the wrong
                // size is a wrong value, and quietly fixing it produces a self-consistently wrong message.
                if (opaque.Width is { } width && bytes.Length != width)
                    throw new ProtoTypeException(
                        $"field '{field.Id}' is {width} octet(s) but the value is {bytes.Length}");

                output.AddRange(bytes);
                break;
            }

            case Pattern.Varint varint:
                output.AddRange(Pack(value.AsInt(), varint));
                break;
        }
    }

    private ProtoValue Convert(ProtoValue value, string? via, bool forward)
    {
        if (via is null) return value;

        if (!_converters.TryGet(via, out var converter) || converter is null)
            throw new ProtoTypeException($"unknown converter '{via}'");

        // Decode applies the inverse, encode the forward direction — one declaration, both ways.
        if (forward) return converter.Apply(value, []);

        if (converter.Inverse is null || !_converters.TryGet(converter.Inverse, out var inverse) || inverse is null)
            throw new ProtoTypeException(
                $"converter '{via}' declares no inverse, so it cannot be used on a field that must decode");

        return inverse.Apply(value, []);
    }

    // ── Octet plumbing ────────────────────────────────────────────────────────

    /// <summary>Hex for a diagnostic. Spelled out because this class has its own <c>Convert</c>, which
    /// would otherwise shadow the framework one at every call site.</summary>
    private static string Hex(ReadOnlySpan<byte> octets) => System.Convert.ToHexString(octets).ToLowerInvariant();

    private static long ReadUnsigned(ReadOnlySpan<byte> run, bool bigEndian = true)
    {
        long v = 0;
        if (bigEndian) foreach (var b in run) v = (v << 8) | b;
        else for (int i = run.Length - 1; i >= 0; i--) v = (v << 8) | run[i];
        return v;
    }

    private static long ReadSigned(ReadOnlySpan<byte> run, bool bigEndian)
    {
        long v = ReadUnsigned(run, bigEndian);
        int bits = run.Length * 8;
        long signBit = 1L << (bits - 1);
        return (v & signBit) != 0 ? v - (1L << bits) : v;
    }

    private static void WriteUnsigned(List<byte> output, long value, int octets, bool bigEndian)
    {
        Span<byte> buffer = stackalloc byte[octets];
        for (int i = octets - 1; i >= 0; i--) { buffer[i] = (byte)(value & 0xFF); value >>= 8; }
        if (!bigEndian) buffer.Reverse();
        foreach (var b in buffer) output.Add(b);
    }
}
