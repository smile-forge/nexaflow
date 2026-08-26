using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.ProductManager;
using Nexaflow.Features.ProductManager.Graph;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The graph search results page: what a query finds across the whole graph, and where each result goes
/// when it is opened.
/// <para>
/// The drill-in is the part worth pinning. A result can be a product node, a type, a file or a line of
/// source, and they do not open the same way: a product node focuses the sunburst, anything with a file
/// behind it goes to whatever normally opens that file, and either can be shown in the graph viewer. A
/// button that sent all of them to the same place would be wrong for most rows.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-search-passes")]
public class ProductSearchPageTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    /// <summary>A well-formed node id that is not among these results — so the decline comes from the lookup, not
    /// from the id failing to parse.</summary>
    protected override SearchHit UnknownHit => new("product:nowhere", "not on this page");

    private GraphFixture _fix = null!;
    private IShellServices _shell = null!;

    [TestInitialize]
    public void Setup()
    {
        _fix   = new GraphFixture();
        _shell = Substitute.For<IShellServices>();
    }

    [TestCleanup]
    public void Teardown() => _fix.Dispose();

    private async Task<ProductSearchViewModel> BuildAsync(string query = "alpha42")
    {
        var vm = new ProductSearchViewModel(_fix.Root, query, _shell);
        await vm.SearchAsync();
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await BuildAsync();

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProductSearchViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.Query}|" +
               string.Join(",", vm.Results.Select(r => $"{r.NodeId}{(r.IsSourceHit ? "@" + r.Line : "")}"));
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

    // ── What a search finds ──────────────────────────────────────────────────

    [TestMethod]
    public void BothHalvesOfTheGraphAreSearched() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();

        var byName   = vm.Results.Where(r => !r.IsSourceHit).Select(r => r.NodeId).ToArray();
        var bySource = vm.Results.Where(r => r.IsSourceHit).Select(r => r.NodeId).ToArray();

        CollectionAssert.AreEqual(new[] { GraphFixture.ProductNode }, byName,
            "the node whose label carries the term");
        CollectionAssert.AreEqual(new[] { GraphFixture.GadgetNode }, bySource,
            "and the node whose SOURCE does — which no name index would have found");
    });

    [TestMethod]
    public void ASourceRowCarriesTheLineItMatchedOn() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();

        var row = vm.Results.Single(r => r.IsSourceHit);
        Assert.AreEqual(5, row.Line);
        StringAssert.Contains(row.Text, "alpha42 lives here", "the line itself, so the hit is judgeable");
        StringAssert.Contains(row.Detail, "src/Gadget.cs:5");
    });

    [TestMethod]
    public void TheStatusLineReportsBothPasses() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();

        StringAssert.Contains(vm.StatusText, "node name");
        StringAssert.Contains(vm.StatusText, "source line");
    });

    [TestMethod]
    public void AQueryThatMatchesNothing_SaysSo_RatherThanLookingBroken() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync("nothinghere");

        Assert.AreEqual(0, vm.Results.Count);
        StringAssert.Contains(vm.StatusText, "No node names matched");
    });

    [TestMethod]
    public void WithNoGraphBuilt_SaysSo() => RunUnpumped(async () =>
    {
        var bare = Path.Combine(Path.GetTempPath(), "nexa-nograph-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        try
        {
            new Nexaflow.Services.Initiatives.Product.Services.ProductStore(bare).Initialize("Bare");
            var vm = new ProductSearchViewModel(bare, "alpha42", _shell);

            await vm.SearchAsync();

            Assert.AreEqual(0, vm.Results.Count);
            StringAssert.Contains(vm.StatusText, "Generate graph");
        }
        finally { try { Directory.Delete(bare, recursive: true); } catch { } }
    });

    // ── Drill-in ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void OpeningAProductNode_FocusesTheSunburstOnIt() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();
        var row = vm.Results.Single(r => r.IsProductNode);
        Assert.AreEqual("Open in tree", row.OpenLabel, "the button says which of the two it is");

        vm.OpenCommand.Execute(row);

        var opened = OpenedWith(_shell, ProductManagerTabRegistration.StaticPageKind);
        Assert.AreEqual(_fix.Root, opened["path"]);
        Assert.AreEqual(GraphFixture.ProductNode, opened["node"]);
    });

    [TestMethod]
    public void OpeningACodeNode_HandsTheFileToItsDefaultHandler() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();
        var row = vm.Results.Single(r => r.IsSourceHit);
        Assert.AreEqual("Open file", row.OpenLabel);

        vm.OpenCommand.Execute(row);

        _shell.Received(1).HandleObject(Path.Combine(_fix.Root, "src", "Gadget.cs"));
        Assert.AreEqual(0, _shell.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IShellServices.OpenTab)),
            "the shell decides which viewer a .cs opens in — the results page does not second-guess it");
    });

    [TestMethod]
    public void ShowInGraph_OpensTheViewerOnThatNode() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();
        var row = vm.Results.Single(r => r.IsSourceHit);

        vm.ShowInGraphCommand.Execute(row);

        var opened = OpenedWith(_shell, GraphViewerTabRegistration.StaticPageKind);
        Assert.AreEqual(_fix.GraphFilePath, opened["path"]);
        Assert.AreEqual(GraphFixture.GadgetNode, opened["node"],
            "a separate button, because 'where does this sit' is a different question from 'take me to it'");
    });

    [TestMethod]
    public void ANodeWithNoFileBehindIt_OffersNoOpen() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();

        var product = vm.Results.Single(r => r.IsProductNode);
        Assert.IsTrue(product.CanOpen, "a product node opens in the tree");

        // A row with neither a product node nor a file has nothing to open — the button is hidden rather
        // than offered and dead.
        var orphan = new ProductSearchRow("external:Foo", "Foo", "external", "external:Foo", "external:Foo",
                                          null, 0, IsSourceHit: false);
        Assert.IsFalse(orphan.CanOpen);
        Assert.AreEqual(string.Empty, orphan.OpenLabel);
    });

    // ── The page answering "?" itself ────────────────────────────────────────

    [TestMethod]
    public void SearchingAgainFromTheResultsPage_ReplacesTheResults() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();
        Assert.IsTrue(vm.Results.Any(r => r.NodeId == GraphFixture.ProductNode));

        // "Gadget" is nowhere in the product node's id or label, so a page that appended would still be
        // showing it.
        await vm.SearchAsync(Query("Gadget"), display: true, default);

        Assert.AreEqual("Gadget", vm.Query);
        Assert.IsTrue(vm.Results.Any(r => r.NodeId == GraphFixture.GadgetNode));
        Assert.IsFalse(vm.Results.Any(r => r.NodeId == GraphFixture.ProductNode),
            "a second search is a new question, not an addition to the last one");
        Assert.IsTrue(vm.IsSearchActive);
    });

    [TestMethod]
    public void ShowResults_KeepsOnlyTheRowsTheAgentChose() => RunUnpumped(async () =>
    {
        var vm = await BuildAsync();
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == GraphFixture.GadgetNode);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { GraphFixture.GadgetNode },
            vm.Results.Select(r => r.NodeId).ToArray());
    });

}
