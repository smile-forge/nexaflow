using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Structures that cannot be named without the ones before them.
///
/// <para>
/// The protocol is CoAP. Each option records <b>how far its number has moved since the previous option</b>
/// rather than what its number is, so the sixth option's identity is not recoverable from its own octets —
/// it is 0, +11, +0, +4, +0, +2, +235 accumulated. Nothing in a per-structure walk can produce that, which
/// is why a chain can thread a value along and each structure moves it on.
/// </para>
///
/// <para>
/// On the way out the same thread runs the other way: an option knows the number it wants, and the delta
/// it must write is that number minus whatever the options before it have already accumulated. That makes
/// each structure depend on the one before it — a genuine ordering, held by the worklist as a dependency
/// rather than assumed by a loop.
/// </para>
///
/// <para>
/// Two nibbles in one octet, each escaping to one or two more octets when it will not fit. They are
/// arithmetic on a plain octet rather than a bit group, because on the way out neither nibble is supplied:
/// both are computed, one from the accumulated number and one from a length that is itself derived.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class ThreadedChainCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>Below 13 the nibble is the value; 13 and 14 escape to one or two further octets, biased so
    /// that the short forms are never spelled the long way.</summary>
    private const string Nibble = "(n < 13 ? n : (n < 269 ? 13 : 14))";

    private const string DeltaNibble = "fields.optionHeader.value >> 4";
    private const string LengthNibble = "fields.optionHeader.value band 0x0f";

    /// <summary>How many octets the escape spends: none, one, or two.</summary>
    private static string Spilled(string nibble) => $"let n = {nibble} in n < 13 ? 0 : (n == 13 ? 1 : 2)";

    /// <summary>Recovering the value on the way in, from the nibble and whatever spilled past it.</summary>
    private static string Widened(string nibble, string spilt)
        => $"let n = {nibble} in n < 13 ? n : (n == 13 ? {spilt} + 13 : {spilt} + 269)";

    /// <summary>And composing it on the way out.</summary>
    private static string Spilling(string whole)
        => $"let d = {whole} in d < 13 ? 0 : (d < 269 ? d - 13 : d - 269)";

    private static readonly string ReadDelta = Widened(DeltaNibble, "fields.deltaExtension.value");
    private static readonly string ReadLength = Widened(LengthNibble, "fields.lengthExtension.value");

    /// <summary>
    /// Minimal-width unsigned octets where zero takes none at all — which is what makes the escape and its
    /// absence the same field rather than two.
    /// </summary>
    private static Conversion Spill => new("minuint", [ProtoValue.Of("empty")]);

    /// <summary>
    /// One option. Its number is <c>carried + delta</c>; its delta on the way out is
    /// <c>item.number - carried</c>.
    /// </summary>
    private static Field Option() => new()
    {
        Id = "option",
        Pattern = new Pattern.Group(
        [
            // Both nibbles are derived, so the octet is composed rather than supplied. The delta comes
            // from where the chain has got to; the length from the extent of a value further down, which
            // has not been measured yet when this is written and does not need to have been.
            new Field
            {
                Id = "optionHeader",
                Pattern = U8,
                Value = Expr.Parse(
                    "let d = item.number - carried in "
                  + "let v = fields.optionValue.extent in "
                  + $"((let n = d in {Nibble}) * 16) + (let n = v in {Nibble})"),
            },

            // Not a choice between packings. The first attempt made these two arms and a fallback, and it
            // could not be encoded: the expression that rebuilds the value names both escape widths, so
            // the dependency on the arm that was NOT taken pointed at a node nobody realised. The resolver
            // was right — the document was wrong. An escape is one run of octets whose width follows a
            // nibble, and zero is a perfectly good width for it.
            new Field
            {
                Id = "deltaExtension",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(Spilled(DeltaNibble))),
                Value = Expr.Parse(Spilling("item.number - carried")),
                Via = Spill,
            },

            new Field
            {
                Id = "lengthExtension",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(Spilled(LengthNibble))),
                Value = Expr.Parse(Spilling("fields.optionValue.extent")),
                Via = Spill,
            },

            new Field
            {
                Id = "optionValue",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(ReadLength)),
                Value = Expr.Parse("item.value"),
            },
        ]),
    };

    /// <summary>
    /// The message. Note what ends the option chain: not a count, and not the region — a marker octet the
    /// continuation has to <b>look at without consuming</b>.
    /// </summary>
    private static MessageDef Definition() => new()
    {
        Id = "datagram",
        Fields =
        [
            new Field
            {
                Id = "header",
                Pattern = new Pattern.Bits([new("version", 2), new("kind", 2), new("tokenLength", 4)]),
                Value = Expr.Parse("inputs.header"),
            },
            new Field
            {
                Id = "code",
                Pattern = new Pattern.Bits([new("codeClass", 3), new("codeDetail", 5)]),
                Value = Expr.Parse("inputs.code"),
            },
            new Field { Id = "messageId", Pattern = U16, Value = Expr.Parse("inputs.messageId") },
            new Field
            {
                Id = "token",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.header.value.tokenLength")),
                Value = Expr.Parse("inputs.token"),
            },

            new Field
            {
                Id = "options",
                Value = Expr.Parse("inputs.options"),
                Pattern = new Pattern.Chain(Option(),
                    Continues: Expr.Parse("room > 0 && peek != 0xff"),
                    Seed: Expr.Parse("0"),
                    Carry: Expr.Parse($"carried + ({ReadDelta})")),
            },

            // At most one, and the direction-asymmetry earns its keep again: on the way out the caller
            // either supplied a payload or did not, and on the way in there either are octets left or
            // there are not.
            new Field
            {
                Id = "payload",
                Value = Expr.Parse("inputs.payload"),
                Pattern = new Pattern.Chain(
                    new Field
                    {
                        Id = "carriedPayload",
                        Pattern = new Pattern.Group(
                        [
                            new Field { Id = "marker", Pattern = U8, Value = Expr.Parse("0xff") },
                            new Field
                            {
                                Id = "body",
                                Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                Value = Expr.Parse("item"),
                            },
                        ]),
                    },
                    Expr.Parse("ordinal < 1 && room > 0")),
            },
        ],

        Rules =
        [
            new Rule.Domain
            {
                Field = "version",
                Allowed = [ValueRange.Exactly(1)],
                Because = "only one version of this framing has ever been defined, and a datagram "
                        + "announcing another is not one this document can claim to have read.",
            },
        ],
    };

    private static byte[] Capture(int index) => ProtocolCorpus.Get("coap").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void An_options_number_is_recovered_from_every_option_before_it()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["version"].AsInt());
        Assert.AreEqual(0x1234, decoded["messageId"].AsInt());
        Assert.AreEqual(4, decoded["token"].AsBytes().Length);

        var options = decoded["options"].AsList();
        Assert.AreEqual(6, options.Count, "the chain ends where the octets do, with no count anywhere");

        // 0 +11 +0 +4 +0 +2 +235. The last option's identity is not in its own octets.
        var numbers = Accumulated(options);
        CollectionAssert.AreEqual(new long[] { 11, 11, 15, 15, 17, 252 }, numbers);

        var values = options.Select(o => ((ProtoValue.Rec)o).Members["optionValue"].AsBytes()).ToList();
        Assert.AreEqual("sensors", System.Text.Encoding.ASCII.GetString(values[0]));
        Assert.AreEqual("temperature", System.Text.Encoding.ASCII.GetString(values[1]));
        Assert.AreEqual(26, values[3].Length, "a length that needed the one-octet escape");
        Assert.AreEqual(8, values[5].Length);
    }

    [TestMethod]
    public void A_two_octet_escape_is_read_the_same_way_as_a_one_octet_one()
    {
        // The third capture exists for the cases the first cannot reach: no token at all, and a delta
        // large enough to need the wider escape.
        var decoded = new MessageCodec(Definition()).Decode(Capture(2));

        Assert.AreEqual(0, decoded["tokenLength"].AsInt());
        Assert.AreEqual(0, decoded["token"].AsBytes().Length, "no token octets follow at all");

        var options = decoded["options"].AsList();
        Assert.AreEqual(2, options.Count);
        CollectionAssert.AreEqual(new long[] { 11, 2049 }, Accumulated(options));
    }

    [TestMethod]
    public void A_payload_is_read_when_there_is_one_and_not_invented_when_there_is_not()
    {
        var withPayload = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(3, withPayload["options"].AsList().Count);

        var carried = withPayload["payload"].AsList();
        Assert.AreEqual(1, carried.Count);

        var body = ((ProtoValue.Rec)carried[0]).Members["body"].AsBytes();
        Assert.AreEqual("{\"temp\":21.5,\"unit\":\"C\"}", System.Text.Encoding.ASCII.GetString(body));

        // And a request carries none — the marker is absent rather than present and empty.
        Assert.AreEqual(0, new MessageCodec(Definition()).Decode(Capture(0))["payload"].AsList().Count);
    }

    [TestMethod]
    public void All_three_captures_re_encode_to_the_exact_original_octets()
    {
        // THE test, and the one the threaded value exists for: on the way out every delta is the option's
        // number minus what the options before it accumulated, so each structure depends on the one before
        // it. Nothing supplies a delta, a nibble, or any of the four possible extension octets.
        foreach (int index in (int[])[0, 1, 2])
        {
            var original = Capture(index);
            var codec = new MessageCodec(Definition());
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    [TestMethod]
    public void Moving_one_option_number_rewrites_the_deltas_after_it()
    {
        // Proof the deltas are threaded rather than carried through: raising the second option's number
        // by one lowers the next delta by one, and nothing in the inputs mentions either.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(2)));

        var options = ((ProtoValue.List)Member(inputs.Root("inputs"), "options")).Items;
        var first = With((ProtoValue.Rec)options[0], ("number", ProtoValue.Of(12L)));

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("options", new ProtoValue.List([first, options[1]]))));

        var decoded = codec.Decode(codec.Encode(inputs));

        CollectionAssert.AreEqual(new long[] { 12, 2049 }, Accumulated(decoded["options"].AsList()),
            "the second option keeps the number it asked for, by writing a smaller delta");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The option numbers, rebuilt from the deltas the way a reader has to.</summary>
    private static long[] Accumulated(IReadOnlyList<ProtoValue> options)
    {
        List<long> numbers = [];
        long running = 0;

        foreach (var option in options.Cast<ProtoValue.Rec>())
        {
            long header = option.Members["optionHeader"].AsInt();
            long nibble = header >> 4;

            long spilt = option.Members["deltaExtension"].AsInt();

            running += nibble < 13 ? nibble : nibble == 13 ? spilt + 13 : spilt + 269;

            numbers.Add(running);
        }

        return [.. numbers];
    }

    /// <summary>
    /// Feeds the decode back in, with each option carrying the <i>number</i> it wants rather than the
    /// delta it was written with — which is the whole point: the caller says what it means, and the
    /// document works out what to put on the wire.
    /// </summary>
    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);
        foreach (var (name, value) in decoded.Captures) inputs[name] = value;

        var numbers = Accumulated(decoded["options"].AsList());

        inputs["options"] = new ProtoValue.List(
        [
            .. decoded["options"].AsList().Select((o, i) => EvalScope.Record(
                ("number", ProtoValue.Of(numbers[i])),
                ("value", ((ProtoValue.Rec)o).Members["optionValue"]))),
        ]);

        inputs["payload"] = new ProtoValue.List(
        [
            .. decoded["payload"].AsList().Select(p => ((ProtoValue.Rec)p).Members["body"]),
        ]);

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Member(ProtoValue record, string name)
        => record is ProtoValue.Rec r && r.Members.TryGetValue(name, out var v) ? v : ProtoValue.Nothing;

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
