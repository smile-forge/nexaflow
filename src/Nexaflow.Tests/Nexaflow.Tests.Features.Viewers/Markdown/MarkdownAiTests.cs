using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// Covers the Markdown AI-integration surface: honest get_context (view mode / size / heading outline),
/// the client-tool surface (read / replace / set-mode / set-document / save) driven exactly as the
/// conversation hub drives it, the file-scoped security context, and the read-only context preview.
/// </summary>
[TestClass]
public class MarkdownAiTests
{
    private readonly List<string> _tempFiles = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { }
    }

    /// <summary>A shell whose RunOnUiAsync actually runs the action — the substitute's default swallows it,
    /// no-opping every UI-marshalled tool edit (set/replace/mode/save).</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private MarkdownViewModel Make(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return new MarkdownViewModel(path, RunningShell());
    }

    [TestMethod]
    [CoversNode("markdown-ai-context")]
    public void Context_ReportsModeSizeAndOutline()
    {
        var vm = Make("# Title\n\n## Section\n\nBody text here.\n");

        var ctx = vm.GetContext();
        StringAssert.Contains(ctx, vm.FileName);
        StringAssert.Contains(ctx, "rendered");     // default view mode named honestly
        StringAssert.Contains(ctx, "Title");        // outline surfaces the heading structure
        StringAssert.Contains(ctx, "Section");
        Assert.IsFalse(ctx.Contains("unsaved"), "a freshly-loaded document is clean");

        vm.SourceOnly = true;
        StringAssert.Contains(vm.GetContext(), "source");   // mode change reflected
    }

    [TestMethod]
    [CoversNode("markdown-ai-preview")]
    public void Scope_IsFilePath_AndOffersContextPreview()
    {
        var vm = Make("# Hi\n");

        // Aspect 4: two markdown tabs on different files stay distinguishable when pinned together.
        Assert.AreEqual(vm.FilePath, vm.GetSecurityContext());
        // The page advertises a read-only context preview for the conversation panel.
        Assert.IsInstanceOfType(vm, typeof(IContextPreview));
    }

    [TestMethod]
    [CoversNode("markdown-ai-act")]
    public async Task AiTools_ReadReplaceModeSetSave_ThroughToolSurface()
    {
        var vm = Make("# Draft\n\nalpha beta alpha\n");

        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "read_document", "set_view_mode", "set_document", "replace_all", "save_file" },
            tools.Select(t => t.Name).ToArray(),
            "the Markdown AI act tool surface changed — update the tree's markdown-ai-act leaves to match");

        // read_document: full source, reflects current content
        var read = tools.Single(t => t.Name == "read_document");
        var r = await read.InvokeAsync(new JsonObject(), CancellationToken.None);
        StringAssert.Contains(r.ModelText, "alpha beta alpha");

        // replace_all: whole-document find/replace (case-insensitive default), marks dirty
        var replace = tools.Single(t => t.Name == "replace_all");
        var rep = await replace.InvokeAsync(new JsonObject { ["find"] = "alpha", ["replace"] = "ALPHA" }, CancellationToken.None);
        Assert.IsFalse(rep.IsError);
        StringAssert.Contains(rep.Summary, "2");            // both "alpha"s replaced
        StringAssert.Contains(vm.Markdown, "ALPHA");
        Assert.IsTrue(vm.IsDirty);

        // set_view_mode: parity with the toolbar toggle
        var mode = tools.Single(t => t.Name == "set_view_mode");
        await mode.InvokeAsync(new JsonObject { ["mode"] = "source" }, CancellationToken.None);
        Assert.IsTrue(vm.SourceOnly);

        // set_document: replace the whole document (preserves the trailing newline via ToolArgs.Raw)
        var set = tools.Single(t => t.Name == "set_document");
        await set.InvokeAsync(new JsonObject { ["text"] = "# New\n" }, CancellationToken.None);
        Assert.AreEqual("# New\n", vm.Markdown);

        // save_file: persists to disk (synchronous save → deterministic) and clears dirty
        var save = tools.Single(t => t.Name == "save_file");
        var s = await save.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(s.IsError);
        Assert.AreEqual("saved", s.Summary);
        Assert.IsFalse(vm.IsDirty);
        StringAssert.Contains(File.ReadAllText(vm.FilePath), "# New");
    }
}
