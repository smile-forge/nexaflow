using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A reading that is not simply the writing backwards.
/// </summary>
/// <remarks>
/// <para>
/// Most of a protocol is read by walking what it writes, and where that holds there is one description and
/// the two directions cannot drift apart. This is for the parts where it does not hold — and they are not
/// rare, because protocols are built for the wire rather than for description: what can be inferred is
/// omitted, what repeats has no count, and what is malformed has rules only a reader can apply.
/// </para>
/// <para>
/// So a message may declare its own reading, and where it does not, nothing changes. That opt-in is what
/// keeps the cost proportional: a protocol pays the risk of two descriptions only where it needs the second
/// one.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol decode path — engine structure, no single product node")]
public class DecodePathTests
{
    /// <summary>Two octets, written one way and read however the case says.</summary>
    private static ProtocolFile.Loaded Definition(string reading) => ProtocolFile.Read($$"""
        {
          "protocol": "twin",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "lead", "kind": "field", "as": "Lead",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "tail", "kind": "field", "as": "Tail",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.lead", "kind": "input", "as": "Lead", "gives": "Int" },
            { "id": "input.tail", "kind": "input", "as": "Tail", "gives": "Int" },
            { "id": "done", "kind": "end-parse" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "lead" },
            { "kind": "then", "from": "lead", "to": "tail" },
            { "kind": "computes", "from": "lead", "to": "input.lead", "facet": "value" },
            {{reading}}
            { "kind": "computes", "from": "tail", "to": "input.tail", "facet": "value" }
          ]
        }
        """);

    private static long Said(RunGraph run, string field)
        => run.Nodes.Single(n => n.Of is Field f && f.Id == field).Value.AsInt();

    /// <summary>Where the written path ends, for a message that declares no reading of its own.</summary>
    private const string Ends = """
        { "kind": "then", "from": "tail", "to": "done" },
        """;

    /// <summary>A reading that goes the same way the writing does, said explicitly.</summary>
    private const string Straight = """
        { "kind": "decode", "from": "m", "to": "lead" },
        { "kind": "decode", "from": "lead", "to": "tail" },
        { "kind": "decode", "from": "tail", "to": "done" },
        """;

    [TestMethod]
    public void A_message_with_no_reading_of_its_own_is_read_the_way_it_is_written()
    {
        // The default, and the case worth protecting: no decode edges, so Then serves both directions and
        // there is exactly one description of the path.
        // The end is still declared and still on the path — what this case lacks is a READING of its own,
        // which is the decode edges. Everything a protocol declares has to be joined to it by something.
        var run = new GraphCodec(Definition(Ends).Graph).Decode([0x01, 0x02]);

        Assert.AreEqual(1, Said(run, "lead"));
        Assert.AreEqual(2, Said(run, "tail"), "it carried on down the written path");
    }

    [TestMethod]
    public void A_message_that_declares_one_is_read_along_it()
    {
        var run = new GraphCodec(Definition(Straight).Graph).Decode([0x01, 0x02]);

        Assert.AreEqual(1, Said(run, "lead"));
        Assert.AreEqual(2, Said(run, "tail"));
    }

