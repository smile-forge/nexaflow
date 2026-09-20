using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// An <c>architecture-beta</c> block drawn on the shared layout tree: the services laid out by the sides their edges
/// leave by, the groups holding them, the junctions edges meet at, and every word typed into where it is drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("architecture")]
public class ArchitectureBuilderTests : MermaidBuilderContract
{
    private const string Cloud =
        "architecture-beta\n    group api(cloud)[API]\n\n    service db(database)[Database] in api\n"
        + "    service disk1(disk)[Storage] in api\n    service disk2(disk)[Storage] in api\n    service server(server)[Server] in api\n\n"
        + "    db:L -- R:server\n    disk1:T -- B:server\n    disk2:T -- B:db";

    public override MermaidDiagram Diagram => MermaidDiagram.Architecture;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the parts of a cloud", Cloud),
        ("every icon there is",
            "architecture-beta\n  service a(cloud)[Cloud]\n  service b(database)[Database]\n  service c(disk)[Disk]\n"
            + "  service d(internet)[Internet]\n  service e(server)[Server]\n  a:R -- L:b\n  b:R -- L:c\n  c:R -- L:d\n  d:R -- L:e"),
        ("an icon nobody here draws", "architecture-beta\n  service a(logos:aws-lambda)[Lambda]\n  service b[Plain]\n  a:R -- L:b"),
        ("junctions several edges meet at",
            "architecture-beta\n  service left(disk)[Disk]\n  service top(disk)[Disk]\n  junction middle\n"
            + "  left:R -- L:middle\n  top:B -- T:middle"),
        ("groups inside groups",
            "architecture-beta\n  group outer(cloud)[Outer]\n  group inner(database)[Inner] in outer\n"
            + "  service a(server)[A] in inner\n  service b(server)[B] in inner\n  a:R -- L:b"),
        ("an edge out of a group",
            "architecture-beta\n  group one(cloud)[One]\n  group two(cloud)[Two]\n  service a(server)[A] in one\n"
            + "  service b(server)[B] in two\n  a{group}:B --> T:b{group}"),
        ("edges of every kind, and one saying what it is",
            "architecture-beta\n  service a\n  service b\n  service c\n  service d\n  a:R -- L:b\n  b:R --> L:c\n  c:R <--> L:d\n  a:B -[reads]- T:c"),
        ("services reaching one the same way",
            "architecture-beta\n  service db1(database)[DB1]\n  service db2(database)[DB2]\n  service db3(database)[DB3]\n"
            + "  service mcp(server)[MCP]\n  db1:R --> L:mcp\n  db2:R --> L:mcp\n  db3:R --> L:mcp\n  align column db1 db2 db3"),
        ("a title and a size of its own",
            "---\nconfig:\n  architecture:\n    iconSize: 56\n    padding: 18\n---\narchitecture-beta\n  title How it runs\n"
            + "  group api(cloud)[API]\n  service a(server)[A] in api\n  service b(database)[B] in api\n  a:R -- L:b"),
        ("a long title under a service", "architecture-beta\n  service a(server)[A service with a good deal more in its name than most]\n  service b(disk)[B]\n  a:R -- L:b"),
        ("nothing joined up at all", "architecture-beta\n  service a(server)[A]\n  service b(server)[B]\n  service c(server)[C]"),
        ("still being written", "architecture-beta\n  service a(\n  service \n  b:R --> "),
        ("what nobody means to write", "architecture-beta\n  service a\n  a:R -- L:nowhere\n  align row a"),
        ("nothing to draw", "architecture-beta"),
    ];

    [TestMethod]
    public void TheSidesAnEdgeLeavesBySayWhereTheServicesAreDrawn() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  service a(server)[A]\n  service b(server)[B]\n  service c(server)[C]\n"
                         + "  a:R -- L:b\n  a:B -- T:c");

        var services = Pieces(laid, ArchitecturePiece.Service).Select(piece => piece.Bounds).ToList();

        Assert.IsTrue(services[1].Left > services[0].Right - 1, "a leaves by its right, so b is drawn to the right of it");
        Assert.AreEqual(services[0].Top, services[1].Top, 1, "and they share a row");
        Assert.IsTrue(services[2].Top > services[0].Bottom - 1, "a leaves by its foot, so c is drawn under it");
    });

    [TestMethod]
    public void AGroupHoldsItsServicesInTheLayout_AndIsDrawnRoundThem() => UiThread.Run(() =>
    {
        var laid = Build(Cloud);
        var group = Pieces(laid, ArchitecturePiece.Group).Single();
        var held = Pieces(laid, ArchitecturePiece.Service).ToList();

        Assert.AreEqual(4, held.Count);
        foreach (var service in held)
        {
            Assert.IsTrue(service.Ancestors().Any(over => over.Kind == ArchitecturePiece.Group), "every service hangs off the group it is in");
            Assert.IsTrue(group.Bounds.Contains(service.Bounds), $"{service.Bounds} sits inside {group.Bounds}");
        }

        Assert.AreEqual("API", Said(Pieces(laid, ArchitecturePiece.Holding).Single()).Single().Words!.Glyphs.Text);
    });

    [TestMethod]
    public void AGroupInsideAGroupIsDrawnInsideIt() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  group outer(cloud)[Outer]\n  group inner(database)[Inner] in outer\n"
                         + "  service a(server)[A] in inner\n  service b(server)[B] in inner\n  a:R -- L:b");

        var boxes = Pieces(laid, ArchitecturePiece.Group).ToList();

        Assert.AreEqual(2, boxes.Count);
        Assert.IsTrue(boxes[0].Bounds.Contains(boxes[1].Bounds), $"{boxes[1].Bounds} sits inside {boxes[0].Bounds}");
        Assert.IsTrue(boxes[1].Ancestors().Any(over => over.Kind == ArchitecturePiece.Group), "and hangs off it");
    });

    [TestMethod]
    public void WhatIsWrittenUnderAServiceIsTheCharactersWritten_OrItsIdWhereNothingIs() => UiThread.Run(() =>
    {
        const string source = "architecture-beta\n  service db(database)[The store]\n  service plain\n  db:R -- L:plain";

        var laid = Build(source);
        var words = Pieces(laid, ArchitecturePiece.Service).Select(piece => Said(piece).Single()).ToList();

        CollectionAssert.AreEqual(new[] { "The store", "plain" }, words.Select(piece => piece.Words!.Glyphs.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "The store", "plain" }, words.Select(piece => Written(source, piece.Part)).ToArray(),
                                  "each typed into where it is drawn");
    });

    [TestMethod]
    public void AnEdgeRunsFromTheSideItLeavesByToTheSideItArrivesAt() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  service a(server)[A]\n  service b(server)[B]\n  a:R -- L:b");

        var services = Pieces(laid, ArchitecturePiece.Service).Select(piece => piece.Bounds).ToList();
        var edge = Pieces(laid, ArchitecturePiece.Edge).Single().Bounds;

        Assert.IsTrue(edge.Left >= services[0].Right - 1, $"it starts at a's right: {edge.Left} over {services[0].Right}");
        Assert.IsTrue(edge.Right <= services[1].Left + 1, $"and stops at b's left: {edge.Right} under {services[1].Left}");
        Assert.AreEqual(services[0].Top + (services[0].Height / 2), edge.Top + (edge.Height / 2), 2, "level with the middle of both");
    });

    [TestMethod]
    public void AnEdgeOutOfAGroupLeavesTheGroupsBox() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  group one(cloud)[One]\n  group two(cloud)[Two]\n  service a(server)[A] in one\n"
                         + "  service b(server)[B] in two\n  a{group}:B --> T:b{group}");

        var boxes = Pieces(laid, ArchitecturePiece.Group).Select(piece => piece.Bounds).ToList();
        var edge = Pieces(laid, ArchitecturePiece.Edge).Single().Bounds;

        Assert.AreEqual(boxes[0].Bottom, edge.Top, 2, "it starts at the foot of the group its service is in");
        Assert.AreEqual(boxes[1].Top, edge.Bottom, 2, "and arrives at the top of the other");
    });

    [TestMethod]
    public void AJunctionIsTheDotItsEdgesMeetAt() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  service left(disk)[Disk]\n  service top(disk)[Disk]\n  junction middle\n"
                         + "  left:R -- L:middle\n  top:B -- T:middle");

        var dot = Pieces(laid, ArchitecturePiece.Junction).Single();

        Assert.AreEqual(1, Pieces(laid, ArchitecturePiece.Junction).Count);
        Assert.AreEqual(0, Said(dot).Count(), "a junction says nothing");
        Assert.IsTrue(dot.Bounds.Width < 20, $"and is a dot rather than a box: {dot.Bounds}");
    });

    [TestMethod]
    public void AnIconIsAPictureWhereMermaidHasOne_AndWhatItIsCalledWhereItDoesNot() => UiThread.Run(() =>
    {
        foreach (var icon in ArchitectureIcons.Drawn)
        {
            var laid = Build($"architecture-beta\n  service a({icon})[A]");
            Assert.AreEqual(0, Said(Pieces(laid, ArchitecturePiece.Icon).Single()).Count(), $"{icon} is drawn rather than written out");
        }

        var lambda = Build("architecture-beta\n  service a(logos:aws-lambda)[A]");
        Assert.AreEqual("logos:aws-lambda", Said(Pieces(lambda, ArchitecturePiece.Icon).Single()).Single().Words!.Glyphs.Text);

        var plain = Build("architecture-beta\n  service a[A]");
        Assert.AreEqual(0, Said(Pieces(plain, ArchitecturePiece.Icon).Single()).Count(), "and a service with no icon is a plain box");
    });

    [TestMethod]
    public void ServicesReachingOneTheSameWayAreSpreadRatherThanDrawnOnTopOfOneAnother() => UiThread.Run(() =>
    {
        var laid = Build("architecture-beta\n  service db1(database)[DB1]\n  service db2(database)[DB2]\n  service db3(database)[DB3]\n"
                         + "  service mcp(server)[MCP]\n  db1:R --> L:mcp\n  db2:R --> L:mcp\n  db3:R --> L:mcp\n  align column db1 db2 db3");

        var boxes = Pieces(laid, ArchitecturePiece.Service).Select(piece => piece.Bounds).Take(3).ToList();

        Assert.AreEqual(3, boxes.Select(box => box.Top).Distinct().Count(), "the three stack, one under another");
        Assert.AreEqual(1, boxes.Select(box => box.Left).Distinct().Count(), "sharing a column, as the align line asks");
        Assert.IsTrue(boxes[1].Top >= boxes[0].Bottom, "and no two of them overlap");
    });

    private static Laid Build(string source, double room = 900) =>
        ArchitectureBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    [TestMethod]
    public void WhatIsWrittenOnAnEdgeIsDrawnOverTheMiddleOfIt() => UiThread.Run(() =>
    {
        const string source = "architecture-beta\n  service a(server)[A]\n  service b(server)[B]\n  a:R -[feeds]- L:b";

        var laid = Build(source);
        var said = Pieces(laid, ArchitecturePiece.Label).Single();
        var edge = Pieces(laid, ArchitecturePiece.Edge).Single().Bounds;

        Assert.AreEqual("feeds", Said(said).Single().Words!.Glyphs.Text);
        Assert.AreEqual("feeds", Written(source, Said(said).Single().Part));
        Assert.AreEqual((edge.Left + edge.Right) / 2, (said.Bounds.Left + said.Bounds.Right) / 2, 1);
    });
}
