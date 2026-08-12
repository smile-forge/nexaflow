using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A value computed over octets rather than over other values.
///
/// <para>
/// The protocol is TCP, and it is the case the corpus had nothing for. Every other relationship in this
/// engine is between a field and a <i>value</i> — a length reads an extent, a discriminator reads a
/// number. A checksum reads the octets themselves, and until <c>Emitted</c> was produced there was no
/// facet to read.
/// </para>
///
/// <para>
/// Nothing else was needed. It is an ordinary edge to an ordinary node asking a third facet, and the two
/// things that made TCP look like it wanted special machinery both turned out to be parts declared beside
/// the message. The pseudo-header is a shape that appears on no wire, so it is declared and not written.
/// The checksum covers the field the checksum sits in, computed with it zeroed — so what the sum covers is
/// a zero declared beside the message, and the document says that rather than the engine knowing a
/// convention about it.
/// </para>
///
/// <para>
/// The capture is the worked example from RFC 1071 §3, whose sum is stated there — so the number this
/// arrives at was not produced by the code that checks it.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class SpanDigestCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);
    private static Pattern U32 => new Pattern.Scalar(4, BigEndian: true);

    private static Field Fixed(string id, string expression) => new()
    {
        Id = id, Pattern = U16, Value = Expr.Parse(expression),
    };

    /// <summary>
    /// What the sum is taken over: a shape assembled from the layer below, then the header with its
    /// checksum standing at zero, then the payload.
    ///
    /// <para>
    /// Every one of these is a node with an id and an ordinary requirement edge into it. There is no list
    /// of covered fields kept anywhere, which matters more than it sounds: coverage that lives beside the
    /// structure is coverage that can disagree with it, and getting a checksum's span wrong is the classic
    /// way one is wrong while looking right.
    /// </para>
    /// </summary>
    private static Field[] Aside() =>
    [
        new Field
        {
            Id = "pseudoHeader",
            Pattern = new Pattern.Group(
            [
                new Field { Id = "pseudoSource", Pattern = new Pattern.Opaque(4),
                            Value = Expr.Parse("inputs.source"), Via = "ipv4" },
                new Field { Id = "pseudoDestination", Pattern = new Pattern.Opaque(4),
                            Value = Expr.Parse("inputs.destination"), Via = "ipv4" },
                new Field { Id = "pseudoZero", Pattern = U8, Value = Expr.Parse("0") },
                new Field { Id = "pseudoProtocol", Pattern = U8, Value = Expr.Parse("6") },

                // The one part of it that is a fact about this message rather than about the layer below.
                new Field
                {
                    Id = "pseudoLength",
                    Pattern = U16,
                    Value = Expr.Parse("fields.header.extent + fields.payload.extent"),
                },
            ]),
        },

        // What stands where the checksum sits while the sum that produces it is being taken. Zero is a
        // convention of this protocol, not a law, so this is the document's word and not the engine's.
        Fixed("checksumStandsAt", "0"),
    ];

    private static MessageDef Segment() => new()
    {
        Id = "segment",
        Context = Context.Given.These("source", "destination", "sourcePort", "destinationPort",
                                      "sequence", "offsetAndFlags", "payload"),
        Apart = Aside(),
        Fields =
        [
            new Field
            {
                Id = "header",
                Pattern = new Pattern.Group(
                [
                    // Split around the checksum, because that is what the sum covers: everything before
                    // it, a zero in its place, everything after. The structure says so rather than a list
                    // of field names kept somewhere that can drift from it.
                    new Field
                    {
                        Id = "beforeChecksum",
                        Pattern = new Pattern.Group(
                        [
                            Fixed("sourcePort", "inputs.sourcePort"),
                            Fixed("destinationPort", "inputs.destinationPort"),
                            new Field { Id = "sequence", Pattern = U32, Value = Expr.Parse("inputs.sequence") },
                            new Field { Id = "acknowledgement", Pattern = U32, Value = Expr.Parse("0") },
                            new Field
                            {
                                Id = "offsetAndFlags",
                                Pattern = new Pattern.Bits(
                                    [new BitSlice("dataOffset", 4), new BitSlice("reserved", 4),
                                     new BitSlice("flags", 8)]),
                                Value = Expr.Parse("inputs.offsetAndFlags"),
                            },
                            Fixed("window", "0x0200"),
                        ]),
                    },

                    // The edge that could not be drawn. It reads the octets of four nodes, two of which
                    // sit in the region this field is inside — and it is not a cycle, because what the sum
                    // covers in this field's own place is the zero declared beside the message.
                    new Field
                    {
                        Id = "checksum",
                        Pattern = U16,
                        Value = Expr.Parse(
                            "fields.pseudoHeader.octets |> concat(fields.beforeChecksum.octets, "
                          + "fields.checksumStandsAt.octets, fields.afterChecksum.octets, "
                          + "fields.payload.octets) |> onesComplementSum()"),
                    },

                    new Field
                    {
                        Id = "afterChecksum",
                        Pattern = new Pattern.Group([Fixed("urgent", "0")]),
                    },
                ]),
            },

            new Field
            {
                Id = "payload",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                Value = Expr.Parse("inputs.payload"),
                Via = "unascii",
            },
        ],
    };

    private static EvalScope Inputs() => new EvalScope().Set("inputs", EvalScope.Record(
        ("source", ProtoValue.Of("10.0.0.1")),
        ("destination", ProtoValue.Of("10.0.0.2")),
        ("sourcePort", ProtoValue.Of(20480L)),
        ("destinationPort", ProtoValue.Of(80L)),
        ("sequence", ProtoValue.Of(1L)),
        ("offsetAndFlags", EvalScope.Record(("dataOffset", ProtoValue.Of(5L)),
                                            ("reserved", ProtoValue.Of(0L)),
                                            ("flags", ProtoValue.Of(0x18L)))),
        ("payload", ProtoValue.Of("hi"))));

    [TestMethod]
    public void A_digest_can_be_taken_over_the_octets_of_another_node()
    {
        var issues = new MessageCodec(Segment()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void The_sum_covers_what_the_structure_says_it_covers()
    {
        var octets = new MessageCodec(Segment()).Encode(Inputs());

        Assert.AreEqual(22, octets.Length, "twenty octets of header and two of payload");

        // Recomputed here from the octets that were produced, the same way a peer would: the sum over the
        // pseudo-header, the header with the checksum zeroed, and the payload. A correct one's-complement
        // checksum makes the whole covered span sum to zero, which is the property the whole scheme rests
        // on and is not something the engine could have arranged by accident.
        byte[] pseudo =
        [
            10, 0, 0, 1, 10, 0, 0, 2, 0, 6,
            (byte)((octets.Length >> 8) & 0xFF), (byte)(octets.Length & 0xFF),
        ];

        var zeroed = (byte[])octets.Clone();
        zeroed[16] = 0;
        zeroed[17] = 0;

        Assert.AreEqual((octets[16] << 8) | octets[17], Sum([.. pseudo, .. zeroed]),
            "what the engine wrote is the sum over the span with the checksum standing at zero");

        // And the property the whole scheme rests on: summed again with the real checksum in place, the
        // covered span comes to zero. That is the peer's check, and it is not something the engine could
        // have arranged by accident from the wrong span.
        Assert.AreEqual(0, Sum([.. pseudo, .. octets]));

        Assert.AreNotEqual(0, (octets[16] << 8) | octets[17], "and it is not simply absent");
    }

    [TestMethod]
    public void What_it_covers_is_visible_in_the_graph_rather_than_kept_beside_it()
    {
        // Coverage that lives in a list somewhere is coverage that can disagree with the structure, and a
        // checksum whose span is subtly wrong is the classic way one is wrong while looking right. Here it
        // is four edges, and asking what the sum covers is asking the graph.
        var segment = Segment();
        var checksum = segment.AllFields.Single(f => f.Id == "checksum");

        CollectionAssert.AreEquivalent(
            new[] { "pseudoHeader", "beforeChecksum", "checksumStandsAt", "afterChecksum", "payload" },
            segment.Graph.From<Reads>(checksum).Where(r => r.Facet == "octets")
                   .Select(r => r.To.Name).ToArray());
    }

    [TestMethod]
    public void A_part_declared_beside_the_message_is_never_written()
    {
        var octets = new MessageCodec(Segment()).Encode(Inputs());

        // Twelve octets of pseudo-header and two of stand-in went into the sum and none onto the wire. A
        // segment carrying its own pseudo-header would be a different protocol.
        Assert.AreEqual(22, octets.Length);

        var segment = Segment();
        Assert.AreEqual(2, segment.Apart.Count);
        Assert.AreEqual(0, segment.Graph.From<Contains>(segment.Root)
                                        .Count(e => e.To.Name is "pseudoHeader" or "checksumStandsAt"));
    }

    [TestMethod]
    public void A_part_beside_the_message_that_reads_a_later_one_is_refused()
    {
        // They are worked out in the order they are written, so one reading a later one reads nothing and
        // every comparison against it goes quietly false. The fix is to move a line, and the diagnostic
        // says which.
        var backwards = Segment() with
        {
            Id = "backwards",
            Apart = [Fixed("first", "fields.second.value"), Fixed("second", "1"), .. Aside()],
        };

        Assert.IsTrue(new MessageCodec(backwards).Validate()
            .Any(i => i.Contains("'first'") && i.Contains("worked out after it")),
            string.Join("\n", new MessageCodec(backwards).Validate()));
    }

    /// <summary>The one's-complement sum, written out here so the assertion does not lean on the converter
    /// it is checking.</summary>
    private static int Sum(byte[] octets)
    {
        int total = 0;

        for (int i = 0; i < octets.Length; i += 2)
            total += (octets[i] << 8) | (i + 1 < octets.Length ? octets[i + 1] : 0);

        while ((total >> 16) != 0) total = (total & 0xFFFF) + (total >> 16);

        return (~total) & 0xFFFF;
    }
}
