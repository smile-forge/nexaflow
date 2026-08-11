using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A length that measures the span it sits inside, and a width written into a bit-field of an octet that
/// has not been emitted yet.
///
/// <para>
/// The protocol is BACnet/IP. Five captures, one document: a Confirmed-Request, its ComplexACK, a segment
/// of a longer one, a SegmentACK and an Error, all under the same BVLC + NPDU + APDU stack.
/// </para>
///
/// <para>
/// Two things here are unlike anything earlier in the corpus. The BVLC length counts the <b>whole</b>
/// message including its own two octets, where every other framed protocol so far measures a span it is
/// outside of — and that is only resolvable because extent and value are separate facets: the length's own
/// extent is two octets whatever its value turns out to be, so the region settles first and the value
/// second. And a BACnet tag carries the width of its value in the low three bits of the tag octet, so
/// encode has to know how wide a value is before writing the octet that precedes it. Both fall out of
/// facets that already existed; neither needed a pass, a placeholder or a back-patch.
/// </para>
///
/// <para>
/// The third thing is what the corpus called recursion. Constructed data is opening tags inside opening
/// tags to arbitrary depth, which no finite field graph can describe — so depth is not structure here, it
/// is <i>data</i>: the chain threads it, each tag moves it, and the run ends at the closing tag that
/// returns it to zero. A parameterless, non-recursive shape expresses arbitrary nesting because the nesting
/// was never structural to begin with.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class TaggedUnionCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>Minimal-width unsigned octets, and no octets at all for zero — which is what makes "the
    /// value is however many octets the tag said" and "the tag says however many octets the value needs"
    /// the same declaration read in opposite directions.</summary>
    private static Conversion Minimal => new("minuint", [ProtoValue.Of("empty")]);

    private static Field Fixed(string id, long octet) => new()
    {
        Id = id,
        Pattern = U8,
        Value = Expr.Parse($"0x{octet:x2}"),
    };

    /// <summary>
    /// A tag octet whose low three bits are the width of the value after it, and the value.
    ///
    /// <para>
    /// The two point at each other in opposite directions, as everywhere else the corpus needed a length:
    /// on the way out the tag reads <c>fields.&lt;value&gt;.extent</c>, and on the way in the value reads
    /// the tag's low bits back. The difference from every earlier case is that the length is not a field —
    /// it is three bits of an octet the encoder must write <i>before</i> the octets it counts.
    /// </para>
    /// </summary>
    private static Field[] Tagged(string id, long tagNumber, string source) =>
    [
        new Field
        {
            Id = $"{id}Tag",
            Pattern = U8,
            Value = Expr.Parse($"0x{(tagNumber << 4) | 0x08:x2} bor fields.{id}.extent"),
        },
        new Field
        {
            Id = id,
            Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Tag.value band 0x07")),
            Via = Minimal,
            Value = Expr.Parse(source),
        },
    ];

    /// <summary>The same shape with the class bit clear — an application tag rather than a context one. The
    /// distinction is one bit and it is the difference between "parameter number 1 of this service" and
    /// "an enumerated value".</summary>
    private static Field[] Enumerated(string id, string source) =>
    [
        new Field
        {
            Id = $"{id}Tag",
            Pattern = U8,
            Value = Expr.Parse($"0x90 bor fields.{id}.extent"),
        },
        new Field
        {
            Id = id,
            Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Tag.value band 0x07")),
            Via = Minimal,
            Value = Expr.Parse(source),
        },
    ];

    /// <summary>
    /// The ReadProperty parameter pair, which the request and its acknowledgement both carry. Distinct ids
    /// because both packings live in one flat scope even though only one is ever taken.
    /// </summary>
    private static Field[] ObjectAndProperty(string prefix, string objectId, string propertyId) =>
    [
        new Field
        {
            Id = $"{prefix}ObjectIdTag",
            Pattern = U8,
            Value = Expr.Parse($"0x08 bor fields.{prefix}ObjectId.extent"),
        },

        // 10 bits of type and 22 of instance, crossing three octet boundaries and aligned with none of
        // them. It is a 32-bit field of two runs; that analog-input 1 is a temperature sensor somewhere is
        // meaning, and meaning is not carried on the wire.
        //
        // The runs are named per occurrence because a run binds alongside the fields rather than beneath
        // them, so the request's type and the acknowledgement's are two names in one scope — the engine
        // refused the collision, which is the same refusal it makes for two fields.
        new Field
        {
            Id = $"{prefix}ObjectId",
            Pattern = new Pattern.Bits([new($"{prefix}ObjectType", 10), new($"{prefix}Instance", 22)]),
            Value = Expr.Parse(objectId),
        },

        .. Tagged($"{prefix}PropertyId", 1, propertyId),

        // Context tag 2, and only when one element of an array is being read rather than the whole
        // property. Nothing already read says whether it is there: on the way in the next octet's tag
        // number does, and on the way out only the caller knows. The reading of the wire may therefore
        // ask the wire's questions, and the selection may ask the caller's.
        new Field
        {
            Id = $"{prefix}Element",
            Pattern = new Pattern.Choice(Expr.Parse("room > 0 && (peek >> 4) == 2"),
            [
                new Arm("wholeProperty", 0, []),
                new Arm("oneElement", 1, [.. Tagged($"{prefix}ArrayIndex", 2, $"inputs.{prefix}ArrayIndex")]),
            ],
            Selects: Expr.Parse($"inputs.{prefix}HasArrayIndex == 1")),
        },
    ];

    /// <summary>
    /// One tagged value inside a constructed run. Its tag number and class come from the value being
    /// written; only the width is derived — and the width decides whether it fits in the tag's low bits or
    /// escapes to a length octet.
    /// </summary>
    private static readonly Field TaggedValue = new()
    {
        Id = "taggedValue",
        Pattern = new Pattern.Group(
        [
            // The low three bits are a width — unless they are 6 or 7, where they are a marker and there
            // is nothing to measure. Deriving them unconditionally is what the corpus could not catch:
            // capture 2 has no marker inside the run, so the wrong rule round-tripped perfectly and would
            // have flattened every nested construct to depth zero.
            new Field
            {
                Id = "valueTag",
                Pattern = U8,
                Value = Expr.Parse(
                    "let marker = item.valueTag band 0x07 in "
                  + "(item.valueTag band 0xf8) bor (marker > 5 ? marker "
                  + ": (fields.valueContent.extent < 5 ? fields.valueContent.extent : 5))"),
            },

            // A tag number of 15 is not a tag number — it says the real one is in the octet after. The
            // same presence-by-comparison as the two length escapes below it, and the reason the tag
            // octet's top nibble is carried through rather than derived.
            new Field
            {
                Id = "extendedTag",
                Pattern = new Pattern.Choice(Expr.Parse("(fields.valueTag.value >> 4) == 15"),
                [
                    new Arm("inline", 0, []),
                    new Arm("extended", 1,
                    [
                        new Field
                        {
                            Id = "extendedTagNumber",
                            Pattern = U8,
                            Value = Expr.Parse("item.extendedTagNumber"),
                        },
                    ]),
                ]),
            },

            // Present exactly when the tag said the width would not fit in it. Zero octets otherwise —
            // which is the same "width follows the octets" the corpus has now wanted five times, and the
            // reason there is no separate optional-field notion.
            new Field
            {
                Id = "valueLength",
                Pattern = Pattern.Opaque.Measured(
                    Expr.Parse("(fields.valueTag.value band 0x07) == 5 ? 1 : 0")),
                Via = Minimal,
                Value = Expr.Parse(
                    "let n = fields.valueContent.extent in "
                  + "n < 5 ? 0 : (n < 254 ? n : (n < 65536 ? 254 : 255))"),
            },

            // And that octet has its own two escapes: 254 says a 16-bit length follows, 255 a 32-bit one.
            // Two presence choices rather than one three-way, because a comparison's cover is provable and
            // a three-way would have needed a fallback arm standing for a case that is not "anything
            // else" but exactly one thing.
            //
            // Recorded as needing a new shape — a fixed-width unsigned span whose width is an expression
            // — and it does not. A fixed-width scalar inside a choice is that, and the choice is a shape
            // the engine has had since step 4.
            new Field
            {
                Id = "wideLength",
                Pattern = new Pattern.Choice(Expr.Parse("fields.valueLength.value == 254"),
                [
                    new Arm("inline", 0, []),
                    new Arm("sixteenBit", 1,
                    [
                        new Field
                        {
                            Id = "valueLength16",
                            Pattern = new Pattern.Scalar(2, BigEndian: true),
                            Value = Expr.Parse("fields.valueContent.extent"),
                        },
                    ]),
                ]),
            },

            new Field
            {
                Id = "hugeLength",
                Pattern = new Pattern.Choice(Expr.Parse("fields.valueLength.value == 255"),
                [
                    new Arm("inline", 0, []),
                    new Arm("thirtyTwoBit", 1,
                    [
                        new Field
                        {
                            Id = "valueLength32",
                            Pattern = new Pattern.Scalar(4, BigEndian: true),
                            Value = Expr.Parse("fields.valueContent.extent"),
                        },
                    ]),
                ]),
            },

            new Field
            {
                Id = "valueContent",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(
                    "let lvt = fields.valueTag.value band 0x07 in "
                  + "let declared = fields.valueLength.value in "
                  + "lvt < 5 ? lvt "
                  + ": (lvt > 5 ? 0 "
                  + ": (declared == 254 ? fields.valueLength16.value "
                  + ": (declared == 255 ? fields.valueLength32.value : declared)))")),
                Value = Expr.Parse("item.valueContent"),
            },
        ]),
    };

    private static readonly Field Values = new()
    {
        Id = "propertyValues",
        Value = Expr.Parse("inputs.propertyValues"),

        // Depth as data. Before each tag: is there room, and if we are back at the top level, is the next
        // octet the closing tag that ends us? An opening tag deepens the count, a closing one lifts it, so
        // a construct nested four deep terminates on its own closing tag and not on the first one it meets.
        Pattern = new Pattern.Chain(TaggedValue,
            Continues: Expr.Parse("room > 0 && (carried > 0 || (peek band 0x0f) != 0x0f)"),
            Seed: Expr.Parse("0"),
            Carry: Expr.Parse(
                "let lvt = fields.valueTag.value band 0x0f in "
              + "carried + (lvt == 0x0e ? 1 : (lvt == 0x0f ? -1 : 0))")),
    };

    // ── The document ──────────────────────────────────────────────────────────

    private static Field Npdu() => new()
    {
        Id = "npdu",
        Pattern = new Pattern.Group(
        [
            Fixed("npduVersion", 0x01),

            new Field
            {
                Id = "npduControl",
                Pattern = new Pattern.Bits(
                [
                    new("networkMessage", 1), new("controlReservedSix", 1), new("hasDestination", 1),
                    new("controlReservedFour", 1), new("hasSource", 1), new("expectingReply", 1),
                    new("priority", 2),
                ]),
                Value = Expr.Parse("inputs.npduControl"),
            },

            // Three sections whose presence is one bit each. An arm with no fields is the honest spelling
            // of "and otherwise nothing is here": the choice still binds a value, so a decode says
            // `destination = "none"` rather than leaving a hole a reader has to interpret.
            new Field
            {
                Id = "destination",
                Pattern = new Pattern.Choice(Expr.Parse("fields.npduControl.value.hasDestination"),
                [
                    new Arm("none", 0, []),
                    new Arm("routed", 1,
                    [
                        new Field { Id = "destinationNet", Pattern = U16, Value = Expr.Parse("inputs.destinationNet") },
                        new Field { Id = "destinationLength", Pattern = U8, Value = Expr.Parse("fields.destinationAddress.extent") },
                        new Field
                        {
                            Id = "destinationAddress",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.destinationLength.value")),
                            Value = Expr.Parse("inputs.destinationAddress"),
                        },
                    ]),
                ]),
            },

            new Field
            {
                Id = "source",
                Pattern = new Pattern.Choice(Expr.Parse("fields.npduControl.value.hasSource"),
                [
                    new Arm("none", 0, []),
                    new Arm("relayed", 1,
                    [
                        new Field { Id = "sourceNet", Pattern = U16, Value = Expr.Parse("inputs.sourceNet") },
                        new Field { Id = "sourceLength", Pattern = U8, Value = Expr.Parse("fields.sourceAddress.extent") },
                        new Field
                        {
                            Id = "sourceAddress",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.sourceLength.value")),
                            Value = Expr.Parse("inputs.sourceAddress"),
                        },
                    ]),
                ]),
            },

            new Field
            {
                Id = "hops",
                Pattern = new Pattern.Choice(Expr.Parse("fields.npduControl.value.hasDestination"),
                [
                    new Arm("local", 0, []),
                    new Arm("counted", 1, [new Field { Id = "hopCount", Pattern = U8, Value = Expr.Parse("inputs.hopCount") }]),
                ]),
            },

            Apdu(),
        ]),
    };

    private static Field Apdu() => new()
    {
        Id = "apdu",
        Pattern = new Pattern.Group(
        [
            // Four bits of type and four of flags whose meaning the type decides. The engine splits the
            // octet; which of the four low bits is "segmented" in a Confirmed-Request and reserved in an
            // Error is per-packing interpretation, so it is read where the packing is known.
            new Field
            {
                Id = "apduType",
                Pattern = new Pattern.Bits([new("pduType", 4), new("pduFlags", 4)]),
                Value = Expr.Parse("inputs.apduType"),
            },

            new Field
            {
                Id = "pdu",
                Pattern = new Pattern.Choice(Expr.Parse("fields.apduType.value.pduType"),
                [
                    new Arm("confirmedRequest", 0,
                    [
                        new Field
                        {
                            Id = "limits",
                            Pattern = new Pattern.Bits(
                            [
                                new("limitsReserved", 1), new("maxSegmentsAccepted", 3),
                                new("maxApduAccepted", 4),
                            ]),
                            Value = Expr.Parse("inputs.limits"),
                        },
                        new Field { Id = "invokeId", Pattern = U8, Value = Expr.Parse("inputs.invokeId") },

                        // Two octets that exist because one bit of an octet already read is set — and
                        // everything after them changes meaning with it. A segment's parameters are a
                        // slice of a buffer that appears on no wire, so they are octets here for the same
                        // reason the acknowledgement's are. Reading tags out of one and reading them out
                        // of a whole request were two different treatments of the same situation, which
                        // is the sort of thing only a reader working from the standard notices.
                        new Field
                        {
                            Id = "requestSegmenting",
                            Pattern = new Pattern.Choice(Expr.Parse("fields.apduType.value.pduFlags band 0x08"),
                            [
                                new Arm("whole", 0,
                                [
                                    new Field { Id = "serviceChoice", Pattern = U8, Value = Expr.Parse("inputs.serviceChoice") },

                                    new Field
                                    {
                                        Id = "serviceRequest",
                                        Pattern = new Pattern.Choice(Expr.Parse("fields.serviceChoice.value"),
                                        [
                                            new Arm("readProperty", 12,
                                                [.. ObjectAndProperty("request", "inputs.requestObjectId", "inputs.requestPropertyId")]),

                                            new Arm("otherService", null,
                                            [
                                                new Field
                                                {
                                                    Id = "serviceParameters",
                                                    Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                                    Value = Expr.Parse("inputs.serviceParameters"),
                                                },
                                            ]),
                                        ]),
                                    },
                                ]),

                                new Arm("partial", 8,
                                [
                                    new Field { Id = "requestSequence", Pattern = U8, Value = Expr.Parse("inputs.requestSequence") },
                                    new Field { Id = "requestWindow", Pattern = U8, Value = Expr.Parse("inputs.requestWindow") },
                                    new Field { Id = "requestSegmentService", Pattern = U8, Value = Expr.Parse("inputs.requestSegmentService") },
                                    new Field
                                    {
                                        Id = "requestSegmentPayload",
                                        Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                        Value = Expr.Parse("inputs.requestSegmentPayload"),
                                    },
                                ]),
                            ]),
                        },
                    ]),

                    new Arm("complexAck", 3,
                    [
                        new Field { Id = "ackInvokeId", Pattern = U8, Value = Expr.Parse("inputs.ackInvokeId") },

                        new Field
                        {
                            Id = "ackSegmenting",
                            Pattern = new Pattern.Choice(Expr.Parse("fields.apduType.value.pduFlags band 0x08"),
                            [
                                new Arm("whole", 0,
                                [
                                    new Field { Id = "ackServiceChoice", Pattern = U8, Value = Expr.Parse("inputs.ackServiceChoice") },

                                    new Field
                                    {
                                        Id = "ackContent",
                                        Pattern = new Pattern.Choice(Expr.Parse("fields.ackServiceChoice.value"),
                                        [
                                            new Arm("readProperty", 12,
                                            [
                                                .. ObjectAndProperty("ack", "inputs.ackObjectId", "inputs.ackPropertyId"),

                                                Fixed("openingTag", 0x3e),
                                                Values,
                                                Fixed("closingTag", 0x3f),
                                            ]),

                                            new Arm("otherResult", null,
                                            [
                                                new Field
                                                {
                                                    Id = "ackParameters",
                                                    Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                                    Value = Expr.Parse("inputs.ackParameters"),
                                                },
                                            ]),
                                        ]),
                                    },
                                ]),

                                // A segment's payload is opaque and that is not a shortcoming: the octets
                                // this datagram carries are a slice of a buffer that appears on no wire,
                                // and a tag cut in half by the segment boundary is not a tag yet.
                                new Arm("partial", 8,
                                [
                                    new Field { Id = "ackSequence", Pattern = U8, Value = Expr.Parse("inputs.ackSequence") },
                                    new Field { Id = "ackWindow", Pattern = U8, Value = Expr.Parse("inputs.ackWindow") },
                                    new Field { Id = "segmentServiceChoice", Pattern = U8, Value = Expr.Parse("inputs.segmentServiceChoice") },
                                    new Field
                                    {
                                        Id = "segmentPayload",
                                        Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                        Value = Expr.Parse("inputs.segmentPayload"),
                                    },
                                ]),
                            ]),
                        },
                    ]),

                    new Arm("segmentAck", 4,
                    [
                        new Field { Id = "ackedInvokeId", Pattern = U8, Value = Expr.Parse("inputs.ackedInvokeId") },
                        new Field { Id = "ackedSequence", Pattern = U8, Value = Expr.Parse("inputs.ackedSequence") },
                        new Field { Id = "grantedWindow", Pattern = U8, Value = Expr.Parse("inputs.grantedWindow") },
                    ]),

                    new Arm("error", 5,
                    [
                        new Field { Id = "errorInvokeId", Pattern = U8, Value = Expr.Parse("inputs.errorInvokeId") },
                        new Field { Id = "errorServiceChoice", Pattern = U8, Value = Expr.Parse("inputs.errorServiceChoice") },

                        .. Enumerated("errorClass", "inputs.errorClass"),
                        .. Enumerated("errorCode", "inputs.errorCode"),
                    ]),

                    // Sixteen PDU types exist and this document knows four. The exhaustiveness check makes
                    // saying so compulsory, which is the difference between a message this document cannot
                    // read and one it silently reads as nothing.
                    new Arm("otherPdu", null,
                    [
                        new Field
                        {
                            Id = "apduPayload",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                            Value = Expr.Parse("inputs.apduPayload"),
                        },
                    ]),
                ]),
            },
        ]),
    };

    internal static MessageDef Definition()
    {
        var npdu = Npdu();

        return new MessageDef
        {
            Id = "bacnetIp",
            Context = Context.Given.These(
                "bvlcFunction", "npduControl", "apduType", "limits", "invokeId", "serviceChoice",
                "requestObjectId", "requestPropertyId", "requestSequence", "requestWindow",
                "requestArrayIndex", "requestHasArrayIndex", "ackArrayIndex", "ackHasArrayIndex",
                "serviceParameters", "ackInvokeId", "ackServiceChoice", "ackObjectId", "ackPropertyId",
                "propertyValues", "ackParameters", "ackSequence", "ackWindow", "segmentServiceChoice",
                "segmentPayload", "requestSegmentService", "requestSegmentPayload",
                "ackedInvokeId", "ackedSequence", "grantedWindow", "errorInvokeId",
                "errorServiceChoice", "errorClass", "errorCode", "apduPayload", "bvlcPayload",
                "destinationNet", "destinationAddress", "sourceNet", "sourceAddress", "hopCount"),

            Fields =
            [
                new Field
                {
                    Id = "bvll",
                    Pattern = new Pattern.Group(
                    [
                        Fixed("bvlcType", 0x81),
                        new Field { Id = "bvlcFunction", Pattern = U8, Value = Expr.Parse("inputs.bvlcFunction") },

                        // The length of the span it is inside, itself included. Resolvable without a pass
                        // or a placeholder because its own extent is two octets whatever its value is: the
                        // region settles from static widths, then the value settles from the region.
                        new Field
                        {
                            Id = "bvlcLength",
                            Pattern = U16,
                            Value = Expr.Parse("fields.bvll.extent"),
                        },

                        new Field
                        {
                            Id = "bvlcBody",

                            // And on the way in the same octets are a boundary: everything below parses
                            // inside a region four octets shorter than what the length declared, so `room`
                            // means what a BACnet decoder needs it to mean and a router that lies about
                            // the length is refused rather than believed.
                            Pattern = new Pattern.Group(
                            [
                                new Field
                                {
                                    Id = "carrier",
                                    Pattern = new Pattern.Choice(Expr.Parse("fields.bvlcFunction.value"),
                                    [
                                        new Arm("originalUnicast", 0x0a, [npdu]),

                                        // The other nine BVLC functions are more arms of this choice, and
                                        // three of them carry an NPDU after a prefix of their own —
                                        // written out again under their own ids, deliberately. A shared,
                                        // reusable subtree would save typing and lose the distinction:
                                        // an NPDU that arrived Forwarded carries the originating BBMD's
                                        // address, must not be re-forwarded, and handles SNET/SADR and
                                        // hop count differently from a direct one. A rule about that case
                                        // has to be able to point at that case. Keeping them apart under
                                        // one shape means qualifying every id by the arm it sits in,
                                        // which is the concatenated id `Occurrence` exists to delete.
                                        new Arm("otherFunction", null,
                                        [
                                            new Field
                                            {
                                                Id = "bvlcPayload",
                                                Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                                Value = Expr.Parse("inputs.bvlcPayload"),
                                            },
                                        ]),
                                    ]),
                                },
                            ],
                            Extent: Expr.Parse("fields.bvlcLength.value - 4")),
                        },
                    ]),
                },
            ],

            Rules =
            [
                new Rule.Domain
                {
                    Field = npdu.Pattern.Nested[1],
                    Run = "networkMessage",
                    Allowed = [ValueRange.Exactly(0)],
                    Because = "a network-layer message carries no APDU at all — it is a different body "
                            + "under the same NPDU header, and this document describes only the "
                            + "application one.",
                },

                new Rule.Invariant
                {
                    Within = TaggedValue,
                    Must = Expr.Parse("(fields.valueTag.value band 0x0e) != 0x0e "
                                    + "|| fields.valueContent.extent == 0"),
                    Because = "an opening or closing tag marks a boundary and carries nothing — its low "
                            + "bits are the marker, not a width, so a value read against them is octets "
                            + "belonging to something else.",
                },
            ],
        };
    }

    private static byte[] Capture(int index) => ProtocolCorpus.Get("bacnet").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void A_thirty_two_bit_identifier_splits_into_two_runs_aligned_with_no_octet()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual("confirmedRequest", decoded["pdu"].AsText());
        Assert.AreEqual("readProperty", decoded["serviceRequest"].AsText());

        var objectId = (ProtoValue.Rec)decoded["requestObjectId"];
        Assert.AreEqual(0, objectId.Members["requestObjectType"].AsInt(), "analog-input, in the top ten bits");
        Assert.AreEqual(1, objectId.Members["requestInstance"].AsInt(), "instance, in the low twenty-two");

        Assert.AreEqual(77, decoded["requestPropertyId"].AsInt(), "object-name, in one octet because it fits");
    }

    [TestMethod]
    public void The_length_measures_the_span_it_is_inside()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(17, decoded["bvlcLength"].AsInt(), "the whole datagram, its own two octets included");
        Assert.AreEqual(17, Capture(0).Length);
    }

    [TestMethod]
    public void Constructed_data_is_a_run_of_tags_that_ends_where_its_depth_returns_to_zero()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual("complexAck", decoded["pdu"].AsText());
        Assert.AreEqual("whole", decoded["ackSegmenting"].AsText());

        var values = decoded["propertyValues"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(1, values.Count, "one value between the opening and closing tags");

        // Tag 7 is a character string, and its twelve octets did not fit in the tag's three low bits, so
        // the width escaped to the octet after it.
        Assert.AreEqual(0x75, values[0].Members["valueTag"].AsInt());
        Assert.AreEqual(12, values[0].Members["valueLength"].AsInt());

        // That the first of the twelve is a character set and the other eleven are text is meaning. The
        // wire has twelve octets, and reading them as a name is something a later step does.
        var content = values[0].Members["valueContent"].AsBytes();
        Assert.AreEqual(12, content.Length);
        Assert.AreEqual(0x00, content[0]);
        Assert.AreEqual("Zone 3 Temp", System.Text.Encoding.ASCII.GetString(content, 1, content.Length - 1));
    }

    [TestMethod]
    public void A_segment_carries_its_headers_and_an_opaque_remainder()
    {
        // The 0xc4 at offset 34 declares four value octets and only two of them are in this datagram. The
        // rest is in the next one, so there is nothing here to parse — which the document says by taking
        // the payload as octets rather than by failing partway through a tag.
        var decoded = new MessageCodec(Definition()).Decode(Capture(2));

        Assert.AreEqual("partial", decoded["ackSegmenting"].AsText());
        Assert.AreEqual(0, decoded["ackSequence"].AsInt(), "segment zero");
        Assert.AreEqual(1, decoded["ackWindow"].AsInt(), "of a window of one");
        Assert.AreEqual(26, decoded["segmentPayload"].AsBytes().Length);
    }

    [TestMethod]
    public void The_short_pdus_take_their_own_packings_of_the_same_document()
    {
        var codec = new MessageCodec(Definition());

        var acked = codec.Decode(Capture(3));
        Assert.AreEqual("segmentAck", acked["pdu"].AsText());
        Assert.AreEqual(2, acked["ackedInvokeId"].AsInt());
        Assert.AreEqual(1, acked["grantedWindow"].AsInt(), "granted down to what was proposed");

        var failed = codec.Decode(Capture(4));
        Assert.AreEqual("error", failed["pdu"].AsText());
        Assert.AreEqual(2, failed["errorClass"].AsInt(), "property");
        Assert.AreEqual(32, failed["errorCode"].AsInt(), "unknown-property");
    }

    [TestMethod]
    public void All_five_captures_re_encode_to_the_exact_original_octets()
    {
        // Every tag octet, every length and the BVLC frame are withheld. The tag octets are the
        // interesting ones: their low three bits are the width of octets that have not been written yet.
        foreach (int index in (int[])[0, 1, 2, 3, 4])
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
    public void A_wider_property_identifier_widens_the_tag_octet_that_counts_it()
    {
        // The whole of gap 4 in one assertion: nothing in the inputs mentions a width, and asking for a
        // property identifier that needs two octets moves the tag octet from 0x19 to 0x1a and the BVLC
        // length from 17 to 18.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(0)));

        inputs.Set("inputs", With(inputs.Root("inputs"), ("requestPropertyId", ProtoValue.Of(512L))));

        var wider = codec.Encode(inputs);
        var decoded = codec.Decode(wider);

        Assert.AreEqual(Capture(0).Length + 1, wider.Length);
        Assert.AreEqual(18, decoded["bvlcLength"].AsInt());
        Assert.AreEqual(0x1a, decoded["requestPropertyIdTag"].AsInt(), "tag number 1, context class, two octets");
        Assert.AreEqual(512, decoded["requestPropertyId"].AsInt());
    }

    [TestMethod]
    public void A_nested_construct_ends_on_its_own_closing_tag_and_not_the_first_one_it_meets()
    {
        // The corpus never nests, so the claim that depth-as-data handles arbitrary nesting has to be
        // shown rather than asserted. This run opens a construct, puts an enumerated value inside it and
        // closes it — and the run keeps going past that inner closing tag, because the depth it returns to
        // is one, not zero. It stops at the outer 0x3f the document reads separately.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(1)));

        inputs.Set("inputs", With(inputs.Root("inputs"), ("propertyValues", new ProtoValue.List(
        [
            Value(0x0e, []),          // opening tag, context 0
            Value(0x91, [0x05]),      // an enumerated, inside it
            Value(0x0f, []),          // closing tag, back to depth zero
        ]))));

        var nested = codec.Encode(inputs);
        var decoded = codec.Decode(nested);

        Assert.AreEqual(22, nested.Length, "32 octets, less the 14-octet string, plus these four");
        Assert.AreEqual(22, decoded["bvlcLength"].AsInt(), "and the frame followed it");

        var values = decoded["propertyValues"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(3, values.Count, "three tags, not one — the inner close did not end the run");

        CollectionAssert.AreEqual(new long[] { 0x0e, 0x91, 0x0f },
            values.Select(v => v.Members["valueTag"].AsInt()).ToArray());

        Assert.AreEqual(0x3f, nested[^1], "and the outer closing tag is still the last octet");
    }

    [TestMethod]
    public void A_value_too_long_for_the_length_octet_escapes_to_a_wider_one()
    {
        // No capture in the corpus carries 254 octets or more, so the escape was modelled from the
        // standard and had to be shown rather than assumed — the same reason the nesting case exists.
        // Under the first version of this document the 0xfe marker was read as a literal length of 254
        // and everything after it desynchronised, which round-tripped all five captures perfectly.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(1)));

        var long300 = new byte[300];
        for (int i = 0; i < long300.Length; i++) long300[i] = (byte)i;

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("propertyValues", new ProtoValue.List([Value(0x75, long300)]))));

        var encoded = codec.Encode(inputs);

        // 32 octets, less the 14 the short string occupied, plus 1 tag + 1 marker + 2 length + 300.
        Assert.AreEqual(322, encoded.Length);
        Assert.AreEqual(322, codec.Decode(encoded)["bvlcLength"].AsInt());

        int tag = encoded.Length - 1 - 304;
        Assert.AreEqual(0x75, encoded[tag], "character string, and the width did not fit in the tag");
        Assert.AreEqual(0xfe, encoded[tag + 1], "so the length octet is the marker for a wider one");
        Assert.AreEqual(300, (encoded[tag + 2] << 8) | encoded[tag + 3]);

        var decoded = codec.Decode(encoded);
        var values = decoded["propertyValues"].AsList().Cast<ProtoValue.Rec>().ToList();
        CollectionAssert.AreEqual(long300, values[0].Members["valueContent"].AsBytes());

        // The third tier — marker 255 and a 32-bit length — is modelled and unreachable over this
        // transport: the BVLC length is two octets, so a datagram can never hold 65536 octets of value.
        // Declared anyway, because what makes it unreachable is the framing rather than the tag.
    }

    [TestMethod]
    public void Reading_one_array_element_adds_a_parameter_the_wire_never_announces()
    {
        // ReadProperty's third parameter is optional and no capture in the corpus carries it. Its presence
        // is readable on the way in — the next tag number is 2 — and unreadable on the way out, where
        // there is no next octet yet. One discriminator, two readings.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(0)));

        Assert.AreEqual("wholeProperty", codec.Decode(Capture(0))["requestElement"].AsText());

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("requestHasArrayIndex", ProtoValue.Of(1L)),
            ("requestArrayIndex", ProtoValue.Of(3L))));

        var element = codec.Encode(inputs);
        var decoded = codec.Decode(element);

        Assert.AreEqual(Capture(0).Length + 2, element.Length);
        Assert.AreEqual(19, decoded["bvlcLength"].AsInt(), "and the frame followed it");

        Assert.AreEqual("oneElement", decoded["requestElement"].AsText());
        Assert.AreEqual(0x29, element[^2], "context tag 2, one value octet");
        Assert.AreEqual(3, decoded["requestArrayIndex"].AsInt());

        // And the acknowledgement is unaffected: its next tag is the opening 0x3e, which is not a 2.
        Assert.AreEqual("wholeProperty", codec.Decode(Capture(1))["ackElement"].AsText());
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_frame_that_lies_about_its_length_is_refused_rather_than_parsed()
    {
        // Real routers emit wrong BVLC lengths. Under an unbounded parse the datagram reads as something
        // plausible; under a bounded region the declaration and the data disagree and that is the answer.
        var tampered = (byte[])[.. Capture(0)];
        tampered[3] = 0x0f;      // two octets short of the truth

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "remain in the enclosing region");
    }

    [TestMethod]
    public void A_network_layer_message_is_refused_by_name_rather_than_read_as_an_apdu()
    {
        // Bit 7 of the control octet says the body is not an APDU. Without the rule the walk reads a
        // network-layer message as a Confirmed-Request and reports nothing at all.
        var tampered = (byte[])[.. Capture(0)];
        tampered[5] = 0x84;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "carries no APDU");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        // Everything the document derives is withheld: the frame, the fixed octets, and every tag octet
        // whose low bits are a width.
        foreach (var (name, value) in decoded.Captures)
            if (name is not ("bvlcType" or "bvlcLength" or "npduVersion" or "openingTag" or "closingTag")
                && !name.EndsWith("Tag", StringComparison.Ordinal)
                && !name.EndsWith("Length", StringComparison.Ordinal))
                inputs[name] = value;

        // Whether an array index was there. The wire says so with a tag number; the caller has to say so
        // with a value, because a writer has no wire to look at yet.
        foreach (var prefix in (string[])["request", "ack"])
        {
            var element = decoded[$"{prefix}Element"];

            inputs[$"{prefix}HasArrayIndex"] = ProtoValue.Of(
                !element.IsNull && element.AsText() == "oneElement" ? 1L : 0L);
        }

        if (decoded["propertyValues"].IsNull) return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));

        inputs["propertyValues"] = new ProtoValue.List(
        [
            .. decoded["propertyValues"].AsList().Cast<ProtoValue.Rec>().Select(v => EvalScope.Record(
                ("valueTag", v.Members["valueTag"]),
                ("valueContent", v.Members["valueContent"]))),
        ]);

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Value(long tag, byte[] content) => EvalScope.Record(
        ("valueTag", ProtoValue.Of(tag)),
        ("valueContent", ProtoValue.Of(content)));

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
