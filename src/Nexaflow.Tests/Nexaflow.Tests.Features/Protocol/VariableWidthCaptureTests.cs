using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The shape whose <b>width is a function of its own value</b>, and the span whose width is a function of
/// something already read.
///
/// <para>
/// This is the case the facet model was restructured for and which nothing had yet exercised: until now
/// every extent in the engine was axiomatic, settled with no prerequisites. A continuation-encoded integer
/// cannot be measured until it is known, so <c>Extent</c> declares a dependency on <c>Value</c> and the
/// worklist does the rest. There is no second pass, no reserved placeholder, and no encode-widen-re-encode
/// loop — the fixed-point iteration the specification proposes for this turns out to be unnecessary
/// wherever the measured region excludes the length field, which is every case in the corpus.
/// </para>
///
/// <para>
/// The protocol is MQTT 3.1.1. Its Remaining Length is a 1–4 octet base-128 chain, and the corpus carries
/// both widths from the same field: <c>8f 01</c> (143) in a CONNECT and <c>02</c> in a CONNACK. Four of its
/// six captures are here; SUBSCRIBE and CONNECT need repetition and optionality that do not exist yet, and
/// are deliberately absent rather than approximated.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class VariableWidthCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>
    /// A continuation chain bounded at four octets, least-significant group first.
    ///
    /// <para>
    /// The order is required and it is not a formality: the same three octets mean different numbers under
    /// each order, and the engine has no defensible default. The codec itself is the existing
    /// <c>base128</c> converter — one implementation, already covered by the inverse laws, rather than a
    /// second copy inside the codec where a duplicate would quietly pick one family's answer.
    /// </para>
    /// </summary>
    private static Pattern Chain => new Pattern.Varint(GroupOrder.LeastSignificantFirst, MaxOctets: 4);

    /// <summary>The shared framing: a type octet, a self-measuring length, and the region it measures.</summary>
    private static MessageDef Framed(string id, params Field[] body) => new()
    {
        Id = id,
        Fields =
        [
            new Field { Id = "fixedHeader", Pattern = U8, Value = Expr.Parse("inputs.fixedHeader") },
            new Field { Id = "remainingLength", Pattern = Chain, Value = Expr.Parse("fields.body.extent") },
            new Field { Id = "body", Pattern = new Pattern.Group(body) },
        ],
    };

    private static MessageDef Acknowledgement() => Framed("connectAck",
        new Field { Id = "acknowledgeFlags", Pattern = U8, Value = Expr.Parse("inputs.acknowledgeFlags") },
        new Field { Id = "returnCode",       Pattern = U8, Value = Expr.Parse("inputs.returnCode") });

    /// <summary>
    /// A repetition with no count field anywhere in the message: the element count is whatever fits in the
    /// framed region. It is expressible here only because the elements are one octet each — the count is
    /// derived from the remaining length by subtracting the extent of what precedes it.
    /// </summary>
    private static MessageDef GrantList() => Framed("subscribeAck",
        new Field { Id = "packetIdentifier", Pattern = U16, Value = Expr.Parse("inputs.packetIdentifier") },
        new Field
        {
            Id = "returnCodes",
            Pattern = new Pattern.Repeat(new Field { Id = "grant", Pattern = U8 },
                // Not `remainingLength - 2`: naming the field whose octets are being discounted is what
                // keeps this true when the header in front of it changes.
                Expr.Parse("fields.remainingLength.value - fields.packetIdentifier.extent")),
            Value = Expr.Parse("inputs.returnCodes"),
        });

    /// <summary>A message with no body at all. The region is empty and measures zero, which is what the
    /// length must emit.</summary>
    private static MessageDef Liveness(string id) => Framed(id);

    private static byte[] Capture(int index) => ProtocolCorpus.Get("mqtt").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Every_document_validates()
    {
        foreach (var message in (MessageDef[])[Acknowledgement(), GrantList(), Liveness("ping")])
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}:\n" + string.Join("\n", issues));
        }
    }

    [TestMethod]
    public void The_acknowledgement_decodes_and_its_length_is_one_octet()
    {
        var decoded = new MessageCodec(Acknowledgement()).Decode(Capture(1));

        Assert.AreEqual(0x20, decoded["fixedHeader"].AsInt());
        Assert.AreEqual(2, decoded["remainingLength"].AsInt(), "one-octet form, because 2 < 128");
        Assert.AreEqual(0, decoded["acknowledgeFlags"].AsInt());
        Assert.AreEqual(0, decoded["returnCode"].AsInt());
    }

    [TestMethod]
    public void The_grant_list_repeats_as_many_times_as_the_frame_allows()
    {
        var decoded = new MessageCodec(GrantList()).Decode(Capture(3));

        Assert.AreEqual(10, decoded["packetIdentifier"].AsInt());

        var grants = decoded["returnCodes"].AsList();
        Assert.AreEqual(3, grants.Count, "no count field exists — the frame is the count");
        CollectionAssert.AreEqual(new long[] { 0x01, 0x01, 0x80 }, grants.Select(g => g.AsInt()).ToArray(),
            "an exact grant, a downgrade, and a refusal");
    }

    [TestMethod]
    public void A_message_with_no_body_measures_zero()
    {
        foreach (var (index, header) in ((int, long)[])[(4, 0xc0), (5, 0xd0)])
        {
            var decoded = new MessageCodec(Liveness("probe")).Decode(Capture(index));
            Assert.AreEqual(header, decoded["fixedHeader"].AsInt());
            Assert.AreEqual(0, decoded["remainingLength"].AsInt());
        }
    }

    [TestMethod]
    public void All_four_captures_re_encode_to_the_exact_original_octets()
    {
        // `remainingLength` is withheld from the inputs throughout: it is measured, not carried.
        foreach (var (index, message) in ((int, MessageDef)[])
                 [(1, Acknowledgement()), (3, GrantList()), (4, Liveness("probe")), (5, Liveness("probe"))])
        {
            var original = Capture(index);
            var codec = new MessageCodec(message);
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original), except: ["remainingLength"]));

            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    // ── The width is a function of the value ──────────────────────────────────

    /// <summary>A payload that runs to the end of the frame — the span whose extent is recovered from a
    /// field already read.</summary>
    private static MessageDef Carrying() => Framed("carrying",
        new Field
        {
            Id = "payload",
            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.remainingLength.value")),
            Value = Expr.Parse("inputs.payload"),
        });

    [TestMethod]
    public void One_declaration_emits_both_the_one_octet_and_the_two_octet_form()
    {
        // The corpus's own point: CONNECT carries `8f 01` and CONNACK carries `02` in the same field of
        // the same protocol. Nothing here chooses a width — it falls out of the value.
        var codec = new MessageCodec(Carrying());

        // total = 1 type octet + the width the length chose + the payload. The middle term is the point:
        // it is 1 up to 127 and 2 from 128, and nothing in the document says so.
        foreach (var (size, expected, total) in ((int, string, int)[])
                 [(2, "02", 4), (127, "7f", 129), (128, "8001", 131), (143, "8f01", 146), (16383, "ff7f", 16386)])
        {
            var encoded = codec.Encode(Inputs(("fixedHeader", ProtoValue.Of(0x10L)),
                                              ("payload", ProtoValue.Of(new byte[size]))));

            Assert.AreEqual(total, encoded.Length, $"a {size}-octet payload");
            Assert.AreEqual(expected, Convert.ToHexString(encoded[1..(1 + expected.Length / 2)]).ToLowerInvariant(),
                $"a {size}-octet payload must be framed by {expected}");

            // …and it reads back as the same number, which is what makes the width a codec property
            // rather than a coincidence of this particular size.
            Assert.AreEqual(size, codec.Decode(encoded)["remainingLength"].AsInt());
        }
    }

    [TestMethod]
    public void The_two_octet_form_matches_the_capture_that_forced_it()
    {
        // The corpus deliberately sized a will payload to push the length to 143 and force the two-octet
        // form, because a one-octet capture would have hidden the whole problem. Those exact octets:
        var codec = new MessageCodec(Carrying());
        var encoded = codec.Encode(Inputs(("fixedHeader", ProtoValue.Of(0x10L)),
                                          ("payload", ProtoValue.Of(new byte[143]))));

        CollectionAssert.AreEqual(Capture(0)[..3], encoded[..3],
            "the first three octets of the CONNECT capture are its header and length");
    }

    [TestMethod]
    public void A_recovered_span_carries_text_and_survives_the_round_trip()
    {
        // The length-prefixed string, which is every variable field in the protocol's connect packet: a
        // width in one field, the octets in the next, and the width derived back from the octets on the
        // way out.
        var message = new MessageDef
        {
            Id = "labelled",
            Fields =
            [
                new Field { Id = "labelLength", Pattern = U16, Value = Expr.Parse("fields.label.extent") },
                new Field
                {
                    Id = "label",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.labelLength.value")),
                    Value = Expr.Parse("inputs.label"),
                    Via = "unascii",
                },
            ],
        };

        var codec = new MessageCodec(message);
        Assert.AreEqual(0, codec.Validate().Count, string.Join("\n", codec.Validate()));

        var encoded = codec.Encode(Inputs(("label", ProtoValue.Of("dev/status"))));

        Assert.AreEqual("000a6465762f737461747573", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.AreEqual("dev/status", codec.Decode(encoded)["label"].AsText());
    }

    // ── What the engine refuses ───────────────────────────────────────────────

    [TestMethod]
    public void A_chain_that_is_not_the_shortest_encoding_of_its_value_is_a_decode_error()
    {
        // `8f 80 00` is a legal-looking chain decoding to 15. Re-encoding 15 minimally is `0f` — one
        // octet — so accepting it would make encode(decode(b)) != b for input the protocol itself calls
        // malformed. Rejecting it is what keeps value→octets injective; remembering the padding in order
        // to reproduce it would be preserving malformed input rather than refusing it.
        var padded = (byte[])[0x10, 0x8f, 0x80, 0x00, .. new byte[15]];

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Carrying()).Decode(padded));
        StringAssert.Contains(ex.Message, "shortest");
        StringAssert.Contains(ex.Message, "0f");
    }

    [TestMethod]
    public void A_chain_that_never_terminates_is_bounded_rather_than_believed()
    {
        // Otherwise the data decides how much of itself there is.
        var runaway = (byte[])[0x10, 0xff, 0xff, 0xff, 0xff, 0xff];

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Carrying()).Decode(runaway));
        StringAssert.Contains(ex.Message, "declared bound");
    }

    [TestMethod]
    public void A_span_with_two_answers_about_its_own_length_is_rejected()
    {
        // Exactly one extent key. Two would silently prefer one; none would read to the end of whatever
        // happened to be next.
        var both = new MessageDef
        {
            Id = "ambiguous",
            Fields = [new Field { Id = "x", Pattern = new Pattern.Opaque(4, Expr.Parse("inputs.n")) }],
        };

        StringAssert.Contains(string.Join("\n", both.Validate()), "exactly one extent");

        var neither = new MessageDef
        {
            Id = "unbounded",
            Fields = [new Field { Id = "x", Pattern = new Pattern.Opaque(null, null) }],
        };

        StringAssert.Contains(string.Join("\n", neither.Validate()), "exactly one extent");
    }

    [TestMethod]
    public void A_declared_width_is_asserted_rather_than_trimmed()
    {
        var message = new MessageDef
        {
            Id = "fixedSpan",
            Fields = [new Field { Id = "x", Pattern = new Pattern.Opaque(4), Value = Expr.Parse("inputs.x") }],
        };

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(message).Encode(Inputs(("x", ProtoValue.Of(new byte[5])))));

        StringAssert.Contains(ex.Message, "but the value is 5");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope Inputs(params (string Name, ProtoValue Value)[] members)
        => new EvalScope().Set("inputs", EvalScope.Record(members));

    private static EvalScope InputsFrom(DecodeResult decoded, params string[] except)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!except.Contains(name, StringComparer.Ordinal)) inputs[name] = value;

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }
}
