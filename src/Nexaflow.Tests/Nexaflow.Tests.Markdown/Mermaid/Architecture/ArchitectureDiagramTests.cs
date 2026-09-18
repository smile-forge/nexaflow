using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Architecture;

/// <summary>
/// What an <c>architecture-beta</c> block's tree is read back into: the groups, the services and junctions in them, the
/// edges between them — and, from the sides those edges leave by, which cell of the grid everything sits in.
/// </summary>
[TestClass]
[CoversNode("architecture-ast")]
public class ArchitectureDiagramTests
{
    [TestMethod]
    public void AGroupHoldsWhatIsPutInIt_WithItsIconAndWhatIsWrittenOnIt()
    {
        var diagram = ArchitectureDiagram.Read(ArchitectureGrammarTests.Cloud);

        Assert.AreEqual("api", diagram.Groups.Single().Id);
        Assert.AreEqual("API", diagram.Groups.Single().Said!.Text);
        Assert.AreEqual("cloud", diagram.Groups.Single().Icon!.Text);
        CollectionAssert.AreEqual(new[] { "db", "disk1", "disk2", "server" }, diagram.Services.Select(service => service.Id).ToArray());
        Assert.IsTrue(diagram.Services.All(service => service.In == "api"));
    }

    [TestMethod]
    public void AServiceSaysWhatIsWrittenUnderIt_OrWhatItIsCalledWhereNothingIs()
    {
        var diagram = ArchitectureDiagram.Read("architecture-beta\n  service db(database)[The store]\n  service plain");

        Assert.AreEqual("The store", diagram.Service("db")!.Said!.Text);
        Assert.AreEqual("database", diagram.Service("db")!.Icon!.Text);
        Assert.AreEqual("plain", diagram.Service("plain")!.Said!.Text, "nothing is written on it, so its id is what it is drawn with");
        Assert.IsNull(diagram.Service("plain")!.Icon);
    }

    [TestMethod]
    public void AJunctionIsAServiceDrawnAsAPlaceEdgesMeet()
    {
        var diagram = ArchitectureDiagram.Read(ArchitectureGrammarTests.Meeting);

        CollectionAssert.AreEqual(new[] { "junctionCenter", "junctionRight" },
                                  diagram.Services.Where(service => service.Junction).Select(service => service.Id).ToArray());
        Assert.AreEqual(6, diagram.Edges.Count);
    }

    [TestMethod]
    public void AnEdgeSaysWhichSideEachEndLeavesBy_AndWhatItDrawsThere()
    {
        var edges = ArchitectureDiagram.Read("architecture-beta\n  service a\n  service b\n  service c\n  service d\n"
                                             + "  a:R -- L:b\n  b:R --> L:c\n  c:T <--> B:d").Edges;

        Assert.AreEqual(ArchitectureSide.Right, edges[0].FromSide);
        Assert.AreEqual(ArchitectureSide.Left, edges[0].ToSide);
        Assert.IsFalse(edges[0].StartHead);
        Assert.IsFalse(edges[0].EndHead);

        Assert.IsTrue(edges[1].EndHead);
        Assert.IsFalse(edges[1].StartHead);

        Assert.IsTrue(edges[2].StartHead && edges[2].EndHead);
        Assert.AreEqual(ArchitectureSide.Top, edges[2].FromSide);
        Assert.AreEqual(ArchitectureSide.Bottom, edges[2].ToSide);
    }

    [TestMethod]
    public void AnEdgeMaySayWhatItIs_AndMayLeaveTheGroupItsServiceIsIn()
    {
        var said = ArchitectureDiagram.Read("architecture-beta\n  service a\n  service b\n  a:R -[reads]- L:b").Edges.Single();
        Assert.AreEqual("reads", said.Said!.Text);

        var across = ArchitectureDiagram.Read("architecture-beta\n  group one(cloud)[One]\n  group two(cloud)[Two]\n"
                                              + "  service a in one\n  service b in two\n  a{group}:B --> T:b{group}").Edges.Single();
        Assert.IsTrue(across.FromGroup);
        Assert.IsTrue(across.ToGroup);
    }

    [TestMethod]
    public void TheSidesAnEdgeLeavesBySayWhereItsEndsSit()
    {
        var diagram = ArchitectureDiagram.Read(ArchitectureGrammarTests.Cloud);

        Assert.AreEqual(diagram.Places["db"].Row, diagram.Places["server"].Row, "the right of one against the left of another shares a row");
        Assert.IsTrue(diagram.Places["server"].Column < diagram.Places["db"].Column, "and db leaving by its left puts the server there");

        Assert.AreEqual(diagram.Places["db"].Column, diagram.Places["disk2"].Column, "the top of one against the foot of another shares a column");
        Assert.IsTrue(diagram.Places["disk2"].Row > diagram.Places["db"].Row, "and disk2 leaving by its top puts it under db");
    }

