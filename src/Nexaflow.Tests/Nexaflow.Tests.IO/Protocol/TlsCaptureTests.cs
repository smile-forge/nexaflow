using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// TLS 1.2 hellos, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// Three lengths one inside the next — two octets, then <b>three</b>, then two — and under them the thing
/// TLS is made of: a <b>vector</b>, which is a length in octets followed by items with no tag on them.
/// Eight cipher suites in sixteen octets, and nothing counts them.
/// </para>
/// <para>
/// One message that forks in the middle, on a field read four octets earlier. What the two hellos differ
/// in is not a value but a <i>shape</i>: the client offers a vector of suites, the server writes the one
/// it picked as a plain number.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class TlsCaptureTests
{
    private const int ClientHello = 0;
    private const int ServerHello = 1;

    private static ProtocolFile.Loaded Tls() => Definitions.Load("tls");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("tls").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Tls().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static long Number(RunGraph run, string field) => Maybe(run, field)!.Value.AsInt();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    private static long[] Numbers(RunGraph run, string field)
        => [.. Each(run, field).Select(n => n.Value.AsInt())];

    private static byte[] Bytes(RunGraph run, string field, int which = 0)
        => Each(run, field)[which].Value.AsBytes();

    /// <summary>The extensions, in wire order, back in the shape a caller supplies.</summary>
    private static ProtoValue[] Extensions(RunGraph run)
        => [.. Each(run, "extensionType")
                 .Zip(Each(run, "extensionData"),
                      (t, d) => EvalScope.Record(("type", t.Value), ("data", d.Value)))];

    private static Dictionary<string, ProtoValue> Common(RunGraph run)
        => new(StringComparer.Ordinal)
        {
            ["Record Version"] = Maybe(run, "recordVersion")!.Value,
            ["Message Type"] = Maybe(run, "messageType")!.Value,
            ["Version"] = Maybe(run, "version")!.Value,
            ["Random"] = Maybe(run, "random")!.Value,
            ["Session ID"] = Maybe(run, "sessionId")!.Value,
            ["Extensions"] = new ProtoValue.List([.. Extensions(run)]),
        };

    // ── C1: the ClientHello ───────────────────────────────────────────────────

    [TestMethod]
    public void A_client_hello_reads_three_lengths_one_inside_the_next()
    {
        var run = Read(ClientHello);

        Assert.AreEqual(191, Number(run, "recordLength"), "196 octets, less the five in front of it");
        Assert.AreEqual(187, Number(run, "handshakeLength"), "and four fewer again");
        Assert.AreEqual(98, Number(run, "extensionsLength"));

        Assert.AreEqual(3, Convert.ToInt64(Maybe(run, "handshakeLength")!.Settled(Facet.Extent)),
            "RFC 5246 §7.4's uint24 — the one length here that is not a power-of-two width");
    }

    [TestMethod]
    public void And_its_header_says_what_it_is_and_what_it_will_speak()
    {
        var run = Read(ClientHello);

        Assert.AreEqual(0x16, Number(run, "contentType"), "handshake");
        Assert.AreEqual(0x0301, Number(run, "recordVersion"),
            "RFC 5246 appendix E.1 — the record header is what a middlebox will not choke on");
        Assert.AreEqual(1, Number(run, "messageType"), "client_hello");
        Assert.AreEqual(0x0303, Number(run, "version"), "and TLS 1.2 is what it offers");

        Assert.AreEqual(32, Bytes(run, "random").Length);
        Assert.AreEqual(32, Number(run, "sessionIdLength"));
    }

    [TestMethod]
    public void And_a_vector_is_spent_rather_than_counted()
    {
        // Sixteen octets of two-octet items. Nothing on the wire says eight, and nothing in the
        // description divides — the reading asks where it is against where the length runs out.
        var run = Read(ClientHello);

        Assert.AreEqual(16, Number(run, "cipherSuitesLength"), "in octets, not in suites");

        CollectionAssert.AreEqual(
            new long[] { 0xc02b, 0xc02f, 0xc02c, 0xc030, 0xcca9, 0xcca8, 0x009c, 0x002f },
            Numbers(run, "suite"), "eight suites, in the order the client prefers them");

        Assert.AreEqual(1, Number(run, "compressionMethodsLength"));
        CollectionAssert.AreEqual(new long[] { 0 }, Numbers(run, "compression"),
            "null compression — and still a vector, because the format cannot be changed");
    }

    [TestMethod]
    public void And_a_run_of_unlike_widths_ends_the_same_way()
    {
        // Nine extensions between four and eighteen octets each. The bound is octets rather than a count,
        // so items of one width and items of many are the same question asked of the same length.
        var run = Read(ClientHello);

        CollectionAssert.AreEqual(
            new long[] { 0x0000, 0x000b, 0x000a, 0x000d, 0x0023, 0xff01, 0x0017, 0x0005, 0x0010 },
            Numbers(run, "extensionType"));

        CollectionAssert.AreEqual(new long[] { 16, 2, 10, 14, 0, 1, 0, 5, 14 },
                                  Numbers(run, "extensionLength"));
    }

    [TestMethod]
    public void And_an_extension_that_is_there_and_empty_is_not_an_extension_that_is_absent()
    {
        // session_ticket and extended_master_secret are four octets that name themselves and say nothing
        // follows. Present and empty; the difference from absent is the four octets.
        var run = Read(ClientHello);

        var types = Numbers(run, "extensionType");
        var data = Each(run, "extensionData");

        Assert.AreEqual(0, data[Array.IndexOf(types, 0x0023L)].Value.AsBytes().Length, "session_ticket");
        Assert.AreEqual(0, data[Array.IndexOf(types, 0x0017L)].Value.AsBytes().Length,
            "extended_master_secret");

        Assert.IsFalse(types.Contains(0x002bL),
            "and supported_versions is simply not among them, which is how a 1.2 hello differs from a 1.3 "
          + "one — inside the extensions rather than in the version octets");
    }

    [TestMethod]
    public void And_what_is_inside_an_extension_is_octets_this_does_not_open()
    {
        // The description stops at type, length, and that many octets — §7.4.1.4 obliges a reader to
        // ignore what it does not recognise, and the registry grows. So the test does the reading a
        // caller would, on octets the description carried without interpreting.
        var run = Read(ClientHello);

        var types = Numbers(run, "extensionType");
        var sni = Bytes(run, "extensionData", Array.IndexOf(types, 0x0000L));

        Assert.AreEqual("example.com", Encoding.ASCII.GetString(sni[5..]),
            "a list length, a name type, a name length, and the name");

        var alpn = Bytes(run, "extensionData", Array.IndexOf(types, 0x0010L));

        Assert.AreEqual("h2", Encoding.ASCII.GetString(alpn[3..5]));
        Assert.AreEqual("http/1.1", Encoding.ASCII.GetString(alpn[6..]));
    }

    [TestMethod]
    public void A_client_hello_written_back_is_the_same_octets()
    {
        var tls = Tls();
        var read = new GraphCodec(tls.Graph).Decode(Capture(ClientHello));

        var asking = Common(read);
        asking["Cipher Suites"] = new ProtoValue.List([.. Each(read, "suite").Select(n => n.Value)]);
        asking["Compression Methods"] =
            new ProtoValue.List([.. Each(read, "compression").Select(n => n.Value)]);

        CollectionAssert.AreEqual(Capture(ClientHello), new GraphCodec(tls.Graph).Encode(asking),
            "196 octets, nine extensions and five lengths, did not survive being read and written again");
    }

    // ── C2: the ServerHello ───────────────────────────────────────────────────

    [TestMethod]
    public void A_server_hello_writes_one_of_each_where_the_client_wrote_a_list()
    {
        // The finding, and it is a shape rather than a value: the specification calls one cipher_suites
        // and the other cipher_suite, and reading the second with the first's description would take
        // 0xc02f as a length of 49199 octets.
        var run = Read(ServerHello);

        Assert.AreEqual(2, Number(run, "messageType"), "server_hello");
        Assert.AreEqual(0xc02f, Number(run, "chosenSuite"), "one of the eight offered");
        Assert.AreEqual(0, Number(run, "chosenCompression"));

        Assert.IsNull(Maybe(run, "cipherSuitesLength"), "and no vector was read here");
        Assert.AreEqual(0, Each(run, "suite").Count);
    }

    [TestMethod]
    public void And_the_arm_not_taken_says_it_is_not_there()
    {
        // Both arms are inside what the handshake length measures. A span waiting on a member the walk
        // stepped past waits for ever, so an arm not taken is absent rather than merely unreached.
        var client = Read(ClientHello);
        var server = Read(ServerHello);

        Assert.IsNull(Maybe(client, "chosenSuite"));
        Assert.IsNull(Maybe(server, "compressionMethodsLength"));

        Assert.AreEqual(104, Number(server, "handshakeLength"), "and the length still adds up");
    }

    [TestMethod]
    public void And_the_server_answers_some_of_what_was_offered_and_not_the_rest()
    {
        // Six extensions back from nine. Which the server declined is carried by the extensions that are
        // NOT there — status_request, supported_groups and signature_algorithms are all missing, and
        // nothing on the wire says so.
        var offered = Numbers(Read(ClientHello), "extensionType");
        var answered = Numbers(Read(ServerHello), "extensionType");

        CollectionAssert.AreEqual(
            new long[] { 0xff01, 0x0000, 0x000b, 0x0023, 0x0017, 0x0010 }, answered);

        foreach (long declined in new long[] { 0x0005, 0x000a, 0x000d })
        {
            Assert.IsTrue(offered.Contains(declined));
            Assert.IsFalse(answered.Contains(declined),
                "declined by omission, which is not something a message format can state");
        }
    }

    [TestMethod]
    public void And_the_session_id_it_sends_back_is_a_different_one()
    {
        // Equality with the client's would have meant resumption. It is a comparison across two messages,
        // so it lives in neither of them — the octets only carry the two ids.
        var client = Read(ClientHello);
        var server = Read(ServerHello);

        Assert.AreEqual(32, Bytes(server, "sessionId").Length);
        CollectionAssert.AreNotEqual(Bytes(client, "sessionId"), Bytes(server, "sessionId"),
            "a full handshake, not a resumption");
    }

    [TestMethod]
    public void A_server_hello_written_back_is_the_same_octets()
    {
        var tls = Tls();
        var read = new GraphCodec(tls.Graph).Decode(Capture(ServerHello));

        var asking = Common(read);
        asking["Cipher Suite"] = Maybe(read, "chosenSuite")!.Value;
        asking["Compression Method"] = Maybe(read, "chosenCompression")!.Value;

        CollectionAssert.AreEqual(Capture(ServerHello), new GraphCodec(tls.Graph).Encode(asking),
            "the other arm of the same description did not survive the round trip");
    }

    // ── Where the scope is stated ─────────────────────────────────────────────

    [TestMethod]
    public void A_record_that_is_not_a_handshake_is_refused()
    {
        // 0x17 is application_data, whose body is AEAD ciphertext. What is inside it is decided by a key,
        // which is not something a message format has.
        var encrypted = Capture(ClientHello).ToArray();
        encrypted[0] = 0x17;

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Tls().Graph).Decode(encrypted));
    }

    [TestMethod]
    public void A_handshake_message_that_is_not_a_hello_is_refused()
    {
        // 11 is Certificate, which travels in the same record as a ServerHello and is a different message
        // entirely. Read as a hello it would take the first octets of a DER certificate as a version.
        var certificate = Capture(ServerHello).ToArray();
        certificate[5] = 0x0b;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Tls().Graph).Decode(certificate));

        StringAssert.Contains(refused.Message, "Certificate");
    }

    [TestMethod]
    public void A_version_older_than_this_describes_is_refused()
    {
        var ssl3 = Capture(ClientHello).ToArray();
        ssl3[2] = 0x00;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Tls().Graph).Decode(ssl3));

        StringAssert.Contains(refused.Message, "SSL 3.0");
    }

    [TestMethod]
    public void A_session_id_longer_than_a_session_id_can_be_is_refused()
    {
        // RFC 5246 §7.4.1.2 writes it as opaque SessionID<0..32>. Believing a longer length would read
        // the cipher suites out of the middle of it.
        var wrong = Capture(ClientHello).ToArray();
        wrong[43] = 0x40;

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Tls().Graph).Decode(wrong));
    }
}
