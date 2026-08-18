using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// One end of a TCP connection, with nothing but the protocol file to go on.
/// </summary>
/// <remarks>
/// <para>
/// The restriction is the point. A host may <b>set values</b> and <b>ask for a message</b>, and that is the
/// whole of its vocabulary — it cannot reach into the run, place a field, or fix up octets afterwards. So
/// anything that comes out right came out of the description, and a host that could compensate for a gap in
/// the description would hide exactly the gaps this is for finding.
/// </para>
/// <para>
/// It keeps its own state between messages because that is what a TCP endpoint is: a sequence number it has
/// got to, and the next one it expects. Those are the <c>state</c> nodes, and they are set the same way an
/// input is.
/// </para>
/// </remarks>
public sealed class Host(string who, ProtocolFile.Loaded protocol)
{
    private readonly Dictionary<string, ProtoValue> _values = new(StringComparer.Ordinal);

    public string Who => who;

    /// <summary>Sets an input or a state, by the name the RFC uses for it.</summary>
    public Host Set(string called, long value) => Set(called, ProtoValue.Of(value));

    public Host Set(string called, ProtoValue value)
    {
        _values[called] = value;
        return this;
    }

    /// <summary>The flags this segment carries. Everything not named is clear.</summary>
    public Host Flags(params string[] set)
    {
        foreach (var flag in new[] { "CWR", "ECE", "URG", "ACK", "PSH", "RST", "SYN", "FIN" })
            Set(flag, set.Contains(flag) ? 1 : 0);

        return this;
    }

    /// <summary>The options this segment carries, in order.</summary>
    public Host Options(params ProtoValue[] options) => Set("Options", new ProtoValue.List(options));

    /// <summary>Builds a segment out of what has been set, and nothing else.</summary>
    public byte[] Generate() => new GraphCodec(protocol.Graph).Encode(_values);

    /// <summary>Reads one that arrived.</summary>
    public RunGraph Receive(byte[] octets) => new GraphCodec(protocol.Graph).Decode(octets);
}

/// <summary>What a decoded segment said, by the name the field has in the description.</summary>
public static class Said
{
    public static ProtoValue Of(RunGraph run, string field)
        => run.Nodes.SingleOrDefault(n => n.Of is Field f && f.Id == field) is { } found && found.Has(Facet.Value)
            ? found.Value
            : throw new AssertFailedException($"the segment said nothing about '{field}'");

    public static long Number(RunGraph run, string field) => Of(run, field).AsInt();

    /// <summary>
    /// What the segment left behind in a state slot.
    /// </summary>
    /// <remarks>
    /// The description works this out now, so a host acknowledges what it was told to rather than adding
    /// one to a sequence number because it happens to know a SYN occupies a place in the sequence space.
    /// That is TCP's rule, and it belongs where TCP is written down.
    /// </remarks>
    public static long Kept(RunGraph run, string slot)
        => run.Nodes.Single(n => n.Of.Name == slot && n.Has(Facet.Value)).Value.AsInt();

    public static string Flags(RunGraph run)
        => string.Join("+", new[] { "cwr", "ece", "urg", "ack", "psh", "rst", "syn", "fin" }
                            .Where(f => Number(run, f) == 1)
                            .Select(f => f.ToUpperInvariant()));
}

