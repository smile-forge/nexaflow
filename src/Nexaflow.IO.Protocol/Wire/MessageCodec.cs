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
/// and a genuine cycle reported as one before any octet is produced.
/// </para>
/// </summary>
public sealed class MessageCodec(MessageDef message, ConverterTable? converters = null)
{
    private readonly MessageDef _message = message;
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;

    /// <summary>Validates the definition. Always call before use; the issues are authoring errors.</summary>
    public IReadOnlyList<string> Validate() => _message.Validate();

    // ── Decode ────────────────────────────────────────────────────────────────

    public DecodeResult Decode(ReadOnlySpan<byte> bytes)
    {
        Dictionary<string, ProtoValue> captures = new(StringComparer.Ordinal);
        List<WireSpan> spans = [];
        int offset = 0;

        foreach (var field in _message.Fields)
        {
            int width = field.Pattern.StaticWidth
                ?? throw new ProtoTypeException(
                    $"field '{field.Id}' has no static width; value-dependent extents are not yet wired "
                  + "into the decoder");

            if (offset + width > bytes.Length)
                throw new ProtoTypeException(
                    $"field '{field.Id}' needs {width} octet(s) at offset {offset}, but the message is "
                  + $"{bytes.Length} octets — the definition and the data disagree");

            var run = bytes.Slice(offset, width);

            switch (field.Pattern)
            {
                case Pattern.Bits bits:
                {
                    long packed = ReadUnsigned(run);
                    int remaining = bits.TotalBits;

                    foreach (var slice in bits.Slices)
                    {
                        remaining -= slice.Width;
                        long v = (packed >> remaining) & ((1L << slice.Width) - 1);
                        captures[slice.Name] = ProtoValue.Of(v);
                        spans.Add(new WireSpan(offset, width, slice.Name, ProtoValue.Of(v)));
                    }
                    break;
                }

                case Pattern.Scalar scalar:
                {
                    long raw = scalar.Signed ? ReadSigned(run, scalar.BigEndian)
                                             : ReadUnsigned(run, scalar.BigEndian);
                    var value = Convert(ProtoValue.Of(raw), field.Via, forward: false);
                    captures[field.CaptureName] = value;
                    spans.Add(new WireSpan(offset, width, field.CaptureName, value));
                    break;
                }

                case Pattern.Opaque:
                {
                    var value = Convert(ProtoValue.Of(run.ToArray()), field.Via, forward: false);
                    captures[field.CaptureName] = value;
                    spans.Add(new WireSpan(offset, width, field.CaptureName, value));
                    break;
                }
            }

            offset += width;
        }

        if (offset != bytes.Length)
            throw new ProtoTypeException(
                $"decoded {offset} octet(s) but the message is {bytes.Length} — trailing data is an error "
              + "rather than something ignored, because ignoring it accepts a malformed capture as valid");

        return new DecodeResult(captures, spans);
    }

    // ── Encode ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces the octets. <paramref name="scope"/> supplies <c>inputs.*</c> and anything else the field
    /// expressions read; <c>fields.&lt;id&gt;.value</c> and <c>.extent</c> resolve through the worklist.
    /// </summary>
    public byte[] Encode(EvalScope scope)
    {
        var evaluator = new Evaluator(_converters);
        var resolver = new Resolver();

        foreach (var field in _message.Fields)
            resolver.Add(NodeFor(field, scope, evaluator));

        var settled = resolver.Resolve();

        // Position is a linearisation, not a fixpoint: one ordered sweep once extents have settled.
        var output = new List<byte>();
        foreach (var field in _message.Fields)
        {
            var value = settled[new FacetRef(field.Id, Facet.Value)];
            Write(output, field, value as ProtoValue ?? ProtoValue.Nothing);
        }
        return [.. output];
    }

    private ResolutionNode NodeFor(Field field, EvalScope scope, Evaluator evaluator)
    {
        // Derived from the expression, never hand-declared — which is what makes every reference visible
        // to the worklist rather than only the ones someone remembered to write down.
        var dependencies = field.Value is null
            ? []
            : MessageDef.FieldReferences(field.Value)
                .Select(r => new FacetRef(r.Field, r.Facet.Equals("extent", StringComparison.OrdinalIgnoreCase)
                                                    ? Facet.Extent : Facet.Value))
                .Distinct()
                .ToList();

        return new ResolutionNode
        {
            Id = field.Id,
            NotApplicable = new HashSet<Facet> { Facet.Realised, Facet.Present, Facet.Emitted },

            DependenciesFor = f => f == Facet.Value ? dependencies : [],

            Settle = (f, inputs) => f switch
            {
                Facet.Extent => FacetResult.Of(field.Pattern.StaticWidth
                    ?? throw new ProtoTypeException($"field '{field.Id}' has no static width")),

                Facet.Value => FacetResult.Of(Evaluate(field, scope, evaluator, inputs)),

                _ => FacetResult.Of(null),
            },
        };
    }

    private ProtoValue Evaluate(Field field, EvalScope scope, Evaluator evaluator,
                                IReadOnlyDictionary<FacetRef, object?> resolved)
    {
        if (field.Value is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' has no value expression, so it cannot be encoded. A decode-only field "
              + "must be declared as such rather than left silent.");

        // Settled facets are exposed as `fields.<id>.value` / `.extent`, so the expression that declared
        // the dependency reads it back by the same name it used.
        var child = scope.Child();
        Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);

        foreach (var (reference, value) in resolved)
        {
            var facet = reference.Facet == Facet.Extent ? "extent" : "value";
            var slot = byField.TryGetValue(reference.NodeId, out var existing) && existing is ProtoValue.Rec r
                ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
                : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

            slot[facet] = value switch
            {
                ProtoValue pv => pv,
                int i => ProtoValue.Of((long)i),
                long l => ProtoValue.Of(l),
                _ => ProtoValue.Nothing,
            };
            byField[reference.NodeId] = new ProtoValue.Rec(slot);
        }

        child.Set("fields", new ProtoValue.Rec(byField));
        return Convert(evaluator.Eval(field.Value, child), field.Via, forward: true);
    }

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
                if (bytes.Length != opaque.Octets)
                    throw new ProtoTypeException(
                        $"field '{field.Id}' is {opaque.Octets} octet(s) but the value is {bytes.Length}");
                output.AddRange(bytes);
                break;
            }
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
