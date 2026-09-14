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
        (project, _) => project == "tests/J/J.csproj",
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

    [TestMethod]
    public void ATestThatShowsAWindow_IsNamedForAPerson_EvenInASuiteOfOrdinaryTests()
    {
        var files = new Dictionary<string, string>(Files, StringComparer.Ordinal)
        {
            ["tests/T/WindowTests.cs"] = "class WindowTests\n{\n    [TestMethod]\n    public void ShowsDouble() => new Api().Double(3);\n}\n",
        };
        var graph = new KnowledgeGraph
        {
            Nodes = [.. Graph.Nodes, Member("code:tests/T/WindowTests.cs#T:WindowTests/M:ShowsDouble", "ShowsDouble", "tests/T/WindowTests.cs", 4, 4)],
            Edges = Graph.Edges,
        };
        var sources = Sources() with
        {
            Read = rel => files.GetValueOrDefault(rel),
            Files = [.. files.Keys],
            TakesOverTheMachine = (project, file) => project == "tests/J/J.csproj" || file == "tests/T/WindowTests.cs",
        };

        var selection = TestSelection.For(graph, "code:src/Lib/Api.cs#T:Api/M:Double", sources);

        CollectionAssert.AreEqual(new[] { ".ApiTests.Doubles" }, selection.Suites.Single(s => s.Project == "tests/T/T.csproj").Filters.ToArray(),
                                  "the ordinary test in the suite runs");
        CollectionAssert.Contains(selection.Journeys.Single(s => s.Project == "tests/T/T.csproj").Filters.ToList(), ".WindowTests.ShowsDouble",
                                  "and the one that shows a window is named for a person, not run");
    }

    [TestMethod]
    public void AView_FindsTheJourneyThatNamesItsIds_ThoughNothingBindsToThem()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/App/View.xaml"]      = "<UserControl>\n  <Button AutomationProperties.AutomationId=\"App_Save\"/>\n</UserControl>\n",
            ["src/App/View.xaml.cs"]   = "class View\n{\n}\n",
            ["tests/J/ViewJourney.cs"] = "class ViewJourney\n{\n    [TestMethod]\n    public void Saves() => Click(\"App_Save\");\n}\n",
        };
        var graph = new KnowledgeGraph
        {
            Nodes =
            [
                new GraphNode { Id = "code:src/App/View.xaml.cs#T:View", Type = NodeType.Type, Label = "View", FilePath = "src/App/View.xaml.cs",
                                Metadata = new Dictionary<string, string> { ["ast"] = "T:View", ["line"] = "1", ["endLine"] = "3" } },
                Member("code:tests/J/ViewJourney.cs#T:ViewJourney/M:Saves", "Saves", "tests/J/ViewJourney.cs", 4, 4),
            ],
        };
        var sources = new TestSelection.Sources(
            rel => files.GetValueOrDefault(rel), [.. files.Keys],
            rel => rel[..rel.LastIndexOf('/')] + "/P.csproj",
            project => project.StartsWith("tests/", StringComparison.Ordinal),
            (project, _) => project == "tests/J/P.csproj",
            (_, _) => null,
            null);

        var selection = TestSelection.For(graph, "code:src/App/View.xaml.cs#T:View", sources);

        var journey = selection.Journeys.Single();
        CollectionAssert.AreEqual(new[] { ".ViewJourney.Saves" }, journey.Filters.ToArray());
        StringAssert.Contains(journey.Reasons.Single(), "App_Save", "and says it was the id that led there");
    }
}
