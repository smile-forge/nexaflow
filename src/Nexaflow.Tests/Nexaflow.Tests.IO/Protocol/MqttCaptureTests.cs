using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// MQTT 3.1.1, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// The first protocol here with more than one message format, and the shape that demands: which format a
/// packet is, is written inside the packet, so nothing can be asked and nothing has been read when the
/// choice has to be made. The reading looks ahead along the path that <c>identifies</c> the discriminator,
/// and the message it picks then reads its own first octet as though it were the only message in the
/// protocol.
/// </para>
/// <para>
/// Also the first to need a varint on the wire. Two of these captures carry the same field at different
/// widths — one octet in a CONNACK, two in a CONNECT — which is the whole of why a width is a form rather
/// than a number.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class MqttCaptureTests
{
    private const int Connect = 0, Connack = 1, Subscribe = 2, Suback = 3, PingReq = 4, PingResp = 5;

    private static ProtocolFile.Loaded Mqtt() => Definitions.Load("mqtt");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("mqtt").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Mqtt().Graph).Decode(Capture(which));

    private static RunNode One(RunGraph run, string field)
        => run.Nodes.Single(n => n.Of is Field f && f.Id == field);

    private static long Number(RunGraph run, string field) => One(run, field).Value.AsInt();

    /// <summary>A field the description says is text, read as text.</summary>
    private static string Said(RunGraph run, string field) => One(run, field).Value.AsText();

    /// <summary>A field the specification calls binary data, which stays octets.</summary>
    private static string Octets(RunGraph run, string field)
        => Encoding.UTF8.GetString(One(run, field).Value.AsBytes());

    /// <summary>Every appearance of a field that turned up more than once, in the order they were read.</summary>
    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    // ── Which message this is ─────────────────────────────────────────────────

    [TestMethod]
    public void Six_message_formats_hang_off_the_protocol()
    {
        // One per control packet type described, which is what §3.1 through §3.13 are: separate sections
        // giving separate formats. Not one message with a fork in it — the packet type does not select a
        // body inside a shared layout, it selects a layout.
        var mqtt = Mqtt();

        CollectionAssert.AreEquivalent(
            new[] { "connect", "connack", "subscribe", "suback", "pingreq", "pingresp" },
            mqtt.Messages.Keys.ToArray());

        foreach (var message in mqtt.Messages.Values)
            Assert.AreEqual(1, mqtt.Graph.From<Then>(message).Count(),
                $"'{message.Name}' has one arrangement and nothing to decide");
    }

    [TestMethod]
    public void Each_message_is_a_single_path_with_no_fork_in_it()
    {
        // The claim the whole shape rests on. Every place inside a message has exactly one way on — steps
        // may be optional, but none of them is a choice — so a message is describable without reference to
        // the five it shares a protocol with.
        var mqtt = Mqtt();

        var forks = mqtt.Graph.Nodes
            .Where(n => n is not global::Nexaflow.IO.Protocol.Wire.Protocol)
            .Where(n => mqtt.Graph.From<Then>(n).Count() > 1)
            .Select(n => n.Name)
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), forks,
            "the only place anything is chosen is the protocol itself");
    }

    [TestMethod]
    public void The_reading_looks_ahead_to_find_out_which_one_it_has()
    {
        var mqtt = Mqtt();

        var looks = mqtt.Graph.From<Identifies>(mqtt.Graph.Root).Single();

        Assert.AreEqual("probe.packetType", looks.To.Name);
        Assert.AreEqual(4, ((Field)looks.To).Form.FixedBits, "§2.2.1: the high four bits of octet zero");

        // And what it finds is matched against the keys, which are the assigned type codes.
        CollectionAssert.AreEquivalent(
            new long[] { 1, 2, 8, 9, 12, 13 },
            mqtt.Graph.From<Then>(mqtt.Graph.Root).Select(w => w.Key!.AsInt()).ToArray());
    }

    [TestMethod]
    public void Looking_ahead_consumes_nothing()
    {
        // The point of reading from a copy. If the probe consumed its four bits, every message would have
        // to be described as starting half an octet in — and would then be undescribable on its own.
        var run = Read(PingReq);

        Assert.AreEqual(12, Number(run, "pingreqType"), "read again, by the message itself");
        Assert.AreEqual(0, Number(run, "pingreqRemaining"));
    }

    [TestMethod]
    public void A_packet_type_nothing_describes_is_refused()
    {
        // 0x30 is PUBLISH — a real MQTT packet this file does not describe. The failure worth engineering
        // against is the quiet one: a reader that shrugs at an unknown type returns a conversation with the
        // interesting packets silently missing from it.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Mqtt().Graph).Decode([0x30, 0x02, 0x00, 0x00]));

        StringAssert.Contains(refused.Message, "picks none of the ways on");
    }

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void A_connect_reads_as_the_breakdown_says()
    {
        var run = Read(Connect);

        Assert.AreEqual(1, Number(run, "connectType"));
        Assert.AreEqual(143, Number(run, "connectRemaining"), "two octets of varint, 15 + 1×128");
        Assert.AreEqual("MQTT", Octets(run, "protocolNameBytes"),
            "a constant this description states, so it stays the octets it states");
        Assert.AreEqual(4, Number(run, "protocolLevel"), "MQTT 3.1.1");
        Assert.AreEqual(60, Number(run, "keepAlive"));
        Assert.AreEqual("nexaflow-probe-01", Said(run, "clientIdBytes"));

        // 0xce, bit by bit — and each bit is a node, so each is answerable.
        Assert.AreEqual(1, Number(run, "userNameFlag"));
        Assert.AreEqual(1, Number(run, "passwordFlag"));
        Assert.AreEqual(0, Number(run, "willRetain"));
        Assert.AreEqual(1, Number(run, "willQos"));
        Assert.AreEqual(1, Number(run, "willFlag"));
        Assert.AreEqual(1, Number(run, "cleanSession"));
        Assert.AreEqual(0, Number(run, "connectReserved"));
    }

    [TestMethod]
    public void The_will_and_the_credentials_are_there_because_the_flags_say_so()
    {
        // 114 of this packet's 146 octets exist only because of three bits read 22 octets earlier. The
        // presence of each part reads the very bit that announces it, so a flag and the part it announces
        // are one fact and cannot disagree.
        var run = Read(Connect);

        Assert.AreEqual("dev/status", Said(run, "willTopicBytes"));
        Assert.AreEqual(80, Number(run, "willMessageLength"));
        Assert.AreEqual("sensoruser", Said(run, "userNameBytes"));
        Assert.AreEqual("s3cr3t", Octets(run, "passwordBytes"));

        StringAssert.Contains(Octets(run, "willMessageBytes"), "ungraceful-disconnect",
            "MQTT gives the will message no structure, so these stay octets where the topic beside them "
            + "converts — §3.1.3.3 calls one binary data and §3.1.3.2 calls the other a UTF-8 string");
    }

    [TestMethod]
    public void A_connack_reads_as_the_breakdown_says()
    {
        var run = Read(Connack);

        Assert.AreEqual(2, Number(run, "connackType"));
        Assert.AreEqual(2, Number(run, "connackRemaining"), "one octet of varint this time");
        Assert.AreEqual(0, Number(run, "sessionPresent"));
        Assert.AreEqual(0, Number(run, "connectReturnCode"), "Connection Accepted");
    }

    [TestMethod]
    public void The_same_length_field_is_one_octet_here_and_two_there()
    {
        // The reason a width is a form rather than a number. Same field, same protocol, two widths — and
        // which one applies is a function of the value it carries, so nothing that fixes a width up front
        // can encode both.
        Assert.AreEqual(2, One(Read(Connect), "connectRemaining").Settled(Facet.Extent));
        Assert.AreEqual(1, One(Read(Connack), "connackRemaining").Settled(Facet.Extent));
    }

    [TestMethod]
    public void A_subscribe_reads_its_three_filters_by_going_round()
    {
        // How many there are is nowhere in the packet: §3.8.3 says the payload holds a list and the
        // remaining length says where it ends. So the reading goes round until the octets run out.
        var run = Read(Subscribe);

        Assert.AreEqual(10, Number(run, "subscribePacketId"));

        CollectionAssert.AreEqual(
            new[] { "dev/status", "dev/+/telemetry", "$SYS/broker/uptime" },
            Each(run, "topicFilterBytes").Select(n => n.Value.AsText()).ToArray());

        CollectionAssert.AreEqual(
            new long[] { 1, 2, 0 },
            Each(run, "requestedQos").Select(n => n.Value.AsInt()).ToArray(),
            "and the second pass did not overwrite the first");
    }

    [TestMethod]
    public void A_suback_reads_a_grant_a_downgrade_and_a_refusal()
    {
        var run = Read(Suback);

        Assert.AreEqual(10, Number(run, "subackPacketId"), "the token the SUBSCRIBE carried");

        CollectionAssert.AreEqual(
            new long[] { 1, 1, 0x80 },
            Each(run, "returnCode").Select(n => n.Value.AsInt()).ToArray(),
            "granted as asked, granted lower than asked, and refused outright");
    }

    [TestMethod]
    [DataRow(PingReq, 12)]
    [DataRow(PingResp, 13)]
    public void A_ping_is_a_fixed_header_and_nothing_else(int which, long type)
    {
        var run = Read(which);
        var name = type == 12 ? "pingreq" : "pingresp";

        Assert.AreEqual(type, Number(run, $"{name}Type"));
        Assert.AreEqual(0, Number(run, $"{name}Remaining"));
    }

    // ── Back out again ────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(Connect, "a CONNECT with a will and credentials")]
    [DataRow(Connack, "a CONNACK")]
    [DataRow(Subscribe, "a SUBSCRIBE with three filters")]
    [DataRow(Suback, "a SUBACK with three return codes")]
    [DataRow(PingReq, "a PINGREQ")]
    [DataRow(PingResp, "a PINGRESP")]
    public void A_capture_written_back_is_the_same_octets(int which, string what)
    {
        var mqtt = Mqtt();
        var read = new GraphCodec(mqtt.Graph).Decode(Capture(which));

        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal);

        // Everything a host may say, and nothing else: the values of the fields that turned up once. It
        // cannot reach into the run, place a field or fix up an octet, so whatever comes out right came out
        // of the description.
        foreach (var field in mqtt.Graph.Nodes.OfType<Field>())
            if (read.Nodes.FirstOrDefault(n => ReferenceEquals(n.Of, field) && n.Index == 0) is { } found
                && found.Has(Facet.Value))
                setting[field.As ?? field.Id] = found.Value;

        // And the two lists, which are what the repeated parts are written once per item of. Neither is a
        // count — the length of the list is how many times round, and no octet anywhere says three.
        if (Each(read, "topicFilterBytes") is { Count: > 0 } filters)
            setting["Subscriptions"] = new ProtoValue.List(
                [.. filters.Zip(Each(read, "requestedQos"), (f, q) => EvalScope.Record(
                       ("filter", f.Value), ("qos", q.Value)))]);

        if (Each(read, "returnCode") is { Count: > 0 } codes)
            setting["Return Codes"] = new ProtoValue.List([.. codes.Select(c => c.Value)]);

        CollectionAssert.AreEqual(Capture(which), new GraphCodec(mqtt.Graph).Encode(setting),
            $"{what} did not survive being read and written again");
    }
}
