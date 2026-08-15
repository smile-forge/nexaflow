using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// mDNS, against the corpus capture.
/// </summary>
/// <remarks>
/// <para>
/// The thing every DNS message is made of and no other protocol here has: a <b>name</b>, which is a run of
/// length-prefixed labels ending at a label whose length is zero. The terminator is not a sentinel beside
/// the run — it is the same field reading nothing, so the reading looks at the next octet before committing
/// to another label.
/// </para>
/// <para>
/// And a header whose second word is ten separate fields, nine of which RFC 6762 §18 requires to be zero in
/// a query. Each is a constant and a check: the constant says what is written, the check says what is
/// accepted, and they cannot disagree because they are the same number.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class MdnsCaptureTests
{
    private const int Query = 0;

    private static ProtocolFile.Loaded Mdns() => Definitions.Load("mdns");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("mdns").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Mdns().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static long Number(RunGraph run, string field) => Maybe(run, field)!.Value.AsInt();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    /// <summary>Every label read, question by question and label by label.</summary>
    /// <remarks>
    /// Two indices, because there are two repetitions: which name (the enclosing appearance) and which
    /// label of it. Ordering by the label's own index alone interleaves the questions — the first label of
    /// each, then the second of each — which is the shape of the thing rather than a quirk of the reading.
    /// </remarks>
    private static string[] Labels(RunGraph run)
        => [.. run.Nodes.Where(n => n.Of is Field { Id: "labelText" } && n.Has(Facet.Value))
                        .OrderBy(n => n.Within!.Index).ThenBy(n => n.Index)
                        .Select(n => n.Value.AsText())];

    /// <summary>One question, back in the shape a caller supplies.</summary>
    /// <remarks>
    /// The labels of a name are grouped by the appearance that encloses them, which the run graph already
    /// holds: a label's frame IS the run of labels it belongs to. Nothing here counts.
    /// </remarks>
    private static ProtoValue Asked(RunGraph run, int which)
    {
        var labels = run.Nodes.Where(n => n.Of is Field { Id: "labelText" } && n.Has(Facet.Value))
                              .GroupBy(n => n.Within!)
                              .Single(g => g.Key.Index == which);

        return EvalScope.Record(
            ("name", new ProtoValue.List([.. labels.OrderBy(n => n.Index).Select(n => n.Value)])),
            ("qtype", At(run, "qtype", which)),
            ("unicast", At(run, "qu", which)),
            ("qclass", At(run, "qclassValue", which)));
    }

    private static ProtoValue At(RunGraph run, string field, int which)
        => Each(run, field)[which].Value;

    // ── The capture ───────────────────────────────────────────────────────────

    [TestMethod]
    public void A_query_reads_its_header_as_the_breakdown_says()
    {
        var run = Read(Query);

        Assert.AreEqual(0, Number(run, "id"), "RFC 6762 §18.1 — zero in a multicast query");
        Assert.AreEqual(0, Number(run, "qr"), "a query");
        Assert.AreEqual(0, Number(run, "opcode"));
        Assert.AreEqual(0, Number(run, "tc"), "nothing follows this one");

        Assert.AreEqual(1, Number(run, "qdcount"));
        Assert.AreEqual(0, Number(run, "ancount"), "no known answers to suppress with");
        Assert.AreEqual(0, Number(run, "nscount"));
        Assert.AreEqual(0, Number(run, "arcount"));
    }

    [TestMethod]
    public void And_its_name_reads_as_labels()
    {
        // Three labels, then the root. The lengths are on the wire and the reading uses each one to size
        // the label after it — a length driving its immediate neighbour, once per time round.
        var run = Read(Query);

        CollectionAssert.AreEqual(new[] { "_http", "_tcp", "local" }, Labels(run));
        CollectionAssert.AreEqual(new long[] { 5, 4, 5 },
                                  Each(run, "labelLength").Select(n => n.Value.AsInt()).ToArray());

        Assert.AreEqual(0, Number(run, "root"), "and the run ends where a length says nothing follows");
    }

    [TestMethod]
    public void And_asks_for_a_PTR_of_the_internet_class()
    {
        var run = Read(Query);

        Assert.AreEqual(12, Number(run, "qtype"), "PTR — RFC 6763 §4.1's browse");
        Assert.AreEqual(1, Number(run, "qu"), "RFC 6762 §5.4 — answer me directly");
        Assert.AreEqual(1, Number(run, "qclassValue"), "IN");
    }

    [TestMethod]
    public void The_class_word_is_two_fields_and_not_one_number()
    {
        // 0x8001 is not a class. Reading it as one would need a note in prose saying the top bit means
        // something else, and nothing could check a note.
        var run = Read(Query);

        Assert.AreNotEqual(0x8001, Number(run, "qclassValue"));
        Assert.AreEqual(0x8001, (Number(run, "qu") << 15) | Number(run, "qclassValue"));
    }

    [TestMethod]
    public void The_reading_looks_at_the_next_octet_before_reading_another_label()
    {
        var mdns = Mdns();

        var looks = mdns.Graph.From<Identifies>(mdns.Named["moreLabels"]).Single();

        Assert.AreEqual("probe.labelLength", looks.To.Name);

        CollectionAssert.AreEquivalent(
            new[] { "root", "labelLength" },
            mdns.Graph.From<Decode>(mdns.Named["moreLabels"]).Select(w => w.To.Name).ToArray());
    }

    // ── Back out again ────────────────────────────────────────────────────────

    [TestMethod]
    public void A_capture_written_back_is_the_same_octets()
    {
        var mdns = Mdns();
        var read = new GraphCodec(mdns.Graph).Decode(Capture(Query));

        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal)
        {
            ["ID"] = ProtoValue.Of(Number(read, "id")),
            ["Truncated"] = ProtoValue.Of(Number(read, "tc")),
            ["Questions"] = new ProtoValue.List([Asked(read, 0)]),
        };

        CollectionAssert.AreEqual(Capture(Query), new GraphCodec(mdns.Graph).Encode(setting),
            "a browse for _http._tcp.local did not survive being read and written again");
    }

    [TestMethod]
    public void As_many_questions_as_the_count_says()
    {
        // RFC 6762 §5.1 permits several, and §7.3 makes a responder answer each. Two things are going
        // round here at once: the questions, ended by QDCOUNT, and the labels of each name, ended by a
        // length of nothing. Neither knows the other exists.
        var mdns = Mdns();

        var asked = new ProtoValue.List([
            Question(["_http", "_tcp", "local"], qtype: 12, unicast: 1),
            Question(["_ipp", "_tcp", "local"], qtype: 12, unicast: 0),
            Question(["nexaprint", "local"], qtype: 1, unicast: 0),
        ]);

        var octets = new GraphCodec(mdns.Graph).Encode(new Dictionary<string, ProtoValue>
        {
            ["ID"] = ProtoValue.Of(0),
            ["Truncated"] = ProtoValue.Of(0),
            ["Questions"] = asked,
        });

        var run = new GraphCodec(mdns.Graph).Decode(octets);

        Assert.AreEqual(3, Number(run, "qdcount"), "counted off the list rather than stated");

        CollectionAssert.AreEqual(
            new[] { "_http", "_tcp", "local", "_ipp", "_tcp", "local", "nexaprint", "local" },
            Labels(run));

        CollectionAssert.AreEqual(new long[] { 12, 12, 1 },
                                  Each(run, "qtype").Select(n => n.Value.AsInt()).ToArray());

        // And the labels belong to their own question, which is a fact the run graph holds rather than
        // one recovered by counting: a label's enclosing appearance IS the name it is part of.
        CollectionAssert.AreEqual(octets,
            new GraphCodec(mdns.Graph).Encode(new Dictionary<string, ProtoValue>
            {
                ["ID"] = ProtoValue.Of(0),
                ["Truncated"] = ProtoValue.Of(0),
                ["Questions"] = new ProtoValue.List([Asked(run, 0), Asked(run, 1), Asked(run, 2)]),
            }));
    }

    private static ProtoValue Question(string[] name, long qtype, long unicast)
        => EvalScope.Record(
               ("name", new ProtoValue.List([.. name.Select(ProtoValue.Of)])),
               ("qtype", ProtoValue.Of(qtype)),
               ("unicast", ProtoValue.Of(unicast)),
               ("qclass", ProtoValue.Of(1)));

    // ── What it says it does not do ───────────────────────────────────────────

    [TestMethod]
    public void A_query_carrying_known_answers_is_refused_rather_than_read_short()
    {
        // RFC 6762 §7.1 lets a querier list what it already holds so responders stay quiet. Those answers
        // are resource records, which this does not describe — so a query carrying them is refused where
        // it is written, which is the difference between a description that does not cover something and
        // one that quietly drops it.
        var withAnswers = Capture(Query).ToArray();
        withAnswers[7] = 1;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Mdns().Graph).Decode(withAnswers));

        StringAssert.Contains(refused.Message, "known-answer suppression");
    }

    [TestMethod]
    public void And_so_is_a_response()
    {
        var asResponse = Capture(Query).ToArray();
        asResponse[2] = 0x84;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(Mdns().Graph).Decode(asResponse));

        StringAssert.Contains(refused.Message, "QR=1 is the response");
    }
}
