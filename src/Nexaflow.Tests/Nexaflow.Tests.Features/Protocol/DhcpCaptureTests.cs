using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// DHCP, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// The option region is TCP's shape met again — a repetition with no count anywhere — with the two
/// additions that defeated the first grammar: a <b>sentinel</b> that ends the region before the octets run
/// out, and <b>bare</b> codes carrying neither a length nor a value. Point a fixed code-length-value walker
/// at a DISCOVER and it reads the END's code, takes the pad octet after it as a length, and returns a
/// message that is plausible and wrong.
/// </para>
/// <para>
/// The two captures also disagree about what follows the sentinel — 33 octets of fill in one, none in the
/// other — which is the pair of facts no fixed-size option block can hold at once.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class DhcpCaptureTests
{
    private const int Discover = 0, Offer = 1;

    private static ProtocolFile.Loaded Dhcp() => Definitions.Load("dhcp");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("dhcp").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Dhcp().Graph).Decode(Capture(which));

    /// <summary>
    /// A field that turns up once, at whatever round the walk was on when it got there.
    /// </summary>
    /// <remarks>
    /// Not necessarily round zero. A reading goes round the option region on its own edge, and its rounds
    /// carry on past the end of it — so the fill after the END option is settled on the walk's fifth
    /// appearance rather than its first, having followed four options.
    /// </remarks>
    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field).OrderBy(n => n.Index).FirstOrDefault();

    private static RunNode One(RunGraph run, string field) => Maybe(run, field)!;

    private static long Number(RunGraph run, string field) => One(run, field).Value.AsInt();

    private static string Hex(RunGraph run, string field)
        => Convert.ToHexString(One(run, field).Value.AsBytes());

    /// <summary>A field that converts, read the way anyone setting it would write it.</summary>
    private static string Said(RunGraph run, string field) => One(run, field).Value.AsText();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    private static long[] Codes(RunGraph run)
        => [.. Each(run, "optionCode").Select(n => n.Value.AsInt())];

    // ── The description ───────────────────────────────────────────────────────

    [TestMethod]
    public void One_message_format_because_the_RFC_draws_one()
    {
        // RFC 2131 §2 Figure 1, singular. DISCOVER and OFFER are two sets of values in one format, told
        // apart by option 53 — a value inside a list, at a position the sender chooses. Making them two
        // messages would put a fork where the specification has a field.
        var dhcp = Dhcp();

        CollectionAssert.AreEqual(new[] { "dhcpMessage" }, dhcp.Messages.Keys.ToArray());
        Assert.AreEqual(0, dhcp.Graph.Of<Identifies>().Count(),
            "nothing reads ahead, because the discriminator is not on a path");
    }

    [TestMethod]
    public void An_option_is_described_by_its_shape_and_there_are_three()
    {
        // RFC 2132 §2: bare PAD, bare END, and code-length-value. Enumerating the codes this file knows
        // would refuse every option it does not — and get the fill after the END wrong as well.
        var dhcp = Dhcp();

        var shapes = dhcp.Graph.From<Then>(dhcp.Named["optionShape"]).ToList();

        Assert.AreEqual(3, shapes.Count);
        CollectionAssert.AreEquivalent(new long[] { 0, 1, 2 },
                                       shapes.Select(a => a.Key!.AsInt()).ToArray());

        Assert.AreEqual(1, dhcp.Graph.Nodes.Count(n => n is Field { Id: "optionCode" }),
            "and one code field serves all three, because it is one concept");
    }

    [TestMethod]
    public void The_hardware_address_length_is_derived_rather_than_supplied()
    {
        // hlen and chaddr can disagree on a wire, and a message where they do is one whose padding is read
        // as part of the address. Here hlen is the length of the address, so there is one fact.
        var dhcp = Dhcp();

        Assert.IsNull(dhcp.Named.Keys.FirstOrDefault(k => k == "input.hlen"),
            "nobody supplies hlen");

        var produces = dhcp.Graph.ProducerOf(dhcp.Named["hlen"], "value");
        Assert.IsInstanceOfType<Evaluated>(produces);
        StringAssert.Contains(((Evaluated)produces!).Runs.Render(), "hardwareAddress");
    }

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void A_discover_reads_as_the_breakdown_says()
    {
        var run = Read(Discover);

        Assert.AreEqual(1, Number(run, "op"), "BOOTREQUEST");
        Assert.AreEqual(1, Number(run, "htype"), "Ethernet");
        Assert.AreEqual(6, Number(run, "hlen"));
        Assert.AreEqual(0, Number(run, "hops"));
        Assert.AreEqual(0x3903f326, Number(run, "xid"));
        Assert.AreEqual(0, Number(run, "secs"));
        Assert.AreEqual(0, Number(run, "broadcastFlag"), "a unicast reply was asked for");
        Assert.AreEqual(0x63825363, Number(run, "magicCookie"));

        Assert.AreEqual("00:0b:82:01:fc:42", Said(run, "hardwareAddress"),
            "a hardware address, because the field says it converts — not six octets to be decoded by "
            + "whoever asked");
        Assert.AreEqual(10, One(run, "chaddrPad").Value.AsBytes().Length, "sixteen less the six used");
        Assert.AreEqual("0.0.0.0", Said(run, "ciaddr"), "it has no address yet — that is what it is asking for");
    }

    [TestMethod]
    public void The_broadcast_bit_and_its_fifteen_companions_span_two_octets()
    {
        // A run of bits is not confined to an octet. What occupies whole octets is the set that holds them,
        // which is the same rule that lets TCP's four-bit Data Offset be a field.
        var dhcp = Dhcp();

        Assert.AreEqual(1, ((Field)dhcp.Named["broadcastFlag"]).Form.FixedBits);
        Assert.AreEqual(15, ((Field)dhcp.Named["flagsReserved"]).Form.FixedBits);

        Assert.AreEqual(10, One(Read(Discover), "flagsReserved").Settled(Facet.Position),
            "and both sit in the two octets at ten — the second one starts mid-octet");
    }

    [TestMethod]
    public void A_discover_reads_its_four_options_and_the_end()
    {
        // 53 Message Type, 61 Client Identifier, 12 Host Name, 55 Parameter Request List, 255 END. No count
        // appears anywhere in the message, and the widths differ, so nothing could have derived one.
        var run = Read(Discover);

        CollectionAssert.AreEqual(new long[] { 53, 61, 12, 55, 255 }, Codes(run));

        var values = Each(run, "optionValue");
        Assert.AreEqual(4, values.Count, "the END carries no value, because it has no room for one");

        Assert.AreEqual("01", Convert.ToHexString(values[0].Value.AsBytes()), "DHCPDISCOVER");
        Assert.AreEqual("nexa01", Encoding.ASCII.GetString(values[2].Value.AsBytes()));
        Assert.AreEqual("0103062A", Convert.ToHexString(values[3].Value.AsBytes()),
            "subnet mask, router, DNS, NTP — and NTP is the one the offer will silently not answer");
    }

    [TestMethod]
    public void An_offer_reads_as_the_breakdown_says()
    {
        var run = Read(Offer);

        Assert.AreEqual(2, Number(run, "op"), "BOOTREPLY");
        Assert.AreEqual(0x3903f326, Number(run, "xid"), "echoed, which is what pairs it with the discover");
        Assert.AreEqual("192.168.0.10", Said(run, "yiaddr"), "the address being offered");
        Assert.AreEqual("192.168.0.1", Said(run, "siaddr"));

        StringAssert.StartsWith(Encoding.ASCII.GetString(One(run, "sname").Value.AsBytes()), "dhcp.nexa.lan");
        Assert.AreEqual(64, One(run, "sname").Value.AsBytes().Length, "the field is sixty-four whatever it holds");

        CollectionAssert.AreEqual(new long[] { 53, 54, 51, 58, 59, 1, 3, 6, 15, 28, 255 }, Codes(run));
    }

    [TestMethod]
    public void The_fill_after_the_end_option_is_there_in_one_and_not_the_other()
    {
        // The pair of facts no fixed-size option block can hold at once. 236 + 4 + 27 = 267, so a DISCOVER
        // is padded to the 300 of RFC 1542 §2.1; an OFFER at 306 is already past it.
        Assert.AreEqual(33, One(Read(Discover), "trailer").Value.AsBytes().Length);

        var offer = Maybe(Read(Offer), "trailer");
        Assert.IsTrue(offer is null || offer.Settled(Facet.Present) is false,
            "and the offer has none at all");
    }

    [TestMethod]
    public void A_message_whose_cookie_is_wrong_is_refused()
    {
        // Four octets with one legal value. Skipping past them unchecked accepts bare BOOTP, an unrelated
        // datagram, or a truncated frame, and calls all three DHCP.
        var octets = Capture(Discover);
        octets[236] = 0x00;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Dhcp().Graph).Decode(octets));

        StringAssert.Contains(refused.Message, "magic cookie");
    }

    // ── Back out again ────────────────────────────────────────────────────────

    [DataTestMethod]
    [DataRow(Discover, "a DHCPDISCOVER padded to three hundred octets")]
    [DataRow(Offer, "a DHCPOFFER with ten options and no fill")]
    public void A_capture_written_back_is_the_same_octets(int which, string what)
    {
        var dhcp = Dhcp();
        var read = new GraphCodec(dhcp.Graph).Decode(Capture(which));

        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal);

        foreach (var field in dhcp.Graph.Nodes.OfType<Field>())
            if (Maybe(read, field.Id) is { } found && found.Has(Facet.Value))
                setting[field.As ?? field.Id] = found.Value;

        // The options, as the list the region is written once per item of. Its length is the only thing
        // that says how many there are — no octet in either capture does.
        var values = Each(read, "optionValue");

        setting["Options"] = new ProtoValue.List(
            [.. Each(read, "optionCode").Select(code => EvalScope.Record(
                   ("code", code.Value),
                   ("value", values.FirstOrDefault(v => v.Index == code.Index)?.Value
                             ?? ProtoValue.Of(Array.Empty<byte>()))))]);

        CollectionAssert.AreEqual(Capture(which), new GraphCodec(dhcp.Graph).Encode(setting),
            $"{what} did not survive being read and written again");
    }
}
