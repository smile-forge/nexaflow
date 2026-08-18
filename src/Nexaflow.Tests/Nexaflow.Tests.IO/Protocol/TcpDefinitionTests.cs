using System.IO;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

// This file's own namespace ends in `Protocol`, so the node type of that name needs saying explicitly.
using ProtocolNode = Nexaflow.IO.Protocol.Wire.Protocol;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>Where the authored protocol definitions live, and how a test gets one.</summary>
public static class Definitions
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Protocol", "Definitions");

    public static ProtocolFile.Loaded Load(string protocol)
    {
        var path = Path.Combine(Directory, protocol + ".json");

        return File.Exists(path)
            ? ProtocolFile.Read(File.ReadAllText(path))
            : throw new FileNotFoundException($"no definition for '{protocol}'", path);
    }
}

/// <summary>
/// TCP, read from the file that defines it.
///
/// <para>
/// These check the <b>description</b>, not the codec: that what RFC 9293 §3.1 says is what the graph says.
/// A definition that loads but describes the wrong protocol fails here rather than three layers down as an
/// off-by-one in a capture nobody can read.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class TcpDefinitionTests
{
    private static ProtocolFile.Loaded Tcp() => Definitions.Load("tcp");

    [TestMethod]
    public void It_loads()
    {
        var tcp = Tcp();

        Assert.AreEqual("tcp", tcp.Id);
        Assert.AreNotEqual(0, tcp.Graph.Nodes.Count);
        Assert.AreNotEqual(0, tcp.Graph.Edges.Count);
    }

    [TestMethod]
    public void It_declares_one_message_format_with_one_arrangement()
    {
        var tcp = Tcp();

        // RFC 9293 §3.1 is titled "Header Format", singular. A SYN segment and an established one are the
        // same format under different flags — and the same ARRANGEMENT too: what differs is which option
        // turns up, which the fork on option-kind says. An arrangement keyed on a flag thirteen fields into
        // the message it arranges cannot be answered in either direction.
        CollectionAssert.AreEqual(new[] { "segment" }, tcp.Messages.Keys.ToArray());

        var packings = tcp.Graph.Nodes.OfType<Packing>().Select(p => p.Name).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "packing.segment" }, packings);
    }

    [TestMethod]
    public void Every_bit_field_is_a_field()
    {
        // The claim being tested is that a control bit is a field that happens to be one bit wide, and not
        // a slice of an octet owned by something else. If bit runs were ever folded back into a group, the
        // eight names below would stop being nodes and this would say so.
        var tcp = Tcp();

        foreach (var (name, bits) in new[]
                 {
                     ("dataOffset", 4), ("reserved", 4),
                     ("cwr", 1), ("ece", 1), ("urg", 1), ("ack", 1),
                     ("psh", 1), ("rst", 1), ("syn", 1), ("fin", 1),
                 })
        {
            var field = tcp.Named[name] as Field;

            Assert.IsNotNull(field, $"'{name}' is a field");
            Assert.IsInstanceOfType<WireForm.Run>(field.Form, $"'{name}' is a run of bits");
            Assert.AreEqual(bits, ((WireForm.Run)field.Form).Bits, $"'{name}' is {bits} bit(s)");
        }
    }

    [TestMethod]
    public void The_control_bits_are_a_set_inside_the_header()
    {
        // RFC 9293 §3.1 names this group, so it is a set here, nested in the header set.
        var tcp = Tcp();

        var control = tcp.Named["controlBits"];
        Assert.IsInstanceOfType<FieldSet>(control);

        CollectionAssert.AreEqual(
            new[] { "cwr", "ece", "urg", "ack", "psh", "rst", "syn", "fin" },
            tcp.Graph.Members(control).Select(m => m.Name).ToArray(),
            "the RFC's wire order");

        // Nested two deep now: the header holds the half before the checksum, which holds the control bits.
        // The split is what lets the checksum be summed over the header with a zero where its own field goes.
        CollectionAssert.Contains(
            tcp.Graph.Members(tcp.Named["header.beforeChecksum"]).ToList(), control,
            "the half of the header before the checksum holds it");

        CollectionAssert.AreEqual(
            new[] { "header.beforeChecksum", "checksum", "header.afterChecksum" },
            tcp.Graph.Members(tcp.Named["header"]).Select(m => m.Name).ToArray());
    }

    [TestMethod]
    public void The_fixed_part_of_the_header_is_twenty_octets()
    {
        // Every member of the header up to the options, summed. It is the one number in TCP that everything
        // else is measured against — Data Offset counts words beyond it, the options span is whatever is
        // left of them — so it is worth counting off the graph rather than assuming.
        var tcp = Tcp();

        IEnumerable<Node> Under(Node place)
            => place is FieldSet set && !ReferenceEquals(place, tcp.Named["options"])
                ? tcp.Graph.Members(set).SelectMany(Under)
                : [place];

        int bits = Under(tcp.Named["header"])
                      .Where(m => !ReferenceEquals(m, tcp.Named["options"]))
                      .OfType<Field>()
                      .Sum(f => f.Form.FixedBits ?? 0);

        Assert.AreEqual(160, bits, "20 octets, counted off the graph rather than declared anywhere");
    }

    [TestMethod]
    public void The_root_is_the_protocol_and_it_leads_to_the_messages()
    {
        // protocol → message → packing → what follows what, by one edge at every step. Rooting the walk at
        // a message instead works while there is exactly one and silently privileges the first the moment
        // there are two.
        var tcp = Tcp();

        Assert.IsInstanceOfType<ProtocolNode>(tcp.Graph.Root);
        Assert.AreEqual("tcp", tcp.Graph.Root.Name);

        var reached = tcp.Graph.From<Then>(tcp.Graph.Root).Select(w => w.To).ToList();
        CollectionAssert.AreEquivalent(tcp.Messages.Values.ToList(), reached);

        foreach (var message in tcp.Messages.Values)
            Assert.AreNotEqual(0, tcp.Graph.From<Then>(message).Count(), $"'{message.Name}' has arrangements");
    }

    [TestMethod]
    public void A_protocol_with_no_root_says_so()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read("""
            { "protocol": "x",
              "nodes": [ { "id": "m", "kind": "message" } ],
              "edges": [] }
            """));

        StringAssert.Contains(refused.Message, "protocol → message → packing");
    }

    [TestMethod]
    public void A_message_nothing_leads_to_says_so()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read("""
            { "protocol": "x",
              "nodes": [ { "id": "x", "kind": "protocol" },
                         { "id": "reachable", "kind": "message" },
                         { "id": "stranded", "kind": "message" } ],
              "edges": [ { "kind": "then", "from": "x", "to": "reachable" } ] }
            """));

        StringAssert.Contains(refused.Message, "stranded");
    }

    [TestMethod]
    public void The_acknowledgment_number_and_the_ack_flag_are_different_fields()
    {
        // Same syllable, unrelated concepts: one is a 32-bit number, the other is one bit saying whether
        // that number means anything. Sharing a node for them would make either unaddressable.
        var tcp = Tcp();

        var number = (Field)tcp.Named["acknowledgmentNumber"];
        var flag = (Field)tcp.Named["ack"];

        Assert.AreNotSame(number, flag);
        Assert.AreEqual(32, number.Form.FixedBits);
        Assert.AreEqual(1, flag.Form.FixedBits);
    }

    [TestMethod]
    public void An_option_is_described_by_its_shape_rather_than_by_its_kind()
    {
        // The other half of the sharing rule: the leading octet of an End-of-Option-List, a No-Operation
        // and a Maximum Segment Size is one concept, so it is one node. What differs is the value it takes
        // on an appearance, and a value belongs to the run graph rather than to the description.
        var tcp = Tcp();

        // RFC 9293 §3.1 gives two cases, not a list of kinds: a single octet, or kind plus length plus
        // that many octets. Enumerating the kinds this file knows would mean a SYN carrying SACK-Permitted
        // does not parse — describing the shapes means an option nobody here has heard of round-trips.
        var shapes = tcp.Graph.From<Then>(tcp.Named["option.shape"]).ToList();

        Assert.AreEqual(2, shapes.Count, "single-octet, and length-carrying");
        CollectionAssert.AreEquivalent(new[] { true, false },
                                       shapes.Select(a => a.Key!.AsBool()).ToArray());

        Assert.AreEqual(1, tcp.Graph.Nodes.Count(n => n is Field { Id: "optionKind" }),
            "and one kind field serves both shapes, because it is one concept");
    }

    [TestMethod]
    public void A_kind_the_reader_does_not_know_is_refused_by_name()
    {
        // The failure worth engineering against is the one that looks like success: a reader that skips
        // what it does not understand gives back a protocol that is shorter by exactly the parts nobody
        // thought to check.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read("""
            { "protocol": "x",
              "nodes": [ { "id": "n", "kind": "sonnet" } ],
              "edges": [] }
            """));

        StringAssert.Contains(refused.Message, "sonnet");
        StringAssert.Contains(refused.Message, "refused rather than skipped");
    }

    [TestMethod]
    public void An_edge_naming_a_node_that_is_not_there_is_refused()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => ProtocolFile.Read("""
            { "protocol": "x",
              "nodes": [ { "id": "a", "kind": "junction" } ],
              "edges": [ { "kind": "then", "from": "a", "to": "b" } ] }
            """));

        StringAssert.Contains(refused.Message, "'b'");
    }
}
