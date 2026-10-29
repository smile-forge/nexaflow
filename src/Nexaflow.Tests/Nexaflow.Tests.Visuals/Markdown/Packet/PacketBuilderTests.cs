using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Packet;

/// <summary>
/// A packet drawn on the shared layout tree: a box for each field's run of bits on a row, as wide as its bits, standing for the
/// line it was written on, its label typed into, and its first and last bit numbered over its ends — and a field that breaks a
/// rule drawn where it can be put right.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("packet")]
public class PacketBuilderTests : MermaidBuilderContract
{
    private const string Tcp =
        "---\ntitle: \"TCP Packet\"\n---\npacket\n0-15: \"Source Port\"\n16-31: \"Destination Port\"\n32-63: \"Sequence Number\"\n"
        + "64-95: \"Acknowledgment Number\"\n96-99: \"Data Offset\"\n100-105: \"Reserved\"\n106: \"URG\"\n107: \"ACK\"\n108: \"PSH\"\n"
        + "109: \"RST\"\n110: \"SYN\"\n111: \"FIN\"\n112-127: \"Window\"\n128-143: \"Checksum\"\n144-159: \"Urgent Pointer\"\n"
        + "160-191: \"(Options and Padding)\"\n192-255: \"Data (variable length)\"";

    private const string Udp = "packet\ntitle UDP Packet\n+16: \"Source Port\"\n+16: \"Destination Port\"\n32-47: \"Length\"\n48-63: \"Checksum\"";

    private const string Register = "---\nconfig:\n  packet:\n    bitOrder: descending\n    bitsPerRow: 16\n---\npacket\n0-3: \"Mode\"\n4-7: \"Speed\"\n8: \"Enable\"\n9-15: \"Reserved\"";

    /// <summary>A packet block is a language of its own: its fence names it, though a Mermaid header naming it is read too.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.Packet;

    /// <inheritdoc/>
    public override string Language => PacketParser.Language;

    /// <inheritdoc/>
    protected override bool FitsTheRoom => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Drawn { get; } =
    [
        ("the documented TCP header", Tcp),
        ("the documented UDP header, counting its bits", Udp),
        ("a register numbered from the left", Register),
        ("no header", "0-15: \"Source Port\"\n16-31: \"Destination Port\""),
        ("a field running across rows", "packet\n0-7: \"A\"\n8-71: \"Wide\""),
        ("labels in either quote, escaped", "packet\n0-7: 'it\\'s'\n8-15: \"say \\\"hi\\\"\"\n16-23: \"two\nlines\""),
        ("bits not numbered, and a geometry of its own",
            "---\nconfig:\n  packet:\n    showBits: false\n    rowHeight: 20\n    bitWidth: 12\n    paddingX: 0\n  themeVariables:\n    packet:\n      blockFillColor: \"#ffeeaa\"\n      labelColor: \"#222222\"\n---\npacket\n0-7: \"A\"\n8-15: \"B\""),
        ("still being written", "packet\n0-7: \"\"\n+: \"B\"\n16-: \"C\"\n24-31:"),
        ("rules broken", "packet\n4-7: \"A\"\n16-23: \"B\"\n20-27: \"C\"\n40-30: \"D\"\n+0: \"E\""),
        ("a label too long for its box", "packet\n0: \"A label far longer than the one bit it names\"\n1-31: \"Rest\""),
        ("nothing to draw", "packet\ntitle Empty"),
    ];

    [TestMethod]
    public void EveryFieldIsABoxStandingForTheLineItWasWrittenOn() => UiThread.Run(() =>
    {
        const string source = "packet\n0-15: \"Source Port\"\n16-31: \"Destination Port\"\n32: \"Flag\"";

        CollectionAssert.AreEqual(new[] { "0-15: \"Source Port\"", "16-31: \"Destination Port\"", "32: \"Flag\"" },
                                  Pieces(Lay(source), PacketPiece.Field).Select(box => Written(source, box.Part)).ToArray());
    });

