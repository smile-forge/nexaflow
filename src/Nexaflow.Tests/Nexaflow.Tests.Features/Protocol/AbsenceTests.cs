using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// What a part being missing means, which is three separate questions.
/// </summary>
/// <remarks>
/// <para>
/// A part that is not there raises three of them and they have different answers: what it is taken to have
/// said, whether it is written anyway, and whether being missing was allowed at all. Collapsing them is how
/// a truncated message decodes into something plausible — the walk simply stops, reports what it bound, and
/// nothing anywhere says a required part never arrived.
/// </para>
/// <para>
/// Written against a definition made for the purpose rather than a real protocol, because the point is the
/// mechanism and a real one would only exercise whichever answer it happened to need.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol absence policy — engine structure, no single product node")]
public class AbsenceTests
{
    /// <summary>A leading octet, then a part that may not be there, described however the case wants.</summary>
    private static ProtocolFile.Loaded Definition(string absence) => ProtocolFile.Read($$"""
        {
          "protocol": "trailing",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "lead",  "kind": "field", "as": "Lead",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "tail",  "kind": "field", "as": "Tail",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.lead", "kind": "input", "as": "Lead", "gives": "Int" },
            { "id": "input.tail", "kind": "input", "as": "Tail", "gives": "Int" },
            { "id": "there",  "kind": "evaluated", "label": "is there more?", "runs": "remaining > 0", "gives": "Bool" },
            { "id": "asked",  "kind": "evaluated", "label": "was one given?", "runs": "state.sending == 1", "gives": "Bool" },
            { "id": "state.sending", "kind": "state", "as": "Sending", "gives": "Int" },
            {{absence}}
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "lead" },
            { "kind": "then", "from": "lead", "to": "tail", "optional": true },
            { "kind": "computes", "from": "lead", "to": "input.lead", "facet": "value" },
            { "kind": "computes", "from": "tail", "to": "input.tail", "facet": "value" },
            { "kind": "computes", "from": "tail", "to": "there", "facet": "presence", "reading": true },
            { "kind": "computes", "from": "tail", "to": "asked", "facet": "presence", "reading": false },
            { "kind": "requires", "from": "asked", "to": "state.sending", "facet": "value", "sequence": 0 },
            { "kind": "assumes", "from": "tail", "to": "fallback" }
          ]
        }
        """);

    /// <summary>A default node, described however the case wants it.</summary>
    private static string Fallback(long? assumes = null, bool written = false, bool omitted = false,
                                   bool required = false, string because = "")
    {
        List<string> said = ["\"id\": \"fallback\"", "\"kind\": \"default\""];

        if (assumes is { } value) said.Add($"\"is\": {{ \"int\": {value} }}");
        if (written) said.Add("\"written\": true");
        if (omitted) said.Add("\"omitted\": true");
        if (required) said.Add("\"missing\": \"Malformed\"");
        said.Add($"\"because\": \"{because}\"");

        return "{ " + string.Join(", ", said) + " }";
    }

    private static long Tail(RunGraph run)
        => run.Nodes.Single(n => n.Of is Field { Id: "tail" }).Value.AsInt();

    // ── What it is taken to have said ─────────────────────────────────────────

    [TestMethod]
    public void An_absent_part_is_taken_to_have_said_its_default()
    {
        var run = new GraphCodec(Definition(
            Fallback(assumes: 7, because: "the specification says an omitted tail means seven")).Graph)
            .Decode([0x01]);

        Assert.AreEqual(7, Tail(run), "it was not there, and that is what not being there means");
    }

    // ── Whether being missing was allowed ─────────────────────────────────────

    [TestMethod]
    public void A_part_the_specification_requires_is_refused_when_missing()
    {
        // The failure this exists to catch. Everything present is well-formed, so a walk that simply stops
        // hands back a bound `lead` and says nothing at all about the part that never arrived.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Definition(
            Fallback(required: true, because: "RFC-something 4 requires a tail")).Graph)
            .Decode([0x01]));

        StringAssert.Contains(refused.Message, "tail");
        StringAssert.Contains(refused.Message, "requires a tail", "and it says whose rule that is");
    }

    // ── Whether it is written anyway ──────────────────────────────────────────

    [TestMethod]
    public void A_default_that_insists_on_being_written_makes_the_part_present()
    {
        // The reserved-octet case: leaving it out would be a shorter message than the protocol allows, so
        // "absent" is not one of the answers available on the way out.
        var written = new GraphCodec(Definition(
            Fallback(assumes: 0, written: true, because: "the octet is reserved and must be present")).Graph)
            .Encode(new Dictionary<string, ProtoValue>
            {
                ["Lead"] = ProtoValue.Of(1),
                ["Tail"] = ProtoValue.Of(0),
                ["Sending"] = ProtoValue.Of(0),   // nothing asked for it, and it goes out regardless
            });

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x00 }, written);
    }

    // ── Whether it is left out when it would say nothing new ──────────────────

    [TestMethod]
    public void A_part_holding_its_default_is_left_out()
    {
        // "Omit the field when it is zero" is the shortest legal encoding, not an optimisation a writer
        // may take or leave — so the writer takes it even though the caller asked for the part.
        var written = new GraphCodec(Definition(
            Fallback(assumes: 0, omitted: true, because: "a zero tail is what an absent one means")).Graph)
            .Encode(new Dictionary<string, ProtoValue>
            {
                ["Lead"] = ProtoValue.Of(1),
                ["Tail"] = ProtoValue.Of(0),
                ["Sending"] = ProtoValue.Of(1),
            });

        CollectionAssert.AreEqual(new byte[] { 0x01 }, written);
    }

    [TestMethod]
    public void And_the_long_form_of_it_is_refused_coming_in()
    {
        // The half that keeps the round trip honest. Accepting both forms gives one value two encodings,
        // and every message taking the longer one comes back different from how it arrived.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Definition(
            Fallback(assumes: 0, omitted: true, because: "a zero tail is what an absent one means")).Graph)
            .Decode([0x01, 0x00]));

        StringAssert.Contains(refused.Message, "should not be here at all");
        StringAssert.Contains(refused.Message, "two encodings");
    }

    [TestMethod]
    public void And_without_that_an_absent_part_is_written_by_not_writing_it()
    {
        var written = new GraphCodec(Definition(
            Fallback(assumes: 0)).Graph)
            .Encode(new Dictionary<string, ProtoValue>
            {
                ["Lead"] = ProtoValue.Of(1),
                ["Tail"] = ProtoValue.Of(0),
                ["Sending"] = ProtoValue.Of(0),
            });

        CollectionAssert.AreEqual(new byte[] { 0x01 }, written,
            "the only pair of behaviours that is self-consistent: assumed coming in, omitted going out");
    }
}
