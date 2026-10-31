using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Markdown.Packet.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Packet;

/// <summary>
/// What a packet's stages write into its tree: the bits each field takes and how they fall across the rows, what is wrong with a
/// field only the fields before it show — said on the number at fault — what each escape in a label says, and what the front
/// matter asks for. Everything a builder needs, so it only lays the packet out.
/// </summary>
[TestClass]
[CoversNode("packet-ast")]
public class PacketStagesTests
{
    [TestMethod, TestCategory("Unit")]
    public void TheDocumentedTcpHeaderTakesTheBitsItIsWrittenWith()
    {
        var fields = Placed(PacketParserTests.Tcp);

        Assert.AreEqual(17, fields.Count);
        Assert.AreEqual((0L, 15L), (fields[0].First, fields[0].Last));
        Assert.AreEqual((106L, 106L), (fields[6].First, fields[6].Last), "one bit");
        Assert.AreEqual((192L, 255L), (fields[^1].First, fields[^1].Last));
        CollectionAssert.AreEqual(new[] { new PacketSpan(1, 32, 63) }, fields[2].Spans.ToArray(), "the third field is the second row, whole");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldCountingItsBitsStartsWhereTheOneBeforeItEnded()
    {
        var fields = Placed(PacketParserTests.Udp);

        Assert.AreEqual((0L, 15L), (fields[0].First, fields[0].Last));
        Assert.AreEqual((16L, 31L), (fields[1].First, fields[1].Last));
        Assert.AreEqual((32L, 47L), (fields[2].First, fields[2].Last), "and one written after them goes on from there");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldRunningPastTheEndOfARowGoesOnIntoTheNext_ARunOnEach()
    {
        var wide = Placed("packet\n0-7: \"A\"\n8-71: \"Wide\"")[1];

        CollectionAssert.AreEqual(new[] { new PacketSpan(0, 8, 31), new PacketSpan(1, 32, 63), new PacketSpan(2, 64, 71) }, wide.Spans.ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void HowManyBitsARowHoldsIsTheFrontMatters()
    {
        var fields = Placed("---\nconfig:\n  packet:\n    bitsPerRow: 8\n---\npacket\n0-11: \"A\"\n+4: \"B\"");

        CollectionAssert.AreEqual(new[] { new PacketSpan(0, 0, 7), new PacketSpan(1, 8, 11) }, fields[0].Spans.ToArray());
        CollectionAssert.AreEqual(new[] { new PacketSpan(1, 12, 15) }, fields[1].Spans.ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldStartingAfterAGapIsDrawnWhereItIsWritten_ItsStartMarked()
    {
        var tree = PacketParserTests.Staged("packet\n0-7: \"A\"\n16-23: \"B\"\n+8: \"C\"");
        var fields = Placed(tree);

        Assert.AreEqual((16L, 23L), (fields[1].First, fields[1].Last));
        StringAssert.Contains(Trouble(tree, PacketRoles.Start, 1), "starts at 8", "said on the start, saying where it starts");
        Assert.AreEqual(24L, fields[2].First, "and the next goes on from it");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldOverlappingTheOneBeforeItIsMarked_AndSoIsAFirstFieldNotAtBitNought()
    {
        var overlapping = PacketParserTests.Staged("packet\n0-15: \"A\"\n8-23: \"B\"");
        StringAssert.Contains(Trouble(overlapping, PacketRoles.Start, 1), "already belongs");

        var late = PacketParserTests.Staged("packet\n4-7: \"A\"");
        StringAssert.Contains(Trouble(late, PacketRoles.Start, 0), "starts at bit 0");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldEndingBeforeItStartsIsABitWide_ItsEndInTheWay()
    {
        var tree = PacketParserTests.Staged("packet\n0-7: \"A\"\n15-8: \"B\"");
        var field = Placed(tree)[1];

        Assert.AreEqual((15L, 15L), (field.First, field.Last));
        Assert.AreEqual(PacketRoles.End, field.Unsized);
        StringAssert.Contains(Trouble(tree, PacketRoles.End, 1), "before");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldTakingNoBitsIsMarked_AndOneWhoseCountIsStillToWriteIsNot()
    {
        var none = PacketParserTests.Staged("packet\n+0: \"A\"");
        Assert.AreEqual(PacketRoles.Bits, Placed(none)[0].Unsized);
        StringAssert.Contains(Trouble(none, PacketRoles.Bits, 0), "+0");

        var writing = PacketParserTests.Staged("packet\n0-7: \"A\"\n+: \"B\"", holes: true);
        var field = Placed(writing)[1];

        Assert.AreEqual((8L, 8L), (field.First, field.Last), "a bit wide where it would start");
        Assert.AreEqual(PacketRoles.Bits, field.Unsized);
        Assert.IsNull(Trouble(writing, PacketRoles.Bits, 1), "nothing is wrong with what is still to write");
    }

    [TestMethod, TestCategory("Unit")]
    public void AFieldWhoseStartIsStillToWriteStartsAfterTheOneBeforeIt()
    {
        var field = Placed(PacketParserTests.Staged("packet\n0-7: \"A\"\n-15: \"B\"", holes: true))[1];

        Assert.AreEqual((8L, 15L), (field.First, field.Last));
        Assert.IsNull(field.Unsized);
    }

    [TestMethod, TestCategory("Unit")]
    public void APacketIsDrawnToTenThousandRows_AndWhatRunsPastThemSaysSo()
    {
        var last = (long)PlaceFields.MostRows * 32;
        var tree = PacketParserTests.Staged($"packet\n0-{last + 99}: \"Huge\"");
        var field = Placed(tree).Single();

        Assert.AreEqual(PlaceFields.MostRows, field.Spans.Count);
        Assert.AreEqual(last - 1, field.Spans[^1].To);
        StringAssert.Contains(Trouble(tree, PacketRoles.End, 0), "10000 rows");
    }

    [TestMethod, TestCategory("Unit")]
    public void AnEscapeSaysTheCharacterItKeeps_AndALineEndingInALabelIsASpace()
    {
        var tree = PacketParserTests.Staged("packet\n0-7: \"say \\\"hi\\\" a\\\\b\\tc\"\n8-15: 'two\nlines'");
        var said = Said(tree);

        CollectionAssert.AreEqual(new[] { "say \"hi\" a\\b c", "two lines" }, said);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterIsReadWithMermaidsDefaultsAndLimits()
    {
        Assert.AreEqual(new PacketConfig(), PacketConfig.Read(null), "nothing written is the defaults");
        Assert.AreEqual((32.0, 32.0, 32, true, PacketBitOrder.Ascending, 5.0, 5.0),
                        (PacketConfig.Default.RowHeight, PacketConfig.Default.BitWidth, PacketConfig.Default.BitsPerRow, PacketConfig.Default.ShowBits,
                         PacketConfig.Default.BitOrder, PacketConfig.Default.PaddingX, PacketConfig.Default.PaddingY));

        var config = PacketConfig.Read(
            "config:\n  packet:\n    rowHeight: 40\n    bitWidth: 20\n    bitsPerRow: 16\n    showBits: false\n    bitOrder: descending\n    paddingX: 2\n    paddingY: 0\n"
            + "  themeVariables:\n    packet:\n      byteFontSize: 9px\n      labelFontSize: 14px\n      titleFontSize: 18px\n      startByteColor: red\n      endByteColor: blue\n"
            + "      labelColor: green\n      titleColor: purple\n      blockFillColor: \"#ffe\"\n      blockStrokeColor: black\n      blockStrokeWidth: 2\n");

        Assert.AreEqual((40.0, 20.0, 16, false, PacketBitOrder.Descending, 2.0, 0.0),
                        (config.RowHeight, config.BitWidth, config.BitsPerRow, config.ShowBits, config.BitOrder, config.PaddingX, config.PaddingY));
        Assert.AreEqual((9.0, 14.0, 18.0, 2.0), (config.ByteFontSize, config.LabelFontSize, config.TitleFontSize, config.BlockStrokeWidth));
        Assert.AreEqual(("red", "blue", "green", "purple", "#ffe", "black"),
                        (config.StartByteColour, config.EndByteColour, config.LabelColour, config.TitleColour, config.BlockFillColour, config.BlockStrokeColour));

        var silly = PacketConfig.Read("config:\n  packet:\n    rowHeight: -4\n    bitsPerRow: 0\n    paddingX: -1\n");
        Assert.AreEqual((1.0, 1, 0.0), (silly.RowHeight, silly.BitsPerRow, silly.PaddingX), "brought back inside what each allows");
    }

    /// <summary>Every field of <paramref name="source"/> as its stages leave it.</summary>
    private static List<PacketFieldNode> Placed(string source) => Placed(PacketParserTests.Staged(source));

    private static List<PacketFieldNode> Placed(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<PacketFieldNode>()];

    /// <summary>What is said to be wrong with the number written as <paramref name="role"/> in the field at <paramref name="at"/>, or null.</summary>
    private static string? Trouble(ContentNode tree, string role, int at) =>
        Placed(tree)[at].SelfAndDescendants().FirstOrDefault(node => node.Kind == Kinds.Number && node.Role == role)?.Trouble;

    /// <summary>What every label says, its escapes and line endings read as what they stand for.</summary>
    private static List<string> Said(ContentNode tree)
    {
        var pieces = MermaidWords.Held(tree);

        return [.. Placed(tree).Select(field => field.Inner(PacketKinds.Label)!.Words()!)
                              .Select(words => pieces.Of(words) is { } said ? string.Concat(said.Select(piece => piece.Says)) : words.Text)];
    }
}
