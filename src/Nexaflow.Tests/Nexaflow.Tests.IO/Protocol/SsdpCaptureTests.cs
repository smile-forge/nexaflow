using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// SSDP, against the corpus captures.
/// </summary>
/// <remarks>
/// <para>
/// The opposite of everything else here: no length field, no fixed width, and no number written as a
/// number. Every span ends because a <b>separator begins</b> — and that needs no field kind of its own. A
/// span with no width whose next node holds a constant runs up to where that constant starts, so the
/// separator stays the next node's own value and is written back out by the thing that owns it.
/// </para>
/// <para>
/// Which means there is almost nothing here a description can refuse. That is the finding rather than an
/// omission: after a separator, everything is text.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class SsdpCaptureTests
{
    private const int Search = 0;
    private const int Answer = 1;

    private static ProtocolFile.Loaded Ssdp() => Definitions.Load("ssdp");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("ssdp").Captures[which].Bytes;

    private static RunGraph Read(int which) => new GraphCodec(Ssdp().Graph).Decode(Capture(which));

    private static RunNode? Maybe(RunGraph run, string field)
        => run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                    .OrderBy(n => n.Index).FirstOrDefault();

    private static string Said(RunGraph run, string field) => Maybe(run, field)!.Value.AsText();

    private static IReadOnlyList<RunNode> Each(RunGraph run, string field)
        => [.. run.Nodes.Where(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value))
                        .OrderBy(n => n.Index)];

    /// <summary>The headers, in wire order, exactly as they were written.</summary>
    private static (string Name, string Value)[] Headers(RunGraph run)
        => [.. Each(run, "headerName").Zip(Each(run, "headerValue"),
                                           (n, v) => (n.Value.AsText(), v.Value.AsText()))];

    private static ProtoValue[] AsRecords(RunGraph run)
        => [.. Headers(run).Select(h => EvalScope.Record(("name", ProtoValue.Of(h.Name)),
                                                         ("value", ProtoValue.Of(h.Value))))];

    // ── C1: the search ────────────────────────────────────────────────────────

    [TestMethod]
    public void A_search_reads_a_start_line_of_three_tokens()
    {
        var run = Read(Search);

        Assert.AreEqual("M-SEARCH", Said(run, "method"));
        Assert.AreEqual("*", Said(run, "target"), "UPnP 1.1 §1.3.2 — there is no resource being named");
        Assert.AreEqual("HTTP/1.1", Said(run, "httpVersion"));

        Assert.IsNull(Maybe(run, "statusVersion"), "and the other shape of start line is not there");
    }

    [TestMethod]
    public void And_every_span_ends_where_a_separator_begins()
    {
        // Nothing measured any of these. `method` is eight octets because a space follows it, `target` is
        // one because a space follows it, and `httpVersion` is eight because a line ending does.
        var run = Read(Search);

        Assert.AreEqual(8, Convert.ToInt64(Maybe(run, "method")!.Settled(Facet.Extent)));
        Assert.AreEqual(1, Convert.ToInt64(Maybe(run, "target")!.Settled(Facet.Extent)));
        Assert.AreEqual(8, Convert.ToInt64(Maybe(run, "httpVersion")!.Settled(Facet.Extent)));

        Assert.AreEqual(" ", Said(run, "methodSpace"), "and the separator is still its own node's value");
        Assert.AreEqual("\r\n", Said(run, "startCrlf"));
    }

    [TestMethod]
    public void And_the_headers_run_until_a_line_with_nothing_on_it()
    {
        // Five of them, and nothing anywhere says five: not counted, not bounded, and not derivable from
        // anything in front of them.
        var run = Read(Search);

        CollectionAssert.AreEqual(
            new[] { "HOST", "MAN", "MX", "ST", "USER-AGENT" },
            Headers(run).Select(h => h.Name).ToArray());

        Assert.AreEqual("\r\n", Said(run, "blankLine"), "and then a line that begins by ending");
    }

    [TestMethod]
    public void And_a_value_may_hold_as_many_colons_as_it_likes()
    {
        // The name ends at the FIRST colon. HOST's value has one more and LOCATION's has two.
        var search = Read(Search);
        var answer = Read(Answer);

        Assert.AreEqual(" 239.255.255.250:1900", Headers(search).Single(h => h.Name == "HOST").Value);
        Assert.AreEqual(" \"ssdp:discover\"", Headers(search).Single(h => h.Name == "MAN").Value);
        Assert.AreEqual(" http://192.168.1.42:49152/description.xml",
                        Headers(answer).Single(h => h.Name == "LOCATION").Value);
    }

    [TestMethod]
    public void And_a_value_is_kept_exactly_as_it_was_written()
    {
        // RFC 7230 §3.2 calls the space after the colon optional whitespace and says it is not part of
        // the value. It is kept, because a datagram that arrived with one and went out without is a
        // different datagram — and there is no length field here that would notice.
        //
        // MX is " 3", not 3. Every number in this protocol is characters, and reading them as a number
        // is the caller's, the same way a BACnet Unsigned's octets are.
        var run = Read(Search);

        Assert.AreEqual(" 3", Headers(run).Single(h => h.Name == "MX").Value);
        Assert.AreEqual(" ssdp:all", Headers(run).Single(h => h.Name == "ST").Value);
    }

    [TestMethod]
    public void A_search_written_back_is_the_same_octets()
    {
        var ssdp = Ssdp();
        var read = new GraphCodec(ssdp.Graph).Decode(Capture(Search));

        CollectionAssert.AreEqual(Capture(Search), new GraphCodec(ssdp.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["Start"] = ProtoValue.Of("M-SEARCH"),
                ["Target"] = Maybe(read, "target")!.Value,
                ["Version"] = Maybe(read, "httpVersion")!.Value,
                ["Headers"] = new ProtoValue.List([.. AsRecords(read)]),
            }),
            "142 octets held together by separators did not survive being read and written again");
    }

    // ── C2: the answer ────────────────────────────────────────────────────────

    [TestMethod]
    public void An_answer_reads_the_same_three_tokens_meaning_different_things()
    {
        // Same positions, different things: the first token is a version where the search's was a method,
        // and the third runs to the end of the line because a reason phrase may hold spaces.
        var run = Read(Answer);

        Assert.AreEqual("HTTP/1.1", Said(run, "statusVersion"));
        Assert.AreEqual("200", Said(run, "status"), "as characters — nothing here is a number");
        Assert.AreEqual("OK", Said(run, "reason"));

        Assert.IsNull(Maybe(run, "method"), "and the arm not taken says it is not there");
    }

    [TestMethod]
    public void And_a_header_that_is_present_and_empty_is_not_a_header_that_is_absent()
    {
        // EXT: is required by UPnP 1.1 §1.3.3 and its value is nothing at all — not even the space every
        // other header has. It is how a device says it understood MAN, so trimming or defaulting would
        // make it indistinguishable from the one thing it must not be confused with.
        var run = Read(Answer);

        Assert.AreEqual("", Headers(run).Single(h => h.Name == "EXT").Value);
        Assert.AreEqual(0, Convert.ToInt64(
            Each(run, "headerValue")[Array.IndexOf(Headers(run).Select(h => h.Name).ToArray(), "EXT")]
                .Settled(Facet.Extent)),
            "a span of no octets, ending where it began because the separator was already there");
    }

    [TestMethod]
    public void And_what_the_device_said_about_itself_is_text_this_does_not_open()
    {
        // The description stops at name and value. That CACHE-CONTROL carries a lifetime, that USN holds
        // a UUID and a device type joined by a double colon, and that BOOTID is the number that goes up
        // when a device reboots are all things a layer above knows.
        var run = Read(Answer);
        var headers = Headers(run);

        Assert.AreEqual(" max-age=1800", headers.Single(h => h.Name == "CACHE-CONTROL").Value);
        Assert.AreEqual(" upnp:rootdevice", headers.Single(h => h.Name == "ST").Value);
        StringAssert.EndsWith(headers.Single(h => h.Name == "USN").Value, "::upnp:rootdevice");
        Assert.AreEqual(" 1", headers.Single(h => h.Name == "BOOTID.UPNP.ORG").Value);
    }

    [TestMethod]
    public void An_answer_written_back_is_the_same_octets()
    {
        var ssdp = Ssdp();
        var read = new GraphCodec(ssdp.Graph).Decode(Capture(Answer));

        CollectionAssert.AreEqual(Capture(Answer), new GraphCodec(ssdp.Graph).Encode(
            new Dictionary<string, ProtoValue>(StringComparer.Ordinal)
            {
                ["Start"] = ProtoValue.Of("HTTP/1.1"),
                ["Status"] = Maybe(read, "status")!.Value,
                ["Reason"] = Maybe(read, "reason")!.Value,
                ["Headers"] = new ProtoValue.List([.. AsRecords(read)]),
            }),
            "nine headers, one of them empty, did not survive the round trip");
    }

    [TestMethod]
    public void And_the_answer_names_what_was_searched_for()
    {
        // The only thing tying the two datagrams together, and it is a comparison between them rather
        // than a field in either: the search asked for ssdp:all, which matches everything, and the device
        // answered with the type it actually is.
        var asked = Headers(Read(Search)).Single(h => h.Name == "ST").Value;
        var told = Headers(Read(Answer)).Single(h => h.Name == "ST").Value;

        Assert.AreEqual(" ssdp:all", asked);
        Assert.AreEqual(" upnp:rootdevice", told);
        Assert.AreNotEqual(asked, told, "which is not something either datagram says about itself");
    }

    // ── Where the scope is stated ─────────────────────────────────────────────

    [TestMethod]
    public void A_start_line_this_does_not_describe_is_refused()
    {
        // NOTIFY * HTTP/1.1 is SSDP's other half — ssdp:alive and ssdp:byebye, sent unsolicited with no
        // request in front of them. Eight characters, exactly as many as the two this does describe, and
        // there is no way on for it.
        var notify = System.Text.Encoding.ASCII.GetBytes(
            "NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nNTS: ssdp:alive\r\n\r\n");

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Ssdp().Graph).Decode(notify));
    }

    [TestMethod]
    public void A_datagram_that_never_reaches_a_blank_line_is_refused()
    {
        // Every span here ends at a separator, so a truncated datagram is one whose last span runs up to
        // something that is not in it — which is a refusal naming the field rather than a short read.
        var cut = Capture(Search)[..^4];

        Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Ssdp().Graph).Decode(cut));
    }
}
