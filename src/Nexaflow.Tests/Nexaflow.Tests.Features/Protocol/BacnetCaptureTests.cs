using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// BACnet/IP, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// Three layers in one datagram, and a discriminator six octets and a nibble in — so this is the longest
/// look-ahead path here, and the reason <c>identifies</c> is a path rather than a name.
/// </para>
/// <para>
/// And the tag encoding ASHRAE 135 §20.2 is built from: four bits of tag number, one of class, three of
/// length — so a value's octet count is a bit-field of the octet <i>before</i> it. Going out that is a
/// length measuring a span not yet laid down; coming in it is a width read a moment earlier. One
/// description, two directions, and neither of them a special case.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class BacnetCaptureTests
{
    private const int Request = 0, Ack = 1, Segment = 2, SegmentAck = 3, Error = 4;

    private static ProtocolFile.Loaded Bacnet() => Definitions.Load("bacnet");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("bacnet").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Bacnet().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static long Number(RunGraph run, string field) => Maybe(run, field)!.Value.AsInt();

    /// <summary>
    /// A BACnet Unsigned, read back as a number.
    /// </summary>
    /// <remarks>
    /// In the host rather than in the description, and deliberately: §20.2.4 spends the fewest octets that
    /// hold the value, and turning those octets back into one is <c>minuint</c> — which takes an argument
    /// saying what a zero comes to, and a field has nowhere for an argument to come from. So the field
    /// says what the octets are and this says what they mean, the same split CoAP's option numbers take.
    /// </remarks>
    private static long Unsigned(RunGraph run, string field)
        => Maybe(run, field)!.Value.AsBytes().Aggregate(0L, (held, octet) => (held << 8) | octet);

    // ── C1: the request ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_read_property_request_reads_as_the_breakdown_says()
    {
        var run = Read(Request);

        Assert.AreEqual(0x81, Number(run, "bvlcType.cr"), "BACnet over IP");
        Assert.AreEqual(0x0a, Number(run, "bvlcFunction.cr"), "Original-Unicast-NPDU");
        Assert.AreEqual(17, Number(run, "bvlcLength.cr"), "and the datagram is seventeen octets");

        Assert.AreEqual(1, Number(run, "npduVersion.cr"));
        Assert.AreEqual(1, Number(run, "npduExpectingReply.cr"), "it is a request");
        Assert.AreEqual(0, Number(run, "npduPriority.cr"), "Normal");

        Assert.AreEqual(1, Number(run, "cr.segmentedResponseAccepted"));
        Assert.AreEqual(2, Number(run, "cr.maxSegments"), "the code for four segments");
        Assert.AreEqual(5, Number(run, "cr.maxApdu"), "the code for 1476 octets");
        Assert.AreEqual(1, Number(run, "cr.invokeId"));
        Assert.AreEqual(12, Number(run, "cr.serviceChoice"), "ReadProperty");
    }

    [TestMethod]
    public void And_asks_for_a_property_of_an_object()
    {
        var run = Read(Request);

        Assert.AreEqual(0, Number(run, "cr.objectType"), "Analog Input");
        Assert.AreEqual(1, Number(run, "cr.objectInstance"));

        Assert.AreEqual(77, Unsigned(run, "cr.property"), "object-name");
    }

    [TestMethod]
    public void A_thirty_two_bit_object_identifier_is_two_fields_that_do_not_line_up_with_octets()
    {
        // §20.2.14 is ten bits of type and twenty-two of instance. Read as one integer it is 0x00000001
        // and says nothing; the split is what makes it an object.
        var run = Read(Request);

        Assert.AreEqual(0, Number(run, "cr.objectType"));
        Assert.AreEqual(1, Number(run, "cr.objectInstance"));

        // And written the other way: Device 260 is object type 8, which straddles the first two octets.
        // Read as a thirty-two-bit integer that is 0x02000104 and means nothing at all.
        var octets = new GraphCodec(Bacnet().Graph).Encode(Asking(objectType: 8, instance: 260,
                                                                  property: 76));

        Assert.AreEqual("02000104", Convert.ToHexString(octets[11..15]));
    }

    /// <summary>A ReadProperty request, as a caller would ask for one.</summary>
    private static Dictionary<string, ProtoValue> Asking(long objectType, long instance, long property)
        => new(StringComparer.Ordinal)
        {
            ["PDU Type"] = ProtoValue.Of(0),
            ["BVLC Function"] = ProtoValue.Of(0x0a),
            ["Expecting Reply"] = ProtoValue.Of(1),
            ["Priority"] = ProtoValue.Of(0),
            ["Segmented Response Accepted"] = ProtoValue.Of(1),
            ["Max Segments Accepted"] = ProtoValue.Of(2),
            ["Max APDU Length Accepted"] = ProtoValue.Of(5),
            ["Invoke ID"] = ProtoValue.Of(1),
            ["Service Choice"] = ProtoValue.Of(12),
            ["Object Type"] = ProtoValue.Of(objectType),
            ["Object Instance"] = ProtoValue.Of(instance),
            ["Property Identifier"] = ProtoValue.Of(property),
        };

    // ── C2 and C3: the answer ─────────────────────────────────────────────────

    [TestMethod]
    public void An_answer_reads_its_value_from_between_two_markers()
    {
        var run = Read(Ack);

        Assert.AreEqual(32, Number(run, "bvlcLength.ca"));
        Assert.AreEqual(0, Number(run, "ca.segmented"), "the whole answer, in one datagram");
        Assert.AreEqual(1, Number(run, "ca.invokeId"), "the request it answers");

        Assert.AreEqual(0, Number(run, "ca.objectType"));
        Assert.AreEqual(1, Number(run, "ca.objectInstance"));
        Assert.AreEqual(77, Unsigned(run, "ca.property"), "object-name");

        Assert.AreEqual(6, Number(run, "ca.openTag.lvt"), "an opening marker, carrying no length");
        Assert.AreEqual(7, Number(run, "ca.closeTag.lvt"), "and a closing one");

        Assert.AreEqual("Zone 3 Temp", Maybe(run, "ca.text")!.Value.AsText());
    }

    [TestMethod]
    public void A_value_too_long_for_three_bits_says_so_and_carries_a_length_octet()
    {
        // §20.2.1.3.1. Eleven characters and a character-set octet is twelve, which does not fit in the
        // three bits the tag keeps for it — so the nibble holds the escape and the length follows it.
        var run = Read(Ack);

        Assert.AreEqual(7, Number(run, "ca.valueTag.number"), "Character String");
        Assert.AreEqual(5, Number(run, "ca.valueTag.lvt"), "the escape, not a length of five");
        Assert.AreEqual(12, Number(run, "ca.valueExtLength"),
            "one octet of character set and eleven of text");
    }

    [TestMethod]
    public void A_piece_of_a_segmented_answer_is_octets_and_says_so()
    {
        // C3 stops two octets into a four-octet object identifier. Reading it as tags would bind three
        // plausible values and then run off the end — so it is not read as tags.
        var run = Read(Segment);

        Assert.AreEqual(1, Number(run, "ca.segmented"), "a piece of a longer answer");
        Assert.AreEqual(1, Number(run, "ca.moreFollows"), "and not the last piece");
        Assert.AreEqual(0, Number(run, "ca.sequenceNumber"), "the first");
        Assert.AreEqual(1, Number(run, "ca.proposedWindowSize"));

        Assert.IsNull(Maybe(run, "ca.text"), "nothing here claims to be a value");

        Assert.AreEqual(26, Maybe(run, "ca.segmentPayload")!.Value.AsBytes().Length,
            "the rest of the datagram, entire and uninterpreted");
    }

    [TestMethod]
    public void And_the_two_shapes_do_not_leave_each_other_half_settled()
    {
        // The arm not taken has to say it is not there: something measuring the APDU waits on every
        // member, and a member the walk stepped past would never settle.
        var whole = Read(Ack);
        var piece = Read(Segment);

        Assert.IsNull(Maybe(whole, "ca.segmentPayload"));
        Assert.IsNull(Maybe(piece, "ca.objectType"));

        Assert.IsNull(Maybe(whole, "ca.sequenceNumber"),
            "and the two octets that pace segments are not there when nothing is being paced");
    }

    [TestMethod]
    public void A_tag_carries_its_value_s_length_in_three_of_its_own_bits()
    {
        var run = Read(Request);

        Assert.AreEqual(0, Number(run, "cr.objectTag.number"), "parameter 0");
        Assert.AreEqual(1, Number(run, "cr.objectTag.class"), "context-specific");
        Assert.AreEqual(4, Number(run, "cr.objectTag.lvt"), "four octets of object identifier follow");

        Assert.AreEqual(1, Number(run, "cr.propertyTag.number"), "parameter 1");
        Assert.AreEqual(1, Number(run, "cr.propertyTag.lvt"), "and one octet of property identifier");
    }

    // ── C5: the error ─────────────────────────────────────────────────────────

    [TestMethod]
    public void An_error_reads_as_the_breakdown_says()
    {
        var run = Read(Error);

        Assert.AreEqual(13, Number(run, "bvlcLength.err"));
        Assert.AreEqual(1, Number(run, "err.invokeId"), "the request it answers");
        Assert.AreEqual(12, Number(run, "err.serviceChoice"), "ReadProperty");

        Assert.AreEqual(2, Unsigned(run, "err.class"), "property");
        Assert.AreEqual(32, Unsigned(run, "err.code"), "unknown-property");

        Assert.AreEqual(9, Number(run, "err.classTag.number"), "Enumerated");
        Assert.AreEqual(0, Number(run, "err.classTag.class"), "application — the tag says what it is");
        Assert.AreEqual(9, Number(run, "err.codeTag.number"),
            "the same tag number, because where it sits is what says which of the two it is");
    }

    // ── C4: the segment acknowledgement ───────────────────────────────────────

    [TestMethod]
    public void A_segment_acknowledgement_reads_as_the_breakdown_says()
    {
        var run = Read(SegmentAck);

        Assert.AreEqual(10, Number(run, "bvlcLength.sa"));
        Assert.AreEqual(1, Number(run, "npduExpectingReply.sa"), "more segments are wanted");

        Assert.AreEqual(0, Number(run, "sa.negative"), "a positive acknowledgement");
        Assert.AreEqual(0, Number(run, "sa.fromServer"), "sent by the original requester");
        Assert.AreEqual(2, Number(run, "sa.invokeId"));
        Assert.AreEqual(0, Number(run, "sa.sequenceNumber"), "segment nought arrived");
        Assert.AreEqual(1, Number(run, "sa.actualWindowSize"));
    }

    // ── Back out again ────────────────────────────────────────────────────────

    [TestMethod]
    public void A_request_written_back_is_the_same_octets()
    {
        var bacnet = Bacnet();
        var read = new GraphCodec(bacnet.Graph).Decode(Capture(Request));

        CollectionAssert.AreEqual(Capture(Request), new GraphCodec(bacnet.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["PDU Type"] = ProtoValue.Of(0),
                ["BVLC Function"] = ProtoValue.Of(Number(read, "bvlcFunction.cr")),
                ["Expecting Reply"] = ProtoValue.Of(Number(read, "npduExpectingReply.cr")),
                ["Priority"] = ProtoValue.Of(Number(read, "npduPriority.cr")),
                ["Segmented Response Accepted"] =
                    ProtoValue.Of(Number(read, "cr.segmentedResponseAccepted")),
                ["Max Segments Accepted"] = ProtoValue.Of(Number(read, "cr.maxSegments")),
                ["Max APDU Length Accepted"] = ProtoValue.Of(Number(read, "cr.maxApdu")),
                ["Invoke ID"] = ProtoValue.Of(Number(read, "cr.invokeId")),
                ["Service Choice"] = ProtoValue.Of(Number(read, "cr.serviceChoice")),
                ["Object Type"] = ProtoValue.Of(Number(read, "cr.objectType")),
                ["Object Instance"] = ProtoValue.Of(Number(read, "cr.objectInstance")),
                ["Property Identifier"] = ProtoValue.Of(Unsigned(read, "cr.property")),
            }),
            "a ReadProperty of analog-input 1's object-name did not survive being read and written again");
    }

    [TestMethod]
    public void An_error_written_back_is_the_same_octets()
    {
        var bacnet = Bacnet();
        var read = new GraphCodec(bacnet.Graph).Decode(Capture(Error));

        CollectionAssert.AreEqual(Capture(Error), new GraphCodec(bacnet.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["PDU Type"] = ProtoValue.Of(5),
                ["BVLC Function"] = ProtoValue.Of(Number(read, "bvlcFunction.err")),
                ["Expecting Reply"] = ProtoValue.Of(Number(read, "npduExpectingReply.err")),
                ["Priority"] = ProtoValue.Of(Number(read, "npduPriority.err")),
                ["Invoke ID"] = ProtoValue.Of(Number(read, "err.invokeId")),
                ["Service Choice"] = ProtoValue.Of(Number(read, "err.serviceChoice")),
                ["Error Class"] = ProtoValue.Of(Unsigned(read, "err.class")),
                ["Error Code"] = ProtoValue.Of(Unsigned(read, "err.code")),
            }));
    }

    [TestMethod]
    public void An_answer_written_back_is_the_same_octets()
    {
        var bacnet = Bacnet();
        var read = new GraphCodec(bacnet.Graph).Decode(Capture(Ack));

        CollectionAssert.AreEqual(Capture(Ack), new GraphCodec(bacnet.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["PDU Type"] = ProtoValue.Of(3),
                ["BVLC Function"] = ProtoValue.Of(Number(read, "bvlcFunction.ca")),
                ["Expecting Reply"] = ProtoValue.Of(Number(read, "npduExpectingReply.ca")),
                ["Priority"] = ProtoValue.Of(Number(read, "npduPriority.ca")),
                ["Segmented"] = ProtoValue.Of(Number(read, "ca.segmented")),
                ["More Follows"] = ProtoValue.Of(Number(read, "ca.moreFollows")),
                ["Invoke ID"] = ProtoValue.Of(Number(read, "ca.invokeId")),
                ["Service Choice"] = ProtoValue.Of(Number(read, "ca.serviceChoice")),
                ["Object Type"] = ProtoValue.Of(Number(read, "ca.objectType")),
                ["Object Instance"] = ProtoValue.Of(Number(read, "ca.objectInstance")),
                ["Property Identifier"] = ProtoValue.Of(Unsigned(read, "ca.property")),
                ["Character Set"] = ProtoValue.Of(Number(read, "ca.charset")),
                ["Value"] = Maybe(read, "ca.text")!.Value,
            }),
            "a ReadProperty-ACK carrying a name did not survive being read and written again");
    }

    [TestMethod]
    public void A_piece_written_back_is_the_same_octets()
    {
        var bacnet = Bacnet();
        var read = new GraphCodec(bacnet.Graph).Decode(Capture(Segment));

        CollectionAssert.AreEqual(Capture(Segment), new GraphCodec(bacnet.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["PDU Type"] = ProtoValue.Of(3),
                ["BVLC Function"] = ProtoValue.Of(Number(read, "bvlcFunction.ca")),
                ["Expecting Reply"] = ProtoValue.Of(Number(read, "npduExpectingReply.ca")),
                ["Priority"] = ProtoValue.Of(Number(read, "npduPriority.ca")),
                ["Segmented"] = ProtoValue.Of(Number(read, "ca.segmented")),
                ["More Follows"] = ProtoValue.Of(Number(read, "ca.moreFollows")),
                ["Invoke ID"] = ProtoValue.Of(Number(read, "ca.invokeId")),
                ["Sequence Number"] = ProtoValue.Of(Number(read, "ca.sequenceNumber")),
                ["Window Size"] = ProtoValue.Of(Number(read, "ca.proposedWindowSize")),
                ["Service Choice"] = ProtoValue.Of(Number(read, "ca.serviceChoice")),
                ["Segment Payload"] = Maybe(read, "ca.segmentPayload")!.Value,
            }));
    }

    [TestMethod]
    public void A_segment_acknowledgement_written_back_is_the_same_octets()
    {
        var bacnet = Bacnet();
        var read = new GraphCodec(bacnet.Graph).Decode(Capture(SegmentAck));

        CollectionAssert.AreEqual(Capture(SegmentAck), new GraphCodec(bacnet.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["PDU Type"] = ProtoValue.Of(4),
                ["BVLC Function"] = ProtoValue.Of(Number(read, "bvlcFunction.sa")),
                ["Expecting Reply"] = ProtoValue.Of(Number(read, "npduExpectingReply.sa")),
                ["Priority"] = ProtoValue.Of(Number(read, "npduPriority.sa")),
                ["Negative Acknowledgement"] = ProtoValue.Of(Number(read, "sa.negative")),
                ["Sent By Server"] = ProtoValue.Of(Number(read, "sa.fromServer")),
                ["Invoke ID"] = ProtoValue.Of(Number(read, "sa.invokeId")),
                ["Sequence Number"] = ProtoValue.Of(Number(read, "sa.sequenceNumber")),
                ["Window Size"] = ProtoValue.Of(Number(read, "sa.actualWindowSize")),
            }));
    }

    // ── What has to hold ──────────────────────────────────────────────────────

    [TestMethod]
    public void A_length_that_disagrees_with_the_datagram_is_refused()
    {
        // The BVLC length counts the whole message including itself. Routers are known to get it wrong,
        // and a datagram whose header disagrees with its own size parses into whatever happens to line up
        // unless something says otherwise.
        var wrong = Capture(Request).ToArray();
        wrong[3] = 0x12;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Bacnet().Graph).Decode(wrong));

        StringAssert.Contains(refused.Message, "BVLC length counts the whole message");
    }

    [TestMethod]
    public void A_routed_message_is_refused_rather_than_read_as_an_unrouted_one()
    {
        // NPDU control bit 5 says DNET, DLEN and a DADR of DLEN octets follow. Reading past that to the
        // PDU type means reading a run whose width is on the wire, and looking ahead happens before
        // anything has been read — so a routed message could not be chosen even if it were described.
        var routed = Capture(Request).ToArray();
        routed[5] |= 0x20;

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Bacnet().Graph).Decode(routed));
    }

    [TestMethod]
    public void A_service_this_does_not_describe_is_refused()
    {
        var writeProperty = Capture(Request).ToArray();
        writeProperty[9] = 15;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Bacnet().Graph).Decode(writeProperty));

        StringAssert.Contains(refused.Message, "ReadProperty");
    }
}
