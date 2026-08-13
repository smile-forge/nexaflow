using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The second graph: one message, as nodes that hold what they came to.
///
/// <para>
/// The protocol graph says what a message is and never changes. This says what <i>this</i> message is, and
/// it is the same shape — every node here stands for exactly one node there. That is what makes navigation
/// the protocol's own edges followed to the appearance you are standing in, rather than a second structure
/// describing the same relationships in different words.
/// </para>
///
/// <para>
/// The rule it exists to make keepable: <b>once a build starts, information comes from the protocol graph
/// or from here, and nowhere else.</b> Inputs and state are settled nodes rather than a dictionary the
/// walk consults, so "this field draws on that input" is an edge with a value at the end of it — the same
/// question, and the same kind of answer, as "this field reads that field's extent".
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol run model — engine structure, no single product node")]
public class RunGraphTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    private static readonly MessageDef Simple = new()
    {
        Id = "simple",
        Context =
        [
            new Context.Given { Key = "count", Purpose = "how many." },
            new Context.Fixed { Key = "magic", Value = ProtoValue.Of(0x2aL), Purpose = "the marker." },
        ],
        Fields =
        [
            new Field { Id = "marker", Pattern = U8, Value = Expr.Parse("inputs.magic") },
            new Field { Id = "count", Pattern = U8, Value = Expr.Parse("inputs.count") },
        ],
    };

    private static RunGraph Run(params (string Key, long Value)[] supplied)
        => RunGraph.Begin(Simple.Graph,
                          supplied.ToDictionary(s => s.Key, s => ProtoValue.Of(s.Value)));

    private static Context Outside(string key)
        => Simple.Graph.Nodes.OfType<Context>().Single(c => c.Key == key);

    private static Field Named(string id)
        => Simple.Graph.Nodes.OfType<Field>().Single(f => f.Id == id);

    // ── What a run starts knowing ─────────────────────────────────────────────

    [TestMethod]
    public void A_supplied_input_is_a_node_that_already_has_its_value()
    {
        var run = Run(("count", 7));

        Assert.AreEqual(7L, run.For(Outside("count")).Value.AsInt());
    }

    [TestMethod]
    public void A_constant_the_document_fixes_needs_nobody_to_supply_it()
    {
        // The one kind of outside value that is not outside at all: the document states it, so the run
        // starts with it settled and a caller that never heard of it still builds the message.
        Assert.AreEqual(0x2aL, Run().For(Outside("magic")).Value.AsInt());
    }

    [TestMethod]
    public void An_input_nobody_supplied_is_unsettled_rather_than_nothing()
    {
        // The distinction the old ambient scope could not make. A missing input read as null, and every
        // comparison against it went quietly false; here asking is an error that names what is missing.
        var run = Run();

        Assert.IsFalse(run.For(Outside("count")).Has(Facet.Value));

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => run.For(Outside("count")).Settled(Facet.Value));

        StringAssert.Contains(refused.Message, "has no Value yet");
        StringAssert.Contains(refused.Message, "missing edge rather than a missing value");
    }

    [TestMethod]
    public void What_an_earlier_message_left_behind_arrives_the_same_way()
    {
        // State is not a place the walk reaches into; it is nodes that are already settled, which is the
        // only version of this that keeps the rule about where information may come from.
        var run = RunGraph.Begin(Simple.Graph, null,
            new Dictionary<string, ProtoValue> { ["table"] = ProtoValue.Of(3L) });

        Assert.AreEqual(3L, run.Remembered("table").Value.AsInt());
        Assert.AreSame(run.Remembered("table"), run.Remembered("table"), "one slot, one node");
    }

    // ── Appearances ───────────────────────────────────────────────────────────

    [TestMethod]
    public void One_protocol_node_can_have_several_appearances_and_they_are_different_nodes()
    {
        var run = Run();
        var field = Named("count");
        var first = run.For(field, null, 0);
        var second = run.For(field, null, 1);

        Assert.AreNotSame(first, second);
        Assert.AreSame(field, first.Of, "and both are appearances of the one declaration");
        Assert.AreSame(field, second.Of);

        first.Settle(Facet.Value, ProtoValue.Of(1L));
        Assert.IsFalse(second.Has(Facet.Value), "settling one says nothing about the other");
    }

    [TestMethod]
    public void Asking_for_the_same_appearance_twice_gives_the_same_node()
    {
        var run = Run();

        Assert.AreSame(run.For(Named("count")), run.For(Named("count")));
    }

    [TestMethod]
    public void Which_appearance_an_edge_means_is_decided_innermost_outward()
    {
        // A structure's own part is its own; anything it does not have is the one out here. This is the
        // whole of the naming logic, and it is about appearances rather than names.
        var run = Run();
        var outer = run.For(Named("marker"));
        var instance = run.For(Named("count"), outer);
        var inner = run.For(Named("marker"), instance);

        Assert.AreSame(inner, run.Reach(instance, Named("marker")), "its own");
        Assert.AreSame(outer, run.Reach(outer, Named("marker")), "and from out here, the outer one");
    }

    // ── Settling ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_node_settles_once()
    {
        var node = Run().For(Named("count"));
        node.Settle(Facet.Value, ProtoValue.Of(1L));

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => node.Settle(Facet.Value, ProtoValue.Of(2L)));

        StringAssert.Contains(refused.Message, "settles once");
    }

    [TestMethod]
    public void Facets_of_one_node_settle_independently()
    {
        // Extent is a function of value for the self-delimiting shapes, and value of extent for none of
        // them — so they are separate facts about one node rather than one fact with two readings.
        var node = Run().For(Named("count"));

        node.Settle(Facet.Extent, 1);
        Assert.IsFalse(node.Has(Facet.Value));

        node.Settle(Facet.Value, ProtoValue.Of(9L));
        Assert.AreEqual(1, node.Settled(Facet.Extent));
        Assert.AreEqual(9L, node.Value.AsInt());
    }

    // ── The shape it keeps ────────────────────────────────────────────────────

    [TestMethod]
    public void Every_appearance_stands_for_exactly_one_protocol_node()
    {
        var run = Run(("count", 1));
        run.For(Named("marker"));
        run.For(Named("count"), run.For(Named("marker")));

        foreach (var node in run.Nodes)
            Assert.IsTrue(Simple.Graph.Nodes.Contains(node.Of) || node.Of.Name == "table",
                $"{node} stands for something that is not in the protocol");
    }
}
