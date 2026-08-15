using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Writing a part once per item of a list.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of a reading that goes round. A reading finds out how many there were by looking; a
/// writing is told, and what tells it is the length of a list something requires.
/// </para>
/// <para>
/// <b>Nothing about the repetition is in the graph's structure.</b> The set says it is written once per
/// item of that list and says nothing about how many items there are — because that number belongs to a
/// message rather than to a protocol, and because in other protocols the list is computed, or derived from
/// a field that was just read. A count sitting in the description could be none of those, and a protocol
/// that also states its count on the wire would have to say it twice with nothing keeping the two equal.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol repetition — engine structure, no single product node")]
public class RepetitionTests
{
    /// <summary>A run of two-octet entries, one per item of whatever the list holds.</summary>
    private const string Entries = """
        {
          "protocol": "entries",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "run", "kind": "set", "as": "the entries" },
            { "id": "tag", "kind": "field", "as": "Tag",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "size", "kind": "field", "as": "Size",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "input.entries", "kind": "input", "as": "Entries" },
            { "id": "eachTag",  "kind": "evaluated", "label": "this entry's tag",  "runs": "item.tag" },
            { "id": "eachSize", "kind": "evaluated", "label": "this entry's size", "runs": "item.size" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "run" },
            { "kind": "then", "from": "run", "to": "tag" },
            { "kind": "then", "from": "tag", "to": "size" },
            { "kind": "holds", "from": "run", "to": "tag", "order": 0 },
            { "kind": "holds", "from": "run", "to": "size", "order": 1 },
            { "kind": "requires", "from": "run", "to": "input.entries", "facet": "each", "sequence": 0 },
            { "kind": "computes", "from": "tag", "to": "eachTag", "facet": "value" },
            { "kind": "computes", "from": "size", "to": "eachSize", "facet": "value" }
          ]
        }
        """;

    private static ProtoValue Entry(long tag, long size)
        => EvalScope.Record(("tag", ProtoValue.Of(tag)), ("size", ProtoValue.Of(size)));

    private static byte[] Written(params ProtoValue[] entries)
        => new GraphCodec(ProtocolFile.Read(Entries).Graph).Encode(
               new Dictionary<string, ProtoValue> { ["Entries"] = new ProtoValue.List(entries) });

    [TestMethod]
    public void A_set_is_written_once_for_each_item()
    {
        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x0a, 0x02, 0x0b, 0x03, 0x0c },
            Written(Entry(1, 10), Entry(2, 11), Entry(3, 12)),
            "three entries, and no number anywhere in the description said three");
    }

    [TestMethod]
    public void And_each_time_round_writes_its_own_item()
    {
        // The failure worth guarding: a loop that goes round the right number of times while every pass
        // reads the first item produces a message of exactly the right length and entirely wrong content.
        var written = Written(Entry(0xaa, 0x01), Entry(0xbb, 0x02));

        Assert.AreEqual("AA01BB02", Convert.ToHexString(written));
    }

    [TestMethod]
    public void One_item_is_not_a_special_case()
    {
        CollectionAssert.AreEqual(new byte[] { 0x07, 0x08 }, Written(Entry(7, 8)));
    }

    [TestMethod]
    public void And_the_fields_read_their_item_by_name()
    {
        // `item.tag` rather than digging index 0 out of a list in every field. The same argument as edges
        // naming their ends: a positional item makes reordering a silent change of meaning.
        var run = ProtocolFile.Read(Entries);

        var tag = (Evaluated)run.Named["eachTag"];

        StringAssert.Contains(tag.Runs.Render(), "item.tag");
    }
}
