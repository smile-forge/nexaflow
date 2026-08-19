using System.Text;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// CoAP, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// The option encoding that no other protocol here has: an option does not carry its own number. §3.1 gives
/// the delta from the previous one, so the same octets are a different option depending on what preceded
/// them — and going out the delta has to be worked out from the list and this option's place in it.
/// </para>
/// <para>
/// Both nibbles escape, and one of the escapes is not a value at all: fifteen marks the payload. So where
/// the options end is decided by looking at the next octet before committing to reading another one.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class CoapCaptureTests
{
    private const int Get = 0, Content = 1, Escaped = 2;

    private static ProtocolFile.Loaded Coap() => Definitions.Load("coap");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("coap").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Coap().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static long Number(RunGraph run, string field) => Maybe(run, field)!.Value.AsInt();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    /// <summary>
    /// The option numbers, summed back out of the deltas the way §3.1 says.
    /// </summary>
    /// <remarks>
    /// The reading gives deltas, because deltas are what the octets hold. Turning them into numbers is a
    /// running total across the options, which is a thing the caller keeps rather than a fact about any one
    /// of them — so it is here, in the host, and not in the description.
    /// </remarks>
    private static IReadOnlyList<(long Number, byte[] Value)> Options(RunGraph run)
    {
        List<(long, byte[])> options = [];
        long running = 0;

        foreach (var nibble in Each(run, "optionDeltaNibble"))
        {
            long delta = nibble.Value.AsInt() switch
            {
                13 => At(run, "optionDeltaExt8", nibble.Index) + 13,
                14 => At(run, "optionDeltaExt16", nibble.Index) + 269,
                var plain => plain,
            };

            running += delta;
            options.Add((running, Each(run, "optionValue").Single(v => v.Index == nibble.Index)
                                                          .Value.AsBytes()));
        }

        return options;
    }

    private static long At(RunGraph run, string field, int index)
        => run.Nodes.Single(n => n.Of is Field f && f.Id == field && n.Index == index).Value.AsInt();

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void A_get_reads_as_the_breakdown_says()
    {
        var run = Read(Get);

        Assert.AreEqual(1, Number(run, "version"));
        Assert.AreEqual(0, Number(run, "type"), "Confirmable");
        Assert.AreEqual(4, Number(run, "tokenLength"));
        Assert.AreEqual(0, Number(run, "codeClass"));
        Assert.AreEqual(1, Number(run, "codeDetail"), "0.01 GET");
        Assert.AreEqual(0x1234, Number(run, "messageId"));

        Assert.AreEqual("9AF31C08", Convert.ToHexString(Maybe(run, "token")!.Value.AsBytes()));
    }

    [TestMethod]
    public void And_its_six_options_sum_to_the_numbers_they_mean()
    {
        // The delta encoding, read forwards. Two Uri-Paths at 11, two Uri-Querys at 15, an Accept at 17,
        // and an Echo at 252 — and only the last of those needed an escape to say so.
        var options = Options(Read(Get));

        CollectionAssert.AreEqual(new long[] { 11, 11, 15, 15, 17, 252 },
                                  options.Select(o => o.Number).ToArray());

        Assert.AreEqual("sensors", Encoding.ASCII.GetString(options[0].Value));
        Assert.AreEqual("temperature", Encoding.ASCII.GetString(options[1].Value));
        Assert.AreEqual("unit=celsius", Encoding.ASCII.GetString(options[2].Value));
        Assert.AreEqual("since=2026-08-09T10:15:00Z", Encoding.ASCII.GetString(options[3].Value),
            "twenty-six octets, which is past what a nibble holds");
    }

    [TestMethod]
    public void A_nibble_of_thirteen_means_an_octet_follows()
    {
        // Both nibbles escape and the capture exercises both: option four's LENGTH is 26, and option
        // six's DELTA is 235. Each is thirteen in the nibble and the rest in an octet of its own.
        var run = Read(Get);

        Assert.AreEqual(13, At(run, "optionLengthNibble", 3));
        Assert.AreEqual(13, At(run, "optionLengthExt8", 3), "and 13 + 13 is the twenty-six octets it has");

        Assert.AreEqual(13, At(run, "optionDeltaNibble", 5));
        Assert.AreEqual(222, At(run, "optionDeltaExt8", 5), "222 + 13 = 235, and 17 + 235 is 252");
    }

    [TestMethod]
    public void A_nibble_of_fourteen_means_two_octets_follow()
    {
        var run = Read(Escaped);

        Assert.AreEqual(0, Number(run, "tokenLength"), "and no token octets follow at all");
        Assert.IsNull(Maybe(run, "token"), "genuinely absent rather than present and empty");

        Assert.AreEqual(14, At(run, "optionDeltaNibble", 1));
        Assert.AreEqual(1769, At(run, "optionDeltaExt16", 1), "1769 + 269 = 2038, and 11 + 2038 is 2049");

        CollectionAssert.AreEqual(new long[] { 11, 2049 }, Options(run).Select(o => o.Number).ToArray());
    }

    [TestMethod]
    public void A_response_reads_its_payload_after_the_marker()
    {
        var run = Read(Content);

        Assert.AreEqual(2, Number(run, "type"), "Acknowledgement");
        Assert.AreEqual(2, Number(run, "codeClass"));
        Assert.AreEqual(5, Number(run, "codeDetail"), "2.05 Content");
        Assert.AreEqual(0x1234, Number(run, "messageId"), "the same id the request carried");

        CollectionAssert.AreEqual(new long[] { 4, 12, 14 }, Options(run).Select(o => o.Number).ToArray());

        Assert.AreEqual(0xFF, Number(run, "payloadMarker"));
        Assert.AreEqual("{\"temp\":21.5,\"unit\":\"C\"}",
                        Encoding.ASCII.GetString(Maybe(run, "payload")!.Value.AsBytes()));
    }

    [TestMethod]
    public void A_message_whose_options_run_to_the_end_has_no_marker_and_no_payload()
    {
        // The pair of facts the reading has to tell apart without a count: a GET's options simply run out,
        // and a response's end at an octet that would otherwise read as an option header.
        var run = Read(Get);

        Assert.IsNull(Maybe(run, "payloadMarker"));
        Assert.IsNull(Maybe(run, "payload"));
    }

    [TestMethod]
    public void The_reading_looks_at_the_next_octet_before_reading_another_option()
    {
        var coap = Coap();

        var looks = coap.Graph.From<Identifies>(coap.Named["optionWhich"]).Single();

        Assert.AreEqual("probe.optionHeader", looks.To.Name);

        // 0xFF is not an option header — §3.1 reserves a delta of fifteen — so it is the one value that
        // takes the other way on.
        CollectionAssert.AreEquivalent(
            new[] { "payloadMarker", "option" },
            coap.Graph.From<Decode>(coap.Named["optionWhich"]).Select(w => w.To.Name).ToArray());
    }

    // ── Back out again ────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(Get, "a GET with six options and two escapes")]
    [DataRow(Content, "an ACK with three options and a payload")]
    [DataRow(Escaped, "a GET with no token and a sixteen-bit delta")]
    public void A_capture_written_back_is_the_same_octets(int which, string what)
    {
        var coap = Coap();
        var read = new GraphCodec(coap.Graph).Decode(Capture(which));

        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal)
        {
            ["T"] = ProtoValue.Of(Number(read, "type")),
            ["class"] = ProtoValue.Of(Number(read, "codeClass")),
            ["detail"] = ProtoValue.Of(Number(read, "codeDetail")),
            ["Message ID"] = ProtoValue.Of(Number(read, "messageId")),
            ["Token"] = Maybe(read, "token")?.Value ?? ProtoValue.Of(Array.Empty<byte>()),
            ["Payload"] = Maybe(read, "payload")?.Value ?? ProtoValue.Of(Array.Empty<byte>()),

            // The numbers, not the deltas — which is what a caller has and what §5.10 assigns. Working the
            // deltas back out is the description's job, and it is the one calculation here that needs to
            // see more than its own item.
            ["Options"] = new ProtoValue.List(
                [.. Options(read).Select(o => EvalScope.Record(
                       ("number", ProtoValue.Of(o.Number)), ("value", ProtoValue.Of(o.Value))))]),
        };

        CollectionAssert.AreEqual(Capture(which), new GraphCodec(coap.Graph).Encode(setting),
            $"{what} did not survive being read and written again");
    }
}