    [TestMethod]
    public void AnEdgeBendingRoundACornerMovesBothWays()
    {
        var places = ArchitectureDiagram.Read("architecture-beta\n  service a\n  service b\n  a:R -- T:b").Places;

        Assert.IsTrue(places["b"].Column > places["a"].Column, "a leaves by its right, so b is to the right of it");
        Assert.IsTrue(places["b"].Row > places["a"].Row, "and arrives at b's top, so b is under it");
    }

    [TestMethod]
    public void AnEdgeLeavingAndArrivingOnTheSameSideMovesNothing()
    {
        var places = ArchitectureDiagram.Read("architecture-beta\n  service a\n  service b\n  a:R -- R:b").Places;

        Assert.AreEqual(places["a"].Row, places["b"].Row);
        Assert.AreNotEqual(places["a"].Column, places["b"].Column, "they are two pieces, laid side by side");
    }

    [TestMethod]
    public void WhatIsReachedByNoEdgeIsAPieceOfItsOwn_LaidBesideTheRest()
    {
        var places = ArchitectureDiagram.Read("architecture-beta\n  service a\n  service b\n  service c").Places;

        Assert.AreEqual(3, places.Count);
        Assert.AreEqual(1, places.Values.Select(place => place.Row).Distinct().Count(), "they share a row");
        Assert.AreEqual(3, places.Values.Select(place => place.Column).Distinct().Count(), "and each has a column of its own");
    }

    [TestMethod]
    public void ServicesReachingTheSameServiceTheSameWaySitInTheSameCell()
    {
        // Mermaid keeps only one neighbour per side, so two of these three land on top of one another and align exists to
        // separate them. Here they all land in one cell, which the drawing spreads along whichever axis align asks for.
        var diagram = ArchitectureDiagram.Read("architecture-beta\n  service db1\n  service db2\n  service db3\n  service mcp\n"
                                               + "  db1:R --> L:mcp\n  db2:R --> L:mcp\n  db3:R --> L:mcp\n  align column db1 db2 db3");

        Assert.AreEqual(diagram.Places["db1"], diagram.Places["db2"]);
        Assert.AreEqual(diagram.Places["db1"], diagram.Places["db3"]);
        Assert.IsTrue(diagram.Places["mcp"].Column > diagram.Places["db1"].Column);

        Assert.AreEqual(ArchitectureAxis.Column, diagram.Spread("db1"), "and the align line says they stack rather than spread across");
        Assert.AreEqual(ArchitectureAxis.Row, diagram.Spread("mcp"), "which nothing says of anything else");
    }

    [TestMethod]
    public void AnAlignLinePullsItsMembersOntoTheRowOrColumnOfTheFirstOfThem()
    {
        var diagram = ArchitectureDiagram.Read(ArchitectureGrammarTests.Tiers);

        foreach (var row in new[] { new[] { "src_a", "src_b", "src_c" }, ["db_one", "db_two", "db_three"] })
            Assert.AreEqual(1, row.Select(id => diagram.Places[id].Row).Distinct().Count(), $"{string.Join(", ", row)} share a row");

        foreach (var column in new[] { new[] { "src_a", "db_one" }, ["src_b", "db_two"], ["src_c", "db_three"] })
            Assert.AreEqual(1, column.Select(id => diagram.Places[id].Column).Distinct().Count(), $"{string.Join(", ", column)} share a column");

        Assert.IsTrue(diagram.Places["db_one"].Row > diagram.Places["src_a"].Row, "and the sources are above the stores they feed");
    }

    [TestMethod]
    public void TheFrontMattersOptionsAreRead()
    {
        Assert.AreEqual(ArchitectureConfig.Icon, ArchitectureDiagram.Read("architecture-beta\n  service a").Config.IconSize);

        var config = ArchitectureDiagram.Read("---\nconfig:\n  architecture:\n    iconSize: 64\n    fontSize: 15\n    padding: 20\n"
                                              + "    nodeSeparation: 50\n    idealEdgeLengthMultiplier: 3\n---\narchitecture-beta\n  service a").Config;

        Assert.AreEqual(64, config.IconSize);
        Assert.AreEqual(15, config.FontSize);
        Assert.AreEqual(20, config.Padding);
        Assert.AreEqual(50, config.NodeSeparation);
        Assert.AreEqual(3, config.EdgeLength);
    }
}
