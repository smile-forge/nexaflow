using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// What a message leaves behind: the state a conversation keeps.
/// </summary>
/// <remarks>
/// <para>
/// A state slot could only be read until now, which made every protocol whose next message depends on this
/// one a protocol the host had to help with — TCP's sequence numbers live in the handshake test's own
/// bookkeeping for exactly that reason.
/// </para>
/// <para>
/// <b>Once per message</b>, and nothing enforces that separately. A slot settles like anything else and
/// settling twice is already an error naming two producers, so a running total across a repetition — which
/// is a fold over what repeats rather than a fact a conversation remembers — refuses itself.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol state updates — engine structure, no single product node")]
public class StateUpdateTests
{
    /// <summary>One octet in, and whatever the case says it leaves behind.</summary>
    private static string Written(string nodes, string edges) => $$"""
        {
          "protocol": "leaves",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "a", "kind": "packing" },
            { "id": "n", "kind": "field", "as": "N",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.n", "kind": "input", "as": "N", "gives": "Int" },
            { "id": "state.seen", "kind": "state", "as": "Seen", "gives": "Int" }
            {{nodes}}
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "a" },
            { "kind": "then", "from": "a", "to": "n" },
            { "kind": "computes", "from": "n", "to": "input.n", "facet": "value" }
            {{edges}}
          ]
        }
        """;

    private static long Kept(RunGraph run, string slot)
        => run.Nodes.Single(x => x.Of.Name == slot && x.Has(Facet.Value)).Value.AsInt();

    [TestMethod]
    public void A_field_puts_what_it_held_into_a_state_slot()
    {
        var written = ProtocolFile.Read(Written("",
            """
            ,{ "kind": "updates", "from": "n", "to": "state.seen" }
            """));

        var run = new GraphCodec(written.Graph).Decode([0x2a]);

        Assert.AreEqual(42, Kept(run, "state.seen"),
            "what the octet said, kept for whatever message comes next");
    }

    [TestMethod]
    public void And_may_be_worked_on_along_the_way()
    {
        // A chain, because what a conversation remembers is rarely the octets exactly: TCP remembers the
        // sequence number PLUS the length it acknowledged, which is a calculation on the way.
        var written = ProtocolFile.Read(Written(
            """
            ,{ "id": "twice", "kind": "evaluated", "label": "double it", "runs": "held * 2",
               "gives": "Int", "takes": { "held": "Int" } }
            """,
            """
            ,{ "kind": "updates", "from": "n", "to": "twice", "parameter": "held" },
             { "kind": "updates", "from": "twice", "to": "state.seen" }
            """));

        Assert.AreEqual(84, Kept(new GraphCodec(written.Graph).Decode([0x2a]), "state.seen"));
    }

    [TestMethod]
    public void A_set_updates_with_a_fact_a_set_has()
    {
        // Which is not its value — a set holds and does not produce, so what travels is its extent. That
        // the edge says which fact is the same choice a requirement already makes.
        var written = ProtocolFile.Read(Written(
            """
            ,{ "id": "both", "kind": "set", "as": "the pair" },
             { "id": "t", "kind": "field", "as": "T",
               "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
             { "id": "input.t", "kind": "input", "as": "T", "gives": "Int" }
            """,
            """
            ,{ "kind": "then", "from": "n", "to": "t" },
             { "kind": "holds", "from": "both", "to": "n", "order": 0 },
             { "kind": "holds", "from": "both", "to": "t", "order": 1 },
             { "kind": "computes", "from": "t", "to": "input.t", "facet": "value" },
             { "kind": "updates", "from": "both", "to": "state.seen", "facet": "extent" }
            """));

        Assert.AreEqual(2, Kept(new GraphCodec(written.Graph).Decode([0x01, 0x02]), "state.seen"));
    }

    [TestMethod]
    public void Going_out_it_leaves_the_same_thing_behind()
    {
        // Both directions, because what a conversation remembers is not a fact about which way the octets
        // were going. A client that sends a sequence number has to remember it exactly as one that reads it.
        var written = ProtocolFile.Read(Written("",
            """
            ,{ "kind": "updates", "from": "n", "to": "state.seen" }
            """));

        var codec = new GraphCodec(written.Graph);
        var octets = codec.Encode(new Dictionary<string, ProtoValue> { ["N"] = ProtoValue.Of(7) });

        CollectionAssert.AreEqual(new byte[] { 0x07 }, octets);
    }

    // ── What it is not for ────────────────────────────────────────────────────

    [TestMethod]
    public void A_slot_written_twice_in_one_message_is_refused()
    {
        // The rule that needed no rule. A repetition updating a slot every time round is reaching for a
        // fact about a conversation to hold a fact about a message — which is a fold, and belongs to
        // whatever repeats. It refuses itself, because a node settles once.
        var written = ProtocolFile.Read("""
            {
              "protocol": "twice",
              "nodes": [
                { "id": "p", "kind": "protocol" },
                { "id": "m", "kind": "message" },
                { "id": "a", "kind": "packing" },
                { "id": "run", "kind": "set", "as": "the entries" },
                { "id": "tag", "kind": "field", "as": "Tag",
                  "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
                { "id": "input.entries", "kind": "input", "as": "Entries", "gives": "List",
                  "of": "Int" },
                { "id": "each", "kind": "evaluated", "label": "this entry", "runs": "item",
                  "gives": "Int" },
                { "id": "state.seen", "kind": "state", "as": "Seen", "gives": "Int" }
              ],
              "edges": [
                { "kind": "then", "from": "p", "to": "m" },
                { "kind": "then", "from": "m", "to": "a" },
                { "kind": "then", "from": "a", "to": "run" },
                { "kind": "then", "from": "run", "to": "tag" },
                { "kind": "holds", "from": "run", "to": "tag", "order": 0 },
                { "kind": "requires", "from": "run", "to": "input.entries", "facet": "each",
                  "sequence": 0 },
                { "kind": "computes", "from": "tag", "to": "each", "facet": "value" },
                { "kind": "updates", "from": "tag", "to": "state.seen" }
              ]
            }
            """);

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(written.Graph).Encode(new Dictionary<string, ProtoValue>
            {
                ["Entries"] = new ProtoValue.List([ProtoValue.Of(1), ProtoValue.Of(2)]),
            }));

        StringAssert.Contains(refused.Message, "already has a Value");
    }

    [TestMethod]
    public void An_update_that_stops_at_a_calculation_is_refused()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read(Written(
            """
            ,{ "id": "twice", "kind": "evaluated", "label": "double it", "runs": "held * 2",
               "gives": "Int", "takes": { "held": "Int" } }
            """,
            """
            ,{ "kind": "updates", "from": "n", "to": "twice", "parameter": "held" }
            """)));

        StringAssert.Contains(refused.Message, "updates nothing itself");
    }

    [TestMethod]
    public void And_one_that_does_not_say_where_the_value_lands_is_refused()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read(Written(
            """
            ,{ "id": "twice", "kind": "evaluated", "label": "double it", "runs": "held * 2",
               "gives": "Int", "takes": { "held": "Int" } }
            """,
            """
            ,{ "kind": "updates", "from": "n", "to": "twice" },
             { "kind": "updates", "from": "twice", "to": "state.seen" }
            """)));

        StringAssert.Contains(refused.Message, "nothing says which of its parameters the value lands on");
    }
}