    [TestMethod]
    public void And_what_it_reads_still_writes_back_the_same()
    {
        // The check that keeps two descriptions honest. A decode path that disagrees with the write path
        // shows up here as a message that will not go back the way it came — loudly, rather than as an
        // encoder and a decoder that are each self-consistent and mutually wrong.
        var twin = Definition(Straight);
        var run = new GraphCodec(twin.Graph).Decode([0x01, 0x02]);

        var written = new GraphCodec(twin.Graph).Encode(new Dictionary<string, ProtoValue>
        {
            ["Lead"] = ProtoValue.Of(Said(run, "lead")),
            ["Tail"] = ProtoValue.Of(Said(run, "tail")),
        });

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 }, written);
    }

    // ── Where it is allowed to stop ───────────────────────────────────────────

    [TestMethod]
    public void A_reading_that_stops_where_it_may_not_is_refused()
    {
        // Everything read is well formed and every octet is accounted for — the message is the right
        // length and its fields hold plausible values. What is wrong is only that the description never
        // said this was an ending, which is precisely what a truncated message looks like from inside.
        // Its own description, because the end has to be somewhere the reading COULD have gone — a node
        // nothing reaches at all is a different fault, and the load refuses that one before this can
        // happen. So a lead of 0xff would have ended legally, and 0x01 walks on to a place that is simply
        // where the description ran out.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(ProtocolFile.Read("""
            {
              "protocol": "twin",
              "nodes": [
                { "id": "p", "kind": "protocol" },
                { "id": "m", "kind": "message" },
                { "id": "arrangement", "kind": "packing" },
                { "id": "lead", "kind": "field", "as": "Lead",
                  "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
                { "id": "tail", "kind": "field", "as": "Tail",
                  "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
                { "id": "input.lead", "kind": "input", "as": "Lead", "gives": "Int" },
                { "id": "input.tail", "kind": "input", "as": "Tail", "gives": "Int" },
                { "id": "reads", "kind": "evaluated", "label": "what did the lead say?",
                  "runs": "said", "gives": "Int", "takes": { "said": "Int" } },
                { "id": "done", "kind": "end-parse" }
              ],
              "edges": [
                { "kind": "then", "from": "p", "to": "m" },
                { "kind": "then", "from": "m", "to": "arrangement" },
                { "kind": "then", "from": "arrangement", "to": "lead" },
                { "kind": "then", "from": "lead", "to": "tail" },
                { "kind": "computes", "from": "lead", "to": "input.lead", "facet": "value" },
                { "kind": "computes", "from": "tail", "to": "input.tail", "facet": "value" },
                { "kind": "decode", "from": "m", "to": "lead" },
                { "kind": "decode", "from": "lead", "to": "done", "key": { "int": 255 } },
                { "kind": "decode", "from": "lead", "to": "tail", "otherwise": true },
                { "kind": "decides", "from": "lead", "to": "reads", "reading": true },
                { "kind": "requires", "from": "reads", "to": "lead", "facet": "value", "sequence": 0,
              "parameter": "said" }
              ]
            }
            """).Graph).Decode([0x01, 0x02]));

        StringAssert.Contains(refused.Message, "not somewhere it is allowed to stop");
        StringAssert.Contains(refused.Message, "'tail'", "and it says where it got to");
    }

    [TestMethod]
    public void A_reading_may_take_a_way_the_writing_does_not_have()
    {
        // The point of the second path: a key on the reading picks somewhere the written order never goes.
        // A lead of 0xff means there is no tail at all, which the written order — lead then tail, always —
        // has no way to say.
        var run = new GraphCodec(ProtocolFile.Read(Branching).Graph).Decode([0xff]);

        Assert.AreEqual(255, Said(run, "lead"), "and nothing was read past it");
    }

    // ── Going round ───────────────────────────────────────────────────────────

    [TestMethod]
    public void A_reading_may_go_round_until_it_is_told_to_stop()
    {
        // A run of octets ended by a zero, which is how a great many protocols say "as many as there are":
        // no count anywhere, because how many there are is a fact about a message and not about a protocol.
        // The written path cannot say it — it is a sequence — and the reading can, by going back.
        var run = new GraphCodec(ProtocolFile.Read(Looping).Graph).Decode([0x05, 0x07, 0x00]);

        var items = run.Nodes.Where(n => n.Of is Field { Id: "item" })
                             .OrderBy(n => n.Index)
                             .Select(n => n.Value.AsInt())
                             .ToList();

        CollectionAssert.AreEqual(new long[] { 5, 7, 0 }, items,
            "three passes, three values, and the second one did not overwrite the first");
    }

    /// <summary>Octets until a zero. The reading goes back to itself; the writing has no way to.</summary>
    private const string Looping = """
        {
          "protocol": "run",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "item", "kind": "field", "as": "Item",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.item", "kind": "input", "as": "Item", "gives": "Int" },
            { "id": "reads", "kind": "evaluated", "label": "what did it say?", "runs": "said", "gives": "Int",
              "takes": { "said": "Int" } },
            { "id": "done", "kind": "end-parse" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "item" },
            { "kind": "computes", "from": "item", "to": "input.item", "facet": "value" },
            { "kind": "decode", "from": "m", "to": "item" },
            { "kind": "decode", "from": "item", "to": "done", "key": { "int": 0 } },
            { "kind": "decode", "from": "item", "to": "item", "otherwise": true },
            { "kind": "decides", "from": "item", "to": "reads", "reading": true },
            { "kind": "requires", "from": "reads", "to": "item", "facet": "value", "sequence": 0,
              "parameter": "said" }
          ]
        }
        """;

    /// <summary>The same two octets, with a reading that may stop after the first.</summary>
    private const string Branching = """
        {
          "protocol": "twin",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "lead", "kind": "field", "as": "Lead",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "tail", "kind": "field", "as": "Tail",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.lead", "kind": "input", "as": "Lead", "gives": "Int" },
            { "id": "input.tail", "kind": "input", "as": "Tail", "gives": "Int" },
            { "id": "reads", "kind": "evaluated", "label": "what did the lead say?",
              "runs": "said", "gives": "Int", "takes": { "said": "Int" } },
            { "id": "done", "kind": "end-parse" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "lead" },
            { "kind": "then", "from": "lead", "to": "tail" },
            { "kind": "computes", "from": "lead", "to": "input.lead", "facet": "value" },
            { "kind": "computes", "from": "tail", "to": "input.tail", "facet": "value" },
            { "kind": "decode", "from": "m", "to": "lead" },
            { "kind": "decode", "from": "lead", "to": "done", "key": { "int": 255 } },
            { "kind": "decode", "from": "lead", "to": "tail", "otherwise": true },
            { "kind": "decode", "from": "tail", "to": "done" },
            { "kind": "decides", "from": "lead", "to": "reads", "reading": true },
            { "kind": "requires", "from": "reads", "to": "lead", "facet": "value", "sequence": 0,
              "parameter": "said" }
          ]
        }
        """;
}
