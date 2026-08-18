using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// What a written protocol has to be true of itself, refused when it loads.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here was paid for. A graph wrong in one of these ways used to load, walk, and fail somewhere
/// else — a length that measured nothing surfaced as "expected Int, got Null" from inside a base-128
/// converter, three layers from the set whose name was misspelled. What these turn into is a sentence
/// naming the node, the expression and the missing edge.
/// </para>
/// <para>
/// They are refusals rather than warnings on purpose. A description that is nearly right is the failure
/// worth engineering against: it produces a message of exactly the right length with a plausible value in
/// the wrong place, and every capture that happens not to exercise the wrong part still passes.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol load-time consistency — engine structure, no single product node")]
public class ConsistencyTests
{
    /// <summary>A message of one octet, with whatever else the case needs bolted on.</summary>
    private static string Written(string nodes, string edges) => $$"""
        {
          "protocol": "x",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "a", "kind": "packing" },
            { "id": "n", "kind": "field", "as": "N",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.n", "kind": "input", "as": "N", "gives": "Int" }{{More(nodes)}}
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "a" },
            { "kind": "then", "from": "a", "to": "n" },
            { "kind": "computes", "from": "n", "to": "input.n", "facet": "value" }{{More(edges)}}
          ]
        }
        """;

    /// <summary>A fragment the case adds, joined on where the fixed part leaves off.</summary>
    private static string More(string fragment)
        => fragment.Trim().Length == 0 ? "" : ",\n" + fragment;

