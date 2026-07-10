using System.IO;
using System.Linq;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

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
    public void BrokenCodeLink_SurfacesAsAnEditableIssue_OfferingTheFilesRealTargets()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });

        var vm = Open();   // ctor scans synchronously via the stub

        Assert.AreEqual(1, vm.IssueCount);
        Assert.IsNotNull(vm.SelectedIssue);
        Assert.IsTrue(vm.SelectedIssue!.CanEdit, "a live link must be editable, not 'from an older scan'");
        Assert.IsTrue(vm.HasTargets, "the dropdown should be enabled");
        // The picker offers the class and its method — exactly what a whole-file parse yields.
        CollectionAssert.Contains(vm.AvailableTargets.Select(t => t.Class).ToList(), "Widget");
        Assert.IsTrue(vm.AvailableTargets.Any(t => t.Method == "Spin"), "the real method should be pickable");
    }

    [TestMethod]
    public void PickingATarget_FillsTheFields_AndApplyResolvesTheIssue()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Wobble" });
        var vm = Open();

        vm.SelectedTarget = vm.AvailableTargets.First(t => t.Method == "Spin");
        Assert.AreEqual("Spin", vm.SelectedIssue!.Method, "picking a target fills the row's fields");

        vm.ApplyCommand.Execute(null);

        Assert.AreEqual(0, vm.IssueCount, "the repaired link should leave the list");
        var saved = _store.Load().Nodes["n"].Snaplinks!.Single();
        Assert.AreEqual("Spin", saved.Method, "the fix persisted to the tree");
    }

    // ── the removal regression ───────────────────────────────────────────────

    [TestMethod]
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
        Assert.IsTrue(vm.SelectedIssue!.CanEdit,
            "the surviving row must stay editable after a sibling was removed (the index-drift bug)");
        Assert.AreEqual(1, _store.Load().Nodes["n"].Snaplinks!.Count, "one link removed from the tree");
    }

    // ── the moved-file aid ───────────────────────────────────────────────────

    [TestMethod]
    public void AMissingFile_SuggestsTheSameNamedFileThatMoved()
    {
        WriteFile("src/new/Widget.cs", WidgetCs);   // the file exists — but elsewhere
        SaveTree(new Snaplink { Type = "code", Doc = "src/old/Widget.cs", Class = "Widget" });
        var vm = Open();

        Assert.AreEqual(1, vm.IssueCount);
        Assert.IsTrue(vm.HasFileSuggestions, "the 'did it move?' dropdown should be enabled");
        CollectionAssert.Contains(vm.FileSuggestions.ToList(), "src/new/Widget.cs");

        vm.UseSuggestionCommand.Execute("src/new/Widget.cs");
        Assert.AreEqual("src/new/Widget.cs", vm.SelectedIssue!.Doc, "the row is repointed at the relocated file");
        Assert.IsTrue(vm.HasTargets, "the relocated file's classes are now offered");
    }

    [TestMethod]
    public void ACleanTree_ShowsNoIssues()
    {
        WriteFile("src/Widget.cs", WidgetCs);
        SaveTree(new Snaplink { Type = "code", Doc = "src/Widget.cs", Class = "Widget", Method = "Spin" });
        var vm = Open();
        Assert.AreEqual(0, vm.IssueCount);
        Assert.IsFalse(vm.HasIssues);
    }
}
