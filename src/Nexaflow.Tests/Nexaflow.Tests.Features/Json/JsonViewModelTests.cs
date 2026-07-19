using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Json;

/// <summary>
/// Headless command/state logic behind the JSON toolbar: view-mode toggles (tree/text/table) and their
/// active-state properties, table-mode availability gating, expand/collapse mutation of the flat display
/// list, breadcrumb rebuilding on selection, JSONPath navigation, and format-marks-modified. The seek-by-item
/// windowed loader itself is covered by <see cref="JsonFileLoaderTests"/>; everything here uses a small
/// (fast-path) document so no streaming / UI-dispatcher machinery is exercised.
/// </summary>
[TestClass]
[CoversNode("view-json")]
public class JsonViewModelTests
{
    private static string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jsonvm_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>Builds a VM over <paramref name="content"/>, loads it synchronously (small-file fast path).</summary>
    private static async Task<(JsonViewModel vm, string path)> LoadVmAsync(string content)
    {
        var path = WriteTempJson(content);
        var vm   = new JsonViewModel(path, new JsonFileLoader(), new JsonPathEvaluator(),
                                     Substitute.For<IShellServices>());
        await vm.LoadAsync(CancellationToken.None);
        return (vm, path);
    }

    // ── AI integration: context + the read/query act tools ───────────────────

    [TestMethod]
    [CoversNode("json-ai-act")]
    [CoversNode("json-ai-context")]
    public async Task AiTools_ReadAndQuery_ThroughToolSurface()
    {
        var (vm, path) = await LoadVmAsync("""{ "store": { "book": [ { "title": "A" } ] }, "count": 2 }""");
        try
        {
            // aspect 4: two JSON tabs on different files stay distinguishable when pinned together
            Assert.AreEqual(path, vm.GetSecurityContext());
            // context is honest about the file + root shape
            StringAssert.Contains(vm.GetContext(), vm.FileName);
            StringAssert.Contains(vm.GetContext(), "object");

            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "query_json_path", "read_json", "format_json" },
                tools.Select(t => t.Name).ToArray(),
                "the Json AI act tool surface changed — update the tree's json-ai-act leaves to match");

