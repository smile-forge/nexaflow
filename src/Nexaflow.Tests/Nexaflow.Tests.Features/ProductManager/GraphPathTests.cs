using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// Routes between two nodes, and the fan-in/fan-out ranking. The interesting cases are not "does BFS work"
/// but the two judgement calls: which traversals are allowed, and what counts as a use.
/// </summary>
[TestClass]
[CoversNode("graph-paths")]
public class GraphPathTests
{
    private static GraphNode N(string id, string type = NodeType.Type) =>
        new() { Id = id, Type = type, Label = id.Split(':')[^1] };

    private static GraphEdge E(string from, string to, string rel) =>
        new() { Source = from, Target = to, Relationship = rel };

    private static KnowledgeGraph Chain() => new()
    {
        Nodes = [N("a"), N("b"), N("c"), N("d")],
        Edges =
        [
            E("a", "b", EdgeRelationship.Calls),
            E("b", "c", EdgeRelationship.References),
            E("c", "d", EdgeRelationship.Instantiates),
        ],
    };

    [TestMethod]
    public void APathNamesEveryRelationshipItTakes()
    {
        var path = GraphQuery.Paths(Chain(), "a", "d").Single();

        Assert.AreEqual(3, path.Hops);
        CollectionAssert.AreEqual(new[] { "calls", "references", "instantiates" },
                                  path.Steps.Select(s => s.Relationship).ToArray());
        Assert.AreEqual("d", path.Steps[^1].Node.Id);
    }

    [TestMethod]
    public void DirectionIsRespected_UnlessAskedOtherwise()
    {
        Assert.AreEqual(0, GraphQuery.Paths(Chain(), "d", "a").Count, "nothing flows backwards by default");
        Assert.AreEqual(1, GraphQuery.Paths(Chain(), "d", "a", undirected: true).Count);
    }

    [TestMethod]
    public void TheHopLimitIsHonoured()
    {
        Assert.AreEqual(0, GraphQuery.Paths(Chain(), "a", "d", maxHops: 2).Count);
        Assert.AreEqual(1, GraphQuery.Paths(Chain(), "a", "d", maxHops: 3).Count);
    }

    [TestMethod]
    public void ContainmentIsATraversableRelationship()
    {
        // The product tree is built from containment: a feature contains its UI contains a panel. Dropping
        // `contains` to avoid the sibling problem would make the tree itself untraversable.
        var g = new KnowledgeGraph
        {
            Nodes = [N("product:feature", NodeType.Product), N("product:ui", NodeType.Product), N("product:panel", NodeType.Product)],
            Edges =
            [
                E("product:feature", "product:ui", EdgeRelationship.Contains),
                E("product:ui", "product:panel", EdgeRelationship.Contains),
            ],
        };

        Assert.AreEqual(2, GraphQuery.Paths(g, "product:feature", "product:panel").Single().Hops);
    }

    [TestMethod]
    public void ClimbingAContainerThenDescendingIntoASiblingIsBarred()
    {
        // Two unrelated members of one file are NOT two hops apart. Allowing up-then-down would make every
        // declaration in a file adjacent to every other, and drown every real route.
        var g = new KnowledgeGraph
        {
            Nodes = [N("file:F.cs", NodeType.File), N("code:F.cs#T:A"), N("code:F.cs#T:B")],
            Edges =
            [
                E("file:F.cs", "code:F.cs#T:A", EdgeRelationship.Contains),
                E("file:F.cs", "code:F.cs#T:B", EdgeRelationship.Contains),
            ],
        };

        Assert.AreEqual(0, GraphQuery.Paths(g, "code:F.cs#T:A", "code:F.cs#T:B", undirected: true).Count,
                        "sharing a file is not a relationship");
        Assert.AreEqual(1, GraphQuery.Paths(g, "code:F.cs#T:A", "file:F.cs", undirected: true).Count,
                        "but climbing to your own container still is");
    }

    [TestMethod]
    public void RankCountsUsesAndIgnoresContainment()
    {
        // Containment would rank a type by how many members it declares - size, not importance.
        var g = new KnowledgeGraph
        {
            Nodes = [N("hub"), N("x"), N("y"), N("big"), N("m1", NodeType.Member), N("m2", NodeType.Member)],
            Edges =
            [
                E("x", "hub", EdgeRelationship.References),
                E("y", "hub", EdgeRelationship.Calls),
                E("big", "m1", EdgeRelationship.Contains),
                E("big", "m2", EdgeRelationship.Contains),
            ],
        };

        var top = GraphQuery.Rank(g).First();
        Assert.AreEqual("hub", top.Node.Id);
        Assert.AreEqual(2, top.FanIn);
        CollectionAssert.DoesNotContain(GraphQuery.Rank(g, byFanIn: false).Select(r => r.Node.Id).ToArray(), "big");
    }
}
