using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// Covers the Tabular AI-integration surface: an enriched get_context (file, delimiter, columns,
/// row count, active-filter state, visible row range), the file-scoped security context, and the
/// client-tool surface driven exactly as the conversation hub drives it — read_rows returns real
/// cell content, while scroll_to_top / scroll_to_end / open_filter move the view and are asserted
/// on VM state (any UI-marshalled mutation runs inline via <see cref="RunningShell"/>).
/// </summary>
[TestClass]
public class TabularAiTests
{
    /// <summary>A shell whose RunOnUiAsync actually runs the action — the substitute's default
    /// swallows it, no-opping every UI-marshalled mutation.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static string WriteCsv(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexatab_ai_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "people.csv");
        File.WriteAllText(csv, "name,age,city\nAlice,30,London\nBob,25,Paris\nCarol,41,Berlin\n");
        return csv;
    }

    [TestMethod]
    [CoversNode("tabular-ai-act")]
    [CoversNode("tabular-ai-context")]
    public async Task AiSurface_ContextScopeAndTools()
    {
        var csv = WriteCsv(out var dir);
        TabularViewModel? vm = null;
        try
        {
            vm = new TabularViewModel(csv, RunningShell(), Substitute.For<IAIService>(),
                                      new TabularTemplatesConfig());
            await vm.Ready;

            // ── security scope is the file path (distinguishes two tabular tabs pinned together) ──
            Assert.AreEqual(csv, vm.GetSecurityContext());

            // ── enriched context: file, delimiter, columns, filter state, visible range, read pointer ──
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, vm.FileName);
            StringAssert.Contains(ctx, "comma");             // detected delimiter, named
            StringAssert.Contains(ctx, "3 columns");         // column count
            StringAssert.Contains(ctx, "name");              // column headers surfaced
            StringAssert.Contains(ctx, "No column filter");  // current filter state
            StringAssert.Contains(ctx, "Visible rows");      // currently loaded window range
            StringAssert.Contains(ctx, "read_rows");         // points the model at the read tool

            // ── the exact tool surface (a change here should force a tree reconcile) ──
            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "read_rows", "open_filter", "scroll_to_top", "scroll_to_end" },
                tools.Select(t => t.Name).ToArray(),
                "the Tabular AI act tool surface changed — update the tree's tabular-ai-act leaves to match");

            // ── read_rows returns the real cell values (the data the view-move tools can't reach) ──
            var read = tools.Single(t => t.Name == "read_rows");
            var r = await read.InvokeAsync(new JsonObject { ["start"] = 1, ["count"] = 10 }, CancellationToken.None);
            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "Alice");
            StringAssert.Contains(r.ModelText, "London");
            StringAssert.Contains(r.ModelText, "Carol");
            StringAssert.Contains(r.ModelText, "Berlin");

            // ── open_filter selects a column → the filter side panel opens ──
            var openFilter = tools.Single(t => t.Name == "open_filter");
            var of = await openFilter.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(of.IsError);
            Assert.IsTrue(vm.IsFilterPanelOpen, "open_filter should select a column and open the panel");

            // ── scroll_to_end anchors the focal row on the last row ──
            var toEnd = tools.Single(t => t.Name == "scroll_to_end");
            await toEnd.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsTrue(vm.KnownRowCount.HasValue, "a small file's row count is known immediately");
            Assert.AreEqual(vm.KnownRowCount!.Value - 1, vm.FocalRow, "scroll_to_end anchors on the last row");

            // ── scroll_to_top returns the focal row to the first row ──
            var toTop = tools.Single(t => t.Name == "scroll_to_top");
            await toTop.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.AreEqual(0, vm.FocalRow, "scroll_to_top anchors on the first row");

            // Flush any fire-and-forget window read the scroll tools kicked off before teardown.
            await vm.RefreshWindowAsync();
        }
        finally
        {
            vm?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
