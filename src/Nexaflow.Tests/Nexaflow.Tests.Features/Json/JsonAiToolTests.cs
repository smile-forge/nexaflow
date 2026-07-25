using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Json;

/// <summary>
/// The JSON viewer's AI surface, one test per tool.
/// <para>
/// The interesting constraint is that <c>read_json</c> returns the <i>loaded</i> document. A large file is
/// held as a front batch plus placeholders, so on one the tool can only ever return part of it — which is
/// why <c>query_json_path</c> exists beside it, and why the context has to say the root is windowed rather
/// than let the model read a partial array and take it for the whole thing.
/// </para>
/// </summary>
[TestClass]
public class JsonAiToolTests
{
    private const string Doc = """{ "store": { "book": [ { "title": "A" } ] }, "count": 2 }""";

    private string _path = string.Empty;

    [TestCleanup]
    public void RemoveTemp() { try { File.Delete(_path); } catch { } }

    private async Task<JsonViewModel> LoadedAsync(string content = Doc)
    {
        _path = Path.Combine(Path.GetTempPath(), $"jsonai_{Guid.NewGuid():N}.json");
        File.WriteAllText(_path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var vm = new JsonViewModel(_path, new JsonFileLoader(), new JsonPathEvaluator(),
                                   Substitute.For<IShellServices>());
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    private static Task<ToolResult> Run(JsonViewModel vm, string tool, JsonObject? args = null)
        => vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    // ── The surface ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-ai-act")]
    public async Task TheToolSurfaceIsExactlyWhatTheTreeSaysItIs()
    {
        var vm = await LoadedAsync();

        CollectionAssert.AreEquivalent(
            new[] { "query_json_path", "read_json", "format_json" },
            vm.GetClientTools().Select(t => t.Name).ToArray(),
            "the Json AI act tool surface changed — update the tree's json-ai-act leaves to match");
    }

    // ── Context honesty ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-ai-context")]
    public async Task TheContextDescribesTheRootShape_NotJustTheFileName()
    {
        var vm = await LoadedAsync();

        var ctx = vm.GetContext();

        StringAssert.Contains(ctx, vm.FileName);
        StringAssert.Contains(ctx, "object", "an object root and an array root are read very differently");
        StringAssert.Contains(ctx, "2 properties");
    }

    [TestMethod]
    [CoversNode("json-ai-context")]
    public async Task TheContextSaysWhichViewIsUp_AndWhatIsSelected()
    {
        var vm = await LoadedAsync();
        StringAssert.Contains(vm.GetContext(), "tree view");
        StringAssert.Contains(vm.GetContext(), "selected node: none");

        vm.SelectedDisplayItem = vm.DisplayItems.First(i => i.Node?.GetJsonPath() == "$.count");

        StringAssert.Contains(vm.GetContext(), "$.count",
                              "asking about \"this value\" only means something if the selection is stated");
    }

    [TestMethod]
    [CoversNode("json-ai-context")]
    public async Task UnsavedChangesAreDeclared_SoTheModelKnowsDiskAndScreenDisagree()
    {
        var vm = await LoadedAsync();
        Assert.IsFalse(vm.GetContext().Contains("unsaved"));

        vm.FormatJsonCommand.Execute(null);

        StringAssert.Contains(vm.GetContext(), "unsaved changes");
    }

    [TestMethod]
    [CoversNode("json-ai-context")]
    public async Task TwoOpenFilesAreDistinctScopes_NotFirstWins()
    {
        var vm = await LoadedAsync();

        Assert.AreEqual(_path, vm.GetSecurityContext());
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-ai-act-read-json")]
    public async Task ReadJson_ReturnsTheWholeLoadedDocument()
    {
        var vm = await LoadedAsync();

        var r = await Run(vm, "read_json");

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "\"store\"");
        StringAssert.Contains(r.ModelText, "\"title\"", "nested content too, not just the top level");
    }

    [TestMethod]
    [CoversNode("json-ai-act-query-json-path")]
    public async Task QueryJsonPath_ReadsAValueAtAnyDepth()
    {
        var vm = await LoadedAsync();

        StringAssert.Contains((await Run(vm, "query_json_path", new JsonObject { ["path"] = "$.count" })).ModelText, "2");
        StringAssert.Contains(
            (await Run(vm, "query_json_path", new JsonObject { ["path"] = "$.store.book[0].title" })).ModelText, "A",
            "through an object, into an array, by index — the point of a path over read_json");
    }

    [TestMethod]
    [CoversNode("json-ai-act-query-json-path")]
    public async Task QueryJsonPath_APathThatMatchesNothingIsAnAnswer_NotAnError()
    {
        var vm = await LoadedAsync();

        var none = await Run(vm, "query_json_path", new JsonObject { ["path"] = "$.nope" });

        Assert.IsFalse(none.IsError, "\"there is no such key\" is a useful result, and a retry loop is not");
        StringAssert.Contains(none.ModelText, "No node matched");
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("json-ai-act-format-json")]
    public async Task FormatJson_ReindentsAndLeavesTheDocumentDirty()
    {
        var vm = await LoadedAsync();

        var f = await Run(vm, "format_json");

        Assert.IsFalse(f.IsError);
        Assert.IsTrue(vm.IsModified, "the re-indent is uncommitted — the user still has to Save");
    }
}
