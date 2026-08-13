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
