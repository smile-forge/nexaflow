using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// SNMPv2c, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// The first description here whose <b>lengths are not a width the author chose</b>. A BER length is one
/// octet under 128 and 0x80+n followed by n octets otherwise, so how wide a length field is depends on the
/// number it holds — which depends on how wide the lengths inside it are. The same document writes the
/// request's five short-form lengths and the response's four long ones.
/// </para>
/// <para>
/// And the finding that is an absence: SNMP has no discriminated union. GetRequest and GetResponse differ
/// in one octet and nothing else, and a varbind's value is a tag, a length and that many octets whatever
/// the type is. What the octets mean lives in a MIB.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class SnmpCaptureTests
{
    private const int Request = 0;
    private const int Response = 1;

    private static ProtocolFile.Loaded Snmp() => Definitions.Load("snmp");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("snmp").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Snmp().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static long Number(RunGraph run, string field) => Maybe(run, field)!.Value.AsInt();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    /// <summary>How many octets a field turned out to occupy.</summary>
    private static long Wide(RunGraph run, string field, int which = 0)
        => Convert.ToInt64(Each(run, field)[which].Settled(Facet.Extent));

    /// <summary>The arcs of one binding's name, in order.</summary>
    /// <remarks>
    /// An arc's frame IS the run of arcs it belongs to, and that run's own index is which binding it is
    /// the name of. So nothing here counts — the grouping the run graph already holds is the grouping
    /// wanted, two levels deep.
    /// </remarks>
    private static IReadOnlyList<RunNode> Arcs(RunGraph run, int which)
        => [.. run.Nodes.Where(n => n.Of is Field { Id: "arc" } && n.Has(Facet.Value))
                        .Where(n => n.Within!.Index == which)
                        .OrderBy(n => n.Index)];

    /// <summary>One binding's name, back in the dotted form a caller hands in.</summary>
    private static string Name(RunGraph run, int which)
    {
        long merged = Each(run, "oidFirst")[which].Value.AsInt();

        return string.Join('.', new[] { merged / 40, merged % 40 }
                                    .Concat(Arcs(run, which).Select(n => n.Value.AsInt())));
    }

    /// <summary>The octets a field came off the wire as, which for a converted one is not its value.</summary>
    private static byte[] Octets(RunGraph run, string field, int which)
        => ((ProtoValue)Each(run, field)[which].Settled(Facet.Emitted)!).AsBytes();

    /// <summary>One binding, back in the shape a caller supplies.</summary>
    private static ProtoValue Bound(RunGraph run, int which)
        => EvalScope.Record(
            ("oid", ProtoValue.Of(Name(run, which))),
            ("tag", Each(run, "valueTag")[which].Value),
            ("value", Each(run, "value")[which].Value));

    private static ProtoValue Bound(string oid, long tag, byte[] value)
        => EvalScope.Record(("oid", ProtoValue.Of(oid)), ("tag", ProtoValue.Of(tag)),
                            ("value", ProtoValue.Of(value)));

    /// <summary>Everything a message needs, taken off one that was read.</summary>
    private static Dictionary<string, ProtoValue> Asking(RunGraph run, int bindings)
        => new(StringComparer.Ordinal)
        {
            ["Community"] = Maybe(run, "community")!.Value,
            ["PDU Tag"] = Maybe(run, "pduTag")!.Value,
            ["Request ID"] = Maybe(run, "requestId")!.Value,
            ["Error Status"] = Maybe(run, "errorStatus")!.Value,
            ["Error Index"] = Maybe(run, "errorIndex")!.Value,
            ["Varbinds"] = new ProtoValue.List(
                [.. Enumerable.Range(0, bindings).Select(i => Bound(run, i))]),
        };

    // ── C1: the request ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_request_reads_its_wrapper_as_the_breakdown_says()
    {
        var run = Read(Request);

        Assert.AreEqual(71, Number(run, "msgLen"), "everything after the first two octets");
        Assert.AreEqual(1, Wide(run, "msgLen"), "short form — one octet says it all");

        Assert.AreEqual(1, Number(run, "version"), "RFC 3416 §3 — v2c writes 1, not 2");
        Assert.AreEqual("public", Maybe(run, "community")!.Value.AsText());

        Assert.AreEqual(0xa0, Number(run, "pduTag"), "GetRequest-PDU");
        Assert.AreEqual(821915, Number(run, "requestId"));
        Assert.AreEqual(0, Number(run, "errorStatus"), "noError");
        Assert.AreEqual(0, Number(run, "errorIndex"));
    }

    [TestMethod]
    public void And_a_request_id_is_as_many_octets_as_it_needs()
    {
        // X.690 §8.3.2 forbids a leading octet that could be dropped, so 821915 is three octets and not
        // four — 0x0c has its top bit clear, so nothing has to precede it to keep the sign.
        var run = Read(Request);

        Assert.AreEqual(3, Number(run, "requestIdLen"));
        Assert.AreEqual("0C8A9B", Convert.ToHexString(Octets(run, "requestId", 0)));

        Assert.AreEqual(1, Number(run, "errorStatusLen"), "and nought is one octet, not none");
        Assert.AreEqual("00", Convert.ToHexString(Octets(run, "errorStatus", 0)));
    }

    [TestMethod]
    public void And_its_names_read_as_arcs()
    {
        // Three bindings, each a run of arcs inside the run of bindings. The first subidentifier of each
        // is 0x2b, which is 40*1+3 — X.690 §8.19.4 merges the leading pair.
        var run = Read(Request);

        Assert.AreEqual("1.3.6.1.2.1.1.1.0", Name(run, 0), "sysDescr.0");
        Assert.AreEqual("1.3.6.1.2.1.1.3.0", Name(run, 1), "sysUpTime.0");
        Assert.AreEqual("1.3.6.1.4.1.2021.10.1.3.1", Name(run, 2), "UCD-SNMP laLoad.1");

        CollectionAssert.AreEqual(new long[] { 43, 43, 43 },
                                  Each(run, "oidFirst").Select(n => n.Value.AsInt()).ToArray());
    }

    [TestMethod]
    public void And_an_arc_too_big_for_seven_bits_spills_into_another_octet()
    {
        // 2021 is 8f 65 — fifteen groups of 128 plus 101, with the top bit marking that more follows.
        // Nothing says how wide an arc is; the octets do.
        var run = Read(Request);
        var third = Arcs(run, 2);

        Assert.AreEqual(2021, third[4].Value.AsInt());
        Assert.AreEqual(2, Convert.ToInt64(third[4].Settled(Facet.Extent)), "two octets for one arc");
        Assert.AreEqual(1, Convert.ToInt64(third[5].Settled(Facet.Extent)), "and one for the next");
    }

    [TestMethod]
    public void And_every_value_it_asks_with_is_an_absence()
    {
        // A GetRequest carries the shape of an answer with nothing in it: tag 5, length 0, no octets.
        // Which is why 05 00 is two octets — a length is written even when there is nothing to measure.
        var run = Read(Request);

        CollectionAssert.AreEqual(new long[] { 5, 5, 5 },
                                  Each(run, "valueTag").Select(n => n.Value.AsInt()).ToArray());
        CollectionAssert.AreEqual(new long[] { 0, 0, 0 },
                                  Each(run, "valueLen").Select(n => n.Value.AsInt()).ToArray());
        Assert.AreEqual(0, Octets(run, "value", 0).Length);
    }

    [TestMethod]
    public void A_request_written_back_is_the_same_octets()
    {
        var snmp = Snmp();
        var read = new GraphCodec(snmp.Graph).Decode(Capture(Request));

        CollectionAssert.AreEqual(Capture(Request),
                                  new GraphCodec(snmp.Graph).Encode(Asking(read, 3)),
                                  "a three-binding GetRequest did not survive being read and written");
    }

    // ── C2: the response ──────────────────────────────────────────────────────

    [TestMethod]
    public void A_response_writes_the_lengths_that_did_not_fit_in_one_octet()
    {
        // The same four fields as the request, and every one of them a different width. 0x82 is not a
        // length of 130 — it is "two octets follow", and they hold the number.
        var run = Read(Response);

        Assert.AreEqual(333, Number(run, "msgLen"));
        Assert.AreEqual(3, Wide(run, "msgLen"), "82 01 4d");

        Assert.AreEqual(318, Number(run, "pduLen"));
        Assert.AreEqual(3, Wide(run, "pduLen"));

        Assert.AreEqual(303, Number(run, "varbindsLen"));
        Assert.AreEqual(3, Wide(run, "varbindsLen"));

        Assert.AreEqual(247, Number(run, "valueLen"), "the description string");
        Assert.AreEqual(2, Wide(run, "valueLen"), "81 f7 — one octet follows");
    }

    [TestMethod]
    public void And_a_short_length_beside_a_long_one_is_still_short()
    {
        // Nothing widens in sympathy. The second binding is sixteen octets and says so in one, in the
        // same message whose outer length took three.
        var run = Read(Response);

        CollectionAssert.AreEqual(new long[] { 260, 16, 19 },
                                  Each(run, "varbindLen").Select(n => n.Value.AsInt()).ToArray());
        CollectionAssert.AreEqual(new long[] { 3, 1, 1 },
                                  Each(run, "varbindLen").Select(n => Convert.ToInt64(n.Settled(Facet.Extent))).ToArray());
    }

    [TestMethod]
    public void And_its_values_are_octets_that_only_a_tag_describes()
    {
        // The description stops at "a tag, a length, and that many octets". What sysDescr.0 IS — text —
        // and what sysUpTime.0 is — hundredths of a second — comes from a MIB, so the test does the
        // reading a caller would.
        var run = Read(Response);

        CollectionAssert.AreEqual(new long[] { 0x04, 0x43, 0x04 },
                                  Each(run, "valueTag").Select(n => n.Value.AsInt()).ToArray());

        var described = Encoding.ASCII.GetString(Octets(run, "value", 0));
        StringAssert.StartsWith(described, "Cisco IOS Software, C2960 Software");
        Assert.AreEqual(247, described.Length);

        Assert.AreEqual("0094A1B2", Convert.ToHexString(Octets(run, "value", 1)),
            "TimeTicks 9740722 — unsigned, so the leading 00 is required and the value is not four "
          + "significant octets");

        Assert.AreEqual("0.31", Encoding.ASCII.GetString(Octets(run, "value", 2)));
    }

    [TestMethod]
    public void And_the_names_came_back_the_ones_that_were_asked_for()
    {
        var asked = Read(Request);
        var answered = Read(Response);

        for (int i = 0; i < 3; i++) Assert.AreEqual(Name(asked, i), Name(answered, i));

        Assert.AreEqual(Number(asked, "requestId"), Number(answered, "requestId"),
            "and the only thing tying the two together is that number");
    }

    [TestMethod]
    public void A_response_written_back_is_the_same_octets()
    {
        var snmp = Snmp();
        var read = new GraphCodec(snmp.Graph).Decode(Capture(Response));

        CollectionAssert.AreEqual(Capture(Response),
                                  new GraphCodec(snmp.Graph).Encode(Asking(read, 3)),
                                  "a response with four long-form lengths did not survive the round trip");
    }

    // ── What a length octet's own width does to the ones around it ────────────

    [TestMethod]
    public void A_length_field_widens_and_every_length_outside_it_counts_the_extra_octet()
    {
        // The claim a back-patch pass over fixed-width placeholders cannot meet: one more octet of
        // payload sometimes costs two octets of message, because a length crossed a width boundary and
        // its own extra octet is counted by every length enclosing it.
        //
        // Four of them cross in the space of forty-four payload sizes, and each is a DIFFERENT length:
        // the PDU's at 101, the varbind list's at 114, the binding's at 116, and the value's own at 128.
        var snmp = Snmp();
        var sizes = Enumerable.Range(100, 45).ToDictionary(n => n, n => Sized(snmp, n));

        var jumped = Enumerable.Range(101, 44).Where(n => sizes[n] - sizes[n - 1] == 2).ToArray();

        CollectionAssert.AreEqual(new[] { 101, 114, 116, 128 }, jumped,
            "four nested lengths widen at four different payload sizes");

        foreach (int n in Enumerable.Range(101, 44))
            Assert.IsTrue(sizes[n] - sizes[n - 1] is 1 or 2,
                $"one more octet of payload grew the message by {sizes[n] - sizes[n - 1]} at {n}");
    }

    private static int Sized(ProtocolFile.Loaded snmp, int octets)
        => new GraphCodec(snmp.Graph).Encode(new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
        {
            ["Community"] = ProtoValue.Of("public"),
            ["PDU Tag"] = ProtoValue.Of(0xa2),
            ["Request ID"] = ProtoValue.Of(821915),
            ["Error Status"] = ProtoValue.Of(0),
            ["Error Index"] = ProtoValue.Of(0),
            ["Varbinds"] = new ProtoValue.List(
                [Bound("1.3.6.1.2.1.1.1.0", 0x04, new byte[octets])]),
        }).Length;

    // ── Where the scope is stated ─────────────────────────────────────────────

    [TestMethod]
    public void An_operation_this_does_not_describe_is_refused()
    {
        // 0xa1 is GetNext, which returns the object AFTER the one named. Same seven fields, entirely
        // different meaning — so it is refused where it is written rather than read as a Get.
        var next = Capture(Request).ToArray();
        next[13] = 0xa1;

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Snmp().Graph).Decode(next));
    }

    [TestMethod]
    public void A_version_this_does_not_describe_is_refused()
    {
        var v1 = Capture(Request).ToArray();
        v1[4] = 0x00;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Snmp().Graph).Decode(v1));

        StringAssert.Contains(refused.Message, "SNMPv1");
    }

    [TestMethod]
    public void A_value_tag_SNMP_does_not_define_is_refused()
    {
        // 0x30 is a SEQUENCE, which is a legal BER tag and not a legal ObjectSyntax. A reading that let
        // it through would size the next span from a claim it had no reason to believe.
        var nested = Capture(Request).ToArray();
        nested[40] = 0x30;

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Snmp().Graph).Decode(nested));
    }
}
