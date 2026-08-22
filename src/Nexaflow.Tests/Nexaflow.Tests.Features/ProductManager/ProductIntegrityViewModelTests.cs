using System.IO;
using System.Linq;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The Integrity page's repair flow, driven through its ViewModel — the same path the page's dropdowns and
/// buttons drive, but runnable headless (the shell stub runs the scan synchronously). Covers what three rounds
/// of manual UI feedback surfaced: the target dropdown must offer the file's real classes/methods, picking one
/// must resolve the issue, a moved file must be suggested, and removing one link must not lock its siblings.
/// </summary>
[TestClass]
public class ProductIntegrityViewModelTests
{
    private string _root = string.Empty;
    private ProductStore _store = null!;
    private IntegrityShellStub _shell = null!;

    private const string WidgetCs = """
        namespace Demo;
        public class Widget
        {
            public void Spin() { }
        }
        """;

    [TestInitialize]
    public void Setup()
    {
        _root  = Path.Combine(Path.GetTempPath(), $"integ_vm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _store = new ProductStore(_root);
        _store.Initialize("Test");
        _shell = new IntegrityShellStub();
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteFile(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void SaveTree(params Snaplink[] nodeLinks) =>
        _store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["n"] = new() { Title = "Node", Snaplinks = [.. nodeLinks] }
        });

    private ProductIntegrityViewModel Open() => new(_store, _root, _shell);

    // ── the dropdown's contents ──────────────────────────────────────────────

    [TestMethod]
    [CoversNode("integrity-issues")]
    public void BrokenCodeLink_SurfacesAsAnEditableIssue_OfferingTheFilesRealTargets()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });

        var vm = Open();   // ctor scans synchronously via the stub

        Assert.AreEqual(1, vm.IssueCount);
        Assert.IsNotNull(vm.SelectedIssue);
        Assert.IsTrue(((IntegrityIssueItem)vm.SelectedIssue!).CanEdit, "a live link must be editable, not 'from an older scan'");
        Assert.IsTrue(vm.HasTargets, "the dropdown should be enabled");
        // The picker offers the class and its method — exactly what a whole-file parse yields.
        CollectionAssert.Contains(vm.AvailableTargets.Select(t => t.Class).ToList(), "Widget");
        Assert.IsTrue(vm.AvailableTargets.Any(t => t.Method == "Spin"), "the real method should be pickable");
    }

    [TestMethod]
    [CoversNode("integrity-relink")]
    public void PickingATarget_FillsTheFields_AndApplyResolvesTheIssue()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });
        var vm = Open();

        vm.SelectedTarget = vm.AvailableTargets.First(t => t.Method == "Spin");
        Assert.AreEqual("Spin", ((IntegrityIssueItem)vm.SelectedIssue!).Method, "picking a target fills the row's fields");

        vm.ApplyCommand.Execute(null);

        Assert.AreEqual(0, vm.IssueCount, "the repaired link should leave the list");
        var saved = _store.Load().Nodes["n"].Snaplinks!.Single();
        Assert.AreEqual("Spin", saved.Method, "the fix persisted to the tree");
    }

    // ── the AI surface ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("product-ai-context-integrity")]
    public void TheContextReportsTheScanVerdict_AndNamesTheBrokenLinks()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });

        var ctx = Open().GetContext();

        StringAssert.Contains(ctx, _root, "which product was scanned");
        StringAssert.Contains(ctx, "Wobble",
                              "naming the actual broken links is the difference between 'something is wrong' "
                            + "and a model that can repair them");
    }

    [TestMethod]
    [CoversNode("product-ai-context-integrity")]
    public void ACleanTreeSaysSoOutright_RatherThanJustOmittingTheIssues()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Spin" });

        var ctx = Open().GetContext();

        StringAssert.Contains(ctx, "every snaplink points at a real target",
                              "silence would read as 'not scanned' rather than 'scanned and clean'");
    }

    [TestMethod]
    [CoversNode("product-ai-context-integrity")]
    public void TheSendIsHeldWhileTheScanIsStillRunning()
    {
        // A full scan tree-sitter-parses every referenced file on the background queue. Pinned mid-scan, an
        // ungated page would report "no issues" for a tree nobody had checked yet — the worst possible
        // answer, because it is indistinguishable from a genuinely clean one.
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });
        var vm = Open();
        Assert.IsTrue(vm.IsContextReady, "precondition: the stub scans synchronously, so it has finished");

        vm.IsScanning = true;

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still running");
    }

    [TestMethod]
    [CoversNode("product-ai-act")]
    public void TheIntegrityPageOffersTheSameProductToolsAsEveryOtherView()
    {
        // This page is where the model is most likely to need them: it is looking at a list of broken
        // snaplinks, and without the tools it could describe the breakage but not repair a single link.
        var vm = Open();

        var names = vm.GetClientTools().Select(t => t.Name).ToList();

        CollectionAssert.IsSubsetOf(
            new[] { "product_validate", "product_remap_snaplinks", "product_remove_node_snaplink",
                    "product_add_concern_snaplink", "product_find" },
            names);
        Assert.AreEqual(_root, vm.GetSecurityContext(), "one scope across all three views of one tree");
    }

    // ── the removal regression ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("integrity-relink")]
    public void RemovingOneLink_DropsIt_AndLeavesTheNodesOtherRowsEditable()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(
            new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" },
            new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Twirl" });
        var vm = Open();
        Assert.AreEqual(2, vm.IssueCount);

        vm.SelectedIssue = vm.Issues[0];
        vm.RemoveLinkCommand.Execute(null);

        Assert.AreEqual(1, vm.IssueCount);
        Assert.IsTrue(((IntegrityIssueItem)vm.SelectedIssue!).CanEdit,
            "the surviving row must stay editable after a sibling was removed (the index-drift bug)");
        Assert.AreEqual(1, _store.Load().Nodes["n"].Snaplinks!.Count, "one link removed from the tree");
    }

    // ── the moved-file aid ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("integrity-relink")]
    public void AMissingFile_SuggestsTheSameNamedFileThatMoved()
    {
        WriteFile("src/new/Widget.cs", WidgetCs);   // the file exists — but elsewhere
        SaveTree(new Snaplink { Type = "code", Doc = "src/old/Widget.cs", Class = "Widget" });
        var vm = Open();

        Assert.AreEqual(1, vm.IssueCount);
        Assert.IsTrue(vm.HasFileSuggestions, "the 'did it move?' dropdown should be enabled");
        CollectionAssert.Contains(vm.FileSuggestions.ToList(), "src/new/Widget.cs");

        vm.UseSuggestionCommand.Execute("src/new/Widget.cs");
        Assert.AreEqual("src/new/Widget.cs", ((IntegrityIssueItem)vm.SelectedIssue!).Doc, "the row is repointed at the relocated file");
        Assert.IsTrue(vm.HasTargets, "the relocated file's classes are now offered");
    }

    [TestMethod]
    [CoversNode("integrity-issues")]
    public void ACleanTree_ShowsNoIssues()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Spin" });
        var vm = Open();
        Assert.AreEqual(0, vm.IssueCount);
        Assert.IsFalse(vm.HasIssues);
    }

    // ── stale ast: a suggestion, not breakage ────────────────────────────────

    private const string ViewXaml = """
        <UserControl x:Class="Demo.View" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Button x:Name="MicButton" />
        </UserControl>
        """;

    [TestMethod]
    [CoversNode("integrity-ast-advisories")]
    public void StaleAst_IsAnAdvisoryRow_AndRePointsInOneClick()
    {
        WriteFile("src/View.xaml", ViewXaml);
        SaveTree(new Snaplink { Type = "code", Doc = "src/View.xaml", Ast = "Mic button" });

        var vm = Open();
        var row = vm.Issues.OfType<SnaplinkAdvisoryItem>().Single();
        Assert.IsTrue(row.IsAdvisory, "a stale ast must never read as a broken link");
        Assert.IsTrue(row.HasSuggestion);

        vm.FixAstCommand.Execute(row);

        Assert.AreEqual("N:MicButton", _store.Load().Nodes["n"].Snaplinks!.Single().Ast);
        Assert.AreEqual(0, vm.Issues.OfType<SnaplinkAdvisoryItem>().Count(), "the row is gone once acted on");
    }

    [TestMethod]
    [CoversNode("integrity-ast-advisories")]
    public void StaleAst_WithNothingToPointAt_IsCleared()
    {
        WriteFile("src/View.xaml", ViewXaml);
        SaveTree(new Snaplink { Type = "code", Doc = "src/View.xaml", Class = "View", Ast = "ROW 4 - AI BAR" });

        var vm = Open();
        var row = vm.Issues.OfType<SnaplinkAdvisoryItem>().Single();
        Assert.IsFalse(row.HasSuggestion);

        vm.FixAstCommand.Execute(row);

        var link = _store.Load().Nodes["n"].Snaplinks!.Single();
        Assert.IsNull(link.Ast, "a value resolving to nothing was never navigating anywhere");
        Assert.AreEqual("View", link.Class, "the rest of the link is untouched");
    }

    [TestMethod]
    [CoversNode("integrity-issues")]
    public void ApplyingAnEditToAnUncheckableTarget_DoesNotClaimItIsFixed()
    {
        // The false green this replaces: "sound" and "nothing could be checked" arrived at the page as the
        // same value, so re-pointing at a file nothing parses was reported as a confirmed fix.
        WriteFile("src/notes.txt", "no structure in here");
        SaveTree(new Snaplink { Type = "code", Doc = "src/Gone.cs", Class = "Widget" });

        var vm = Open();
        var row = (IntegrityIssueItem)vm.SelectedIssue!;
        row.Doc = "src/notes.txt";
        vm.ApplyCommand.Execute(null);

        StringAssert.Contains(_shell.Notifications.LastOrDefault() ?? "", "isn't confirmed");
        Assert.IsFalse((_shell.Notifications.LastOrDefault() ?? "").Contains("fixed"), "nothing was verified, so nothing may claim to be");
    }
}