    private static ProtoTypeException Refused(string nodes, string edges = "")
        => Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read(Written(nodes, edges)));

    // ── Nothing is stranded ───────────────────────────────────────────────────

    [TestMethod]
    public void A_node_nothing_joins_to_the_graph_is_refused()
    {
        // Two value sets shipped in the MQTT description exactly this way, documenting return codes that
        // nothing checked. A description full of prose reads the same whether its nodes are wired or not.
        var refused = Refused("""
            { "id": "loose", "kind": "junction" }
            """);

        StringAssert.Contains(refused.Message, "'loose' has no edges at all");
    }

    // ── An expression sees its edges and nothing else ─────────────────────────

    [TestMethod]
    public void An_expression_reading_what_it_does_not_take_is_refused()
    {
        // What used to be "a name with no edge", now impossible to write by accident: an expression names
        // parameters, so there is no node id inside it to misspell. A name it never declared is all that
        // is left, and it says so rather than evaluating to nothing.
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "how wide?",
              "runs": "ghost * 8", "gives": "Int" }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" }
            """);

        StringAssert.Contains(refused.Message, "reads `ghost`, which it does not take");
    }

    [TestMethod]
    public void A_parameter_nothing_fills_is_refused()
    {
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "how wide?", "runs": "count * 8",
              "gives": "Int", "takes": { "count": "Int" } }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" }
            """);

        StringAssert.Contains(refused.Message, "takes a 'count' and no edge fills it");
    }

    [TestMethod]
    public void A_parameter_the_expression_never_reads_is_refused()
    {
        // The other direction: a declaration that came adrift from what it was written for, so the
        // computation waits on a fact nobody reads.
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "always four", "runs": "4",
              "gives": "Int", "takes": { "count": "Int" } }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" },
                 { "kind": "requires", "from": "e", "to": "n", "facet": "value", "sequence": 0,
                   "parameter": "count" }
            """);

        StringAssert.Contains(refused.Message, "takes a 'count' and never reads it");
    }

    [TestMethod]
    public void An_edge_that_says_nothing_about_which_parameter_it_fills_is_refused()
    {
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "how wide?", "runs": "count * 8",
              "gives": "Int", "takes": { "count": "Int" } }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" },
                 { "kind": "requires", "from": "e", "to": "n", "facet": "value", "sequence": 0 }
            """);

        StringAssert.Contains(refused.Message, "nothing says which of its parameters that fills");
    }

    [TestMethod]
    public void A_parameter_handed_the_wrong_kind_is_refused()
    {
        // The check that only half ran until expressions declared what they take: an edge into an
        // expression had no declared kind on the far end, so nothing compared them.
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "how wide?", "runs": "count * 8",
              "gives": "Int", "takes": { "count": "Text" } }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" },
                 { "kind": "requires", "from": "e", "to": "n", "facet": "value", "sequence": 0,
                   "parameter": "count" }
            """);

        StringAssert.Contains(refused.Message, "takes Text as its 'count', and is handed Int");
    }

    // ── A computation says what it gives ──────────────────────────────────────

    [TestMethod]
    public void A_computation_that_does_not_say_what_it_gives_is_refused()
    {
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "always four", "runs": "4" }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "extent" }
            """);

        StringAssert.Contains(refused.Message, "does not say what kind of value it gives");
    }

    [TestMethod]
    public void A_kind_that_cannot_go_where_it_is_put_is_refused()
    {
        // Text into a field that lays down an integer. Nothing declared either side until now, so this
        // was a throw from inside the form, at the moment a message was being written.
        var refused = Refused(
            """
            { "id": "e", "kind": "evaluated", "label": "a name", "runs": "'bob'", "gives": "Text" }
            """,
            """
            { "kind": "computes", "from": "n", "to": "e", "facet": "presence" }
            """);

        StringAssert.Contains(refused.Message, "gives Text where Bool is what can go there");
    }

    // ── Converters take what they take ────────────────────────────────────────

    [TestMethod]
    public void A_converter_given_a_parameter_it_does_not_take_is_refused()
    {
        var refused = Refused(
            """
            { "id": "fill", "kind": "field", "as": "Fill", "form": { "of": "opaque" } },
                 { "id": "zero", "kind": "constant", "label": "a zero octet", "holds": { "hex": "00" } },
                 { "id": "made", "kind": "converted", "applies": "repeat", "label": "n zeros" }
            """,
            """
            { "kind": "then", "from": "n", "to": "fill" },
                 { "kind": "computes", "from": "fill", "to": "made", "facet": "value" },
                 { "kind": "requires", "from": "made", "to": "zero", "sequence": 0 },
                 { "kind": "requires", "from": "made", "to": "n", "facet": "value", "sequence": 1,
                   "parameter": "width" }
            """);

        StringAssert.Contains(refused.Message, "'width', which 'repeat' does not take");
        StringAssert.Contains(refused.Message, "It takes: count");
    }

    [TestMethod]
    public void And_an_argument_may_be_computed_because_it_arrives_on_an_edge()
    {
        // The point of moving arguments off the converter and onto the graph. How many zeros is a value
        // read from another field — which a literal written beside the converter's name could never be.
        var written = ProtocolFile.Read(Written(
            """
            { "id": "fill", "kind": "field", "as": "Fill", "form": { "of": "opaque" } },
                 { "id": "zero", "kind": "constant", "label": "a zero octet", "holds": { "hex": "00" } },
                 { "id": "made", "kind": "converted", "applies": "repeat", "label": "n zeros" }
            """,
            """
            { "kind": "then", "from": "n", "to": "fill" },
                 { "kind": "computes", "from": "fill", "to": "made", "facet": "value" },
                 { "kind": "requires", "from": "made", "to": "zero", "sequence": 0 },
                 { "kind": "requires", "from": "made", "to": "n", "facet": "value", "sequence": 1,
                   "parameter": "count" }
            """));

        var octets = new GraphCodec(written.Graph).Encode(
            new Dictionary<string, ProtoValue> { ["N"] = ProtoValue.Of(3) });

        CollectionAssert.AreEqual(new byte[] { 0x03, 0x00, 0x00, 0x00 }, octets,
            "three zeros, because the field before it said three");
    }

    [TestMethod]
    public void A_converter_handed_the_wrong_kind_for_a_parameter_is_refused()
    {
        // Naming the parameter was only half of it. `repeat` takes a `count`, and until the table said
        // what kind a count is, wiring a run of octets into it passed the check and failed inside the
        // converter — which is the check reading as safety while the failure still happens.
        var refused = Refused(
            """
            { "id": "fill", "kind": "field", "as": "Fill", "form": { "of": "opaque" } },
                 { "id": "zero", "kind": "constant", "label": "a zero octet", "holds": { "hex": "00" } },
                 { "id": "made", "kind": "converted", "applies": "repeat", "label": "n zeros" }
            """,
            """
            { "kind": "then", "from": "n", "to": "fill" },
                 { "kind": "computes", "from": "fill", "to": "made", "facet": "value" },
                 { "kind": "requires", "from": "made", "to": "zero", "sequence": 0 },
                 { "kind": "requires", "from": "made", "to": "n", "facet": "octets", "sequence": 1,
                   "parameter": "count" }
            """);

        StringAssert.Contains(refused.Message, "takes Int as its 'count', and is handed Bytes");
    }

    // ── What one item is ──────────────────────────────────────────────────────

    /// <summary>A run of one-octet entries, written once per item of whatever the list holds.</summary>
    private static string Repeating(string list, string reads) => $$"""
        {
          "protocol": "entries",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "a", "kind": "packing" },
            { "id": "run", "kind": "set", "as": "the entries" },
            { "id": "tag", "kind": "field", "as": "Tag",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.entries", "kind": "input", "as": "Entries", "gives": "List"{{list}} },
            { "id": "each", "kind": "evaluated", "label": "this entry", "runs": "{{reads}}",
              "gives": "Int" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "a" },
            { "kind": "then", "from": "a", "to": "run" },
            { "kind": "then", "from": "run", "to": "tag" },
            { "kind": "holds", "from": "run", "to": "tag", "order": 0 },
            { "kind": "requires", "from": "run", "to": "input.entries", "facet": "each", "sequence": 0 },
            { "kind": "computes", "from": "tag", "to": "each", "facet": "value" }
          ]
        }
        """;

    [TestMethod]
    public void A_list_that_does_not_say_what_an_item_is_is_refused()
    {
        // The last name answerable to nothing. `item` is bound by the repetition rather than by an edge,
        // so until a list declared its items there was no way to be wrong about one — and `item.tag`
        // against a list of bare numbers read as nothing, exactly the way a missing edge used to.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => ProtocolFile.Read(Repeating("", "item.tag")));

        StringAssert.Contains(refused.Message, "nothing says what an item is");
    }

    [TestMethod]
    public void And_reading_a_member_the_items_do_not_have_is_refused()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => ProtocolFile.Read(Repeating(", \"of\": { \"tag\": \"Int\" }", "item.size")));

        StringAssert.Contains(refused.Message, "reads `item.size`");
        StringAssert.Contains(refused.Message, "tag: Int");
    }

    [TestMethod]
    public void A_list_of_bare_values_reads_the_item_whole()
    {
        // Not every list holds records. SUBACK's return codes are one octet each, so `item` IS the value
        // and asking it for a member would be the mistake.
        var written = ProtocolFile.Read(Repeating(", \"of\": \"Int\"", "item"));

        var octets = new GraphCodec(written.Graph).Encode(new Dictionary<string, ProtoValue>
        {
            ["Entries"] = new ProtoValue.List([ProtoValue.Of(7), ProtoValue.Of(8), ProtoValue.Of(9)]),
        });

        CollectionAssert.AreEqual(new byte[] { 0x07, 0x08, 0x09 }, octets);
    }

    // ── A field's own conversion ──────────────────────────────────────────────

    [TestMethod]
    public void A_field_may_convert_on_the_way_out_and_back_on_the_way_in()
    {
        // What the file format could not say until now, so every string in MQTT and every address in DHCP
        // is octets with an apology in its description.
        //
        // `via` names the conversion applied on the way OUT, so text becoming octets is `unutf8` and the
        // reading inverts it. Naming the other one is caught before a message is ever built: the field
        // would be handed text by something declaring text, and be told it can only lay down octets.
        var written = ProtocolFile.Read(Written(
            """
            { "id": "name", "kind": "field", "as": "Name", "form": { "of": "opaque", "octets": 3 },
                   "via": "unutf8" },
                 { "id": "input.name", "kind": "input", "as": "Name", "gives": "Text" }
            """,
            """
            { "kind": "then", "from": "n", "to": "name" },
                 { "kind": "computes", "from": "name", "to": "input.name", "facet": "value" }
            """));

        var octets = new GraphCodec(written.Graph).Encode(new Dictionary<string, ProtoValue>
        {
            ["N"] = ProtoValue.Of(1),
            ["Name"] = ProtoValue.Of("abc"),
        });

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x61, 0x62, 0x63 }, octets);

        var read = new GraphCodec(written.Graph).Decode(octets);

        Assert.AreEqual("abc",
            read.Nodes.Single(x => x.Of is Field { Id: "name" }).Value.AsText(),
            "and it came back as text rather than as three octets to be decoded by whoever asked");
    }

    [TestMethod]
    public void A_conversion_that_needs_an_argument_is_refused_on_a_field()
    {
        // A parameter is an edge, and the edges into a field say what follows what. So a conversion that
        // takes one is a computation — refused here rather than at the moment a message is read, where the
        // missing argument surfaces as an index out of range.
        var refused = Refused(
            """
            { "id": "name", "kind": "field", "as": "Name", "form": { "of": "opaque", "octets": 8 },
                   "via": "cstr" },
                 { "id": "input.name", "kind": "input", "as": "Name", "gives": "Text" }
            """,
            """
            { "kind": "then", "from": "n", "to": "name" },
                 { "kind": "computes", "from": "name", "to": "input.name", "facet": "value" }
            """);

        StringAssert.Contains(refused.Message, "a field has nowhere for an argument to come from");
    }

    [TestMethod]
    public void A_conversion_with_no_way_back_is_refused()
    {
        var refused = Refused(
            """
            { "id": "sum", "kind": "field", "as": "Sum", "form": { "of": "opaque", "octets": 32 },
                   "via": "sha256" },
                 { "id": "input.sum", "kind": "input", "as": "Sum", "gives": "Bytes" }
            """,
            """
            { "kind": "then", "from": "n", "to": "sum" },
                 { "kind": "computes", "from": "sum", "to": "input.sum", "facet": "value" }
            """);

        StringAssert.Contains(refused.Message, "declares no inverse");
    }
}
