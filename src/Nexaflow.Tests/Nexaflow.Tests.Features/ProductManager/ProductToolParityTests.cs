using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The AI's product surface against a real <c>.product/</c> folder.
/// <para>
/// The point of these is <i>parity</i>. Everything the <c>nfi</c> CLI can do to the tree,
/// the assistant can do from inside the app — otherwise a model told to follow the modelling pass can edit a
/// node but cannot create one, cannot search for the id it needs, and cannot run the checks that would tell
/// it the edit was wrong. The checks are the sharp end: without <c>product_validate</c> and
/// <c>product_lint</c> a model can set <c>tests=done</c> and never learn it has just made an unbacked claim.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-ai-act")]
public class ProductToolParityTests
{
    private string _root = string.Empty;

    /// <summary>A throwaway product with one feature, a UI node and one leaf — enough shape for find,
    /// query, tree, lint and the structural edits to have something real to work on.</summary>
    [TestInitialize]
    public void CreateProduct()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-prodtools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var store = new ProductStore(_root);
        store.Initialize("TestProduct");
        var state = store.Load();

        state.Nodes["root"] = Node("Root", null, "widget");
        state.Nodes["widget"] = Node("Widget", "root", "widget-ui");
        state.Nodes["widget-ui"] = Node("UI", "widget", "widget-button");
        // The leaf carries a tests concern with nothing backing it — the exact state `query --unbacked` and
        // `lint` exist to surface.
        state.Nodes["widget-button"] = Node("Save Button", "widget-ui");
        state.Nodes["widget-button"].Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done }];
        store.SaveTree(state.Nodes);
    }

    [TestCleanup]
    public void RemoveProduct() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static ProductNode Node(string title, string? parent, params string[] children) => new()
    {
        Title = title,
        Parent = parent,
        Children = [.. children],
        Status = Status.Should,
    };

    private Task<ToolResult> Run(string tool, JsonObject? args = null) =>
        ProductTools.ForRoot(_root).Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    private ProductState Reload() => new ProductStore(_root).Load();

    // ── Parity ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryCliVerbThatChangesOrReadsTheTreeHasATool()
    {
        var names = ProductTools.ForRoot(_root).Select(t => t.Name).ToHashSet();

        // The CLI verbs a modelling pass actually uses, and the tool each maps to. `batch` is deliberately
        // absent — it is CLI ergonomics over these same operations, which the model already has one by one.
        foreach (var (verb, tool) in new[]
                 {
                     ("find", "product_find"), ("query", "product_query"), ("tree", "product_tree"),
                     ("describe", "product_zoom"), ("validate", "product_validate"), ("lint", "product_lint"),
                     ("doctor", "product_doctor"), ("add-node", "product_add_node"),
                     ("move", "product_move_node"), ("rename", "product_rename_node"),
                     ("remove", "product_remove_node"), ("remap", "product_remap_snaplinks"),
                     ("set-status", "product_set_node_status"), ("set-node", "product_edit_node"),
                     ("set-concern", "product_set_concern_status"),
                     ("remove-concern", "product_remove_concern"),
                     ("add-snaplink", "product_add_node_snaplink"),
                     ("remove-snaplink", "product_remove_node_snaplink"),
                 })
            Assert.IsTrue(names.Contains(tool), $"CLI verb '{verb}' has no '{tool}' tool");
    }

    /// <summary>
    /// Every tool the assistant is handed, and whether it asks the user first.
    /// <para>
    /// This is one table rather than a list of the interesting ones because the interesting ones were the
    /// problem. The safety rule used to be asserted in two places — six tools here, six others in
    /// <c>ProductToolsTests</c> — and between them they named 17 of the 34 tools on offer. Whether a new
    /// tool prompted before deleting something was, for half the surface, whatever the author happened to
    /// type. A table plus <see cref="TheSafetyTableNamesEveryToolOnOffer"/> makes the omission the failure.
    /// </para>
    /// <para>
    /// The line the values encode: reading never prompts, and neither does an edit that is trivially
    /// reversible from the UI (setting a status, adding a snaplink — the tree is in git and the page is
    /// watching the file). Anything that <em>removes</em> something, or changes the shape of the tree, asks.
    /// </para>
    /// </summary>
    private static readonly (string Tool, ToolSafety Expected)[] SafetyContract =
    [
        // ── Product: reads ────────────────────────────────────────────────────
        ("product_survey",                  ToolSafety.SafeOperation),
        ("product_zoom",                    ToolSafety.SafeOperation),
        ("product_needs_attention",         ToolSafety.SafeOperation),
        ("product_find",                    ToolSafety.SafeOperation),
        ("product_query",                   ToolSafety.SafeOperation),
        ("product_tree",                    ToolSafety.SafeOperation),
        ("product_validate",                ToolSafety.SafeOperation),
        ("product_lint",                    ToolSafety.SafeOperation),

        // ── Product: additive edits, reversible from the page ─────────────────
        ("product_set_node_status",         ToolSafety.SafeOperation),
        ("product_edit_node",               ToolSafety.SafeOperation),
        ("product_set_concern_status",      ToolSafety.SafeOperation),
        ("product_add_node_snaplink",       ToolSafety.SafeOperation),
        ("product_add_concern_snaplink",    ToolSafety.SafeOperation),

        // ── Product: removals and reshaping — must ask ────────────────────────
        ("product_add_concern",             ToolSafety.RequiresApproval),
        ("product_remove_concern",          ToolSafety.RequiresApproval),
        ("product_remove_node_snaplink",    ToolSafety.RequiresApproval),
        ("product_remove_concern_snaplink", ToolSafety.RequiresApproval),
        ("product_add_node",                ToolSafety.RequiresApproval),
        ("product_move_node",               ToolSafety.RequiresApproval),
        ("product_rename_node",             ToolSafety.RequiresApproval),
        ("product_remove_node",             ToolSafety.RequiresApproval),
        ("product_remap_snaplinks",         ToolSafety.RequiresApproval),
        ("product_doctor",                  ToolSafety.RequiresApproval),

        // ── Graph: reads, plus the one command that writes graph.json ─────────
        ("graph_search",                    ToolSafety.SafeOperation),
        ("graph_context",                   ToolSafety.SafeOperation),
        ("graph_node",                      ToolSafety.SafeOperation),
        ("graph_walk",                      ToolSafety.SafeOperation),
        ("graph_grep",                      ToolSafety.SafeOperation),
        ("graph_code",                      ToolSafety.SafeOperation),
        ("graph_stats",                     ToolSafety.SafeOperation),
        ("graph_orphans",                   ToolSafety.SafeOperation),
        ("graph_paths",                     ToolSafety.SafeOperation),
        ("graph_rank",                      ToolSafety.SafeOperation),
        ("graph_build",                     ToolSafety.RequiresApproval),
    ];

    [TestMethod]
    public void EveryToolAsksOrDoesNotAsk_AsTheSafetyContractSays()
    {
        var actual = ProductTools.ForRoot(_root).ToDictionary(t => t.Name, t => t.Safety);

        var wrong = SafetyContract
            .Where(row => actual.TryGetValue(row.Tool, out var s) && s != row.Expected)
            .Select(row => $"{row.Tool}: expected {row.Expected}, got {actual[row.Tool]}")
            .ToList();

        Assert.AreEqual(0, wrong.Count,
            "A tool's prompt behaviour changed. If that was deliberate, move its row — don't edit the "
            + "expectation to match the code, or the table stops being a decision:\n  " + string.Join("\n  ", wrong));
    }

    [TestMethod]
    public void TheSafetyTableNamesEveryToolOnOffer()
    {
        // The half that actually bites. A tool added without a row is a tool whose prompt behaviour nobody
        // decided — and the old pair of partial lists would have passed it in silence.
        var offered = ProductTools.ForRoot(_root).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var tabled  = SafetyContract.Select(r => r.Tool).ToHashSet(StringComparer.Ordinal);

        var untabled = offered.Except(tabled).Order().ToList();
        var stale    = tabled.Except(offered).Order().ToList();

        Assert.AreEqual(0, untabled.Count,
            "These tools are handed to the assistant with no declared safety expectation. Add a row to "
            + $"{nameof(SafetyContract)} saying whether each should ask first:\n  " + string.Join("\n  ", untabled));
        Assert.AreEqual(0, stale.Count,
            $"{nameof(SafetyContract)} names tools that no longer exist:\n  " + string.Join("\n  ", stale));
    }

    // ── Navigate ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Find_TurnsAFeatureNameIntoNodeIds()
    {
        var r = await Run("product_find", new JsonObject { ["term"] = "widget" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "widget-button");
        StringAssert.Contains(r.ModelText, "match(es)");
    }

    [TestMethod]
    public async Task Find_ATermThatMatchesNothingIsAnAnswer_NotAnError()
    {
        var r = await Run("product_find", new JsonObject { ["term"] = "nothing-like-this" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "No nodes match");
    }

    [TestMethod]
    public async Task Query_FindsTheLeavesThatStillOweATest()
    {
        var r = await Run("product_query", new JsonObject
        {
            ["under"] = "widget", ["concern"] = "tests", ["unbacked"] = true,
        });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "widget-button",
                              "the whole point of query: which claims have nothing behind them");
    }

    [TestMethod]
    public async Task Query_UnbackedWithoutAConcernIsRefusedRatherThanGuessed()
    {
        var r = await Run("product_query", new JsonObject { ["unbacked"] = true });

        Assert.IsTrue(r.IsError);
        StringAssert.Contains(r.ModelText, "concern");
    }

    [TestMethod]
    public async Task Tree_PrintsTheWholeSubtree()
    {
        var r = await Run("product_tree", new JsonObject { ["node_id"] = "widget" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "widget-ui");
        StringAssert.Contains(r.ModelText, "widget-button");
        StringAssert.Contains(r.ModelText, "node(s).");
    }

    [TestMethod]
    public async Task Tree_OfANodeThatIsNotThereSaysSo()
    {
        var r = await Run("product_tree", new JsonObject { ["node_id"] = "no-such-node" });

        Assert.IsTrue(r.IsError);
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Validate_ReportsTheSnaplinkVerdictAndHowMuchWasScanned()
    {
        var r = await Run("product_validate");

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "scanned");
    }

    [TestMethod]
    public async Task Validate_CatchesASnaplinkTheModelJustBroke()
    {
        // Exactly the loop the tools exist to close: claim a test, point it at nothing, then check.
        await Run("product_add_node_snaplink", new JsonObject
        {
            ["node_id"] = "widget-button", ["type"] = "code", ["target"] = "src/NoSuchFile.cs",
        });

        var r = await Run("product_validate");

        StringAssert.Contains(r.ModelText, "NoSuchFile.cs",
                              "a model that cannot see this will happily leave the tree broken");
        StringAssert.Contains(r.ModelText, "broken snaplink");
    }

    [TestMethod]
    public async Task Lint_IsAdvisory_AndSaysSo()
    {
        var r = await Run("product_lint", new JsonObject { ["under"] = "widget" });

        Assert.IsFalse(r.IsError);
        // Either it is clean or it names findings — but it must never read as a build failure.
        if (r.ModelText.Contains("finding(s)"))
            StringAssert.Contains(r.ModelText, "advisory");
    }

    // ── Structure ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AddNode_GrowsTheTreeAndAttachesTheDefaultConcerns()
    {
        var r = await Run("product_add_node", new JsonObject
        {
            ["parent_id"] = "widget-ui", ["title"] = "Cancel Button",
        });

        Assert.IsFalse(r.IsError);
        var state = Reload();
        Assert.IsTrue(state.Nodes.ContainsKey("cancel-button"), "the id defaults to a slug of the title");
        Assert.IsTrue(state.Nodes["widget-ui"].Children.Contains("cancel-button"));
        Assert.IsTrue(state.Nodes["cancel-button"].Concerns?.Count > 0,
                      "a new node arrives lint-clean rather than needing its concerns added by hand");
    }

    [TestMethod]
    public async Task AddNode_RefusesAnIdThatIsAlreadyTaken()
    {
        var r = await Run("product_add_node", new JsonObject
        {
            ["parent_id"] = "widget-ui", ["title"] = "Another", ["node_id"] = "widget-button",
        });

        Assert.IsTrue(r.IsError, "ids are one flat namespace — a silent collision would corrupt the tree");
    }

    [TestMethod]
    public async Task MoveNode_Reparents()
    {
        var r = await Run("product_move_node", new JsonObject
        {
            ["node_id"] = "widget-button", ["new_parent_id"] = "widget",
        });

        Assert.IsFalse(r.IsError);
        var state = Reload();
        Assert.AreEqual("widget", state.Nodes["widget-button"].Parent);
        Assert.IsFalse(state.Nodes["widget-ui"].Children.Contains("widget-button"), "and leaves its old parent");
    }

    [TestMethod]
    public async Task MoveNode_RefusesACycle()
    {
        var r = await Run("product_move_node", new JsonObject
        {
            ["node_id"] = "widget", ["new_parent_id"] = "widget-button",
        });

        Assert.IsTrue(r.IsError);
        StringAssert.Contains(r.ModelText, "cycle");
    }

    [TestMethod]
    public async Task RenameNode_RetargetsTheTree_AndWarnsAboutTestSource()
    {
        var r = await Run("product_rename_node", new JsonObject
        {
            ["node_id"] = "widget-button", ["new_id"] = "widget-save",
        });

        Assert.IsFalse(r.IsError);
        var state = Reload();
        Assert.IsTrue(state.Nodes.ContainsKey("widget-save"));
        Assert.IsFalse(state.Nodes.ContainsKey("widget-button"));
        Assert.IsTrue(state.Nodes["widget-ui"].Children.Contains("widget-save"));
        StringAssert.Contains(r.ModelText, "CoversNode",
                              "a rename cannot reach test source — the model has to be told to follow up");
    }

    [TestMethod]
    public async Task RemoveNode_RefusesAParentUnlessRecursiveIsAskedFor()
    {
        var refused = await Run("product_remove_node", new JsonObject { ["node_id"] = "widget-ui" });
        Assert.IsTrue(refused.IsError);
        Assert.IsTrue(Reload().Nodes.ContainsKey("widget-button"), "and nothing was deleted");

        var done = await Run("product_remove_node", new JsonObject
        {
            ["node_id"] = "widget-ui", ["recursive"] = true,
        });

        Assert.IsFalse(done.IsError);
        var state = Reload();
        Assert.IsFalse(state.Nodes.ContainsKey("widget-ui"));
        Assert.IsFalse(state.Nodes.ContainsKey("widget-button"), "the subtree goes with it");
    }

    [TestMethod]
    public async Task Doctor_ReportsWithoutChangingAnythingUntilAskedToFix()
    {
        // Point a child id at a node that does not exist — the shape doctor repairs.
        var store = new ProductStore(_root);
        var state = store.Load();
        state.Nodes["widget-ui"].Children.Add("ghost-node");
        store.SaveTree(state.Nodes);

        var report = await Run("product_doctor");
        Assert.IsFalse(report.IsError);
        StringAssert.Contains(report.ModelText, "fix=true");
        Assert.IsTrue(Reload().Nodes["widget-ui"].Children.Contains("ghost-node"), "a bare doctor is read-only");

        var fixIt = await Run("product_doctor", new JsonObject { ["fix"] = true });

        Assert.IsFalse(fixIt.IsError);
        Assert.IsFalse(Reload().Nodes["widget-ui"].Children.Contains("ghost-node"));
    }
}
