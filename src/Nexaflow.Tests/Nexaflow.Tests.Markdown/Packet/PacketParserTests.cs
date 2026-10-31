using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Packet;

/// <summary>
/// What a <c>packet</c> block is read into: its fields, each its bits and its label, every character kept — the frame every block
/// written Mermaid's way has read as Mermaid reads it, and a label read as the string Mermaid's newer grammars write, in either
/// quote, a backslash keeping a character and a line ending held inside it.
/// </summary>
[TestClass]
[CoversNode("packet-ast")]
public class PacketParserTests
{
    /// <summary>The TCP header Mermaid's documentation draws, titled in its front matter.</summary>
    public const string Tcp =
        """
        ---
        title: "TCP Packet"
        ---
        packet
        0-15: "Source Port"
        16-31: "Destination Port"
        32-63: "Sequence Number"
        64-95: "Acknowledgment Number"
        96-99: "Data Offset"
        100-105: "Reserved"
        106: "URG"
        107: "ACK"
        108: "PSH"
        109: "RST"
        110: "SYN"
        111: "FIN"
        112-127: "Window"
        128-143: "Checksum"
        144-159: "Urgent Pointer"
        160-191: "(Options and Padding)"
        192-255: "Data (variable length)"
        """;

    /// <summary>The UDP header it draws with fields counting their bits, titled on a line of its own.</summary>
    public const string Udp =
        """
        packet
        title UDP Packet
        +16: "Source Port"
        +16: "Destination Port"
        32-47: "Length"
        48-63: "Checksum"
        64-95: "Data (variable length)"
        """;

    /// <summary>A register drawn the way hardware is, its highest bit on the left.</summary>
    public const string Register =
        """
        ---
        config:
          packet:
            bitOrder: descending
            bitsPerRow: 16
        ---
        packet
        0-3: "Mode"
        4-7: "Speed"
        8: "Enable"
        9-15: "Reserved"
        """;

    /// <summary>The blocks Mermaid's documentation shows, and what it says alongside them, which read with nothing wrong with them.</summary>
    public static readonly string[] Documented =
    [
        Tcp,
        Udp,
        Register,
        "packet\n+1: \"Block name\"\n+8: \"Block name\"\n9-15: \"Manually set start and end, it's fine to mix and match\"",
        "packet-beta\n0-15: \"Source Port\"\n16-31: \"Destination Port\"",
    ];

