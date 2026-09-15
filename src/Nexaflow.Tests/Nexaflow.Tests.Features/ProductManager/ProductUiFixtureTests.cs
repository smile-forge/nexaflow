using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// Builds the two products the Product UI journey opens, and holds the seeded one to what that journey needs.
/// It runs here because a product is written through ProductStore, which the journey suite must not reference.
/// <para>
/// The seed is shaped by the journey's route rather than by realism: a faulted node, so the needs-attention
/// list has a row; a code link to a missing file and a markdown link to a missing heading, so the Integrity
/// page offers both pickers; a declared-but-unlinked test, for the coverage suggestion; a link whose class is
/// real but whose ast is not, for the stale-ast advisory; and a small graph, so a search has rows and the
/// graph viewer a node with a file behind it. The assertions are those preconditions, checked headlessly — a
/// seed that stopped producing them would otherwise surface as a journey failing somewhere downstream.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Builds a fixture for another suite; asserts only that fixture's own preconditions.")]
public class ProductUiFixtureTests
{
    [TestMethod]
    public void SeedsTheProductFixturesForTheUiJourney()
    {
        SeedEmpty(UiFixtures.ProductEmptyRoot);

        var root = UiFixtures.ProductRoot;
        Seed(root);

        var store = new ProductStore(root);
        var state = store.Load();
        var report = SnaplinkValidator.Validate(state, root, [root]);
        Assert.AreEqual(2, report.Issues.Count, "the journey needs one broken code link and one broken markdown link");
        Assert.AreEqual(1, report.Advisories.Count, "the journey needs one stale ast");

        var coverage = TestCoverageReconciler.Reconcile(state, store.LoadTestCoverage());
        Assert.AreEqual(1, coverage.Advisories.Count(a => a.CanAdd), "the journey needs one addable coverage suggestion");
    }

    private static void SeedEmpty(string root)
    {
        UiFixtures.Delete(root);
        Directory.CreateDirectory(root);
        new ProductStore(root).Initialize("Empty Journey Product");
    }

    private static void Seed(string root)
    {
        UiFixtures.Delete(root);
        Write(root, "src/Widget.cs", "namespace Demo;\n\npublic class Widget\n{\n    public void Spin() { }\n}\n");
        Write(root, "src/Gadget.cs", "namespace Demo;\n\npublic class Gadget\n{\n    public void Use() { }\n}\n");
        Write(root, "tests/AlphaTests.cs", "namespace Demo.Tests;\n\npublic class AlphaTests\n{\n    public void Works() { }\n}\n");
        Write(root, "docs/guide.md", "# Guide\n\nHow the journey product is used.\n");
        Write(root, "docs/testing.md", "# Testing\n\nHow the journey product is tested.\n");

        var store = new ProductStore(root);
        store.Initialize("Journey Product");
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["alpha"] = new()
            {
                Title = "Alpha",
                Status = Status.Faulted,
                Concerns =
                [
                    new ConcernLink
                    {
                        Tag = "tests",
                        Status = Status.Faulted,
                        Snaplinks = [new Snaplink { Type = "markdown", Doc = "docs/testing.md", TitlePath = ["Testing"] }],
                    },
                    new ConcernLink { Tag = "docs", Status = Status.Should },
                ],
                Snaplinks =
                [
                    new Snaplink { Type = "code", Doc = "src/Missing.cs", Class = "Missing" },
                    new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Ast = "T:Widget/M:Gone" },
                ],
            },
            ["beta"] = new()
            {
                Title = "Beta",
                Status = Status.Should,
                Snaplinks = [new Snaplink { Type = "markdown", Doc = "docs/guide.md", TitlePath = ["No such heading"] }],
            },
        });

        store.SaveTestCoverage(new TestCoverageManifest
        {
            Coverage =
            {
                ["alpha"] = [new TestRef { Assembly = "Demo.Tests", Class = "Demo.Tests.AlphaTests", File = "tests/AlphaTests.cs" }],
            },
        });

        store.SaveSnapshot(new KnowledgeGraph
        {
            Nodes =
            [
                new GraphNode { Id = "product:alpha", Type = NodeType.Product, Label = "Alpha" },
                new GraphNode
                {
                    Id = "code:src/Widget.cs#T:Widget", Type = NodeType.Type, Label = "Widget", FilePath = "src/Widget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3" },
                },
                new GraphNode
                {
                    Id = "code:src/Gadget.cs#T:Gadget", Type = NodeType.Type, Label = "Gadget", FilePath = "src/Gadget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3" },
                },
            ],
            Edges = [new GraphEdge { Source = "product:alpha", Target = "code:src/Widget.cs#T:Widget", Relationship = "implemented_by" }],
        }, new GraphCache());
    }

    private static void Write(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
