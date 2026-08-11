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

    /// <summary>
    /// Structure comes from here, not from the declaration.
    ///
    /// <para>
    /// The declaration and the graph agreed while both were walked, and two descriptions that agree today
    /// are two descriptions. Reading containment from the edges means anything the graph gains — a
    /// reference that is not containment, a constraint on an ordering — is visible to the walk instead of
    /// needing the walk to be taught about it separately.
    /// </para>
    /// </summary>
    private ProtocolGraph Graph => _message.Graph;

    /// <summary>What a node contains, in wire order.</summary>
    private IEnumerable<Field> Inside(Node node) => Graph.Children(node).OfType<Field>();

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

        Enforce(reading.MessageScope(), new Evaluator(_converters));
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

        /// <summary>What each enclosing chain has threaded this far. A stack, because chains nest and an
        /// inner one's running value must not leak out over the outer one's.</summary>
        private readonly List<ProtoValue> _carried = [ProtoValue.Nothing];

        /// <summary>Which structure of its chain each open one is. Bound for the whole structure, not
        /// only for the continuation that asked whether to start it — a rule inside a structure can say
        /// "the first one is different" on the way in as well as on the way out.</summary>
        private readonly List<int> _ordinals = [0];

        /// <summary>Opens a structure: its own field scope for expressions, its own bindings for the value
        /// it will become, and whatever its chain has threaded to it.</summary>
        public void EnterStructure(ProtoValue carried, int ordinal)
        {
            _scopes.Add(new Dictionary<string, (ProtoValue, int)>(StringComparer.Ordinal));
            _bindings.Add(new Dictionary<string, ProtoValue>(StringComparer.Ordinal));
            _carried.Add(carried);
            _ordinals.Add(ordinal);
        }

        /// <summary>Closes it, handing back everything it bound.</summary>
        public ProtoValue LeaveStructure()
        {
            _scopes.RemoveAt(_scopes.Count - 1);
            _carried.RemoveAt(_carried.Count - 1);
            _ordinals.RemoveAt(_ordinals.Count - 1);
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

        /// <summary>
        /// The message-level scope a rule about the whole message is read in.
        ///
        /// <para>
        /// The same shape the encode side builds, which it had to be told: the message rules were handed
        /// raw captures here and settled facet records there, through one signature that accepted both.
        /// So a rule reading <c>.extent</c> saw nothing on the way in and a rule naming a bit run by
        /// itself saw nothing on the way out, and neither said so.
        /// </para>
        /// </summary>
        public EvalScope MessageScope()
        {
            Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

            foreach (var (id, facet) in _scopes[0])
                byField[id] = EvalScope.Record(("value", facet.Value),
                                               ("extent", ProtoValue.Of((long)facet.Extent)));

            // Bit runs, which bind alongside fields and have no extent of their own. They arrive here as
            // captures because that is where a run is recorded; what matters is that the name answers,
            // and that it answers the same on the way out.
            foreach (var (name, value) in _bindings[0])
                if (!byField.ContainsKey(name)) byField[name] = EvalScope.Record(("value", value));

            return new EvalScope().Set("fields", new ProtoValue.Rec(byField));
        }

        public ProtoValue Eval(Expr expression, params (string Root, ProtoValue Value)[] extra)
        {
            Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

            // Outermost first, so an instance's own names shadow the ones around it.
            foreach (var level in _scopes)
                foreach (var (id, facet) in level)
                    byField[id] = EvalScope.Record(("value", facet.Value),
                                                   ("extent", ProtoValue.Of((long)facet.Extent)));

            var child = scope.Child().Set("fields", new ProtoValue.Rec(byField));

            // Available to every expression, not only a chain's. "Is there anything left, and what does
            // it start with" is how a great many optional trailing sections are decided, and neither
            // question is answerable from the fields alone.
            child.Set("room", ProtoValue.Of((long)Room));
            child.Set("peek", Offset < Limit ? ProtoValue.Of((long)Bytes[Offset]) : ProtoValue.Nothing);
            child.Set("carried", _carried[^1]);
            child.Set("ordinal", ProtoValue.Of((long)_ordinals[^1]));

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

                foreach (var child in Inside(field))
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
                var arm = _message.Choose(field, key);
                int start = r.Offset;

                foreach (var child in Inside(arm))
                    Read(child, r, ChildPath(path, child, exposed), exposed);

                // The arm NAME is the value. A later step then branches on which shape arrived, rather
                // than re-deriving it from the raw discriminator and hoping the two rules stay in step.
                return Bind(field, r, ProtoValue.Of(arm.Name), r.Offset - start, exposed);
            }

            case Pattern.Chain chain:
            {
                int start = r.Offset;
                List<ProtoValue> instances = [];

                // The value threaded along, where there is one. A structure that records how far its
                // identifier has moved since the last cannot be named without this.
                var carried = chain.Seed is null ? ProtoValue.Nothing : r.Eval(chain.Seed);

                while (Continues(field, chain, r, instances.Count, carried))
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
                    r.EnterStructure(carried, instances.Count);
                    var element = Read(chain.Element, r, $"{path}[{instances.Count}]", exposed: false);

                    // Rules written about this structure, checked here — once per structure, because the
                    // rule points at the element and every instance is that element.
                    Enforce(r, chain.Element);

                    // Computed inside the structure's own scope, before it closes, so it can be worked out
                    // from what that structure actually said.
                    var next = chain.Carry is null ? ProtoValue.Nothing : r.Eval(chain.Carry);
                    var bound = r.LeaveStructure();
                    carried = next;

                    // A structure built from a single wire shape IS that value; anything composite is
                    // everything it bound. Wrapping a lone scalar in a one-member record would make the
                    // simplest case the awkward one to read.
                    var instance = chain.Element.Pattern.Nested.Count == 0 ? element : bound;

                    if (r.Offset == before)
                        throw new ProtoTypeException(
                            $"field '{field.Id}': an instance consumed no octets, so the continuation "
                          + "condition would never stop being true");

                    // Distinctness is over the whole run, not the pair — a duplicate can be any distance
                    // back, which is exactly why an arrangement rule cannot say this.
                    foreach (var rule in _message.RulesOn(field).OfType<Rule.Distinct>())
                    {
                        var mine = Member(instance, rule.Of.CaptureName);

                        for (int seen = 0; seen < instances.Count; seen++)
                            if (Equals(Member(instances[seen], rule.Of.CaptureName), mine))
                                throw new ProtoTypeException(
                                    $"structure {instances.Count} of '{field.Id}' carries "
                                  + $"{rule.Of.Name} {mine}, which structure {seen} already carries. "
                                  + rule.Because);
                    }

                    // How this structure sits against the one before it. Not a value rule and not a
                    // containment one: it is about the arrangement, so it lives on the pair.
                    if (instances.Count > 0)
                        foreach (var rule in _message.RulesOn(field).OfType<Rule.Arrangement>())
                            if (!Holds(rule.Must, Structured(instance, instances[^1]), new Evaluator(_converters)))
                                throw new ProtoTypeException(
                                    $"structure {instances.Count} of '{field.Id}' may not follow the one "
                                  + $"before it: {rule.Must.Render()} does not hold. {rule.Because}");

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
    private static bool Continues(Field field, Pattern.Chain chain, Reading r, int ordinal, ProtoValue carried)
    {
        var answer = r.Eval(chain.Continues,
            ("ordinal", ProtoValue.Of((long)ordinal)),
            ("carried", carried));

        if (answer is not ProtoValue.Bool)
            throw new ProtoTypeException(
                $"field '{field.Id}': the continuation must answer whether another structure follows, "
              + $"which is a Bool — got {answer.Kind}");

        return answer.AsBool();
    }

    private static string ChildPath(string path, Field child, bool exposed)
        => exposed ? child.CaptureName : $"{path}.{child.CaptureName}";

    private ProtoValue Bind(Field field, Reading r, ProtoValue value, int extent, bool exposed)
    {
        Confine(field, value);
        r.Note(field.Id, value, extent);
        r.Capture(field.CaptureName, value);

        // Rules about this field, checked the moment it is bound rather than when its structure closes.
        // An illegal value is frequently one that derails the rest of the read — a reserved nibble sends
        // the walk off by two octets — and a diagnostic about where it ended up is no use to anyone.
        Enforce(r, field);
        return value;
    }

    /// <summary>
    /// The value rules on one field, checked where that field is.
    ///
    /// <para>
    /// Found by reference, so a rule on a segment inside a repeated structure applies to every structure —
    /// which is the point of the rule pointing at a node rather than naming one. While rules named their
    /// subject the name had to be resolved somewhere, the somewhere was the message, and everything inside
    /// a chain was silently beyond reach.
    /// </para>
    /// </summary>
    /// <summary>The same check on the way out, so the engine cannot emit what it would refuse to read.</summary>
    private ProtoValue Confined(Field field, ProtoValue value)
    {
        Confine(field, value);
        return value;
    }

    /// <summary>
    /// A value checked against the set its field draws from.
    ///
    /// <para>
    /// What happens to an unlisted value is the set's answer, not the engine's: a closed set is refused,
    /// and an open one is accepted because a registry that keeps growing produces numbers this document
    /// has not heard of, and that is a newer peer rather than a corrupt packet. Collapsing the two was
    /// what made "the legal values are…" unable to say either.
    /// </para>
    /// </summary>
    private void Belongs(Field field, ProtoValue value)
    {
        if (_message.DrawnFrom(field) is not { } edge) return;

        var set = (ValueSet)edge.To;

        var subject = edge.Run is { } run && value is ProtoValue.Rec rec
            ? rec.Members.GetValueOrDefault(run, ProtoValue.Nothing)
            : value;

        if (set.Bounding == Bounding.Open || set.Admits(subject)) return;

        throw new ProtoTypeException(
            $"'{edge.Run ?? field.Name}' is {subject}, which is not in '{set.Id}' — a closed set, so there "
          + $"is no value outside it to be newer than this document. Members: "
          + $"{string.Join(", ", set.Members)}. {set.Because}");
    }

    private void Confine(Field field, ProtoValue value)
    {
        Belongs(field, value);

        foreach (var rule in _message.RulesOn(field).OfType<Rule.Domain>())
        {
            var subject = rule.Run is { } run && value is ProtoValue.Rec rec
                ? rec.Members.GetValueOrDefault(run, ProtoValue.Nothing)
                : value;

            if (rule.Admits(subject)) continue;

            throw new ProtoTypeException(
                $"'{rule.What}' is {subject}, which is not a value it may take — the legal ones are "
              + $"{string.Join(", ", rule.Allowed)}. {rule.Because}");
        }
    }

    /// <summary>
    /// How far it is to the next separator. The separator is left where it is: it stays a field of its
    /// own, which is what leaves something to write it back out with and somewhere to fix its value.
    /// </summary>
    private static int Upto(Field field, byte[] separator, int ceiling, Reading r)
    {
        int last = Math.Min(r.Limit, r.Offset + ceiling) - separator.Length;

        for (int at = r.Offset; at <= last; at++)
            if (r.Bytes.AsSpan(at, separator.Length).SequenceEqual(separator))
                return at - r.Offset;

        throw new ProtoTypeException(
            $"field '{field.Id}': no {Hex(separator)} within {ceiling} octet(s) of offset {r.Offset}, and "
          + "nothing else says where this span ends");
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
            Pattern.Opaque { Until: { } separator } opaque => Upto(field, separator, opaque.Ceiling, r),
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

        var after = Encoder.Follows.Start;

        foreach (var field in _message.Fields)
        {
            foreach (var node in encoder.Nodes(field, frame, after)) resolver.Add(node);
            after = Encoder.Follows.After(frame.Of(field));
        }

        var settled = resolver.Resolve();

        // Checked before a single octet is produced. A rule that only ran on the way in would let the
        // engine emit a message it would itself refuse to read.
        var evaluator = new Evaluator(_converters);
        Enforce(new EvalScope().Set("fields", new ProtoValue.Rec(NamedValues(settled))), evaluator);

        // And once per structure, for the rules written about one. On the way in these run as each field
        // binds, because an illegal value derails the read and a diagnostic about where it ended up is no
        // use; nothing derails out here, so they run on the finished picture instead.
        foreach (var (_, frames) in encoder.Instances)
            for (int i = 0; i < frames.Count; i++)
                EnforceStructure(frames[i], i, settled, evaluator);

        // Distinctness is about the run rather than any one structure, so it cannot ride along with the
        // per-structure pass. It has to be here at all for the reason every rule runs in both directions:
        // a check that only read would let the engine write what it would then refuse.
        foreach (var (chain, frames) in encoder.Instances) EnforceDistinct(chain, frames, settled);

        // Position is a linearisation, not a fixpoint: one ordered sweep once extents have settled, now
        // through the realised shape rather than a flat list.
        var output = new List<byte>();
        foreach (var field in _message.Fields) Emit(field, frame, encoder, settled, output);
        return [.. output];
    }

    /// <summary>
    /// Message-scope facets by the name a rule uses for them.
    ///
    /// <para>
    /// Both facets, and bit runs <b>only</b> under the group they belong to. This once flattened a run
    /// into the field namespace and said it was "matching how decode binds them" — it was not. Decode
    /// notes the group and reaches a run as <c>fields.&lt;group&gt;.value.&lt;run&gt;</c>, so a rule
    /// naming a run directly held on the way out and failed on the way in. One address, both directions.
    /// </para>
    /// </summary>
    private static Dictionary<string, ProtoValue> NamedValues(IReadOnlyDictionary<FacetRef, object?> settled)
    {
        Dictionary<string, List<(string, ProtoValue)>> facets = new(StringComparer.Ordinal);

        foreach (var (reference, value) in settled)
        {
            // An appearance inside a structure belongs to that structure, not to the message. A question
            // the occurrence answers directly, where a string id had to be inspected for a bracket.
            if (reference.Facet is not (Facet.Value or Facet.Extent)
                || reference.Node is not Occurrence { Within: null } occurrence)
                continue;

            var settledValue = value switch
            {
                ProtoValue pv => pv,
                int i => ProtoValue.Of((long)i),
                _ => ProtoValue.Nothing,
            };

            if (settledValue.IsNull && reference.Facet == Facet.Extent) continue;

            if (!facets.TryGetValue(occurrence.Declared.Name, out var slots))
                facets[occurrence.Declared.Name] = slots = [];

            slots.Add((reference.Facet == Facet.Extent ? "extent" : "value", settledValue));
        }

        Dictionary<string, ProtoValue> named = new(StringComparer.Ordinal);
        foreach (var (name, slots) in facets) named[name] = EvalScope.Record([.. slots]);

        // And the runs, alongside their group as decode binds them.
        foreach (var (_, slots) in facets)
            if (slots.FirstOrDefault(s => s.Item1 == "value").Item2 is ProtoValue.Rec runs)
                foreach (var (run, sliced) in runs.Members)
                    named.TryAdd(run, EvalScope.Record(("value", sliced)));

        return named;
    }

    /// <summary>
    /// Applies the message's rules. Each kind reports in its own words, because "this is illegal" without
    /// saying which sort of illegal is the diagnostic equivalent of a shrug.
    /// </summary>
    private void Enforce(EvalScope scope, Evaluator evaluator)
    {
        if (_message.Rules.Count == 0) return;

        foreach (var rule in _message.Rules)
            switch (rule)
            {
                // Value rules are not here: they are checked at the field, which is the only place that
                // works for a field inside a repeated structure and the only place that can name it.
                case Rule.Requires requires:
                {
                    if (!Holds(requires.When, scope, evaluator) || Holds(requires.Then, scope, evaluator)) break;

                    throw new ProtoTypeException(
                        $"{requires.When.Render()} holds, which obliges {requires.Then.Render()} — and it "
                      + $"does not. {requires.Because}");
                }

                case Rule.Excludes excludes:
                {
                    if (!Holds(excludes.One, scope, evaluator) || !Holds(excludes.Other, scope, evaluator)) break;

                    throw new ProtoTypeException(
                        $"{excludes.One.Render()} and {excludes.Other.Render()} both hold, and they may "
                      + $"never combine. {excludes.Because}");
                }
            }
    }

    /// <summary>
    /// The rules written about one structure, or about anything inside it, applied to the structure the
    /// encoder just built.
    /// </summary>
    private void EnforceStructure(NameFrame frame, int ordinal,
                                  IReadOnlyDictionary<FacetRef, object?> settled, Evaluator evaluator)
    {
        // One builder. This used to assemble its own `fields` — both facets, but also flattening bit runs
        // into the namespace, which decode does not do — and to bind `ordinal` but not `carried`. Every
        // difference between a hand-rolled scope and the real one is a name that answers here and not
        // there, which is how three defects in a row happened.
        var scope = ScopeFor(frame, settled);
        scope.Set("ordinal", ProtoValue.Of((long)ordinal));

        foreach (var subject in frame.Here.Keys.Cast<Node>().Append(frame.Instance?.Declared).OfType<Node>())
            foreach (var rule in _message.RulesOn(subject))
                switch (rule)
                {
                    case Rule.Invariant invariant when !Holds(invariant.Must, scope, evaluator):
                        throw new ProtoTypeException(
                            $"in structure {ordinal}: {invariant.Must.Render()} does not hold. "
                          + invariant.Because);

                    case Rule.Requires requires when Holds(requires.When, scope, evaluator)
                                                  && !Holds(requires.Then, scope, evaluator):
                        throw new ProtoTypeException(
                            $"in structure {ordinal}: {requires.When.Render()} holds, which obliges "
                          + $"{requires.Then.Render()} — and it does not. {requires.Because}");

                    case Rule.Excludes excludes when Holds(excludes.One, scope, evaluator)
                                                  && Holds(excludes.Other, scope, evaluator):
                        throw new ProtoTypeException(
                            $"in structure {ordinal}: {excludes.One.Render()} and "
                          + $"{excludes.Other.Render()} both hold, and they may never combine. "
                          + excludes.Because);
                }
    }

    private static ProtoValue Member(ProtoValue structure, string name)
        => structure is ProtoValue.Rec rec ? rec.Members.GetValueOrDefault(name, ProtoValue.Nothing) : structure;

    /// <summary>No two structures of one chain carrying the same value, checked on the way out.</summary>
    private void EnforceDistinct(Occurrence chain, List<NameFrame> frames,
                                 IReadOnlyDictionary<FacetRef, object?> settled)
    {
        foreach (var rule in _message.RulesOn(chain.Declared).OfType<Rule.Distinct>())
        {
            List<ProtoValue> seen = [];

            for (int i = 0; i < frames.Count; i++)
            {
                if (!settled.TryGetValue(new FacetRef(frames[i].Of(rule.Of), Facet.Value), out var settledValue)
                    || settledValue is not ProtoValue value)
                    continue;

                int first = seen.FindIndex(v => Equals(v, value));

                if (first >= 0)
                    throw new ProtoTypeException(
                        $"structure {i} of '{chain.Declared.Name}' carries {rule.Of.Name} {value}, which "
                      + $"structure {first} already carries. {rule.Because}");

                seen.Add(value);
            }
        }
    }

    /// <summary>Two structures side by side, for a rule about how they sit together.</summary>
    private static EvalScope Structured(ProtoValue current, ProtoValue previous)
        => new EvalScope().Set("item", current).Set("previous", previous);

    /// <summary>
    /// The rules written about one scope, checked while that scope is open.
    ///
    /// <para>
    /// A rule about a repeated structure runs once per structure, in that structure's own scope, which is
    /// the whole reason its subject is a reference. It reads the same names the structure's own fields do.
    /// </para>
    /// </summary>
    private void Enforce(Reading r, Node scope)
    {
        // Through the edges, in the order they carry. The first failure is the one anyone reads, so which
        // rule is reached first is a decision the document gets to make rather than an accident of how a
        // list happened to be sorted.
        foreach (var rule in _message.RulesOn(scope))
        {
            switch (rule)
            {
                case Rule.Invariant invariant when !r.Eval(invariant.Must).AsBool():
                    throw new ProtoTypeException(
                        $"{invariant.Must.Render()} does not hold. {invariant.Because}");

                case Rule.Requires requires when r.Eval(requires.When).AsBool() && !r.Eval(requires.Then).AsBool():
                    throw new ProtoTypeException(
                        $"{requires.When.Render()} holds, which obliges {requires.Then.Render()} — and it "
                      + $"does not. {requires.Because}");

                case Rule.Excludes excludes when r.Eval(excludes.One).AsBool() && r.Eval(excludes.Other).AsBool():
                    throw new ProtoTypeException(
                        $"{excludes.One.Render()} and {excludes.Other.Render()} both hold, and they may "
                      + $"never combine. {excludes.Because}");
            }
        }
    }

    private static bool Holds(Expr condition, EvalScope scope, Evaluator evaluator)
    {
        var answer = evaluator.Eval(condition, scope);

        return answer is ProtoValue.Bool b ? b.Value
             : throw new ProtoTypeException(
                 $"a rule's condition `{condition.Render()}` must answer true or false, got {answer.Kind}");
    }

    /// <summary>
    /// One field scope on the encode side: which ids belong to it, what to prefix them with in the
    /// resolver's flat namespace, and the evaluation scope its expressions see.
    /// </summary>
    /// <summary>
    /// One appearance of a declared node.
    ///
    /// <para>
    /// A chain declares one structure and realises many, so "the length prefix of the second subscription"
    /// needs an identity the declaration does not have. It used to be a string built by concatenation —
    /// <c>subscriptions[1].filterLength</c> — which is a dictionary key wearing a node's clothes: two
    /// occurrences were the same thing exactly when their names happened to match. This is an object, so
    /// they are the same thing exactly when they are.
    /// </para>
    /// </summary>
    private sealed class Occurrence(Node declared, Occurrence? within, string? tag = null)
    {
        public Node Declared { get; } = declared;

        /// <summary>The structure this appearance belongs to, or null at message level.</summary>
        public Occurrence? Within { get; } = within;

        public override string ToString()
        {
            var here = Declared.Name + tag;
            return Within is null ? here : $"{Within}.{here}";
        }
    }

    private sealed record NameFrame(IReadOnlyDictionary<Field, Occurrence> Here, EvalScope Scope, NameFrame? Outer)
    {
        /// <summary>
        /// The node holding what this structure's chain threaded to it, if any.
        ///
        /// <para>
        /// <c>carried</c> is a root rather than a field reference, so nothing in an expression's text
        /// makes it a dependency. It has to be one: instance 3's value is computed from instance 2's
        /// fields, which is a genuine ordering the worklist can hold perfectly well once it is told.
        /// </para>
        /// </summary>
        public Occurrence? Carried { get; init; }

        /// <summary>This frame's own occurrence, if it is one structure of a chain.</summary>
        public Occurrence? Instance { get; init; }

        public static NameFrame Root(IReadOnlyList<Field> fields, EvalScope scope)
            => new(Occurrences(MessageDef.ScopeFields(fields), null), scope, null);

        /// <summary>An instance's scope: its own occurrences, everything else still reachable outward, and
        /// <c>item</c> bound to the structure being written.</summary>
        public NameFrame Structure(Field element, Occurrence instance, ProtoValue item, Occurrence? carried)
            => new(Occurrences(MessageDef.ScopeFields([element]), instance),
                   Scope.Child().Set("item", item), this)
               { Carried = carried, Instance = instance };

        private static Dictionary<Field, Occurrence> Occurrences(IEnumerable<Field> fields, Occurrence? within)
            => fields.Distinct().ToDictionary(f => f, f => new Occurrence(f, within));

        /// <summary>Which appearance of a field this scope means. Innermost outward, so a structure's own
        /// length prefix is its own.</summary>
        public Occurrence Of(Field field)
            => Here.TryGetValue(field, out var here) ? here
             : Outer?.Of(field) ?? new Occurrence(field, null);
    }

    private void Emit(Field field, NameFrame frame, Encoder encoder,
                      IReadOnlyDictionary<FacetRef, object?> settled, List<byte> output)
    {
        var nodeId = frame.Of(field);

        switch (field.Pattern)
        {
            case Pattern.Group:
                foreach (var child in Inside(field)) Emit(child, frame, encoder, settled, output);
                break;

            case Pattern.Choice:
                foreach (var child in Inside(encoder.Chosen[nodeId])) Emit(child, frame, encoder, settled, output);
                break;

            case Pattern.Chain chain:
                foreach (var instance in encoder.Instances[nodeId])
                    Emit(chain.Element, instance, encoder, settled, output);
                break;

            default:
                Write(output, field,
                      OnWire(field, settled[new FacetRef(nodeId, Facet.Value)] as ProtoValue ?? ProtoValue.Nothing));
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
        public readonly Dictionary<Occurrence, Arm> Chosen = [];

        /// <summary>Chain node id → one frame per instance, in order.</summary>
        public readonly Dictionary<Occurrence, List<NameFrame>> Instances = [];

        private static readonly HashSet<Facet> LeafNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Emitted];

        private static readonly HashSet<Facet> RegionNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Value, Facet.Emitted];

        private static readonly HashSet<Facet> ExpandingNotApplicable = [Facet.Present, Facet.Emitted];

        /// <summary>A threaded value occupies no octets; it exists only so one structure can depend on
        /// the one before it.</summary>
        private static readonly HashSet<Facet> ThreadNotApplicable =
            [Facet.Realised, Facet.Present, Facet.Extent, Facet.Emitted];

        /// <summary>
        /// Where a node starts, expressed as the one node it follows. Two references rather than a running
        /// total: a node begins where the previous sibling ended, and the first child of a container begins
        /// where the container does. That makes position O(1) to declare and keeps the chain of
        /// dependencies out of any node's value.
        /// </summary>
        internal readonly record struct Follows(FacetRef? Position, FacetRef? Extent)
        {
            /// <summary>The start of the message.</summary>
            public static Follows Start => new(null, null);

            /// <summary>The first thing inside a container starts where the container starts.</summary>
            public static Follows Inside(object container) => new(new FacetRef(container, Facet.Position), null);

            /// <summary>Whatever comes after this node.</summary>
            public static Follows After(object node)
                => new(new FacetRef(node, Facet.Position), new FacetRef(node, Facet.Extent));

            public IReadOnlyList<FacetRef> Needs
                => Position is null ? [] : Extent is null ? [Position.Value] : [Position.Value, Extent.Value];

            public int At(IReadOnlyDictionary<FacetRef, object?> inputs)
                => (Position is null ? 0 : Offset(inputs[Position.Value]))
                 + (Extent is null ? 0 : Offset(inputs[Extent.Value]));

            private static int Offset(object? settled) => settled switch
            {
                int i => i,
                long l => (int)l,
                ProtoValue.Int v => (int)v.Value,
                _ => 0,
            };
        }

        /// <summary>An arm's fields, the first starting where the choice does.</summary>
        private List<ResolutionNode> ArmNodes(Arm arm, NameFrame frame, object choice)
        {
            List<ResolutionNode> nodes = [];
            var within = Follows.Inside(choice);

            foreach (var child in codec.Inside(arm))
            {
                nodes.AddRange(Nodes(child, frame, within));
                within = Follows.After(frame.Of(child));
            }

            return nodes;
        }

        public List<ResolutionNode> Nodes(Field field, NameFrame frame, Follows after)
        {
            var nodeId = frame.Of(field);
            List<ResolutionNode> nodes = [];

            switch (field.Pattern)
            {
                case Pattern.Group group:
                {
                    var within = Follows.Inside(nodeId);

                    foreach (var child in codec.Inside(field))
                    {
                        nodes.AddRange(Nodes(child, frame, within));
                        within = Follows.After(frame.Of(child));
                    }

                    var extents = codec.Inside(field).Select(c => new FacetRef(frame.Of(c), Facet.Extent)).ToList();

                    nodes.Add(new ResolutionNode
                    {
                        Id = nodeId,
                        NotApplicable = RegionNotApplicable,

                        DependenciesFor = f => f switch
                        {
                            Facet.Extent => extents,
                            Facet.Position => after.Needs,
                            _ => [],
                        },

                        Settle = (f, inputs) => FacetResult.Of(f switch
                        {
                            Facet.Extent => Sum(extents, inputs),
                            Facet.Position => after.At(inputs),
                            _ => (object?)null,
                        }),
                    });
                    break;
                }

                case Pattern.Choice choice:
                {
                    // The writer's reading of the discriminator, where the two differ. `room` and `peek`
                    // answer only for a reader, so a section that is present when the region still has
                    // octets in it has to be decided some other way out here — by whether the caller
                    // supplied one.
                    var deciding = choice.Deciding(encoding: true);
                    var keyNeeds = Refs(field, choice.Selects is null ? Roles.Discriminator : Roles.Selection,
                                        deciding, frame);
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
                            Facet.Position => after.Needs,
                            _ => [],
                        },

                        Settle = (f, inputs) =>
                        {
                            switch (f)
                            {
                                case Facet.Realised:
                                {
                                    long key = evaluator.Eval(deciding, Fields(frame, inputs)).AsInt();
                                    var arm = codec._message.Choose(field, key);

                                    Chosen[nodeId] = arm;
                                    armExtents = codec.Inside(arm)
                                        .Select(c => new FacetRef(frame.Of(c), Facet.Extent)).ToList();

                                    return new FacetResult(arm.Name,
                                        [.. ArmNodes(arm, frame, nodeId)]);
                                }

                                case Facet.Value: return FacetResult.Of(ProtoValue.Of(Chosen[nodeId].Name));
                                case Facet.Extent: return FacetResult.Of(Sum(armExtents!, inputs));
                                case Facet.Position: return FacetResult.Of(after.At(inputs));
                                default: return FacetResult.Of(null);
                            }
                        },
                    });
                    break;
                }

                case Pattern.Chain chain:
                {
                    List<FacetRef> valueNeeds = field.Value is null ? [] : Refs(field, Roles.Value, field.Value, frame);
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

                        NameFrame? previous = null;
                        Occurrence? previousCarried = null;

                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            Occurrence? carriedId = null;

                            if (chain.Threads)
                            {
                                // A node of its own, so the ordering between structures is something the
                                // worklist schedules rather than something a loop assumes. The first comes
                                // from the seed in the scope around the chain; every later one from the
                                // previous structure's scope, once that structure has settled.
                                carriedId = new Occurrence(field, nodeId, $"[{i}]~carried");

                                children.Add(previous is null
                                    ? Threaded(carriedId, field, Roles.Seed, frame, chain.Seed!, [])
                                    : Threaded(carriedId, field, Roles.Carry, previous, chain.Carry!,
                                               [new FacetRef(previousCarried!, Facet.Value)]));
                            }

                            // Each instance is a separate structure and its names are its own. `item` is
                            // where its values come from; anything it does not declare resolves outward,
                            // so it can still read the message metadata around it.
                            var instance = frame.Structure(chain.Element,
                                new Occurrence(chain.Element, nodeId, $"[{i}]"), list.Items[i], carriedId);

                            children.AddRange(Nodes(chain.Element, instance,
                                i == 0 ? Follows.Inside(nodeId)
                                       : Follows.After(frames[i - 1].Of(chain.Element))));
                            extents.Add(new FacetRef(instance.Of(chain.Element), Facet.Extent));
                            frames.Add(instance);

                            previous = instance;
                            previousCarried = carriedId;
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
                            Facet.Position => after.Needs,
                            _ => [],
                        },

                        Settle = (f, inputs) => f switch
                        {
                            Facet.Value => FacetResult.Of(structures = codec.Evaluate(field, frame, evaluator, inputs)),
                            Facet.Realised => Expand(),
                            Facet.Extent => FacetResult.Of(Sum(instanceExtents!, inputs)),
                            Facet.Position => FacetResult.Of(after.At(inputs)),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }

                default:
                {
                    List<FacetRef> valueNeeds = field.Value is null ? [] : Refs(field, Roles.Value, field.Value, frame);

                    // A reference waits for its target to land. Not for the whole layout — only for the
                    // extents between the start and that node, which usually settle long before the rest
                    // of the message does.
                    var target = field.Points is null || codec._message.Named(field.Points.Target) is not Field named
                        ? null
                        : (object)frame.Of(named);
                    if (target is not null) valueNeeds = [.. valueNeeds, new FacetRef(target, Facet.Position)];

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
                            Facet.Position => after.Needs,
                            _ => [],
                        },

                        Settle = (f, inputs) => f switch
                        {
                            Facet.Extent => FacetResult.Of(fixedWidth ?? codec.Measure(field, settledValue)),
                            Facet.Position => FacetResult.Of(after.At(inputs)),
                            Facet.Value => FacetResult.Of(settledValue = codec.Confined(field,
                                target is null
                                    ? codec.Evaluate(field, frame, evaluator, inputs)
                                    : codec.Rendered(field, frame, evaluator, inputs, target))),
                            _ => FacetResult.Of(null),
                        },
                    });
                    break;
                }
            }

            return nodes;
        }

        /// <summary>A zero-width node holding what one structure threads to the next.</summary>
        private ResolutionNode Threaded(Occurrence id, Field owner, string role, NameFrame within,
                                        Expr expression, IReadOnlyList<FacetRef> also)
        {
            var needs = Refs(owner, role, expression, within).Concat(also).Distinct().ToList();

            return new ResolutionNode
            {
                Id = id,
                NotApplicable = ThreadNotApplicable,
                DependenciesFor = f => f == Facet.Value ? needs : [],
                Settle = (f, inputs) => FacetResult.Of(
                    f == Facet.Value ? evaluator.Eval(expression, Fields(within, inputs)) : null),
            };
        }

        private EvalScope Fields(NameFrame frame, IReadOnlyDictionary<FacetRef, object?> resolved)
            => codec.ScopeFor(frame, resolved);

        private static int Sum(IReadOnlyList<FacetRef> extents, IReadOnlyDictionary<FacetRef, object?> inputs)
            => extents.Sum(r => Extent(inputs[r]));

        private static int Extent(object? settled) => settled switch
        {
            int i => i,
            long l => (int)l,
            ProtoValue v => (int)v.AsInt(),
            _ => throw new ProtoTypeException($"expected an extent, got {settled?.GetType().Name ?? "nothing"}"),
        };

        /// <summary>
        /// What one of a field's expressions depends on, taken from the graph.
        ///
        /// <para>
        /// The reads were resolved once when the graph was built; this only says which appearance of each
        /// target is meant. Re-deriving them here by scanning expression text was how a reference could
        /// survive until run time — the text was available everywhere and meant something different
        /// depending on where you read it from.
        /// </para>
        /// </summary>
        private List<FacetRef> Refs(Field owner, string role, Expr expression, NameFrame frame)
        {
            var needs = codec.Graph.From<Reads>(owner)
                .Where(r => r.Role == role)
                .Select(r => new FacetRef(frame.Of((Field)r.To),
                    r.Facet.Equals("extent", StringComparison.OrdinalIgnoreCase) ? Facet.Extent : Facet.Value))
                .ToList();

            // `carried` is a root, so nothing in the expression's text makes it a dependency. It is one.
            if (frame.Carried is { } carried && expression.FreeRootNames().Contains("carried"))
                needs.Add(new FacetRef(carried, Facet.Value));

            return [.. needs.Distinct()];
        }
    }

    private ProtoValue Evaluate(Field field, NameFrame frame, Evaluator evaluator,
                                IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        if (field.Value is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' has no value expression, so it cannot be encoded. A decode-only field "
              + "must be declared as such rather than left silent.");

        // NOT converted here. What settles is the value the DOCUMENT means; the transform to octets
        // happens where octets are wanted, which is measurement and emission. Converting at this point
        // made `fields.<id>.value` mean the document's value on the way in and the wire's on the way out
        // for any field with a `Via` — so an expression reading a converted field got a number when
        // decoding and a byte run when encoding, and every comparison against it silently went false.
        // Found by a length escape whose marker a sibling had to test for.
        return evaluator.Eval(field.Value, ScopeFor(frame, resolved));
    }

    /// <summary>
    /// A reference, written down. <c>position</c> is bound here and at no other site — an offset is how a
    /// relationship gets spelled, not a fact the pointed-at node carries.
    /// </summary>
    private ProtoValue Rendered(Field field, NameFrame frame, Evaluator evaluator,
                                IReadOnlyDictionary<FacetRef, object?> resolved, object target)
    {
        var at = resolved.TryGetValue(new FacetRef(target, Facet.Position), out var settled) && settled is int i
            ? i
            : throw new ProtoTypeException(
                  $"field '{field.Id}' points at '{field.Points!.Target.Name}', which has not been placed");

        var scope = ScopeFor(frame, resolved);
        scope.Set(Vocabulary.Position, ProtoValue.Of((long)at));

        return Convert(evaluator.Eval(field.Points!.Render, scope), field, forward: true);
    }

    /// <summary>
    /// The scope an expression in this frame sees. One builder for every caller: an earlier version had
    /// the choice keys reading a threaded value that ordinary field expressions could not, which is the
    /// sort of difference nothing notices until one construct works and its neighbour does not.
    /// </summary>
    private EvalScope ScopeFor(NameFrame frame, IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        var scope = frame.Scope.Child().Set("fields", FieldsRecord(frame, resolved));

        // Bound from the node rather than captured when the frame was built: it does not exist yet at
        // that point, which is the whole reason it had to become a node.
        if (frame.Carried is { } id
            && resolved.TryGetValue(new FacetRef(id, Facet.Value), out var carried)
            && carried is ProtoValue value)
            scope.Set("carried", value);

        return scope;
    }

    /// <summary>
    /// Settled facets as <c>fields.&lt;id&gt;.value</c> / <c>.extent</c>, keyed by the name the expression
    /// used rather than by the resolver's qualified node id — so a dependency reads back as it was written.
    ///
    /// <para>
    /// Resolved <b>through the frame</b>, outermost first so an instance's own names shadow the ones
    /// around it. This used to sweep every settled facet and key it by declared name, which inside a
    /// chain meant whichever instance happened to settle last: instance 2's expression could read
    /// instance 1's length. Nothing caught it because the dependency edges were computed per occurrence
    /// and were right — only the lookup that read them back was flat. It surfaced when the structure-rule
    /// scope, which had its own frame-correct copy, was folded into this one.
    /// </para>
    /// </summary>
    private ProtoValue FieldsRecord(NameFrame frame, IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

        List<NameFrame> chain = [];
        for (var at = frame; at is not null; at = at.Outer) chain.Add(at);
        chain.Reverse();

        foreach (var level in chain)
            foreach (var (field, occurrence) in level.Here)
            {
                List<(string, ProtoValue)> slots = [];

                foreach (var facet in (Facet[])[Facet.Value, Facet.Extent])
                    if (resolved.TryGetValue(new FacetRef(occurrence, facet), out var settled))
                        slots.Add((facet == Facet.Extent ? "extent" : "value", settled switch
                        {
                            ProtoValue pv => pv,
                            int i => ProtoValue.Of((long)i),
                            long l => ProtoValue.Of(l),
                            _ => ProtoValue.Nothing,
                        }));

                if (slots.Count == 0) continue;

                byField[field.CaptureName] = EvalScope.Record([.. slots]);

                // Bit runs bind alongside their group, in both directions — which is why two runs sharing
                // a name is a document error rather than something that silently shadows.
                if (field.Pattern is Pattern.Bits
                    && slots.FirstOrDefault(s => s.Item1 == "value").Item2 is ProtoValue.Rec runs)
                    foreach (var (run, sliced) in runs.Members)
                        byField.TryAdd(run, EvalScope.Record(("value", sliced)));
            }

        return new ProtoValue.Rec(byField);
    }

    /// <summary>How many octets a value-dependent shape will occupy. Measured by producing the octets, so
    /// the extent and the emission cannot disagree — which is also why the transform runs here rather than
    /// where the value settled: what is being asked is how wide the <i>octets</i> are.</summary>
    private int Measure(Field field, ProtoValue? value) => field.Pattern switch
    {
        Pattern.Varint varint => Pack(OnWire(field, value).AsInt(), varint).Length,
        Pattern.EscapedInline escaped => PackEscaped(OnWire(field, value).AsInt(), escaped).Length,
        Pattern.Opaque { Width: null } => OnWire(field, value).AsBytes().Length,
        _ => throw new ProtoTypeException($"field '{field.Id}' has no way to determine its width"),
    };

    /// <summary>The octets a settled value becomes. One place, so measurement and emission cannot apply
    /// different transforms to the same value.</summary>
    private ProtoValue OnWire(Field field, ProtoValue? value)
        => Convert(Settled(field, value), field, forward: true);

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

                // A span that ends at a separator may not contain one, or reading it back would stop
                // somewhere else and every field after it would be a different field.
                if (opaque.Until is { } separator && Contains(bytes, separator))
                    throw new ProtoTypeException(
                        $"field '{field.Id}' runs up to {Hex(separator)} and the value contains it, so what "
                      + "was written would not read back as what was meant");

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
    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
            if (haystack.AsSpan(at, needle.Length).SequenceEqual(needle)) return true;

        return false;
    }

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
