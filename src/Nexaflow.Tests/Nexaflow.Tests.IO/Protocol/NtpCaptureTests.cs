using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// NTP, against packets nobody wrote to suit it.
/// </summary>
/// <remarks>
/// <para>
/// The captures come from the corpus, which predates the engine and was assembled to defeat it — each one
/// has a byte-level breakdown accounting for every octet. So this is not a codec agreeing with itself: the
/// values asserted below were written down from the wire format, not read out of a run.
/// </para>
/// <para>
/// And the round trip is the test that matters. Decoding is easy to get accidentally right — a field read
/// two octets early still produces a number. Re-encoding what was read and getting the same octets back
/// means every width, order and position was right, because anything else lands somewhere else.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class NtpCaptureTests
{
    private static ProtocolFile.Loaded Ntp() => Definitions.Load("ntp");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("ntp").Captures[which].Bytes;

    /// <summary>Reads a packet and hands back what each field came to.</summary>
    private static RunGraph Read(int which) => new GraphCodec(Ntp().Graph).Decode(Capture(which));

    /// <summary>
    /// Writes a packet from what a decoded one said, and from nothing else.
    /// </summary>
    /// <remarks>
    /// Every input is fed by the name the RFC gives it, which is the same name the field carries — so this
    /// is the description's own vocabulary going round, and a field the graph forgot to feed shows up as a
    /// refusal rather than a zero.
    /// </remarks>
    private static byte[] Rewrite(ProtocolFile.Loaded ntp, RunGraph read)
    {
        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal);

        foreach (var field in ntp.Graph.Nodes.OfType<Field>())
        {
            var appearance = read.Nodes.SingleOrDefault(n => ReferenceEquals(n.Of, field));

            if (appearance is { } found && found.Has(Facet.Value))
                setting[field.As ?? field.Id] = found.Value;
        }

        // Whether the association is authenticated is a decision a host makes, not a field it read — so
        // this is the host saying it, having noticed a MAC arrived. On the way in the packet's own length
        // said so; there is no length on the way out to say it again.
        setting["Authenticated"] = ProtoValue.Of(setting.ContainsKey("Key Identifier") ? 1 : 0);

        return new GraphCodec(ntp.Graph).Encode(setting);
    }

    private static long Number(RunGraph run, string field)
        => run.Nodes.Single(n => n.Of is Field f && f.Id == field).Value.AsInt();

    // ── What the wire said ────────────────────────────────────────────────────

    [TestMethod]
    public void A_client_request_reads_as_the_breakdown_says()
    {
        // Capture A, first octet 0x23 = 00|100|011.
        var run = Read(0);

        Assert.AreEqual(0, Number(run, "leapIndicator"), "LI: no leap warning");
        Assert.AreEqual(4, Number(run, "versionNumber"), "VN: NTPv4");
        Assert.AreEqual(3, Number(run, "mode"), "Mode: client");

        Assert.AreEqual(0, Number(run, "stratum"), "unspecified, which is normal in a client packet");
        Assert.AreEqual(6, Number(run, "poll"), "log2 seconds — a 64 second poll");

        // The one that says the sign bit was honoured: 0xec is -20, not 236.
        Assert.AreEqual(-20, Number(run, "precision"), "0xec read signed");

        Assert.AreEqual(3995265600, Number(run, "transmitTimestamp.seconds"));
        Assert.AreEqual(1288490189, Number(run, "transmitTimestamp.fraction"));

        // A client has nothing to say about where it got the time from.
        Assert.AreEqual(0, Number(run, "referenceTimestamp.seconds"));
        Assert.AreEqual(0, Number(run, "originTimestamp.seconds"));
    }

    [TestMethod]
    public void A_server_response_reads_as_the_breakdown_says()
    {
        // Capture B, first octet 0x24 = 00|100|100.
        var run = Read(1);

        Assert.AreEqual(4, Number(run, "versionNumber"));
        Assert.AreEqual(4, Number(run, "mode"), "Mode: server");
        Assert.AreEqual(2, Number(run, "stratum"), "a secondary server");
        Assert.AreEqual(-23, Number(run, "precision"), "0xe9 read signed");

        Assert.AreEqual(655, Number(run, "rootDelay.fraction"), "0x028f");
        Assert.AreEqual(1176, Number(run, "rootDispersion.fraction"), "0x0498");

        // The reference id is four octets whose reading depends on the stratum, so the graph keeps them as
        // octets rather than picking one reading. At stratum 2 these are 129.6.15.28.
        var reference = run.Nodes.Single(n => n.Of is Field { Id: "referenceId" }).Value.AsBytes();
        CollectionAssert.AreEqual(new byte[] { 129, 6, 15, 28 }, reference);

        // The server echoes the client's transmit time back as the origin, verbatim.
        Assert.AreEqual(3995265600, Number(run, "originTimestamp.seconds"));
        Assert.AreEqual(1288490189, Number(run, "originTimestamp.fraction"));
    }

    // ── And what it writes back ───────────────────────────────────────────────

    [DataTestMethod]
    [DataRow(0, "a client request")]
    [DataRow(1, "a server response")]
    [DataRow(2, "a request carrying a symmetric-key MAC")]
    public void A_capture_written_back_is_the_same_octets(int which, string what)
    {
        var ntp = Ntp();
        var read = new GraphCodec(ntp.Graph).Decode(Capture(which));

        CollectionAssert.AreEqual(Capture(which), Rewrite(ntp, read),
            $"{what} did not survive being read and written again");
    }

    [TestMethod]
    public void A_packet_with_octets_left_over_is_refused()
    {
        // Four octets past the end of an authenticated packet. Everything the walk reads is well-formed —
        // that is the point. A decoder that stops when its description runs out reports success here and
        // hands back a message it has only read part of.
        var trailing = Capture(2).Concat(new byte[] { 0xde, 0xad, 0xbe, 0xef }).ToArray();

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Ntp().Graph).Decode(trailing));

        StringAssert.Contains(refused.Message, "left over");
        StringAssert.Contains(refused.Message, "prefix that happens to parse");
    }

    [TestMethod]
    public void A_version_the_protocol_does_not_define_is_refused()
    {
        // Version 7 in the second field of the first octet: 00|111|011. Nothing else about the packet is
        // wrong, which is the point — a decoder that only checks widths reads this happily.
        var wrong = Capture(0).ToArray();
        wrong[0] = 0b00_111_011;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Ntp().Graph).Decode(wrong));

        StringAssert.Contains(refused.Message, "versionNumber");
    }
}
