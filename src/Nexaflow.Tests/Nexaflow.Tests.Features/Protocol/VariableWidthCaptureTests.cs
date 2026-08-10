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
    private static Pattern Continuation => new Pattern.Varint(GroupOrder.LeastSignificantFirst, MaxOctets: 4);

    /// <summary>
    /// The shared framing: a type octet, a self-measuring length, and the region it measures.
    ///
    /// <para>
    /// The region carries a decode-side bound as well as measuring its children on the way out. That is
    /// what a length field is <i>for</i> when reading, and it is what gives "is there room for another
    /// structure" something to mean.
    /// </para>
    /// </summary>
    private static MessageDef Framed(string id, params Field[] body) => new()
    {
        Id = id,
        Fields =
        [
            new Field { Id = "fixedHeader", Pattern = U8, Value = Expr.Parse("inputs.fixedHeader") },
            new Field { Id = "remainingLength", Pattern = Continuation, Value = Expr.Parse("fields.body.extent") },
            new Field { Id = "body", Pattern = new Pattern.Group(body, Expr.Parse("fields.remainingLength.value")) },
        ],
    };

    private static MessageDef Acknowledgement() => Framed("connectAck",
        new Field { Id = "acknowledgeFlags", Pattern = U8, Value = Expr.Parse("inputs.acknowledgeFlags") },
        new Field { Id = "returnCode",       Pattern = U8, Value = Expr.Parse("inputs.returnCode") });

    /// <summary>
    /// Structures that chain with no count field anywhere in the message: there is another for as long as
    /// the region has room. Not expressible as a count — and here the elements happen to be one octet each,
    /// so a count could have been faked, which is exactly why the next document matters.
    /// </summary>
    private static MessageDef GrantList() => Framed("subscribeAck",
        new Field { Id = "packetIdentifier", Pattern = U16, Value = Expr.Parse("inputs.packetIdentifier") },
        new Field
        {
            Id = "grants",
            Pattern = new Pattern.Chain(new Field { Id = "grant", Pattern = U8, Value = Expr.Parse("item") },
                                        Expr.Parse("room > 0")),
            Value = Expr.Parse("inputs.grants"),
        });

    /// <summary>
    /// The document that could not be written before: structures that chain, each carrying <b>its own
    /// length prefix</b>, with no count anywhere.
    ///
    /// <para>
    /// This is the case the corpus recorded as blocking — a length prefix inside a repetition, where a
    /// document-global span name would mean instance 2's length referred to instance 1's region, or was
    /// cyclic. Each instance has its own field scope, so <c>fields.filter.extent</c> means <i>this
    /// structure's</i> filter and nothing else. And their number genuinely is not a function of the byte
    /// count, because they vary in length — the only statement available is "there is another while there
    /// is room".
    /// </para>
    /// </summary>
    private static MessageDef FilterList() => Framed("subscribe",
        new Field { Id = "packetIdentifier", Pattern = U16, Value = Expr.Parse("inputs.packetIdentifier") },
        new Field
        {
            Id = "subscriptions",
            Value = Expr.Parse("inputs.subscriptions"),
            Pattern = new Pattern.Chain(
                new Field
                {
                    Id = "subscription",
                    Pattern = new Pattern.Group(
                    [
                        new Field { Id = "filterLength", Pattern = U16, Value = Expr.Parse("fields.filter.extent") },
                        new Field
                        {
                            Id = "filter",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.filterLength.value")),
                            Value = Expr.Parse("item.filter"),
                            Via = "unascii",
                        },
                        new Field { Id = "requestedQos", Pattern = U8, Value = Expr.Parse("item.requestedQos") },
                    ]),
                },
                Expr.Parse("room > 0")),
        });

    /// <summary>A message with no body at all. The region is empty and measures zero, which is what the
    /// length must emit.</summary>
    private static MessageDef Liveness(string id) => Framed(id);

    /// <summary>A length-prefixed string, which is every variable field in the connect packet.</summary>
    private static Field[] Prefixed(string id, string? via = "unascii") =>
    [
        new Field { Id = $"{id}Length", Pattern = U16, Value = Expr.Parse($"fields.{id}.extent") },
        new Field
        {
            Id = id,
            Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Length.value")),
            Value = Expr.Parse($"inputs.{id}"),
            Via = via,
        },
    ];

    /// <summary>
    /// A section that exists only because one bit says so.
    ///
    /// <para>
    /// Presence needs no construct of its own: it is a choice between two packings, one of which is empty.
    /// And because the discriminator reads a named bit run whose width is in the declaration, the arms are
    /// still checked for exhaustiveness — a one-bit flag has exactly two answers and both must be given.
    /// </para>
    /// </summary>
    private static Field GatedBy(string id, string flag, params Field[] fields) => new()
    {
        Id = id,
        Pattern = new Pattern.Choice(Expr.Parse($"fields.connectFlags.value.{flag}"),
        [
            new Arm("absent", 0, []),
            new Arm("present", 1, fields),
        ]),
    };

    /// <summary>
    /// The connect packet: 143 octets of body, of which 114 exist only because of three bits in an octet
    /// read 22 positions earlier.
    /// </summary>
    private static MessageDef Connect() => Framed("connect",
        [
            .. Prefixed("protocolName"),
            new Field { Id = "protocolLevel", Pattern = U8, Value = Expr.Parse("inputs.protocolLevel") },
            new Field
            {
                Id = "connectFlags",
                Pattern = new Pattern.Bits(
                [
                    new("userNameFlag", 1), new("passwordFlag", 1), new("willRetain", 1),
                    new("willQos", 2), new("willFlag", 1), new("cleanSession", 1), new("reserved", 1),
                ]),
                Value = Expr.Parse("inputs.connectFlags"),
            },
            new Field { Id = "keepAlive", Pattern = U16, Value = Expr.Parse("inputs.keepAlive") },
            .. Prefixed("clientId"),

            GatedBy("will", "willFlag", [.. Prefixed("willTopic"), .. Prefixed("willMessage", via: null)]),
            GatedBy("credential", "userNameFlag", Prefixed("userName")),
            GatedBy("secret", "passwordFlag", Prefixed("password")),
        ]);

    private static byte[] Capture(int index) => ProtocolCorpus.Get("mqtt").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Every_document_validates()
    {
        foreach (var message in (MessageDef[])
                 [Connect(), Acknowledgement(), GrantList(), FilterList(), Liveness("ping")])
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
    public void The_grant_list_chains_for_as_long_as_the_region_has_room()
    {
        var decoded = new MessageCodec(GrantList()).Decode(Capture(3));

        Assert.AreEqual(10, decoded["packetIdentifier"].AsInt());

        var grants = decoded["grants"].AsList();
        Assert.AreEqual(3, grants.Count, "no count field exists — the region is what ends it");
        CollectionAssert.AreEqual(new long[] { 0x01, 0x01, 0x80 }, grants.Select(g => g.AsInt()).ToArray(),
            "an exact grant, a downgrade, and a refusal");
    }

    [TestMethod]
    public void Structures_carrying_their_own_length_prefix_chain_without_colliding()
    {
        // Three (length, filter, qos) structures of 13, 18 and 21 octets. Their number is not a function
        // of the byte count, and each one's length prefix refers to its own filter — the two things that
        // made this document unwritable before.
        var decoded = new MessageCodec(FilterList()).Decode(Capture(2));

        Assert.AreEqual(10, decoded["packetIdentifier"].AsInt());

        var subscriptions = decoded["subscriptions"].AsList();
        Assert.AreEqual(3, subscriptions.Count);

        var topics = subscriptions.Select(s => ((ProtoValue.Rec)s).Members["filter"].AsText()).ToArray();
        CollectionAssert.AreEqual(new[] { "dev/status", "dev/+/telemetry", "$SYS/broker/uptime" }, topics);

        var qos = subscriptions.Select(s => ((ProtoValue.Rec)s).Members["requestedQos"].AsInt()).ToArray();
        CollectionAssert.AreEqual(new long[] { 1, 2, 0 }, qos);

        // Each structure's own prefix, not the first one's repeated.
        var lengths = subscriptions.Select(s => ((ProtoValue.Rec)s).Members["filterLength"].AsInt()).ToArray();
        CollectionAssert.AreEqual(new long[] { 10, 15, 18 }, lengths);
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
    public void The_connect_packet_decodes_the_sections_its_flag_bits_admit()
    {
        var decoded = new MessageCodec(Connect()).Decode(Capture(0));

        Assert.AreEqual(143, decoded["remainingLength"].AsInt(), "the two-octet form");
        Assert.AreEqual("MQTT", decoded["protocolName"].AsText());
        Assert.AreEqual(4, decoded["protocolLevel"].AsInt());
        Assert.AreEqual(60, decoded["keepAlive"].AsInt());
        Assert.AreEqual("nexaflow-probe-01", decoded["clientId"].AsText());

        // 0xce = 1 1 0 01 1 1 0
        Assert.AreEqual(1, decoded["userNameFlag"].AsInt());
        Assert.AreEqual(1, decoded["willFlag"].AsInt());
        Assert.AreEqual(1, decoded["willQos"].AsInt());
        Assert.AreEqual(0, decoded["reserved"].AsInt());

        // Each gated section reports which packing it took, so a later step branches on that rather than
        // re-testing the flag byte and hoping the two rules agree.
        foreach (var section in (string[])["will", "credential", "secret"])
            Assert.AreEqual("present", decoded[section].AsText(), section);

        Assert.AreEqual("dev/status", decoded["willTopic"].AsText());
        Assert.AreEqual(80, decoded["willMessage"].AsBytes().Length);
        Assert.AreEqual("sensoruser", decoded["userName"].AsText());
        Assert.AreEqual("s3cr3t", decoded["password"].AsText());
    }

    [TestMethod]
    public void Clearing_a_flag_bit_removes_its_section_and_every_length_follows()
    {
        // The proof that presence is structural rather than cosmetic: drop the will flag and 94 octets
        // leave the packet, with the outer length re-measured — including back down to the one-octet form.
        var codec = new MessageCodec(Connect());
        var inputs = InputsFrom(codec.Decode(Capture(0)), except: Derived);

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("connectFlags", With(Member(inputs.Root("inputs"), "connectFlags"),
                                  ("willFlag", ProtoValue.Of(0L)), ("willQos", ProtoValue.Of(0L))))));

        var encoded = codec.Encode(inputs);
        var decoded = codec.Decode(encoded);

        // 146 − 94 is 52, and the answer is 51: the body drops to 49 octets, which no longer needs the
        // two-octet form of the length, so the length field shrinks too. Two derivations moving together,
        // neither of them written down anywhere.
        Assert.AreEqual(51, encoded.Length, "1 header + 1 length + 49 body");
        Assert.AreEqual(49, decoded["remainingLength"].AsInt());
        Assert.AreEqual("absent", decoded["will"].AsText());
        Assert.IsTrue(decoded["willTopic"].IsNull, "not there rather than empty");
        Assert.AreEqual("s3cr3t", decoded["password"].AsText(), "the sections after it still line up");
    }

    [TestMethod]
    public void All_six_captures_re_encode_to_the_exact_original_octets()
    {
        // Every length is withheld from the inputs: the outer one, all six inside the connect packet, and
        // the per-structure ones in the subscribe payload. If any were echoed rather than measured, this
        // is where it would show.
        foreach (var (index, message) in ((int, MessageDef)[])
                 [(0, Connect()), (1, Acknowledgement()), (2, FilterList()), (3, GrantList()),
                  (4, Liveness("probe")), (5, Liveness("probe"))])
        {
            var original = Capture(index);
            var codec = new MessageCodec(message);
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original), except: Derived));

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

    /// <summary>Every field these documents derive rather than accept. Withholding them is what makes a
    /// round trip evidence instead of a restatement.</summary>
    private static readonly string[] Derived =
    [
        "remainingLength",
        "protocolNameLength", "clientIdLength", "willTopicLength", "willMessageLength",
        "userNameLength", "passwordLength",
        "subscriptions.filterLength",
    ];

    private static EvalScope Inputs(params (string Name, ProtoValue Value)[] members)
        => new EvalScope().Set("inputs", EvalScope.Record(members));

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

    /// <summary>
    /// Feeds decoded captures back in as <c>inputs.*</c>, minus the ones the document must derive.
    ///
    /// <para>
    /// An <c>except</c> entry of the form <c>list.member</c> strips that member from every structure of a
    /// chained field — which is how the per-structure length prefixes are withheld, not just the outer one.
    /// Leaving them in would let an echoed value pass for a derived one.
    /// </para>
    /// </summary>
    private static EvalScope InputsFrom(DecodeResult decoded, params string[] except)
    {
        var whole = except.Where(e => !e.Contains('.')).ToHashSet(StringComparer.Ordinal);
        var members = except.Where(e => e.Contains('.'))
                            .Select(e => e.Split('.', 2))
                            .ToLookup(p => p[0], p => p[1], StringComparer.Ordinal);

        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
        {
            if (whole.Contains(name)) continue;

            inputs[name] = members.Contains(name) && value is ProtoValue.List list
                ? new ProtoValue.List([.. list.Items.Select(i => Without(i, members[name]))])
                : value;
        }

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Without(ProtoValue structure, IEnumerable<string> members)
    {
        if (structure is not ProtoValue.Rec rec) return structure;

        var kept = new Dictionary<string, ProtoValue>(rec.Members, StringComparer.Ordinal);
        foreach (var member in members) kept.Remove(member);
        return new ProtoValue.Rec(kept);
    }
}