            // read_json returns the whole loaded document
            var read = tools.Single(t => t.Name == "read_json");
            var r = await read.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "\"store\"");
            StringAssert.Contains(r.ModelText, "\"title\"");

            // query_json_path reads a value at a path — data now flows back to the model
            var query = tools.Single(t => t.Name == "query_json_path");
            var q = await query.InvokeAsync(new JsonObject { ["path"] = "$.count" }, CancellationToken.None);
            Assert.IsFalse(q.IsError);
            StringAssert.Contains(q.ModelText, "2");

            var deep = await query.InvokeAsync(new JsonObject { ["path"] = "$.store.book[0].title" }, CancellationToken.None);
            Assert.IsFalse(deep.IsError);
            StringAssert.Contains(deep.ModelText, "A");

            // an unmatched path is reported, not an error
            var none = await query.InvokeAsync(new JsonObject { ["path"] = "$.nope" }, CancellationToken.None);
            Assert.IsFalse(none.IsError);
            StringAssert.Contains(none.ModelText, "No node matched");

            // format_json re-indents in place and marks the document modified
            var fmt = tools.Single(t => t.Name == "format_json");
            var f = await fmt.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(f.IsError);
            Assert.IsTrue(vm.IsModified);
        }
        finally { File.Delete(path); }
    }

    // ── Load populates the flat display list ─────────────────────────────────

    [TestMethod]
    public async Task LoadAsync_SmallObject_ExpandsRootIntoDisplayList()
    {
        var (vm, path) = await LoadVmAsync("""{ "a": 1, "b": 2, "c": 3 }""");
        try
        {
            Assert.IsNull(vm.ErrorMessage);
            Assert.IsInstanceOfType(vm.Root, typeof(JsonObjectNodeModel));
            // Root + its 3 depth-1 children are all in the display list after the auto-expand.
            var treeItems = vm.DisplayItems.OfType<JsonTreeDisplayItem>().ToList();
            Assert.AreEqual(4, treeItems.Count);
            Assert.AreEqual(3, treeItems.Count(i => i.Depth == 1));
        }
        finally { File.Delete(path); }
    }

    // ── Expand / collapse ────────────────────────────────────────────────────

    [TestMethod]
    public async Task ToggleExpand_ExpandsThenCollapsesChildNode()
    {
        var (vm, path) = await LoadVmAsync("""{ "outer": { "x": 1, "y": 2 } }""");
        try
        {
            var outer = ((JsonObjectNodeModel)vm.Root!).Children[0];
            Assert.IsTrue(vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == outer).HasChildren);

            var before = vm.DisplayItems.Count;
            vm.ToggleExpandCommand.Execute(outer);        // expand: x, y appear
            Assert.IsTrue(vm.DisplayItems.Count > before);
            Assert.IsTrue(vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == outer).IsExpanded);

            vm.ToggleExpandCommand.Execute(outer);        // collapse: back to the original count
            Assert.AreEqual(before, vm.DisplayItems.Count);
            Assert.IsFalse(vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == outer).IsExpanded);
        }
        finally { File.Delete(path); }
    }

    // ── View-mode toggle state ───────────────────────────────────────────────

    [TestMethod]
    public async Task ViewMode_DefaultsToTree_ForSelectedNode()
    {
        var (vm, path) = await LoadVmAsync("""{ "obj": { "n": 1 } }""");
        try
        {
            var obj = ((JsonObjectNodeModel)vm.Root!).Children[0];
            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == obj);

            Assert.IsTrue(vm.IsTreeModeActive);
            Assert.IsFalse(vm.IsTextModeActive);
            Assert.IsFalse(vm.IsTableModeActive);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task SetTextMode_ActivatesTextState_AndInsertsInlineContent()
    {
        var (vm, path) = await LoadVmAsync("""{ "obj": { "n": 1 } }""");
        try
        {
            var obj = ((JsonObjectNodeModel)vm.Root!).Children[0];
            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == obj);

            vm.SetTextModeCommand.Execute(null);

            Assert.IsTrue(vm.IsTextModeActive);
            Assert.IsFalse(vm.IsTreeModeActive);
            var inline = vm.DisplayItems.OfType<JsonInlineContentDisplayItem>().SingleOrDefault();
            Assert.IsNotNull(inline);
            Assert.AreEqual(NodeViewMode.Text, inline!.ViewMode);
            Assert.IsFalse(string.IsNullOrEmpty(inline.RawJson));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task SetTextThenSetTreeMode_RemovesInlineContent()
    {
        var (vm, path) = await LoadVmAsync("""{ "obj": { "n": 1 } }""");
        try
        {
            var obj = ((JsonObjectNodeModel)vm.Root!).Children[0];
            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == obj);

            vm.SetTextModeCommand.Execute(null);
            Assert.AreEqual(1, vm.DisplayItems.OfType<JsonInlineContentDisplayItem>().Count());

            vm.SetTreeModeCommand.Execute(null);
            Assert.IsFalse(vm.DisplayItems.OfType<JsonInlineContentDisplayItem>().Any());
            Assert.IsTrue(vm.IsTreeModeActive);
        }
        finally { File.Delete(path); }
    }

    // ── Table-mode availability gating ───────────────────────────────────────

    [TestMethod]
    public async Task IsTableModeAvailable_TrueForArrayOfObjects_FalseForScalarArray()
    {
        var (vm, path) = await LoadVmAsync("""{ "rows": [ { "id": 1 }, { "id": 2 } ], "flat": [ 1, 2, 3 ] }""");
        try
        {
            var root = (JsonObjectNodeModel)vm.Root!;
            var rows = root.Children.First(c => c.Key == "rows");
            var flat = root.Children.First(c => c.Key == "flat");

            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == rows);
            Assert.IsTrue(vm.IsTableModeAvailable, "array of objects supports table mode");

            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == flat);
            Assert.IsFalse(vm.IsTableModeAvailable, "array of scalars has no columns → no table mode");
        }
        finally { File.Delete(path); }
    }

    // ── Breadcrumbs ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SelectingNestedNode_RebuildsBreadcrumbChain()
    {
        var (vm, path) = await LoadVmAsync("""{ "outer": { "inner": 5 } }""");
        try
        {
            var outer = ((JsonObjectNodeModel)vm.Root!).Children[0];
            vm.ToggleExpandCommand.Execute(outer);      // reveal "inner"
            var inner = ((JsonObjectNodeModel)outer).Children[0];

            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Node == inner);

            // $ / "outer" / "inner"
            var labels = vm.SelectedNodeBreadcrumbs.Select(b => b.Label).ToList();
            Assert.AreEqual(3, labels.Count);
            Assert.AreEqual("$", labels[0]);
            CollectionAssert.Contains(labels, "\"outer\"");
            CollectionAssert.Contains(labels, "\"inner\"");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task SelectingNull_ClearsBreadcrumbs()
    {
        var (vm, path) = await LoadVmAsync("""{ "a": 1 }""");
        try
        {
            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(i => i.Depth == 1);
            Assert.IsTrue(vm.SelectedNodeBreadcrumbs.Count > 0);

            vm.SelectedDisplayItem = null;
            Assert.AreEqual(0, vm.SelectedNodeBreadcrumbs.Count);
        }
        finally { File.Delete(path); }
    }

    // ── JSONPath navigation ──────────────────────────────────────────────────

    [TestMethod]
    public async Task EvaluateJsonPath_SelectsAndExpandsToMatch()
    {
        var (vm, path) = await LoadVmAsync("""{ "outer": { "target": 99 } }""");
        try
        {
            vm.EvaluateJsonPath("$.outer.target");

            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreEqual("target", vm.SelectedNode!.Key);
            // Ancestor was expanded so the match is visible in the flat list.
            Assert.IsTrue(vm.DisplayItems.OfType<JsonTreeDisplayItem>().Any(i => i.Node == vm.SelectedNode));
        }
        finally { File.Delete(path); }
    }

    // ── Format marks modified ────────────────────────────────────────────────

    [TestMethod]
    public async Task FormatJson_MarksDocumentModified()
    {
        var (vm, path) = await LoadVmAsync("""{"a":1,"b":[1,2]}""");
        try
        {
            Assert.IsFalse(vm.IsModified);
            vm.FormatJsonCommand.Execute(null);
            Assert.IsTrue(vm.IsModified);
            Assert.IsNotNull(vm.Root);
        }
        finally { File.Delete(path); }
    }
}
