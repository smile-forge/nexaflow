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
/// and a genuine cycle reported as one before any octet is produced. Regions, choices and chained
/// structures come into existence through <see cref="Facet.Realised"/>, so a branch that never expanded is
/// a named failure rather than a short message that looks structurally valid.
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
    /// Reads the message. <paramref name="scope"/> supplies anything a bound or continuation expression
    /// reads besides <c>fields.*</c>; the field vocabulary is built up as the walk proceeds, so an
    /// expression sees every field ahead of it and none behind.
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

    /// <summary>The live state of one decode: where we are, how far we may go, and what has been bound.</summary>
    private sealed class Reading(byte[] bytes, Evaluator evaluator, EvalScope scope)
    {
        public readonly byte[] Bytes = bytes;
        public int Offset;
        public readonly List<WireSpan> Spans = [];

        /// <summary>
        /// What has been bound, outermost first. The outermost frame is the message's captures; a chained
        /// structure pushes one, and that frame <i>is</i> the structure's value.
        ///
        /// <para>
        /// Flat within a frame rather than mirroring the nesting, which is the same shape the message level
        /// has always had. A tree-shaped instance value looks tidier and loses things: an arm's fields
        /// belong to whichever arm was taken, so under a tree they hang off a node whose own value is the
        /// arm's name, and nothing at the top can see them.
        /// </para>
        /// </summary>
        private readonly List<Dictionary<string, ProtoValue>> _bindings = [new(StringComparer.Ordinal)];

        public IReadOnlyDictionary<string, ProtoValue> Captures => _bindings[0];

        public void Capture(string name, ProtoValue value) => _bindings[^1][name] = value;

        /// <summary>Field scopes, outermost first. A chain instance pushes one, so instance 2's names are
        /// its own and anything it does not declare resolves outward.</summary>
        private readonly List<Dictionary<string, (ProtoValue Value, int Extent)>> _scopes =
            [new(StringComparer.Ordinal)];

        /// <summary>Region bounds, outermost first. The message itself is the outermost.</summary>
        private readonly List<int> _limits = [bytes.Length];

        /// <summary>How far the innermost enclosing region runs.</summary>
        public int Limit => _limits[^1];

        /// <summary>Unread octets left in that region — what "is there another" usually asks about.</summary>
        public int Room => Limit - Offset;

        public void Note(string fieldId, ProtoValue value, int extent) => _scopes[^1][fieldId] = (value, extent);

        /// <summary>Opens a structure: its own field scope for expressions, and its own bindings for the
        /// value it will become.</summary>
        public void EnterStructure()
        {
            _scopes.Add(new Dictionary<string, (ProtoValue, int)>(StringComparer.Ordinal));
            _bindings.Add(new Dictionary<string, ProtoValue>(StringComparer.Ordinal));
        }

        /// <summary>Closes it, handing back everything it bound.</summary>
        public ProtoValue LeaveStructure()
        {
            _scopes.RemoveAt(_scopes.Count - 1);
            var bound = _bindings[^1];
            _bindings.RemoveAt(_bindings.Count - 1);
            return new ProtoValue.Rec(bound);
        }

        public void EnterRegion(string fieldId, int limit)
        {
            if (limit > Limit)
                throw new ProtoTypeException(
                    $"region '{fieldId}' declares that it runs to offset {limit}, which is past the "
                  + $"{Limit} its own container allows");

            _limits.Add(limit);
        }

        public void LeaveRegion() => _limits.RemoveAt(_limits.Count - 1);

        public ProtoValue Eval(Expr expression, params (string Root, ProtoValue Value)[] extra)
        {
            Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

            // Outermost first, so an instance's own names shadow the ones around it.
            foreach (var level in _scopes)
                foreach (var (id, facet) in level)
                    byField[id] = EvalScope.Record(("value", facet.Value),
                                                   ("extent", ProtoValue.Of((long)facet.Extent)));

            var child = scope.Child().Set("fields", new ProtoValue.Rec(byField));
            foreach (var (root, value) in extra) child.Set(root, value);
            return evaluator.Eval(expression, child);
        }
    }

    /// <summary>
    /// Reads one field, composites included.
    /// </summary>
    /// <param name="path">Name used for spans. Qualified inside a chain, because instance 1 and instance 2
    /// are not the same octets and a breakdown that calls them both by the element's name cannot be
    /// checked against a capture.</param>
    /// <param name="exposed">Whether this field's binding is a message-level capture. A chained
    /// structure's members are not: they belong to the instance.</param>
    private ProtoValue Read(Field field, Reading r, string path, bool exposed)
    {
        switch (field.Pattern)
        {
            case Pattern.Group group:
            {
                int start = r.Offset;

                // A declared bound turns the region into a boundary rather than a measurement, which is
                // what gives "is there room for another" something to mean.
                if (group.Extent is { } declared)
                    r.EnterRegion(field.Id, start + Bounded(field, r.Eval(declared).AsInt(), r));

                Dictionary<string, ProtoValue> members = new(StringComparer.Ordinal);

                foreach (var child in group.Fields)
                    members[child.CaptureName] = Read(child, r, ChildPath(path, child, exposed), exposed);

                if (group.Extent is not null)
                {
                    // Consuming less than the region declared is not a harmless remainder: it means the
                    // declaration and the data disagree about what is in here, and whatever is left will
                    // be read as the next field of the container.
                    if (r.Offset != r.Limit)
                        throw new ProtoTypeException(
                            $"region '{field.Id}' declared {r.Limit - start} octet(s) but its fields "
                          + $"consumed {r.Offset - start}");

                    r.LeaveRegion();
                }

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

            case Pattern.Chain chain:
            {
                int start = r.Offset;
                List<ProtoValue> instances = [];

                while (Continues(field, chain, r, instances.Count))
                {
                    if (instances.Count >= ProtoLimits.MaxChainedInstances)
                        throw new ProtoTypeException(
                            $"field '{field.Id}': the chain reached {ProtoLimits.MaxChainedInstances} "
                          + "instances, which is its ceiling. A corrupt or hostile bound must not be able "
                          + "to buy an unbounded allocation.");

                    int before = r.Offset;

                    // The instance's own field scope. This is what lets it carry its own length prefix:
                    // `fields.body.extent` inside instance 2 means instance 2's, not instance 1's and not
                    // an ambiguous document-global span.
                    r.EnterStructure();
                    var element = Read(chain.Element, r, $"{path}[{instances.Count}]", exposed: false);
                    var bound = r.LeaveStructure();

                    // A structure built from a single wire shape IS that value; anything composite is
                    // everything it bound. Wrapping a lone scalar in a one-member record would make the
                    // simplest case the awkward one to read.
                    var instance = chain.Element.Pattern.Nested.Count == 0 ? element : bound;

                    if (r.Offset == before)
                        throw new ProtoTypeException(
                            $"field '{field.Id}': an instance consumed no octets, so the continuation "
                          + "condition would never stop being true");

                    instances.Add(instance);
                }

                return Bind(field, r, new ProtoValue.List(instances), r.Offset - start, exposed);
            }

            default:
                return ReadLeaf(field, r, path, exposed);
        }
    }

    /// <summary>
    /// Is there another structure? Asked before each instance, with <c>ordinal</c> and <c>room</c> bound —
    /// so "as many as fit in the region" and "as many as the count says" are the same construct.
    /// </summary>
    private static bool Continues(Field field, Pattern.Chain chain, Reading r, int ordinal)
    {
        var answer = r.Eval(chain.Continues,
            ("ordinal", ProtoValue.Of((long)ordinal)),
            ("room", ProtoValue.Of((long)r.Room)));

        if (answer is not ProtoValue.Bool)
            throw new ProtoTypeException(
                $"field '{field.Id}': the continuation must answer whether another structure follows, "
              + $"which is a Bool — got {answer.Kind}");

        return answer.AsBool();
    }

    private static string ChildPath(string path, Field child, bool exposed)
        => exposed ? child.CaptureName : $"{path}.{child.CaptureName}";

    private static ProtoValue Bind(Field field, Reading r, ProtoValue value, int extent, bool exposed)
    {
        r.Note(field.Id, value, extent);
        r.Capture(field.CaptureName, value);
        return value;
    }

    private static int Bounded(Field field, long extent, Reading r)
        => extent >= 0 && extent <= r.Bytes.Length
            ? (int)extent
            : throw new ProtoTypeException(
                $"field '{field.Id}': {extent} cannot be an extent inside a {r.Bytes.Length}-octet message");

    private ProtoValue ReadLeaf(Field field, Reading r, string path, bool exposed)
    {
        // The two shapes whose width is not in the declaration. A continuation chain measures itself; an
        // octet run is measured by something already read.
        int width = field.Pattern switch
        {
            Pattern.Varint varint => ScanContinuation(field, varint, r),
            Pattern.EscapedInline escaped => MarkerWidth(field, escaped, r),
            Pattern.Opaque { Length: { } length } => Bounded(field, r.Eval(length).AsInt(), r),
            _ => field.Pattern.StaticWidth
                 ?? throw new ProtoTypeException($"field '{field.Id}' has no way to determine its width"),
        };

        if (r.Offset + width > r.Limit)
            throw new ProtoTypeException(
                $"field '{field.Id}' needs {width} octet(s) at offset {r.Offset}, but only {r.Room} "
              + "remain in the enclosing region — the definition and the data disagree");

        var run = r.Bytes.AsSpan(r.Offset, width);
        int at = r.Offset;
        ProtoValue value;

        // What the octets said before any converter touched them. Kept because canonicality is a property
        // of the octets, not of the value they were turned into.
        ProtoValue asRead = ProtoValue.Nothing;

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
                    r.Capture(slice.Name, sliced);
                }

                value = new ProtoValue.Rec(slices);
                break;
            }

            case Pattern.Scalar scalar:
            {
                asRead = ProtoValue.Of(scalar.Signed ? ReadSigned(run, scalar.BigEndian)
                                                     : ReadUnsigned(run, scalar.BigEndian));
                value = Convert(asRead, field, forward: false);
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }

            case Pattern.Varint varint:
            {
                asRead = Unpack(run.ToArray(), varint);
                value = Convert(asRead, field, forward: false);

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

            case Pattern.EscapedInline escaped:
            {
                asRead = UnpackEscaped(run, escaped);
                value = Convert(asRead, field, forward: false);

                if (escaped.Minimal)
                {
                    var shortest = PackEscaped(value.AsInt(), escaped);
                    if (!shortest.AsSpan().SequenceEqual(run))
                        throw new ProtoTypeException(
                            $"field '{field.Id}': {Hex(run)} is not the shortest encoding of {value} — that "
                          + $"is {Hex(shortest)}. Escaping when the value would have fitted inline, or "
                          + "carrying it in more octets than it needs, is rejected rather than carried.");
                }
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }

            default:
            {
                asRead = ProtoValue.Of(run.ToArray());
                value = Convert(asRead, field, forward: false);
                r.Spans.Add(new WireSpan(at, width, path, value));
                break;
            }
        }

        Canonical(field, r, value, asRead);

        r.Offset += width;
        return Bind(field, r, value, width, exposed);
    }

    /// <summary>
    /// Two checks with one justification: <b>what arrived must be what we would have written</b>.
    ///
    /// <para>
    /// Both were missing, and both fail the same way — silently. A field the document fixes to a constant
    /// was read, bound, and then re-encoded as the constant, so a flipped tag octet came back changed with
    /// nothing raising a word; and a value whose encoding is not canonical decoded fine and re-encoded
    /// shorter. In each case the octets out differ from the octets in while every claim the engine makes
    /// still appears to hold.
    /// </para>
    ///
    /// <para>
    /// Neither needs new vocabulary. The document already says what the value must be, and already says
    /// which converter produces it; the omission was only ever failing to look.
    /// </para>
    /// </summary>
    private void Canonical(Field field, Reading r, ProtoValue value, ProtoValue asRead)
    {
        // A value with no free names is one the document settled by itself, so the wire has no say in it.
        if (field.Value is { } declared && declared.FreeRootNames().Count == 0)
        {
            var required = r.Eval(declared);
            if (!Equals(required, value))
                throw new ProtoTypeException(
                    $"field '{field.Id}' is fixed at {required} by the document, but {value} arrived. "
                  + "Binding it anyway would re-encode as the fixed value and quietly change the octets.");
        }

        // A reversible converter must have produced these octets from this value, or the value is one the
        // encoder could never have written and the round trip is already broken.
        if (field.Via is { } via
            && _converters.TryGet(via.Name, out var converter) && converter is { Role: ConverterRole.Bijection }
            && !Equals(Through(value, via, forward: true, field.Id), asRead))
            throw new ProtoTypeException(
                $"field '{field.Id}': {asRead} is not what '{via}' produces from {value} — it produces "
              + $"{Through(value, via, forward: true, field.Id)}. A non-canonical encoding decodes cleanly "
              + "and re-encodes to different octets, so it is refused rather than carried.");
    }

    /// <summary>
    /// How many octets the continuation chain at the cursor occupies. Bounded, because the alternative is
    /// letting the data decide how much of it there is.
    /// </summary>
    private static int ScanContinuation(Field field, Pattern.Varint varint, Reading r)
    {
        for (int n = 0; n < varint.MaxOctets; n++)
        {
            if (r.Offset + n >= r.Limit)
                throw new ProtoTypeException(
                    $"field '{field.Id}': the continuation chain at offset {r.Offset} runs off the end of "
                  + "its region");

            if ((r.Bytes[r.Offset + n] & 0x80) == 0) return n + 1;
        }

        throw new ProtoTypeException(
            $"field '{field.Id}': the continuation chain at offset {r.Offset} is still continuing after "
          + $"{varint.MaxOctets} octet(s), which is its declared bound");
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
    private byte[] Pack(long value, Pattern.Varint varint)
        => Apply("base128", ProtoValue.Of(value), Order(varint)).AsBytes();

    private ProtoValue Unpack(byte[] octets, Pattern.Varint varint)
        => Apply("unbase128", ProtoValue.Of(octets), Order(varint));

    private static ProtoValue Order(Pattern.Varint varint)
        => ProtoValue.Of(varint.Order == GroupOrder.MostSignificantFirst ? "msbFirst" : "lsbFirst");

    /// <summary>
    /// The escaped-inline codec. Also borrowed rather than rewritten: the escaped form's payload is just
    /// minimal-width unsigned octets, which the converter table already knows how to produce and read.
    /// </summary>
    private byte[] PackEscaped(long value, Pattern.EscapedInline escaped)
    {
        if (value < 0)
            throw new ProtoTypeException($"an escaped-inline value cannot be negative, got {value}");

        if (value < escaped.InlineLimit) return [(byte)value];

        var octets = Apply("minuint", ProtoValue.Of(value), ProtoValue.Of("oneByte")).AsBytes();

        if (octets.Length > escaped.MaxOctets)
            throw new ProtoTypeException(
                $"{value} needs {octets.Length} octet(s), past the {escaped.MaxOctets} this field allows");

        return [(byte)(escaped.InlineLimit + octets.Length), .. octets];
    }

    private ProtoValue UnpackEscaped(ReadOnlySpan<byte> run, Pattern.EscapedInline escaped)
        => run[0] < escaped.InlineLimit
            ? ProtoValue.Of((long)run[0])
            : Apply("unminuint", ProtoValue.Of(run[1..].ToArray()));

    private static int MarkerWidth(Field field, Pattern.EscapedInline escaped, Reading r)
    {
        if (r.Offset >= r.Limit)
            throw new ProtoTypeException($"field '{field.Id}': no octets left for its marker");

        long marker = r.Bytes[r.Offset];
        if (marker < escaped.InlineLimit) return 1;

        long counted = marker - escaped.InlineLimit;

        if (counted > escaped.MaxOctets)
            throw new ProtoTypeException(
                $"field '{field.Id}': the marker at offset {r.Offset} counts {counted} octet(s), past the "
              + $"{escaped.MaxOctets} this field allows");

        return (int)(1 + counted);
    }

    private ProtoValue Apply(string converter, ProtoValue value, params ProtoValue[] args)
        => _converters.TryGet(converter, out var c) && c is not null
            ? c.Apply(value, args)
            : throw new ProtoTypeException($"the converter table has no '{converter}'");

    // ── Encode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces the octets. <paramref name="scope"/> supplies <c>inputs.*</c> and anything else the field
    /// expressions read; <c>fields.&lt;id&gt;.value</c> and <c>.extent</c> resolve through the worklist.
    /// </summary>
    public byte[] Encode(EvalScope scope)
    {
        var encoder = new Encoder(this, new Evaluator(_converters));
        var resolver = new Resolver();
        var frame = NameFrame.Root(_message.Fields, scope);

        foreach (var field in _message.Fields)
            foreach (var node in encoder.Nodes(field, frame))
                resolver.Add(node);

        var settled = resolver.Resolve();

        // Position is a linearisation, not a fixpoint: one ordered sweep once extents have settled, now
        // through the realised shape rather than a flat list.
        var output = new List<byte>();
        foreach (var field in _message.Fields) Emit(field, frame, encoder, settled, output);
        return [.. output];
    }

    /// <summary>
    /// One field scope on the encode side: which ids belong to it, what to prefix them with in the
    /// resolver's flat namespace, and the evaluation scope its expressions see.
    /// </summary>
    private sealed record NameFrame(string Prefix, IReadOnlySet<string> Local, EvalScope Scope, NameFrame? Outer)
    {
        public static NameFrame Root(IReadOnlyList<Field> fields, EvalScope scope)
            => new("", MessageDef.ScopeIds(fields), scope, null);

        /// <summary>An instance's scope: its own names prefixed, everything else still reachable outward,
        /// and <c>item</c> bound to the structure being written.</summary>
        public NameFrame Instance(Field element, string prefix, ProtoValue item)
            => new(prefix, MessageDef.ScopeIds([element]), Scope.Child().Set("item", item), this);

        public string NodeId(string fieldId) => Local.Contains(fieldId) ? Prefix + fieldId : Outward(fieldId);

        private string Outward(string fieldId) => Outer?.NodeId(fieldId) ?? fieldId;
    }

    private void Emit(Field field, NameFrame frame, Encoder encoder,
                      IReadOnlyDictionary<FacetRef, object?> settled, List<byte> output)
    {
        var nodeId = frame.Prefix + field.Id;

        switch (field.Pattern)
        {
            case Pattern.Group group:
                foreach (var child in group.Fields) Emit(child, frame, encoder, settled, output);
                break;

            case Pattern.Choice:
                foreach (var child in encoder.Chosen[nodeId].Fields) Emit(child, frame, encoder, settled, output);
                break;

            case Pattern.Chain chain:
                foreach (var instance in encoder.Instances[nodeId])
                    Emit(chain.Element, instance, encoder, settled, output);
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
    private sealed class Encoder(MessageCodec codec, Evaluator evaluator)
    {
        /// <summary>Choice node id → the arm that was selected.</summary>
        public readonly Dictionary<string, Arm> Chosen = new(StringComparer.Ordinal);

        /// <summary>Chain node id → one frame per instance, in order.</summary>
        public readonly Dictionary<string, List<NameFrame>> Instances = new(StringComparer.Ordinal);

        private static readonly HashSet<Facet> LeafNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Emitted];

        private static readonly HashSet<Facet> RegionNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Value, Facet.Emitted];

        private static readonly HashSet<Facet> ExpandingNotApplicable = [Facet.Present, Facet.Emitted];

        public List<ResolutionNode> Nodes(Field field, NameFrame frame)
        {
            var nodeId = frame.Prefix + field.Id;
            List<ResolutionNode> nodes = [];

            switch (field.Pattern)
            {
                case Pattern.Group group:
                {
                    foreach (var child in group.Fields) nodes.AddRange(Nodes(child, frame));
                    var extents = group.Fields.Select(c => new FacetRef(frame.NodeId(c.Id), Facet.Extent)).ToList();

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
                    var keyNeeds = Refs(choice.Key, frame);
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
                                    long key = evaluator.Eval(choice.Key, Fields(frame, inputs)).AsInt();
                                    var arm = choice.Select(key, field.Id);

                                    Chosen[nodeId] = arm;
                                    armExtents = arm.Fields
                                        .Select(c => new FacetRef(frame.NodeId(c.Id), Facet.Extent)).ToList();

                                    return new FacetResult(arm.Name,
                                        [.. arm.Fields.SelectMany(c => Nodes(c, frame))]);
                                }

                                case Facet.Value: return FacetResult.Of(ProtoValue.Of(Chosen[nodeId].Name));
                                case Facet.Extent: return FacetResult.Of(Sum(armExtents!, inputs));
                                default: return FacetResult.Of(null);
                            }
                        },
                    });
                    break;
                }

                case Pattern.Chain chain:
                {
                    List<FacetRef> valueNeeds = field.Value is null ? [] : Refs(field.Value, frame);
                    List<FacetRef>? instanceExtents = null;
                    ProtoValue? structures = null;

                    // Realising the instances is what publishes their extents back to this node, which is
                    // why the count cannot be a prerequisite of the extent: it is a result of it.
                    FacetResult Expand()
                    {
                        if (structures is not ProtoValue.List list)
                            throw new ProtoTypeException(
                                $"field '{field.Id}' chains, so its value must be a list of the structures "
                              + $"to write, got {structures?.Kind ?? "Null"}");

                        if (list.Items.Count > ProtoLimits.MaxChainedInstances)
                            throw new ProtoTypeException(
                                $"field '{field.Id}': {list.Items.Count} instances exceeds the "
                              + $"{ProtoLimits.MaxChainedInstances} ceiling");

                        List<ResolutionNode> children = [];
                        List<FacetRef> extents = [];
                        List<NameFrame> frames = [];

                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            // Each instance is a separate structure and its names are its own. `item` is
                            // where its values come from; anything it does not declare resolves outward,
                            // so it can still read the message metadata around it.
                            var instance = frame.Instance(chain.Element, $"{nodeId}[{i}].", list.Items[i]);

                            children.AddRange(Nodes(chain.Element, instance));
                            extents.Add(new FacetRef(instance.Prefix + chain.Element.Id, Facet.Extent));
                            frames.Add(instance);
                        }

                        Instances[nodeId] = frames;
                        instanceExtents = extents;
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
                            Facet.Extent => instanceExtents is null
                                ? [new FacetRef(nodeId, Facet.Realised)]
                                : [new FacetRef(nodeId, Facet.Realised), .. instanceExtents],
                            _ => [],
                        },

                        Settle = (f, inputs) => f switch
                        {
                            Facet.Value => FacetResult.Of(structures = codec.Evaluate(field, frame, evaluator, inputs)),
                            Facet.Realised => Expand(),
                            Facet.Extent => FacetResult.Of(Sum(instanceExtents!, inputs)),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }

                default:
                {
                    List<FacetRef> valueNeeds = field.Value is null ? [] : Refs(field.Value, frame);

                    // The edge a fixed width does not need. A continuation chain or a recovered octet run
                    // cannot be measured until it is known, so `Extent` declares a dependency on `Value`
                    // and the worklist does the rest. No pass, no placeholder, no widen-and-retry.
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
                            Facet.Value => FacetResult.Of(
                                settledValue = codec.Evaluate(field, frame, evaluator, inputs)),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }
            }

            return nodes;
        }

        private EvalScope Fields(NameFrame frame, IReadOnlyDictionary<FacetRef, object?> resolved)
            => frame.Scope.Child().Set("fields", codec.FieldsRecord(frame, resolved));

        private static int Sum(IReadOnlyList<FacetRef> extents, IReadOnlyDictionary<FacetRef, object?> inputs)
            => extents.Sum(r => Extent(inputs[r]));

        private static int Extent(object? settled) => settled switch
        {
            int i => i,
            long l => (int)l,
            ProtoValue v => (int)v.AsInt(),
            _ => throw new ProtoTypeException($"expected an extent, got {settled?.GetType().Name ?? "nothing"}"),
        };

        /// <summary>The facets an expression reads, resolved through the scope it was written in — so
        /// <c>fields.body</c> inside instance 2 names instance 2's region.</summary>
        private static List<FacetRef> Refs(Expr expression, NameFrame frame)
            => MessageDef.FieldReferences(expression)
                .Select(r => new FacetRef(frame.NodeId(r.Field),
                    r.Facet.Equals("extent", StringComparison.OrdinalIgnoreCase) ? Facet.Extent : Facet.Value))
                .Distinct()
                .ToList();
    }

    private ProtoValue Evaluate(Field field, NameFrame frame, Evaluator evaluator,
                                IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        if (field.Value is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' has no value expression, so it cannot be encoded. A decode-only field "
              + "must be declared as such rather than left silent.");

        var child = frame.Scope.Child().Set("fields", FieldsRecord(frame, resolved));
        return Convert(evaluator.Eval(field.Value, child), field, forward: true);
    }

    /// <summary>
    /// Settled facets as <c>fields.&lt;id&gt;.value</c> / <c>.extent</c>, keyed by the name the expression
    /// used rather than by the resolver's qualified node id — so a dependency reads back as it was written.
    /// </summary>
    private ProtoValue FieldsRecord(NameFrame frame, IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

        foreach (var (reference, value) in resolved)
        {
            if (reference.Facet is not (Facet.Value or Facet.Extent)) continue;

            var name = Unqualified(reference.NodeId, frame);

            var slot = byField.TryGetValue(name, out var existing) && existing is ProtoValue.Rec r
                ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
                : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

            slot[reference.Facet == Facet.Extent ? "extent" : "value"] = value switch
            {
                ProtoValue pv => pv,
                int i => ProtoValue.Of((long)i),
                long l => ProtoValue.Of(l),
                _ => ProtoValue.Nothing,
            };

            byField[name] = new ProtoValue.Rec(slot);
        }

        return new ProtoValue.Rec(byField);
    }

    private static string Unqualified(string nodeId, NameFrame frame)
        => frame.Prefix.Length > 0 && nodeId.StartsWith(frame.Prefix, StringComparison.Ordinal)
            ? nodeId[frame.Prefix.Length..]
            : nodeId;

    /// <summary>How many octets a value-dependent shape will occupy. Measured by producing the octets, so
    /// the extent and the emission cannot disagree.</summary>
    private int Measure(Field field, ProtoValue? value) => field.Pattern switch
    {
        Pattern.Varint varint => Pack(Settled(field, value).AsInt(), varint).Length,
        Pattern.EscapedInline escaped => PackEscaped(Settled(field, value).AsInt(), escaped).Length,
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

            case Pattern.EscapedInline escaped:
                output.AddRange(PackEscaped(value.AsInt(), escaped));
                break;
        }
    }

    /// <summary>
    /// The field's conversion slot, both ways from one declaration.
    ///
    /// <para>
    /// A transform sits <i>further from the wire</i> than a converter, so encode runs it first and decode
    /// runs it last. That ordering is the only sensible one: the converter is the octet-level step and the
    /// transform is the family-level rule composed on top of it.
    /// </para>
    /// </summary>
    private ProtoValue Convert(ProtoValue value, Field field, bool forward)
    {
        if (forward)
        {
            if (field.Through is { } transform) value = transform.Apply(value, evaluator: new Evaluator(_converters));
            return Through(value, field.Via, forward: true, field.Id);
        }

        value = Through(value, field.Via, forward: false, field.Id);

        if (field.Through is { } inverse)
        {
            if (inverse.Inverse is null)
                throw new ProtoTypeException(
                    $"field '{field.Id}': transform '{inverse.Name}' is a derivation, so it cannot be used "
                  + "on a field that must decode — a derived value is recomputed and compared, never inverted");

            value = inverse.Undo(value, evaluator: new Evaluator(_converters));
        }

        return value;
    }

    private ProtoValue Through(ProtoValue value, Conversion? via, bool forward, string fieldId)
    {
        if (via is null) return value;

        if (!_converters.TryGet(via.Name, out var converter) || converter is null)
            throw new ProtoTypeException($"field '{fieldId}': unknown converter '{via.Name}'");

        // Decode applies the inverse, encode the forward direction — one declaration, both ways, and the
        // same arguments: a converter that needs a fraction width needs it in either direction.
        if (forward) return converter.Apply(value, via.Args);

        if (converter.Inverse is null || !_converters.TryGet(converter.Inverse, out var inverse) || inverse is null)
            throw new ProtoTypeException(
                $"converter '{via.Name}' declares no inverse, so it cannot be used on a field that must decode");

        return inverse.Apply(value, via.Args);
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
