using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Which tests run for a node. Over a small repository in memory: a helper, the API that uses it, a test of the API, a
/// journey that clicks through it, and a test that only declares it covers the feature.
/// </summary>
[TestClass]
[CoversNode("nfi-test")]
public class TestSelectionTests
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["src/Lib/Helper.cs"]     = "class Helper\n{\n    public int Twice(int n) => n * 2;\n}\n",
        ["src/Lib/Api.cs"]        = "class Api\n{\n    public int Double(int n) => new Helper().Twice(n);\n}\n",
        ["tests/T/ApiTests.cs"]   = "class ApiTests\n{\n    [TestMethod]\n    public void Doubles()\n    {\n        Assert.AreEqual(4, new Api().Double(2));\n    }\n}\n",
        ["tests/T/CoverTests.cs"] = "class CoverTests\n{\n    [TestMethod]\n    public void Covers() { }\n}\n",
        ["tests/J/Journey.cs"]    = "class Journey\n{\n    [TestMethod]\n    public void ClicksDouble() => new Api().Double(1);\n}\n",
    };

    private static GraphNode Member(string id, string label, string file, int line, int end) =>
        new() { Id = id, Type = NodeType.Member, Label = label, FilePath = file,
                Metadata = new Dictionary<string, string> { ["ast"] = id[(id.IndexOf('#') + 1)..], ["line"] = $"{line}", ["endLine"] = $"{end}" } };

    private static readonly KnowledgeGraph Graph = new()
    {
        Nodes =
        [
            new GraphNode { Id = "product:lib", Type = NodeType.Product, Label = "Lib" },
            new GraphNode { Id = "code:src/Lib/Helper.cs#T:Helper", Type = NodeType.Type, Label = "Helper", FilePath = "src/Lib/Helper.cs",
                            Metadata = new Dictionary<string, string> { ["ast"] = "T:Helper", ["line"] = "1", ["endLine"] = "4" } },
            Member("code:src/Lib/Helper.cs#T:Helper/M:Twice", "Twice", "src/Lib/Helper.cs", 3, 3),
            Member("code:src/Lib/Api.cs#T:Api/M:Double", "Double", "src/Lib/Api.cs", 3, 3),
            Member("code:tests/T/ApiTests.cs#T:ApiTests/M:Doubles", "Doubles", "tests/T/ApiTests.cs", 4, 7),
            Member("code:tests/J/Journey.cs#T:Journey/M:ClicksDouble", "ClicksDouble", "tests/J/Journey.cs", 4, 4),
        ],
        Edges = [new GraphEdge { Source = "product:lib", Target = "code:src/Lib/Helper.cs#T:Helper", Relationship = EdgeRelationship.References }],
    };

    private static TestSelection.Sources Sources(TestCoverageManifest? coverage = null) => new(
        rel => Files.GetValueOrDefault(rel), [.. Files.Keys],
        rel => rel.StartsWith("src/Lib/", StringComparison.Ordinal) ? "src/Lib/Lib.csproj"
             : rel.StartsWith("tests/T/", StringComparison.Ordinal) ? "tests/T/T.csproj"
             : rel.StartsWith("tests/J/", StringComparison.Ordinal) ? "tests/J/J.csproj" : null,
        project => project.StartsWith("tests/", StringComparison.Ordinal),
        project => project == "tests/J/J.csproj",
        (_, _) => null,
        coverage);

    [TestMethod]
    public void TheTestsOfWhatUsesIt_AreChosen_ThroughTheCodeInBetween()
    {
        var selection = TestSelection.For(Graph, "code:src/Lib/Helper.cs#T:Helper/M:Twice", Sources());

        var suite = selection.Suites.Single();
        Assert.AreEqual("tests/T/T.csproj", suite.Project);
        CollectionAssert.AreEqual(new[] { ".ApiTests.Doubles" }, suite.Filters.ToArray(),
                                  "Twice is used by Double, and Double by the test");
    }

    [TestMethod]
    public void AJourney_IsNamedForAPerson_AndNeverChosenToRun()
    {
        var selection = TestSelection.For(Graph, "code:src/Lib/Api.cs#T:Api/M:Double", Sources());

        Assert.IsFalse(selection.Suites.Any(s => s.Project == "tests/J/J.csproj"), "a journey takes over the machine");
        CollectionAssert.AreEqual(new[] { ".Journey.ClicksDouble" }, selection.Journeys.Single().Filters.ToArray());
    }

    [TestMethod]
    public void ATestDeclaringItCoversTheFeature_IsChosenToo()
    {
        var coverage = new TestCoverageManifest
        {
            Coverage = new()
            {
                ["product:lib"] = [new TestRef { Class = "Tests.CoverTests", Method = "Covers", File = "tests/T/CoverTests.cs" }],
            },
        };

        var selection = TestSelection.For(Graph, "code:src/Lib/Helper.cs#T:Helper", Sources(coverage));

        CollectionAssert.Contains(selection.Suites.Single().Filters.ToList(), "Tests.CoverTests.Covers");
    }

    [TestMethod]
    public void WithNoManifest_TheCoverageThatCouldNotBeLookedUp_IsSaid()
    {
        var selection = TestSelection.For(Graph, "code:src/Lib/Helper.cs#T:Helper", Sources(coverage: null));

        Assert.IsTrue(selection.Notes.Any(n => n.Contains("scan-tests", StringComparison.Ordinal)), string.Join("\n", selection.Notes));
    }
}