    /// <summary>Every construct read, and what nobody means to write.</summary>
    public static readonly (string What, string Source)[] Blocks =
    [
        ("a header and nothing else", "packet"),
        ("the header Mermaid wrote while it was new", "packet-beta\n0-7: \"Flags\""),
        ("no header, as a packet fence needs none", "0-15: \"Source Port\"\n16-31: \"Destination Port\""),
        ("a title after the header", "packet title TCP\n0-15: \"Source Port\""),
        ("a comment after the header", "packet %% the header\n0-7: \"Flags\""),
        ("a title on a line of its own, an apostrophe and all", "packet\ntitle Bob's packet\n0-7: 'Flags'"),
        ("a title still being written", "packet\ntitle"),
        ("one bit", "packet\n0: \"Flag\""),
        ("a range", "packet\n0-15: \"Port\""),
        ("a range of one bit", "packet\n0-0: \"Flag\""),
        ("bits counted", "packet\n+8: \"Length\"\n+1: \"Flag\""),
        ("space everywhere", "packet\n  0 - 15 :  \"Port\"  \n  + 8 : \"Length\""),
        ("no space anywhere", "packet\n0-15:\"Port\"\n+8:\"Length\""),
        ("a label in single quotes", "packet\n0-7: 'Flags'"),
        ("a quote kept by a backslash", "packet\n0-7: \"say \\\"hi\\\"\"\n8-15: 'it\\'s'"),
        ("a backslash kept by a backslash", "packet\n0-7: \"a\\\\b\""),
        ("a control character named", "packet\n0-7: \"two\\nwords\\ttabbed\""),
        ("a label running across a line ending", "packet\n0-7: \"two\nlines\"\n8-15: \"after\""),
        ("a comment, a comment after a field and a pair of percents in a label",
            "packet\n%% the fields\n0-7: \"Flags\" %% eight of them\n8-15: \"100%% sure\""),
        ("a blank line between fields", "packet\n0-7: \"A\"\n\n8-15: \"B\""),
        ("accessibility lines", "packet\naccTitle: A header\naccDescr: Its fields\n0-7: \"A\""),
        ("an accessible description across lines", "packet\naccDescr {\n  Its fields\n}\n0-7: \"A\""),
        ("a directive", "%%{init: {'theme': 'dark'}}%%\npacket\n0-7: \"A\""),
        ("front matter with its config and theme",
            "---\nconfig:\n  packet:\n    rowHeight: 40\n    bitWidth: 20\n    showBits: false\n  themeVariables:\n    packet:\n      blockFillColor: \"#ffe\"\n---\npacket\n0-7: \"A\""),
        ("written on Windows", "packet\r\n0-15: \"Source Port\"  \r\n16-31: \"Destination Port\"\r\n"),
        // Half written.
        ("a start and nothing more", "packet\n0"),
        ("a start and its dash", "packet\n0-"),
        ("a range with no colon yet", "packet\n0-15"),
        ("a range and its colon", "packet\n0-15:"),
        ("a range, its colon and the space after it", "packet\n0-15: "),
        ("a label with nothing in it", "packet\n0-15: \"\""),
        ("a count still to write", "packet\n0-7: \"A\"\n+: \"B\""),
        ("an end still to write", "packet\n0-: \"A\""),
        ("a start still to write", "packet\n: \"A\"\n-15: \"B\""),
        ("a plus and nothing more", "packet\n+"),
        // What nobody means to write.
        ("a label never closed", "packet\n0-15: \"Source Port\n16-31: \"Destination\""),
        ("a label never closed on the last line", "packet\n0-15: \"Source"),
        ("a label not in quotes", "packet\n0-15: Source Port"),
        ("no colon before the label", "packet\n0-15 \"Source Port\""),
        ("a bit number with noughts before it", "packet\n00-15: \"A\""),
        ("a bit number that is no whole number", "packet\n1.5: \"A\""),
        ("a bit number too large to read", "packet\n0-99999999999: \"A\""),
        ("something after a field", "packet\n0-7: \"A\" and more"),
        ("a header that is not the first line", "%% first\n0-7: \"A\"\npacket"),
        ("a header taking more than a title", "packet beta\n0-7: \"A\""),
        ("nothing anybody means to write", "packet\n??? !!!"),
        ("nothing at all", ""),
        // Read, and wrong only taken together — the stages' to say.
        ("a gap between two fields", "packet\n0-7: \"A\"\n16-23: \"B\""),
        ("two fields overlapping", "packet\n0-15: \"A\"\n8-23: \"B\""),
        ("a field ending before it starts", "packet\n0-7: \"A\"\n15-8: \"B\""),
        ("a field taking no bits", "packet\n+0: \"A\""),
    ];

