using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
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
            { "id": "input.entries", "kind": "input", "as": "Entries", "gives": "List",
              "of": { "tag": "Int", "size": "Int" } },
            { "id": "eachTag",  "kind": "evaluated", "label": "this entry's tag",  "runs": "item.tag", "gives": "Int" },
            { "id": "eachSize", "kind": "evaluated", "label": "this entry's size", "runs": "item.size", "gives": "Int" }
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

    // ── One inside another ────────────────────────────────────────────────────

    /// <summary>
    /// A run of names, each of which is a run of labels — which is what every DNS message is made of, and
    /// what SNMP's varbinds, TLS's extensions and BACnet's properties are too.
    /// </summary>
    /// <remarks>
    /// The inner list comes off the outer item, so this also pins the thing that makes nesting worth having
    /// rather than merely legal: the labels of a name are that name's, and an expression asking for them is
    /// answered with the record it is standing in.
    /// </remarks>
    private const string Names = """
        {
          "protocol": "names",
          "nodes": [
            { "id": "p", "kind": "protocol" },
            { "id": "m", "kind": "message" },
            { "id": "arrangement", "kind": "packing" },
            { "id": "names", "kind": "set", "as": "the names" },
            { "id": "name", "kind": "set", "as": "one name" },
            { "id": "labels", "kind": "set", "as": "the labels of it" },
            { "id": "length", "kind": "field", "as": "L",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "text", "kind": "field", "as": "T", "via": "unascii",
              "form": { "of": "opaque" } },
            { "id": "root", "kind": "field", "as": "Root",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "zero", "kind": "constant", "holds": { "int": 0 }, "label": "the root" },
            { "id": "input.names", "kind": "input", "as": "Names", "gives": "List",
              "of": { "labels": "List" } },
            { "id": "itsLabels", "kind": "evaluated", "label": "the labels of this name",
              "runs": "item.labels", "gives": "List", "of": "Text" },
            { "id": "itsLength", "kind": "evaluated", "label": "how long this label is",
              "runs": "len(unascii(item))", "gives": "Int" },
            { "id": "itsText", "kind": "evaluated", "label": "this label", "runs": "item", "gives": "Text" },
            { "id": "itsExtent", "kind": "evaluated", "label": "octets of label, from the length",
              "runs": "length", "gives": "Int", "takes": { "length": "Int" } },
            { "id": "more", "kind": "junction", "as": "another label, or the root?" },
            { "id": "probe.length", "kind": "field", "as": "the next length octet",
              "form": { "of": "scalar", "octets": 1, "big": true, "signed": false } },
            { "id": "another", "kind": "junction", "as": "another name?" },
            { "id": "anyLeft", "kind": "evaluated", "label": "is there another name?",
              "runs": "remaining > 0", "gives": "Bool" },
            { "id": "done", "kind": "end-parse", "as": "the end" }
          ],
          "edges": [
            { "kind": "then", "from": "p", "to": "m" },
            { "kind": "then", "from": "m", "to": "arrangement" },
            { "kind": "then", "from": "arrangement", "to": "names" },
            { "kind": "then", "from": "names", "to": "name" },
            { "kind": "then", "from": "name", "to": "labels" },
            { "kind": "then", "from": "labels", "to": "length" },
            { "kind": "then", "from": "length", "to": "text" },
            { "kind": "then", "from": "text", "to": "root" },
            { "kind": "holds", "from": "names", "to": "name", "order": 0 },
            { "kind": "holds", "from": "name", "to": "labels", "order": 0 },
            { "kind": "holds", "from": "name", "to": "root", "order": 1 },
            { "kind": "holds", "from": "labels", "to": "length", "order": 0 },
            { "kind": "holds", "from": "labels", "to": "text", "order": 1 },
            { "kind": "requires", "from": "names", "to": "input.names", "facet": "each",
              "sequence": 0 },
            { "kind": "requires", "from": "labels", "to": "itsLabels", "facet": "each", "sequence": 0 },
            { "kind": "computes", "from": "length", "to": "itsLength", "facet": "value" },
            { "kind": "computes", "from": "text", "to": "itsText", "facet": "value" },
            { "kind": "computes", "from": "root", "to": "zero", "facet": "value" },

            { "kind": "requires", "from": "itsExtent", "to": "length", "facet": "value",
              "sequence": 0, "parameter": "length" },
            { "kind": "computes", "from": "text", "to": "itsExtent", "facet": "extent",
              "reading": true },

            { "kind": "decode", "from": "text", "to": "more" },
            { "kind": "identifies", "from": "more", "to": "probe.length" },
            { "kind": "decode", "from": "more", "to": "root", "key": { "int": 0 } },
            { "kind": "decode", "from": "more", "to": "length", "otherwise": true },

            { "kind": "decode", "from": "root", "to": "another" },
            { "kind": "decides", "from": "another", "to": "anyLeft", "reading": true },
            { "kind": "decode", "from": "another", "to": "length", "key": { "bool": true } },
            { "kind": "decode", "from": "another", "to": "done", "otherwise": true }
          ]
        }
        """;

    private static ProtoValue Name(params string[] labels)
        => EvalScope.Record(("labels", new ProtoValue.List([.. labels.Select(ProtoValue.Of)])));

    private static byte[] Both(params ProtoValue[] names)
        => new GraphCodec(ProtocolFile.Read(Names).Graph).Encode(
               new Dictionary<string, ProtoValue> { ["Names"] = new ProtoValue.List(names) });

    [TestMethod]
    public void A_repetition_may_hold_another_one()
    {
        // Two names of two and one labels. A single counter shared by the walk cannot write this: the
        // inner run advances the number the outer one is measured by, so the outer stops after as many
        // rounds as the first name had labels — and does it without complaining.
        Assert.AreEqual("02" + "6162" + "02" + "6364" + "00"
                      + "02" + "6566" + "00",
                        Convert.ToHexString(Both(Name("ab", "cd"), Name("ef"))),
            "ab.cd. then ef. — and no number in the description says two names or two labels");
    }

    [TestMethod]
    public void And_the_inner_run_is_the_outer_item_s_own()
    {
        // The point of nesting rather than merely the fact of it: `item.labels` is asked of the name being
        // written, so the second name's labels are the second name's.
        var written = Convert.ToHexString(Both(Name("a"), Name("bb", "ccc")));

        Assert.AreEqual("01" + "61" + "00"
                      + "02" + "6262" + "03" + "636363" + "00", written,
            "one label then two, and the second name's are the second name's");
    }

    [TestMethod]
    public void And_each_round_of_the_inner_one_starts_again()
    {
        // Three names of one label each. If the inner rounds carried across names, the second name would
        // be written from the first's second label — which does not exist, so it would come out empty.
        Assert.AreEqual("017800017900017A00",
                        Convert.ToHexString(Both(Name("x"), Name("y"), Name("z"))));
    }

    [TestMethod]
    public void And_a_reading_goes_round_both_of_them()
    {
        // Coming in there is no list and no count — each run finds its own end by looking. The labels end
        // at a length of nothing and the names end when the octets do, and neither loop knows the other
        // exists.
        var run = new GraphCodec(ProtocolFile.Read(Names).Graph).Decode(
            Both(Name("ab", "cd"), Name("ef"), Name("g", "h", "i")));

        var read = run.Nodes.Where(n => n.Of.Name == "text" && n.Has(Facet.Value))
                            .Select(n => n.Value.AsText())
                            .ToList();

        CollectionAssert.AreEqual(new[] { "ab", "cd", "ef", "g", "h", "i" }, read);

        Assert.AreEqual(3, run.Nodes.Count(n => n.Of.Name == "root" && n.Has(Facet.Value)),
            "three names, and nothing counted them");
    }

    [TestMethod]
    public void And_what_it_read_writes_back_the_same()
    {
        var written = Both(Name("ab", "cd"), Name("ef"), Name("g", "h", "i"));

        var run = new GraphCodec(ProtocolFile.Read(Names).Graph).Decode(written);

        // Back into the shape a caller supplies: a list of names, each a list of labels. Which is the run
        // graph read by frame — a label's enclosing appearance IS the run of labels it belongs to, so the
        // grouping is a fact the graph already holds rather than one the caller reconstructs by counting.
        var names = run.Nodes.Where(n => n.Of.Name == "text" && n.Has(Facet.Value))
                             .GroupBy(n => n.Within)
                             .Select(g => Name([.. g.OrderBy(n => n.Index).Select(n => n.Value.AsText())]))
                             .ToArray();

        CollectionAssert.AreEqual(written, Both(names));
    }
}
