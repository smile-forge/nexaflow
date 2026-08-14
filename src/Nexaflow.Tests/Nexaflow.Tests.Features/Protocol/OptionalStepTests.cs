using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A part that may not be there: a step the path may not take, and a condition that says when.
///
/// <para>
/// Optionality used to be a <i>shape</i>. A part was made optional by wrapping it in an alternation with
/// an empty arm, so "this header may be absent" and "this is one of four packings" were the same
/// declaration, and which one a document meant had to be worked out by looking at whether an arm happened
/// to be empty. It also meant the engine derived a map of optional fields by scanning alternations, which
/// is a fact about the arrangement recovered from a fact about a pattern.
/// </para>
///
/// <para>
/// It rides the way on instead. A part is not "an optional kind of field" — it is a step the path may or
/// may not take, which is also why one field can be required in one arrangement and optional in another
/// without becoming two declarations.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol optionality — engine structure, no single product node")]
public class OptionalStepTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>A marker, a middle part that is there only when asked for, and a trailer.</summary>
    private static MessageDef Sometimes() => new()
    {
        Id = "sometimes",
        Context = Context.Given.These("wanted"),
        Fields =
        [
            new Field { Id = "marker", Pattern = U8, Value = Expr.Parse("0xaa") },
            new Field
            {
                Id = "extra",
                Pattern = U8,
                Value = Expr.Parse("0xbb"),
                When = Expr.Parse("inputs.wanted"),
            },
            new Field { Id = "trailer", Pattern = U8, Value = Expr.Parse("0xcc") },
        ],
    };

    private static byte[] Written(bool wanted)
        => new GraphCodec(Sometimes().Graph).Encode(new Dictionary<string, ProtoValue>
            { ["wanted"] = ProtoValue.Of(wanted) });

    // ── Where it lives ────────────────────────────────────────────────────────

    [TestMethod]
    public void Optionality_is_on_the_way_on_rather_than_on_the_thing()
    {
        var graph = Sometimes().Graph;
        var optional = graph.Of<Then>().Where(w => w.Optional).ToList();

        Assert.AreEqual(1, optional.Count, "one step the path may not take");
        Assert.AreEqual("extra", optional[0].To.Name);

        Assert.IsTrue(graph.Of<Then>().Where(w => w.To.Name is "marker" or "trailer").All(w => !w.Optional),
                      "and the steps that are always taken say so");
    }

    [TestMethod]
    public void What_decides_it_is_a_computation_reaching_an_input()
    {
        var message = Sometimes();
        var extra = message.AllFields.Single(f => f.Id == "extra");
        var deciding = message.ProducerOf(extra, "presence");

        Assert.IsNotNull(deciding, "presence is a facet something produces, like an extent");

        var reaches = message.InputsOf(deciding).Select(e => e.To).OfType<Context>().Select(c => c.Key);

        CollectionAssert.AreEquivalent(new[] { "wanted" }, reaches.ToArray());
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_part_that_is_wanted_is_written()
    {
        CollectionAssert.AreEqual(new byte[] { 0xaa, 0xbb, 0xcc }, Written(wanted: true));
    }

    [TestMethod]
    public void A_part_that_is_not_wanted_writes_nothing_and_what_follows_closes_up()
    {
        CollectionAssert.AreEqual(new byte[] { 0xaa, 0xcc }, Written(wanted: false));
    }

    [TestMethod]
    public void Being_reached_and_being_present_are_different_facts()
    {
        // The walk goes through the place either way — it has to, or nothing downstream of an absent part
        // would be reached at all. What changes is whether it contributed.
        var message = Sometimes();
        var run = RunGraph.Begin(message.Graph, new Dictionary<string, ProtoValue>
            { ["wanted"] = ProtoValue.Of(false) });

        _ = new GraphCodec(message.Graph).Encode(new Dictionary<string, ProtoValue>
            { ["wanted"] = ProtoValue.Of(false) });

        Assert.IsFalse(run.Nodes.Any(n => n.Of.Name == "extra" && n.Has(Facet.Emitted)),
                       "an absent part emits nothing");
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Reading_asks_whether_it_is_there_before_it_takes_any_octets()
    {
        // It has to come first on the way in: everything after decides how many octets to consume, and
        // consuming the next field's is not a mistake anything downstream can catch.
        var run = new GraphCodec(Sometimes().Graph).Decode(new byte[] { 0xaa, 0xcc },
            new Dictionary<string, ProtoValue> { ["wanted"] = ProtoValue.Of(false) });

        var extra = run.Nodes.Single(n => n.Of.Name == "extra");

        Assert.IsFalse((bool)extra.Settled(Facet.Present)!);
        Assert.AreEqual(0, extra.Settled(Facet.Extent));

        var trailer = run.Nodes.Single(n => n.Of.Name == "trailer");

        Assert.AreEqual(1, trailer.Settled(Facet.Position), "the trailer moved up into the gap");
        Assert.AreEqual(0xccL, trailer.Value.AsInt());
    }

    [TestMethod]
    public void The_same_octets_read_differently_when_the_condition_differs()
    {
        // Not a curiosity — it is why the condition has to be declared rather than guessed. These three
        // octets are a complete message under both readings, and only the document can say which.
        var withIt = new GraphCodec(Sometimes().Graph).Decode(new byte[] { 0xaa, 0xbb, 0xcc },
            new Dictionary<string, ProtoValue> { ["wanted"] = ProtoValue.Of(true) });

        Assert.AreEqual(0xbbL, withIt.Nodes.Single(n => n.Of.Name == "extra").Value.AsInt());
        Assert.AreEqual(2, withIt.Nodes.Single(n => n.Of.Name == "trailer").Settled(Facet.Position));
    }

    [TestMethod]
    public void What_it_writes_it_reads_back()
    {
        foreach (var wanted in new[] { true, false })
        {
            var octets = Written(wanted);
            var run = new GraphCodec(Sometimes().Graph).Decode(octets,
                new Dictionary<string, ProtoValue> { ["wanted"] = ProtoValue.Of(wanted) });

            Assert.AreEqual(wanted, (bool)run.Nodes.Single(n => n.Of.Name == "extra").Settled(Facet.Present)!);
        }
    }

    // ── The rule ──────────────────────────────────────────────────────────────

    /// <summary>
    /// An optional step with nothing deciding it is a definition error.
    /// </summary>
    /// <remarks>
    /// There is no honest thing for the engine to do with one. Presence from whether a caller filled a
    /// dictionary entry makes the wire depend on how someone happened to build a map; presence from
    /// leftover octets makes it depend on what came after. Both are the engine answering a question the
    /// document never asked, and both look like they work until the day they do not.
    /// </remarks>
    [TestMethod]
    public void Every_arrival_is_checked_for_the_rule_and_a_sound_document_passes_it()
    {
        // The rule is a consistency check between two derivations of one fact: the builder marks the
        // packing edge from the declaration, and separately builds the condition from the same
        // declaration. If those ever drift this is what says so — the shape of defect that produced a
        // walk claiming to handle a field its writer could not write.
        Assert.AreEqual(0, Sometimes().Validate().Count);

        var arrivals = Sometimes().Arrivals().ToList();

        Assert.AreEqual(1, arrivals.Count(a => a.Optional), "exactly the one step that may be skipped");
        Assert.IsTrue(arrivals.Where(a => a.Optional).All(a => a.Place.Name == "extra"));
    }

    // ── Inside a set ──────────────────────────────────────────────────────────

    /// <summary>
    /// A part inside a group can be absent too, and the rule reaches it.
    /// </summary>
    /// <remarks>
    /// The hole this closes. A group's members arrive by <c>Holds</c> rather than by a way on, so a first
    /// version that asked only about ways on gave a group member a condition that decided <b>nothing</b> —
    /// the field was always written, the condition was never consulted, and the validator did not look.
    /// Silently ignoring a stated rule is worse than refusing it. Both are packing edges; both carry it.
    /// </remarks>
    [TestMethod]
    public void A_part_inside_a_set_may_be_absent_and_the_rule_reaches_it()
    {
        var grouped = new MessageDef
        {
            Id = "grouped",
            Context = Context.Given.These("wanted"),
            Fields =
            [
                new Field
                {
                    Id = "header",
                    Pattern = new Pattern.Group(
                    [
                        new Field { Id = "always", Pattern = U8, Value = Expr.Parse("0x11") },
                        new Field
                        {
                            Id = "maybe",
                            Pattern = U8,
                            Value = Expr.Parse("0x22"),
                            When = Expr.Parse("inputs.wanted"),
                        },
                    ]),
                },
            ],
        };

        Assert.AreEqual(0, grouped.Validate().Count, "the condition is seen, so it is not misreported");
        Assert.IsTrue(grouped.Arrivals().Any(a => a.Optional && a.Place.Name == "maybe"),
                      "a member arrives by Holds, and that edge carries optionality too");

        var codec = new GraphCodec(grouped.Graph);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 },
            codec.Encode(new Dictionary<string, ProtoValue> { ["wanted"] = ProtoValue.Of(true) }));

        CollectionAssert.AreEqual(new byte[] { 0x11 },
            codec.Encode(new Dictionary<string, ProtoValue> { ["wanted"] = ProtoValue.Of(false) }));
    }
}
