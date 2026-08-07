using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.ProductManager;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Search;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// A small product with a hand-built graph over two real source files — the fixture both product-search
/// suites share, so the page and the tab that opens it are held to the same graph.
/// </summary>
internal sealed class GraphFixture : IDisposable
{
    public string Root { get; }

    public const string ProductNode = "product:alpha42-widget";
    public const string TypeNode    = "code:src/Widget.cs#T:Widget";
    public const string GadgetNode  = "code:src/Gadget.cs#T:Gadget";

    public GraphFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "nexa-prodsearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "src"));

        // The seed sits in a NODE LABEL here…
        File.WriteAllText(Path.Combine(Root, "src", "Widget.cs"), """
            namespace Demo;

            public class Widget
            {
                public void Spin() { }
            }
            """.Replace("\r\n", "\n"));

        // …and in a SOURCE LINE here, so the two passes can be told apart.
        File.WriteAllText(Path.Combine(Root, "src", "Gadget.cs"), """
            namespace Demo;

            public class Gadget
            {
                public void Use() { /* alpha42 lives here */ }
            }
            """.Replace("\r\n", "\n"));

        var store = new ProductStore(Root);
        store.Initialize("GraphDemo");
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["root"]           = new() { Title = "Root", Children = ["alpha42-widget"] },
            ["alpha42-widget"] = new() { Title = "alpha42 widget", Parent = "root", Children = [] },
        });

        store.SaveGraph(new KnowledgeGraph
        {
            Nodes =
            [
                new GraphNode { Id = ProductNode, Type = NodeType.Product, Label = "alpha42 widget" },
                new GraphNode
                {
                    Id = TypeNode, Type = NodeType.Type, Label = "Widget", FilePath = "src/Widget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3" },
                },
                new GraphNode
                {
                    Id = GadgetNode, Type = NodeType.Type, Label = "Gadget", FilePath = "src/Gadget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3" },
                },
            ],
            Edges = [new GraphEdge { Source = ProductNode, Target = TypeNode, Relationship = "implemented_by" }],
        });
    }

    public string GraphFilePath => new ProductStore(Root).GraphFilePath;

    public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { } }
}

/// <summary>
/// The Product tab answering <c>?</c>.
/// <para>
/// What is worth pinning beyond the shared contract: the tab does not filter the sunburst — the graph is
/// the product tree crossed with the whole repo, and three quarters of what it can match has nowhere to sit
/// on a sunburst — so it opens the results as their own tab, and reports its own answer as
/// <see cref="SearchOutcome.Narrowed"/> because it only did the node-name half.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-search")]
public class ProductSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private GraphFixture _fix = null!;
    private IShellServices _shell = null!;

    [TestInitialize]
    public void Setup()
    {
        _fix   = new GraphFixture();
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>())
              .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    [TestCleanup]
    public void Teardown() => _fix.Dispose();

    private ProductViewModel Build()
    {
        var store = new ProductStore(_fix.Root);
        return new ProductViewModel(store, new ProductGit(_fix.Root), _fix.Root, _shell);
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProductViewModel)page;
        // The sunburst focus is the whole of this page's visible state as far as a search is concerned —
        // it must not move, because the answer opens elsewhere.
        return string.Join(">", vm.CurrentPath.Select(p => p.NodeId ?? p.Label));
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static Dictionary<string, string> OpenedWith(IShellServices shell, string kind)
    {
        var call = shell.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IShellServices.OpenTab))
            .Select(c => c.GetArguments())
            .FirstOrDefault(a => (string?)a[0] == kind);
        Assert.IsNotNull(call, $"no tab of kind '{kind}' was opened");
        return (Dictionary<string, string>)call[1]!;
    }

    // ── Product-tab behaviour beyond the shared contract ──────────────────────

    [TestMethod]
    public void DisplayingSearch_OpensTheResultsTab_RatherThanFilteringTheSunburst() => WithPage(async page =>
    {
        var vm = (ProductViewModel)page;
        var before = Snapshot(page);

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        var opened = OpenedWith(_shell, ProductSearchTabRegistration.StaticPageKind);
        Assert.AreEqual("alpha42", opened["query"]);
        Assert.AreEqual(_fix.Root, opened["path"]);
        Assert.AreEqual(before, Snapshot(page),
            "the sunburst draws the product tree; the graph holds types, files and lines it cannot show");
    });

    [TestMethod]
    public void ARegexSurvivesTheTripToTheResultsTab() => WithPage(async page =>
    {
        var vm = (ProductViewModel)page;

        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);

        Assert.AreEqual(@"/alpha\d+/", OpenedWith(_shell, ProductSearchTabRegistration.StaticPageKind)["query"],
            "handed over in the shared syntax — as literal text the results page would search for the slashes");
    });

    [TestMethod]
    public void TheTabsOwnAnswerIsNodeNames_AndSaysSo() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { GraphFixture.ProductNode },
            outcome.Hits.Select(h => h.Id).ToArray(),
            "the node whose LABEL matched; the source line in Gadget.cs is the results page's half");
        StringAssert.Contains(outcome.Message ?? "", "greps the source",
            "reporting a name-only count as the whole answer is what Narrowed exists to prevent");
    });

    [TestMethod]
    public void AGlobIsRefused_RatherThanQuietlyIgnored() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(
            SearchSyntax.ParseRequest("*.cs", [new Nexaflow.IO.Common.GlobTermRecognizer()]),
            display: false, default);

        Assert.IsTrue(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "Filename filters");
    });

    [TestMethod]
    public void WithNoGraphBuilt_SaysSo_RatherThanReportingNoMatches() => RunUnpumped(async () =>
    {
        var bare = Path.Combine(Path.GetTempPath(), "nexa-nograph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        try
        {
            var store = new ProductStore(bare);
            store.Initialize("Bare");
            var vm = new ProductViewModel(store, new ProductGit(bare), bare, _shell);

            var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

            Assert.AreEqual(0, outcome.MatchCount);
            Assert.IsFalse(outcome.Failed, "the page understood the query — there is just no graph yet");
            StringAssert.Contains(outcome.Message ?? "", "Generate graph");
        }
        finally { try { Directory.Delete(bare, recursive: true); } catch { } }
    });
}