[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class TcpHandshakeTests
{
    /// <summary>An option, as the description asks for one: a kind and whatever it carries.</summary>
    private static ProtoValue Option(long kind, params byte[] data)
        => EvalScope.Record(("kind", ProtoValue.Of(kind)), ("data", ProtoValue.Of(data)));

    /// <summary>Maximum Segment Size — kind 2, two octets of it.</summary>
    private static ProtoValue Mss(int mss) => Option(2, (byte)(mss >> 8), (byte)(mss & 0xff));

    /// <summary>No-Operation — kind 1, and nothing else to it.</summary>
    private static ProtoValue Nop() => Option(1);

    private static Host End(string who)
    {
        var tcp = Definitions.Load("tcp");

        return new Host(who, tcp)
            .Set("Source Address", ProtoValue.Of(new byte[] { 192, 168, 1, 10 }))
            .Set("Destination Address", ProtoValue.Of(new byte[] { 192, 168, 1, 20 }))
            .Set("Window", 65535)
            .Options()
            .Set("Data", ProtoValue.Of(Array.Empty<byte>()));
    }

    [TestMethod]
    public void A_syn_is_generated_from_what_was_set_and_nothing_else()
    {
        var client = End("client")
            .Set("Source Port", 49152)
            .Set("Destination Port", 80)
            .Set("Sequence Number", 1000)
            .Set("Acknowledgment Number", 0)
            .Options(Mss(1460))
            .Set("Synchronising", 1)
            .Flags("SYN");

        var syn = client.Generate();

        Assert.AreEqual(24, syn.Length, "20 octets of header plus a four-octet MSS option");

        // Everything except the checksum, which is checked below by summing what arrived.
        Assert.AreEqual("C000", Convert.ToHexString(syn[0..2]), "source port 49152");
        Assert.AreEqual("0050", Convert.ToHexString(syn[2..4]), "destination port 80");
        Assert.AreEqual("000003E8", Convert.ToHexString(syn[4..8]), "sequence number 1000");
        Assert.AreEqual("00000000", Convert.ToHexString(syn[8..12]), "no acknowledgment yet");

        // The octet the engine had to pack out of two four-bit fields: six 32-bit words of header, and
        // four reserved bits of zero.
        Assert.AreEqual("60", Convert.ToHexString(syn[12..13]), "data offset 6, reserved 0");

        // And the one it had to pack out of eight one-bit fields.
        Assert.AreEqual("02", Convert.ToHexString(syn[13..14]), "SYN alone");

        Assert.AreEqual("FFFF", Convert.ToHexString(syn[14..16]), "window 65535");
        Assert.AreEqual("0000", Convert.ToHexString(syn[18..20]), "no urgent data");
        Assert.AreEqual("020405B4", Convert.ToHexString(syn[20..24]), "kind 2, length 4, MSS 1460");
    }

    [TestMethod]
    public void The_checksum_covers_the_pseudo_header_and_verifies()
    {
        // The property that says the checksum was computed over the right octets: summing a segment
        // INCLUDING its checksum comes to 0xFFFF. Nothing here recomputes it the way the graph did — this
        // is the receiver's arithmetic, so a checksum over the wrong span fails it.
        var syn = End("client")
            .Set("Source Port", 49152).Set("Destination Port", 80)
            .Set("Sequence Number", 1000).Set("Acknowledgment Number", 0)
            .Options(Mss(1460)).Set("Synchronising", 1).Flags("SYN")
            .Generate();

        byte[] pseudo = [192, 168, 1, 10, 192, 168, 1, 20, 0, 6, 0, (byte)syn.Length];

        int sum = 0;
        foreach (var pair in pseudo.Concat(syn).Chunk(2)) sum += (pair[0] << 8) | pair[1];
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);

        Assert.AreEqual(0xFFFF, sum,
            "a segment summed with its own checksum comes to all ones, or the checksum covered the "
          + "wrong octets");

        Assert.AreNotEqual("0000", Convert.ToHexString(syn[16..18]),
            "and it is not simply the zero that was joined in where the field goes");
    }

    [TestMethod]
    public void A_syn_carrying_several_options_is_read_to_the_end_of_them()
    {
        // A real SYN: Maximum Segment Size, then padding to a word boundary with No-Operations. Data
        // Offset 7 means twenty-eight octets of header, so eight of options — and nothing in the segment
        // says how many options that is. The reading finds out by going round until the header ends.
        byte[] syn =
        [
            0xc0, 0x00, 0x00, 0x50,                          // ports
            0x00, 0x00, 0x03, 0xe8,                          // sequence
            0x00, 0x00, 0x00, 0x00,                          // no acknowledgment
            0x70, 0x02,                                      // offset 7, SYN
            0xff, 0xff, 0x91, 0x7c, 0x00, 0x00,              // window, checksum, urgent
            0x02, 0x04, 0x05, 0xb4,                          // MSS 1460
            0x01, 0x01, 0x01, 0x01,                          // four No-Operations
        ];

        var run = End("server").Receive(syn);

        var kinds = run.Nodes.Where(n => n.Of is Field { Id: "optionKind" })
                             .OrderBy(n => n.Index)
                             .Select(n => n.Value.AsInt())
                             .ToList();

        CollectionAssert.AreEqual(new long[] { 2, 1, 1, 1, 1 }, kinds,
            "five options: one MSS and four No-Operations, and no count anywhere said so");

        // And the option that carries something has its data, still as octets — this description says how
        // an option is SHAPED, not what kind 2 means.
        var mss = run.Nodes.Single(n => n.Of is Field { Id: "optionData" }).Value.AsBytes();

        Assert.AreEqual("05B4", Convert.ToHexString(mss), "1460, as the two octets it was written as");
    }

    [TestMethod]
    public void A_segment_writes_as_many_options_as_it_was_given()
    {
        // Three options of two different shapes: one carrying data, two that are a single octet. Nothing
        // in the description says three — the list has three items, and how many options a segment carries
        // is a fact about that segment.
        var syn = End("client")
            .Set("Source Port", 49152).Set("Destination Port", 80)
            .Set("Sequence Number", 1000).Set("Acknowledgment Number", 0)
            .Options(Mss(1460), Nop(), Nop())
            .Set("Synchronising", 1).Flags("SYN")
            .Generate();

        // 20 fixed + 4 (MSS) + 1 + 1 (No-Operations) = 26, padded to 28 so Data Offset counts whole words.
        Assert.AreEqual(28, syn.Length, "padded to a word boundary");
        Assert.AreEqual(7, syn[12] >> 4, "Data Offset 7, which is the padded length and not the raw one");
        Assert.AreEqual("020405B4010100 00", Convert.ToHexString(syn[20..]).Insert(14, " ").Insert(6, "").Replace(" ", " "),
            "the options, then two octets of padding");
    }

    [TestMethod]
    public void And_reads_them_all_back()
    {
        var client = End("client")
            .Set("Source Port", 49152).Set("Destination Port", 80)
            .Set("Sequence Number", 1000).Set("Acknowledgment Number", 0)
            .Options(Mss(1460), Nop(), Nop())
            .Set("Synchronising", 1).Flags("SYN");

        var run = End("server").Receive(client.Generate());

        var kinds = run.Nodes.Where(n => n.Of is Field { Id: "optionKind" })
                             .OrderBy(n => n.Index)
                             .Select(n => n.Value.AsInt())
                             .ToList();

        // Four, not three — and that is the wire being read honestly rather than a fault. Padding to a
        // word boundary is written as zeros, and a zero octet IS an End of Option List. So the reading
        // finds the three that were asked for and the one the padding amounts to, and stops there because
        // RFC 9293 §3.1 says everything past an End of Option List is padding.
        CollectionAssert.AreEqual(new long[] { 2, 1, 1, 0 }, kinds,
            "three options, then the End of Option List the padding begins with");
    }

    /// <summary>
    /// Two hosts, five segments, and one word of payload getting across.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every segment here is built the same way: a host sets values on input and state nodes and asks for a
    /// message. It never says how long anything is, never places a field, never computes a checksum, and
    /// never touches an octet. What it <i>does</i> do is read the segment that arrived and decide what to
    /// send back — which is the part that belongs to a host and not to a protocol description.
    /// </para>
    /// <para>
    /// So the exchange is a test of the description. If the graph got a length, an offset, a flag or a sum
    /// wrong, the other end reads something other than what was meant and the conversation stops making
    /// sense — which is exactly what happens on a real network, and exactly what would otherwise be found
    /// by a packet capture six months later.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Two_hosts_shake_hands_and_get_a_word_across()
    {
        const string Message = "nexaflow";

        var client = End("client").Set("Source Port", 49152).Set("Destination Port", 80);
        var server = End("server").Set("Source Port", 80).Set("Destination Port", 49152)
                                  .Set("Source Address", ProtoValue.Of(new byte[] { 192, 168, 1, 20 }))
                                  .Set("Destination Address", ProtoValue.Of(new byte[] { 192, 168, 1, 10 }));

        List<string> exchange = [];

        // ── 1. the client opens ──────────────────────────────────────────────
        var syn = client.Set("Sequence Number", 1000).Set("Acknowledgment Number", 0)
                        .Options(Mss(1460)).Set("Synchronising", 1).Flags("SYN")
                        .Generate();

        var atServer = server.Receive(syn);
        exchange.Add($"client → server  {Said.Flags(atServer)} seq={Said.Number(atServer, "sequenceNumber")}");

        Assert.AreEqual("SYN", Said.Flags(atServer));
        Assert.AreEqual(1000, Said.Number(atServer, "sequenceNumber"));
        // The option came through as the shape it is — a kind and its octets — because that is what this
        // description says an option is. What kind 2 MEANS is a different description.
        Assert.AreEqual(2, Said.Number(atServer, "optionKind"));
        Assert.AreEqual("05B4", Convert.ToHexString(Said.Of(atServer, "optionData").AsBytes()),
            "1460, as the two octets it arrived as");

        // ── 2. the server accepts, acknowledging what the SYN advanced the sequence to ──
        // Not `sequenceNumber + 1`. How far a segment carries the sequence space is RFC 9293 §3.3.1 — a
        // SYN and a FIN each occupy one and the payload occupies its length — and the description says so,
        // so what arrives at a host is the number to acknowledge rather than the ingredients for it.
        var synAck = server.Set("Sequence Number", 5000)
                           .Set("Acknowledgment Number", Said.Kept(atServer, "state.nextSequence"))
                           .Options(Mss(1460)).Set("Synchronising", 1).Flags("SYN", "ACK")
                           .Generate();

        var atClient = client.Receive(synAck);
        exchange.Add($"server → client  {Said.Flags(atClient)} seq={Said.Number(atClient, "sequenceNumber")} "
                   + $"ack={Said.Number(atClient, "acknowledgmentNumber")}");

        Assert.AreEqual("ACK+SYN", Said.Flags(atClient), "the RFC's wire order, not the name's");
        Assert.AreEqual(1001, Said.Number(atClient, "acknowledgmentNumber"), "our SYN was acknowledged");

        // ── 3. the client acknowledges, and the connection is open ───────────
        var ack = client.Set("Sequence Number", Said.Number(atClient, "acknowledgmentNumber"))
                        .Set("Acknowledgment Number", Said.Kept(atClient, "state.nextSequence"))
                        .Set("Synchronising", 0).Options().Flags("ACK")
                        .Generate();

        Assert.AreEqual(20, ack.Length, "no options once the handshake is done, so a five-word header");

        var opened = server.Receive(ack);
        exchange.Add($"client → server  {Said.Flags(opened)} seq={Said.Number(opened, "sequenceNumber")} "
                   + $"ack={Said.Number(opened, "acknowledgmentNumber")}");

        Assert.AreEqual(5, Said.Number(opened, "dataOffset"), "and the offset says so");

        // ── 4. the client sends the word ─────────────────────────────────────
        var payload = System.Text.Encoding.ASCII.GetBytes(Message);

        var sent = client.Set("Data", ProtoValue.Of(payload)).Flags("PSH", "ACK").Generate();

        Assert.AreEqual(20 + payload.Length, sent.Length);

        var arrived = server.Receive(sent);
        var got = System.Text.Encoding.ASCII.GetString(Said.Of(arrived, "data").AsBytes());
        exchange.Add($"client → server  {Said.Flags(arrived)} \"{got}\"");

        Assert.AreEqual(Message, got, "the word got across");

        // ── 5. the server acknowledges what it received ──────────────────────
        var acked = server.Set("Synchronising", 0).Options()
                          .Set("Sequence Number", Said.Number(opened, "acknowledgmentNumber"))
                          .Set("Acknowledgment Number", Said.Kept(arrived, "state.nextSequence"))
                          .Flags("ACK")
                          .Generate();

        var settled = client.Receive(acked);
        exchange.Add($"server → client  {Said.Flags(settled)} ack={Said.Number(settled, "acknowledgmentNumber")}");

        Assert.AreEqual(1001 + payload.Length, Said.Number(settled, "acknowledgmentNumber"),
            "every octet of the word is accounted for");

        System.Console.WriteLine(string.Join(Environment.NewLine, exchange));
    }
}