    private static IEnumerable<(string What, string Source)> All => Blocks.Concat(Documented.Select(source => ("documented", source)));

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = PacketParser.Parse(source);
        return new AstPipeline(PacketPipeline.Of(tree, holes)).Run(tree);
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
        {
            Assert.AreEqual(source, PacketParser.Parse(source).Print(), what);
            Assert.AreEqual(source, Staged(source).Print(), $"{what}, after the stages");
            Assert.AreEqual(source, Staged(source, holes: true).Print(), $"{what}, with holes");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void AndSoDoesEveryStageOnTheWayThere()
    {
        foreach (var (what, source) in All)
            foreach (var holes in new[] { false, true })
            {
                var tree = PacketParser.Parse(source);

                foreach (var stage in PacketPipeline.Of(tree, holes))
                {
                    tree = stage.Run(tree);
                    Assert.AreEqual(source, tree.Print(), $"{what}: after {stage.Name}{(holes ? ", with holes" : "")}");
                }
            }
    }

    [TestMethod, TestCategory("Unit")]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
                Assert.AreEqual(source[..length], Staged(source[..length], holes: true).Print(), $"{what}: after {length} character(s)");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in PacketParser.Parse(source).Placed())
                if (place.Node.IsLeaf)
                    Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
    }

    [TestMethod]
    public void EveryPieceSaysWhereItWasRead()
    {
        foreach (var (what, source) in All)
            foreach (var (stage, tree) in new[] { ("read", PacketParser.Parse(source)), ("after the stages", Staged(source)) })
            {
                var faults = AstOracle.Faults(source, tree).ToList();
                Assert.AreEqual(0, faults.Count, $"{what}, {stage}\n{string.Join("\n", faults)}");
            }
    }

    [TestMethod]
    public void AReversedTreeStillPrintsWhatWasWritten()
    {
        foreach (var (what, source) in All)
            Assert.AreEqual(source, AstOracle.Reversed(PacketParser.Parse(source)).Print(), what);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        foreach (var source in Documented)
        {
            var trouble = Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatWillNotReadSaysWhy()
    {
        foreach (var source in new[]
                 {
                     "packet\n0-15: \"Source", "packet\n0-15: Source Port", "packet\n0-15 \"Source Port\"", "packet\n00-15: \"A\"",
                     "packet\n1.5: \"A\"", "packet\n0-99999999999: \"A\"", "packet\n0-7: \"A\" and more", "packet beta", "packet\n??? !!!",
                 })
            Assert.IsTrue(PacketParser.Parse(source).SelfAndDescendants().Any(node => node.Trouble is not null), source);
    }

    [TestMethod, TestCategory("Unit")]
    public void NothingStillToWriteIsWrong()
    {
        foreach (var source in new[] { "packet\n0-15:", "packet\n0-15: ", "packet\n0-: \"A\"", "packet\n+: \"A\"", "packet\n: \"A\"", "packet\n0-15: \"\"", "packet\ntitle" })
            Assert.IsFalse(Staged(source, holes: true).SelfAndDescendants().Any(node => node.Trouble is not null && !node.IsDerived), source);
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldIsItsBitsAndItsLabel_InWhicheverWayItCountsThem()
    {
        var fields = Fields(PacketParser.Parse("packet\n0-15: \"Port\"\n16: \"Flag\"\n+8: \"Length\""));

        Assert.AreEqual(3, fields.Count);
        Assert.AreEqual("0", Number(fields[0], PacketRoles.Start));
        Assert.AreEqual("15", Number(fields[0], PacketRoles.End));
        Assert.AreEqual("16", Number(fields[1], PacketRoles.Start));
        Assert.IsNull(Number(fields[1], PacketRoles.End), "one bit has no end of its own");
        Assert.AreEqual("8", Number(fields[2], PacketRoles.Bits));
        Assert.IsNull(Number(fields[2], PacketRoles.Start), "and a count starts nowhere it says");
        CollectionAssert.AreEqual(new[] { "Port", "Flag", "Length" }, fields.Select(Label).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ALabelIsReadInEitherQuote_ABackslashKeepingWhatWouldEndIt()
    {
        var fields = Fields(PacketParser.Parse("packet\n0-7: \"say \\\"hi\\\"\"\n8-15: 'it\\'s'"));

        Assert.AreEqual("say \\\"hi\\\"", Label(fields[0]), "the label runs past the quotes its backslashes keep");
        Assert.AreEqual("it\\'s", Label(fields[1]));
        Assert.AreEqual(2, fields[0].SelfAndDescendants().Count(node => node.Kind == PacketKinds.Escape), "each kept quote a piece of its own");
    }

    [TestMethod, TestCategory("Unit")]
    public void ALabelRunsAcrossALineEndingToItsClosingQuote_AndTheFieldIsOneLineOfTheTree()
    {
        var tree = PacketParser.Parse("packet\n0-7: \"two\nlines\"\n8-15: \"after\"");
        var fields = Fields(tree);

        Assert.AreEqual(2, fields.Count);
        Assert.AreEqual("two\nlines", Label(fields[0]));
        Assert.AreEqual(1, fields[0].SelfAndDescendants().Count(node => node.Kind == PacketKinds.Break));
        Assert.AreEqual(3, tree.Children.Count, "the header, the field across two rows, and the one after it");
    }

    [TestMethod, TestCategory("Unit")]
    public void PercentsInsideALabelAreTheLabels_AndAfterItACommentsAndADirectivesAreNot()
    {
        var tree = PacketParser.Parse("packet\n0-7: \"100%% sure\" %% eight bits");
        var field = Fields(tree).Single();

        Assert.AreEqual("100%% sure", Label(field));
        Assert.AreEqual("%% eight bits", field.SelfAndDescendants().Single(node => node.Kind == Kinds.Comment).Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnApostropheInATitleOpensNoLabel()
    {
        var tree = PacketParser.Parse("packet\ntitle Bob's packet\n0-7: 'Flags'");

        Assert.AreEqual("Bob's packet", MermaidBlock.Of(tree).TitleText);
        Assert.AreEqual("Flags", Label(Fields(tree).Single()), "and the field under it is read on its own");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheHeaderIsOptional_AndMayCarryTheTitle()
    {
        Assert.AreEqual(2, Fields(PacketParser.Parse("0-15: \"A\"\n16-31: \"B\"")).Count, "a packet fence needs no header");

        var titled = PacketParser.Parse("packet title TCP\n0-15: \"A\"");
        Assert.AreEqual("TCP", MermaidBlock.Of(titled).TitleText);
        Assert.AreEqual(MermaidDiagram.Packet, MermaidBlock.Of(titled).Diagram);
        Assert.AreEqual(MermaidDiagram.Packet, MermaidBlock.Of(PacketParser.Parse("packet-beta\n0-15: \"A\"")).Diagram);
    }

    [TestMethod, TestCategory("Unit")]
    public void AMermaidBlockHeadedPacketIsHeldAsWrittenInThePacketLanguage()
    {
        foreach (var header in new[] { "packet", "packet-beta" })
        {
            var held = MermaidFenceParser.Parse($"{header}\n0-15: \"A\"");

            Assert.AreEqual(header, ((BlockNode)held).Language, "the fence hands the block to the language its header names");
            Assert.AreEqual(Kinds.Nested, held.Children.Single().Kind);
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void WrittenIntoALabel_WhatWouldEndItIsKeptByABackslash()
    {
        Assert.AreEqual("say \\\"hi\\\" a\\\\b", PacketParser.Escaped("say \"hi\" a\\b", '"'));
        Assert.AreEqual("it\\'s \"so\"", PacketParser.Escaped("it's \"so\"", '\''), "only the quote the label is in");
        Assert.AreEqual("one two", PacketParser.Escaped("one\ntwo", '"'), "and a label is one line");
    }

    /// <summary>Every field read, in the order written.</summary>
    internal static List<ContentNode> Fields(ContentNode tree) => [.. tree.SelfAndDescendants().Where(node => node.Kind == PacketKinds.Field)];

    /// <summary>The bit number written in a field as <paramref name="role"/>, or null where none is.</summary>
    internal static string? Number(ContentNode field, string role) =>
        field.SelfAndDescendants().FirstOrDefault(node => node.Kind == Kinds.Number && node.Role == role)?.Text;

    /// <summary>A field's label as it was written between its quotes.</summary>
    internal static string Label(ContentNode field) => field.Inner(PacketKinds.Label)!.Words()!.Print();
}
