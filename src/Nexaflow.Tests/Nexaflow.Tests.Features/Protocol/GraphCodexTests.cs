using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A protocol graph written down and read back, and the codec unable to tell.
///
/// <para>
/// The claim worth testing is not that the JSON looks right — it is that a graph nothing built from a
/// declaration produces <b>the same octets</b>. Anything less is a serialiser that looks complete because
/// what it dropped was not on the path the test happened to walk.
/// </para>
///
/// <para>
/// It is checked against NTP, which is a real document with a real capture: a bit group whose runs each
/// compute themselves, fixed-width scalars, an opaque span, and a length. Not one written to suit.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol graph serialisation — engine structure, no single product node")]
public class GraphCodexTests
{
    private static ProtocolGraph Ntp() => EndToEndCaptureTests.Definition().Graph;

    private static byte[] Capture() => ProtocolCorpus.Get("ntp").Captures[0].Bytes;

    private static ProtocolGraph RoundTripped(ProtocolGraph graph)
        => GraphCodex.Read(GraphCodex.Write(graph));

    // ── What survives ─────────────────────────────────────────────────────────

    [TestMethod]
    public void The_shape_comes_back_the_same_size()
    {
        var there = Ntp();
        var back = RoundTripped(there);

        Assert.AreEqual(there.Nodes.Count, back.Nodes.Count, "every node");
        Assert.AreEqual(there.Edges.Count, back.Edges.Count, "every edge");
        Assert.AreEqual(there.Id, back.Id);
        Assert.AreEqual(there.Root.Name, back.Root.Name);
    }

    [TestMethod]
    public void Edges_still_join_the_nodes_they_joined()
    {
        // Ends are written as indices, so this is the check that the indices mean what they meant. A
        // serialiser that shifts one end by one produces a graph that is the right size and wrong.
        var there = Ntp();
        var back = RoundTripped(there);

        static IEnumerable<string> Joins(ProtocolGraph g)
            => g.Edges.Select(e => $"{e.GetType().Name}:{e.From.Name}→{e.To.Name}").Order();

        CollectionAssert.AreEqual(Joins(there).ToList(), Joins(back).ToList());
    }

    // ── What it is for ────────────────────────────────────────────────────────

    /// <summary>
    /// The test the format exists to pass.
    /// </summary>
    /// <remarks>
    /// Reading the capture through a graph that was written down and read back has to give what reading it
    /// through the original gives — every value, on every node. If any fact is reached by something the
    /// format quietly dropped, this is where it shows, and it shows as a difference rather than as an
    /// absence nobody notices.
    /// </remarks>
    [TestMethod]
    public void A_capture_reads_the_same_through_a_graph_that_was_written_down()
    {
        var direct = new GraphCodec(Ntp()).Decode(Capture());
        var revived = new GraphCodec(RoundTripped(Ntp())).Decode(Capture());

        static Dictionary<string, string> Held(RunGraph run)
            => run.Nodes.Where(n => n.Has(Facet.Value))
                  .ToDictionary(n => n.ToString()!, n => n.Value.ToString()!);

        var one = Held(direct);
        var two = Held(revived);

        Assert.AreNotEqual(0, one.Count, "the direct read settled something to compare against");
        CollectionAssert.AreEquivalent(one.Keys.ToList(), two.Keys.ToList(), "the same nodes settled");

        foreach (var (where, what) in one)
            Assert.AreEqual(what, two[where], $"'{where}' read differently after a round trip");
    }

    // Writing back through a revived graph is NOT tested here, and the reason is worth stating rather than
    // leaving as a gap someone rediscovers. A bit group's runs are nodes — each owns its own requirements
    // path, which is why they are nodes — but Pattern.Bits carries a second list of run objects with the
    // same names and widths. Reading survives that, because reading only needs the widths. Writing does
    // not: it has to find what computes each run, and the copies inside the shape compute nothing.
    //
    // Every workaround for it is the same shape — ask the graph, not the pattern — and there is one left
    // that cannot be worked around, because the shape is where a run's width lives. The fix is for a run
    // to stop being part of a shape at all: its own node, with the packing of several into an octet being
    // a form like any other. Until then this direction is untested rather than quietly wrong.

    // ── What it will not do ───────────────────────────────────────────────────

    /// <summary>
    /// A kind it cannot carry is refused, not dropped.
    /// </summary>
    /// <remarks>
    /// The failure worth engineering against is the one that looks like success. A format that writes what
    /// it understands and silently omits the rest gives back a protocol that is almost right — shorter by
    /// exactly the parts nobody thought to check — and every test that walks the covered path still passes.
    /// </remarks>
    [TestMethod]
    public void A_kind_it_cannot_carry_is_refused_by_name()
    {
        // A chain is a shape rather than a form, and this does not write shapes yet.
        var chained = new MessageDef
        {
            Id = "listy",
            Context = Context.Given.These("items"),
            Fields =
            [
                new Field
                {
                    Id = "items",
                    Pattern = new Pattern.Chain(
                        new Field { Id = "item", Pattern = new Pattern.Scalar(1, true) },
                        Expr.Parse("room > 0")),
                    Value = Expr.Parse("inputs.items"),
                },
            ],
        };

        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => GraphCodex.Write(chained.Graph));

        StringAssert.Contains(refused.Message, "Chain");
        StringAssert.Contains(refused.Message, "refused rather than dropped");
    }
}