    [TestMethod]
    public void TheLayoutIsItsRowsAsDrawn_EachHoldingTheFieldsOnIt() => UiThread.Run(() =>
    {
        const string source = "packet\n0-7: \"A\"\n8-47: \"Wide\"\n48-63: \"B\"";
        var rows = Pieces(Lay(source), PacketPiece.Row);

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows[0].Bounds.Top < rows[1].Bounds.Top, "top to bottom");
        CollectionAssert.AreEqual(new[] { "0-7: \"A\"", "8-47: \"Wide\"" }, Fields(rows[0]).Select(field => Written(source, field.Part)).ToArray());
        CollectionAssert.AreEqual(new[] { "8-47: \"Wide\"", "48-63: \"B\"" }, Fields(rows[1]).Select(field => Written(source, field.Part)).ToArray());
        Assert.IsTrue(rows.All(row => row.Part is null), "a row is how the bits are laid out, and stands for nothing written");
    });

    [TestMethod]
    public void AFieldAcrossTwoRowsIsARunInEach_BothStandingForTheOneFieldWritten() => UiThread.Run(() =>
    {
        const string source = "packet\n0-7: \"A\"\n8-47: \"Wide\"";
        var runs = Pieces(Lay(source), PacketPiece.Row).Select(row => Fields(row).Last()).ToList();

        Assert.AreEqual(2, runs.Count);
        Assert.AreNotSame(runs[0].Parent, runs[1].Parent, "one in each row");
        Assert.AreSame(((ContentPart)runs[0].Part!).Node, ((ContentPart)runs[1].Part!).Node, "and both point back to the same field");
    });

    [TestMethod]
    public void ARunHoldsItsBoxAndItsLabel_AndTheNumbersOverItAreItsRowsOwn() => UiThread.Run(() =>
    {
        var laid = Lay("packet\n0-15: \"A\"\n+16: \"B\"");

        Assert.IsTrue(Pieces(laid, PacketPiece.Label).All(label => label.Parent.Kind == PacketPiece.Field), "each label in its run");
        Assert.IsTrue(Pieces(laid, PacketPiece.Bit).All(bit => bit.Parent.Kind == PacketPiece.Bits && bit.Parent.Parent.Kind == PacketPiece.Row),
                      "and the numbers in the band over the row, apart from its runs");
        CollectionAssert.AreEqual(new[] { "0", "15" }, Numbers(Pieces(laid, PacketPiece.Field)[0]));
    });

    [TestMethod]
    public void ChoosingAFieldWashesItsBoxAndLabel_NotTheNumbersOverIt() => UiThread.Run(() =>
    {
        var field = Pieces(Lay("packet\n0-15: \"A\"\n16-31: \"B\""), PacketPiece.Field)[0];

        Assert.AreEqual(Box(field).Top, field.Bounds.Top, 0.01, "what stands for the field reaches no higher than its box");
        Assert.AreEqual(Box(field).Bottom, field.Bounds.Bottom, 0.01);
    });

    [TestMethod]
    public void ALabelCutShortToFitItsBoxIsSaidWholeWhereItIsRestedOn() => UiThread.Run(() =>
    {
        var fields = Pieces(Lay("packet\n0: \"Urgent pointer field\"\n1-31: \"Rest\""), PacketPiece.Field);

        Assert.AreEqual("Urgent pointer field", fields[0].Acts?.Tip, "too wide for one bit, so said whole");
        Assert.IsNull(fields[1].Acts?.Tip, "and one that fits says nothing more");
    });

    [TestMethod]
    public void TheNumbersOverOneBitFieldsNeverRunIntoOneAnother_HoweverNarrowTheRoom() => UiThread.Run(() =>
    {
        const string source = "packet\n0-5: \"A\"\n106: \"URG\"\n107: \"ACK\"\n108: \"PSH\"\n109: \"RST\"\n110: \"SYN\"\n111: \"FIN\"\n112-127: \"Window\"";

        foreach (var room in new[] { 700d, 240d })
            foreach (var band in Pieces(Lay(source, room), PacketPiece.Bits))
            {
                var numbers = band.Children.Where(piece => piece.Kind == PacketPiece.Bit).OrderBy(bit => bit.Bounds.Left).ToList();
                Assert.IsTrue(numbers.Zip(numbers.Skip(1)).All(pair => pair.First.Bounds.Right <= pair.Second.Bounds.Left + 0.01),
                              $"room {room}: {string.Join(" ", numbers.Select(Says))}");
            }
    });

    [TestMethod]
    public void ALabelIsSetInTheThemesText_WhateverTheTheme() => UiThread.Run(() =>
    {
        foreach (var style in new[] { WpfStyles.Dark, WpfStyles.Light })
        {
            var label = Pieces(Laying.Lay(PacketParser.Language, "packet\n0-15: \"A\"", 700, style), PacketPiece.Label).Single();
    var words = label.SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<TextMark>().First();
            Assert.AreSame(style.Text, words.Ink, "as every diagram's node is");
        }
    });

    [TestMethod]
    public void ABoxIsAsWideAsItsBits_AndStandsWhereTheyAreOnItsRow() => UiThread.Run(() =>
    {
        var boxes = Pieces(Lay("packet\n0-15: \"A\"\n16-23: \"B\"\n24: \"C\"\n25-31: \"D\""), PacketPiece.Field).Select(Box).ToList();

        Assert.AreEqual(boxes[0].Width, boxes[1].Width * 2, 6, "sixteen bits are twice eight");
        Assert.IsTrue(boxes[2].Width < boxes[1].Width / 4, "and one bit is narrow");
        Assert.IsTrue(boxes[0].Right <= boxes[1].Left && boxes[1].Right <= boxes[2].Left && boxes[2].Right <= boxes[3].Left, "left to right, in the order of their bits");
        Assert.IsTrue(boxes.All(box => Math.Abs(box.Top - boxes[0].Top) < 0.5), "on one row");
    });

    [TestMethod]
    public void AFieldRunningPastTheEndOfARowIsABoxOnEachRow_ItsLabelTypedIntoOnlyOnTheFirst() => UiThread.Run(() =>
    {
        const string source = "packet\n0-7: \"A\"\n8-71: \"Wide\"";
        var laid = Lay(source);
        var wide = Pieces(laid, PacketPiece.Field).Where(box => Written(source, box.Part).StartsWith("8-71", StringComparison.Ordinal)).Select(Box).ToList();

        Assert.AreEqual(3, wide.Count, "the rest of the first row, the whole second and a part of the third");
        Assert.IsTrue(wide[0].Top < wide[1].Top && wide[1].Top < wide[2].Top);

        var labels = Pieces(laid, PacketPiece.Label).Where(label => label.Bounds.IntersectsWith(wide[0]) || label.Bounds.IntersectsWith(wide[1])).ToList();
        Assert.IsTrue(labels.Any(label => label.Words is { Maps: true }), "typed into on the row it starts on");
        Assert.IsTrue(labels.Any(label => label.Words is not { Maps: true } && label.Bounds.IntersectsWith(wide[1])), "and only said again after it");
    });

    [TestMethod]
    public void TheEndsOfARunAreNumbered_WorkedOutFromItsBits_NeverWrittenInNorStandingForAnything() => UiThread.Run(() =>
    {
        var bits = Pieces(Lay("packet\n0-15: \"A\"\n+16: \"B\""), PacketPiece.Bit);

        CollectionAssert.AreEqual(new[] { "0", "15", "16", "31" }, bits.Select(Says).ToArray());
        Assert.IsTrue(bits.All(bit => bit.Words is not { Maps: true } && bit.Part is null && bit.Stops == LayoutStops.None),
                      "which bits a field takes follows from the fields before it, so no number is a place to write or a thing to press");
        Assert.IsTrue(bits[0].Bounds.Left < bits[1].Bounds.Left, "the first over the left end");
    });

    [TestMethod]
    public void OneBitIsNumberedOnceOverItsMiddle() => UiThread.Run(() =>
    {
        var laid = Lay("packet\n0-30: \"A\"\n31: \"Flag\"");
        var flag = Box(Pieces(laid, PacketPiece.Field)[1]);
        var number = Pieces(laid, PacketPiece.Bit).Single(bit => Says(bit) == "31").Bounds;

        Assert.AreEqual(flag.Left + (flag.Width / 2), number.Left + (number.Width / 2), 1);
        Assert.IsTrue(number.Bottom <= flag.Top, "over it");
    });

    [TestMethod]
    public void ARegisterIsNumberedFromItsHighestBitOnTheLeft() => UiThread.Run(() =>
    {
        var fields = Pieces(Lay(Register), PacketPiece.Field);

        CollectionAssert.AreEqual(new[] { "9-15: \"Reserved\"", "8: \"Enable\"", "4-7: \"Speed\"", "0-3: \"Mode\"" },
                                  fields.Select(field => Written(Register, field.Part)).ToArray(), "the row holds its fields as it is drawn, highest bits first");
        Assert.IsTrue(fields.Zip(fields.Skip(1)).All(pair => Box(pair.First).Right <= Box(pair.Second).Left), "left to right");
        CollectionAssert.AreEqual(new[] { "3", "0" }, Numbers(fields[^1]), "and a field reads from its highest bit down");
    });

    [TestMethod]
    public void BitsNotShownAreNotNumbered() => UiThread.Run(() =>
        Assert.AreEqual(0, Pieces(Lay("---\nconfig:\n  packet:\n    showBits: false\n---\npacket\n0-15: \"A\""), PacketPiece.Bit).Count));

    [TestMethod]
    public void AMermaidBlockHeadedPacketIsThePacket() => UiThread.Run(() =>
    {
        foreach (var header in new[] { "packet", "packet-beta" })
            Assert.AreEqual(2, Pieces(Laying.Lay("mermaid", $"{header}\n0-15: \"A\"\n16-31: \"B\"", 700), PacketPiece.Field).Count, header);
    });

    [TestMethod]
    public void WhereARuleIsBroken_TheBlockIsShownAsWritten_TheNumberAtFaultMarked() => UiThread.Run(() =>
    {
        const string source = "packet\n0-7: \"A\"\n16-23: \"B\"";

        foreach (var writing in new[] { false, true })
        {
            var laid = Lay(source, writing: writing);

            Assert.AreEqual(0, Pieces(laid, PacketPiece.Field).Count, "a number is not written in where it is drawn, so it is put right where it is written");
            Assert.IsTrue(laid.Trouble.Any(trouble => trouble.Message.Contains("starts at 8", StringComparison.Ordinal)), string.Join(" | ", laid.Trouble.Select(t => t.Message)));
        }
    });

    [TestMethod]
    public void AFieldWhoseWidthIsStillToWriteIsABitWide() => UiThread.Run(() =>
    {
        var laid = Lay("packet\n0-7: \"A\"\n+: \"B\"", writing: true);
        var boxes = Pieces(laid, PacketPiece.Field);

        Assert.IsTrue(Box(boxes[1]).Width < Box(boxes[0]).Width / 4, "a bit wide");
        Assert.AreEqual(0, laid.Holes.Count, "and no hole: a bit number is not written in where it is drawn");
    });

    private static string Says(LayoutPiece piece) => piece.Words?.Glyphs.Text ?? string.Empty;

    /// <summary>The runs of fields a row holds, in the order it holds them.</summary>
    private static List<LayoutPiece> Fields(LayoutPiece row) => [.. row.Children.Where(piece => piece.Kind == PacketPiece.Field)];

    /// <summary>Where a run's box stands — its shape, less the label in it and the numbers set over it.</summary>
    private static LayoutRect Box(LayoutPiece field) => field.Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds;

    /// <summary>The numbers over a run, left to right — set in its row's band, over its box.</summary>
    private static string[] Numbers(LayoutPiece field)
    {
        var box = Box(field);

        return [.. field.Parent.Children.Where(piece => piece.Kind == PacketPiece.Bits).SelectMany(band => band.Children)
                    .Where(bit => bit.Kind == PacketPiece.Bit && bit.Bounds.Left >= box.Left - 0.5 && bit.Bounds.Right <= box.Right + 0.5)
                    .OrderBy(bit => bit.Bounds.Left).Select(Says)];
    }
}
