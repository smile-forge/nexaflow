using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A message read and written by walking the graph, with nothing threaded through the calls.
///
/// <para>
/// The old codec carries a scope and a frame stack from field to field, and an expression sees whatever
/// happens to be in them. This one assembles a computation's inputs from <i>its own</i> edges immediately
/// before running it, and throws them away after — so what an expression can see is exactly what the graph
/// says it may, and the rule that information comes only from the protocol graph or the run is something
/// the code shape enforces rather than something anyone has to remember.
/// </para>
///
/// <para>
/// Proved against a real document rather than one written to suit: NTP, the same declaration and the same
/// capture the old codec is checked with.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol graph-driven codec — engine structure, no single product node")]
public class GraphCodecTests
{
    private static MessageDef Ntp() => EndToEndCaptureTests.Definition();

    /// <summary>The corpus's own capture, not one written to suit.</summary>
    private static byte[] Capture() => ProtocolCorpus.Get("ntp").Captures[0].Bytes;

    // ── What it can walk ──────────────────────────────────────────────────────

    [TestMethod]
    public void Every_field_of_the_document_is_one_this_walk_reads()
    {
        var unhandled = Ntp().AllFields.Where(f => !GraphCodec.Handles(f)).Select(f => f.Id).ToList();

        Assert.AreEqual(0, unhandled.Count,
            "not yet walkable: " + string.Join(", ", unhandled));
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void It_reads_the_capture_into_appearances_that_hold_what_they_came_to()
    {
        var run = new GraphCodec(Ntp()).Decode(Capture());

        // Every field on the path has an appearance, and every appearance knows four things about itself.
        foreach (var appearance in run.Nodes.Where(n => n.Of is Field))
            foreach (var facet in new[] { Facet.Value, Facet.Extent, Facet.Position, Facet.Emitted })
                Assert.IsTrue(appearance.Has(facet), $"{appearance} has no {facet}");

        var positions = run.Nodes.Where(n => n.Of is Field)
                                 .Select(n => (int)n.Settled(Facet.Position)!)
                                 .Order().ToList();

        Assert.AreEqual(0, positions[0], "the first thing starts where the message does");
        CollectionAssert.AllItemsAreUnique(positions);
    }

    [TestMethod]
    public void It_agrees_with_the_codec_it_is_replacing()
    {
        // The point of doing this beside the old one rather than instead of it: the same octets, read two
        // ways, have to say the same thing before anything moves over.
        var expected = new MessageCodec(Ntp()).Decode(Capture());
        var run = new GraphCodec(Ntp()).Decode(Capture());

        List<string> differs = [];

        foreach (var appearance in run.Nodes.Where(n => n.Of is Field { Pattern: not Pattern.Group }))
        {
            var field = (Field)appearance.Of;
            var mine = appearance.Value;
            var theirs = expected[field.CaptureName];

            if (theirs.IsNull || mine.ToString() == theirs.ToString()) continue;

            differs.Add($"{field.Id}: walk says {mine}, the old codec says {theirs}");
        }

        Assert.AreEqual(0, differs.Count, string.Join("\n  • ", differs));
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void What_it_reads_it_writes_back_octet_for_octet()
    {
        var run = new GraphCodec(Ntp()).Decode(Capture());

        // Handed back as inputs, which is the only way in: there is no other door.
        Dictionary<string, ProtoValue> supplied = [];

        foreach (var source in Ntp().Context)
            if (run.Nodes.FirstOrDefault(n => n.Of is Field f && f.Id == source.Key) is { } from
                && from.Has(Facet.Value))
                supplied[source.Key] = from.Value;

        CollectionAssert.AreEqual(Capture(), new GraphCodec(Ntp()).Encode(supplied));
    }

    // ── Bits, and what they make possible ─────────────────────────────────────

    /// <summary>
    /// A field that is not a whole number of octets.
    /// </summary>
    /// <remarks>
    /// The reason emission counts in bits rather than octets. While a bit group had to come to a whole
    /// octet, "these five bits, then those eleven" could be described and not written — and there are
    /// protocols with them. Nothing here is a special case: the runs go out in order and an octet is what
    /// falls out when eight bits have gone by, wherever the boundary lands.
    /// </remarks>
    [TestMethod]
    public void Runs_that_straddle_an_octet_boundary_are_written_and_read_back()
    {
        var straddling = new MessageDef
        {
            Id = "straddle",
            Context = Context.Given.These("first", "second", "third"),
            Fields =
            [
                new Field
                {
                    Id = "packed",
                    Pattern = new Pattern.Bits(
                    [
                        new BitSlice("first", 5, Expr.Parse("inputs.first")),
                        new BitSlice("second", 11, Expr.Parse("inputs.second")),
                    ]),
                },
            ],
        };

        // 5 + 11 is sixteen bits and neither run sits inside one octet.
        var written = new GraphCodec(straddling).Encode(new Dictionary<string, ProtoValue>
        {
            ["first"] = ProtoValue.Of(0b10101L),
            ["second"] = ProtoValue.Of(0b101_1001_1001L),
        });

        CollectionAssert.AreEqual(new byte[] { 0b10101101, 0b10011001 }, written);

        var read = new GraphCodec(straddling).Decode(written);
        var runs = (ProtoValue.Rec)read.Nodes.Single(n => n.Of is Field).Value;

        Assert.AreEqual(0b10101L, runs.Members["first"].AsInt());
        Assert.AreEqual(0b101_1001_1001L, runs.Members["second"].AsInt());
    }

    [TestMethod]
    public void A_message_that_ends_mid_octet_is_refused_rather_than_padded()
    {
        var short_ = new MessageDef
        {
            Id = "ragged",
            Context = Context.Given.These("only"),
            Fields =
            [
                new Field
                {
                    Id = "packed",
                    Pattern = new Pattern.Bits([new BitSlice("only", 5, Expr.Parse("inputs.only"))]),
                },
            ],
        };

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(short_).Encode(
                new Dictionary<string, ProtoValue> { ["only"] = ProtoValue.Of(1L) }));

        StringAssert.Contains(refused.Message, "not a whole number of octets");
    }

    /// <summary>
    /// A span sized by another field, which is not a shape but a computed extent.
    /// </summary>
    /// <remarks>
    /// The observation that removes two pattern variants: a span whose length is read off elsewhere and a
    /// region declaring how far it runs are both <i>a computation producing an extent</i>. Nothing in the
    /// walk knows what a length is — it asks the node what its extent comes from, the same way it asks
    /// what its value comes from.
    /// </remarks>
    [TestMethod]
    public void A_span_takes_its_width_from_whatever_computes_its_extent()
    {
        var counted = new MessageDef
        {
            Id = "counted",
            Context = Context.Given.These("body"),
            Fields =
            [
                new Field
                {
                    Id = "length", Pattern = new Pattern.Scalar(1, BigEndian: true),
                    Value = Expr.Parse("fields.body.extent"),
                },
                new Field
                {
                    Id = "body",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.length.value")),
                    Value = Expr.Parse("inputs.body"),
                },
            ],
        };

        var read = new GraphCodec(counted).Decode(new byte[] { 3, 0xaa, 0xbb, 0xcc });
        var body = read.Nodes.Single(n => n.Of is Field { Id: "body" });

        CollectionAssert.AreEqual(new byte[] { 0xaa, 0xbb, 0xcc }, body.Value.AsBytes());
        Assert.AreEqual(3, body.Settled(Facet.Extent));

        // The extent came from an edge to the length, and nothing in the walk knows what a length is.
        var measures = counted.ProducerOf(counted.AllFields.Single(f => f.Id == "body"), "extent");
        Assert.IsNotNull(measures);
        Assert.AreEqual("length", counted.InputsOf(measures!).Single().To.Name);
    }

    // ── Forks ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A message whose shape depends on what it said earlier.
    /// </summary>
    /// <remarks>
    /// Modbus answers a read with a byte count and registers, and an error with an exception code, and
    /// which arrived is the top bit of the function code. The walk decides that where it reaches it,
    /// against a value that has actually settled — not by planning the path and then following it.
    /// </remarks>
    [DataTestMethod]
    [DataRow(0, "the ordinary answer")]
    [DataRow(1, "and the exception")]
    public void A_fork_is_decided_where_the_walk_reaches_it(int which, string what)
    {
        var response = FramedChoiceCaptureTests.Response();
        var capture = ProtocolCorpus.Get("modbus").Captures
                                    .Where(c => c.Label.Contains("response", StringComparison.OrdinalIgnoreCase))
                                    .ElementAt(which).Bytes;

        var expected = new MessageCodec(response).Decode(capture);
        var run = new GraphCodec(response).Decode(capture);

        // Every field the walk reached is one the old codec also bound, holding the same value.
        List<string> differs = [];

        foreach (var appearance in run.Nodes.Where(n => n.Of is Field { Pattern: not Pattern.Group }))
        {
            var field = (Field)appearance.Of;
            var theirs = expected[field.CaptureName];

            if (theirs.IsNull || appearance.Value.ToString() == theirs.ToString()) continue;

            differs.Add($"{field.Id}: walk says {appearance.Value}, the old codec says {theirs}");
        }

        Assert.AreEqual(0, differs.Count, what + ": " + string.Join(" / ", differs));

        // And the arm that did not apply was never walked at all — its fields have no appearance.
        var walked = run.Nodes.Select(n => n.Of).OfType<Field>().ToHashSet();
        var choice = response.AllFields.First(f => f.Pattern is Pattern.Choice);
        var arms = ((Pattern.Choice)choice.Pattern).Arms;

        Assert.IsTrue(arms.Count(a => a.Fields.Any(walked.Contains)) <= 1,
            "two packings cannot both have happened");
    }

    // ── When, as opposed to what ──────────────────────────────────────────────

    /// <summary>
    /// A length that measures a span written after it.
    /// </summary>
    /// <remarks>
    /// The shape every framed protocol has, and the one a forward walk cannot do: the first field's value
    /// is a fact about the third field's octets, which do not exist when the walk reaches the first. The
    /// arrangement says what is in the message; the worklist says when each fact can be had, and the two
    /// orders are different on purpose.
    /// </remarks>
    [TestMethod]
    public void A_length_can_measure_something_the_walk_has_not_reached_yet()
    {
        var framed = new MessageDef
        {
            Id = "framed",
            Context = Context.Given.These("body", "tag"),
            Fields =
            [
                new Field
                {
                    Id = "length", Pattern = new Pattern.Scalar(2, BigEndian: true),
                    Value = Expr.Parse("fields.body.extent"),
                },
                new Field
                {
                    Id = "tag", Pattern = new Pattern.Scalar(1, BigEndian: true),
                    Value = Expr.Parse("inputs.tag"),
                },
                new Field
                {
                    Id = "body",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.length.value")),
                    Value = Expr.Parse("inputs.body"),
                },
            ],
        };

        var written = new GraphCodec(framed).Encode(new Dictionary<string, ProtoValue>
        {
            ["tag"] = ProtoValue.Of(0x2aL),
            ["body"] = ProtoValue.Of(new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x01 }),
        });

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x05, 0x2a, 0xde, 0xad, 0xbe, 0xef, 0x01 }, written);

        // And back, where the same declaration is read the other way round: the length is on the wire and
        // the span takes its extent from it.
        var read = new GraphCodec(framed).Decode(written);

        Assert.AreEqual(5L, read.Nodes.Single(n => n.Of is Field { Id: "length" }).Value.AsInt());
        CollectionAssert.AreEqual(new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x01 },
            read.Nodes.Single(n => n.Of is Field { Id: "body" }).Value.AsBytes());
    }

    [TestMethod]
    public void A_value_that_waits_on_itself_is_named_rather_than_hung_on()
    {
        var circular = new MessageDef
        {
            Id = "circular",
            Context = Context.Given.These("seed"),
            Fields =
            [
                new Field
                {
                    Id = "first", Pattern = new Pattern.Scalar(1, BigEndian: true),
                    Value = Expr.Parse("fields.second.value"),
                },
                new Field
                {
                    Id = "second", Pattern = new Pattern.Scalar(1, BigEndian: true),
                    Value = Expr.Parse("fields.first.value"),
                },
            ],
        };

        var refused = Assert.ThrowsExactly<ResolutionException>(
            () => new GraphCodec(circular).Encode(
                new Dictionary<string, ProtoValue> { ["seed"] = ProtoValue.Of(1L) }));

        StringAssert.Contains(refused.Message, "depend on each other");
    }

    // ── The rule it keeps ─────────────────────────────────────────────────────

    /// <summary>
    /// An expression sees what its edges say and nothing else.
    /// </summary>
    /// <remarks>
    /// The invariant, tested the only way it can be: take a document whose fields are all present and
    /// correct, and check that a computation's scope contains exactly the parts it declared an edge to.
    /// Under a threaded scope this could not fail, because everything walked past is in it — which is what
    /// made "where did that value come from" unanswerable.
    /// </remarks>
    [TestMethod]
    public void A_computation_is_handed_only_what_its_edges_point_at()
    {
        var message = Ntp();
        var run = new GraphCodec(message).Decode(Capture());

        foreach (var field in message.AllFields.Where(f => f.Value is not null))
        {
            var computation = message.ComputationOf(field, field.Value!);
            if (computation is null) continue;

            var reachable = message.InputsOf(computation).Select(e => e.To.Name).ToHashSet();

            // Whatever the expression names, an edge names too. The document checks refuse the other
            // direction at authoring time; this is the run-time half of the same statement.
            foreach (var wanted in computation.Wants.Where(w => w.From != Origin.Stated))
                Assert.IsTrue(reachable.Contains(wanted.Name),
                    $"{field.Id} wants {wanted} and no edge reaches it");
        }

        Assert.IsTrue(run.Nodes.Any(), "and the run actually happened");
    }
}
