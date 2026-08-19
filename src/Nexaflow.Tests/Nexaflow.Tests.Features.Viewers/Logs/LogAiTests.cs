using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// Covers the Logs AI-integration surface: an honest <c>GetContext</c> (line count, detected timestamp
/// span, active filters/highlights), the file-scoped security context, and the client-tool surface —
/// reading the tail/lines/search results the user sees, plus the view-state tools (regex/time filter,
/// level highlight, pause/resume tail) that mirror the toolbar. Runs under <see cref="AsyncPump"/> because
/// the content lives in a thread-affine AvalonEdit <c>TextDocument</c>; the tools marshal their document
/// access through <see cref="IShellServices.RunOnUiAsync{T}"/>, which the running shell here executes inline.
/// </summary>
[TestClass]
public class LogAiTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logai_{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>A shell whose RunOnUiAsync overloads actually run the delegate — the default substitute
    /// swallows them, no-opping every UI-marshalled tool (the reads and the filter/highlight/pause writes).</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<ToolResult>>>())
             .Returns(ci => ci.Arg<Func<Task<ToolResult>>>()());
        return shell;
    }

    [TestMethod]
    [CoversNode("log-viewer-ai-act")]
    [CoversNode("log-viewer-ai-context")]
    public void AiSurface_HonestContext_ReadsAndMirrorsToolbar() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp(
            "2024-01-01 00:00:00 INFO service starting\n" +
            "2024-01-01 00:00:01 WARN low disk space\n" +
            "2024-01-01 00:00:02 ERROR connection refused\n" +
            "2024-01-01 00:00:03 INFO retry scheduled\n");
        try
        {
            using var vm = new LogViewModel(path, RunningShell()) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            // ── Scope: file-path security boundary keeps two log tabs distinguishable when pinned ──
            Assert.AreEqual(path, vm.GetSecurityContext());

            // ── Context is honest about the file, count, detected timestamp span, and no active filters ──
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, vm.FileName);
            StringAssert.Contains(ctx, "2024-01-01");        // detected timestamp range surfaced
            StringAssert.Contains(ctx, "read_tail");         // points the model at the read tools
            Assert.IsFalse(ctx.Contains("regex filter"), "no filter should be active on a fresh load");

            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                // No search tool of its own: the page is ISearchable, so the shell attaches
                // search_page / show_search_results. Those are Core's to test, not this surface's.
                new[]
                {
                    "read_tail", "read_lines", "read_selected_lines",
                    "set_regex_filter", "set_time_filter", "highlight_levels", "set_tail_paused",
                },
                tools.Select(t => t.Name).ToArray(),
                "the Logs AI act tool surface changed — update the tree's log-viewer-ai-act leaves to match");

            // ── read_tail: the most recent lines only ──
            var readTail = tools.Single(t => t.Name == "read_tail");
            var tail = await readTail.InvokeAsync(new JsonObject { ["lines"] = 2 }, CancellationToken.None);
            Assert.IsFalse(tail.IsError);
            StringAssert.Contains(tail.ModelText, "retry scheduled");
            Assert.IsFalse(tail.ModelText.Contains("service starting"), "only the last lines were requested");

            // ── read_lines: a specific 1-based range ──
            var readLines = tools.Single(t => t.Name == "read_lines");
            var range = await readLines.InvokeAsync(new JsonObject { ["start"] = 1, ["count"] = 1 }, CancellationToken.None);
            Assert.IsFalse(range.IsError);
            StringAssert.Contains(range.ModelText, "service starting");

            // ── read_selected_lines: honest when there is no selection ──
            var readSel = tools.Single(t => t.Name == "read_selected_lines");
            var sel = await readSel.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(sel.IsError);
            StringAssert.Contains(sel.Summary, "no selection");

            // ── set_regex_filter: mirrors the filter box (UI-marshalled) and context reflects it ──
            var setFilter = tools.Single(t => t.Name == "set_regex_filter");
            var applied = await setFilter.InvokeAsync(new JsonObject { ["pattern"] = "WARN|ERROR" }, CancellationToken.None);
            Assert.IsFalse(applied.IsError);
            Assert.IsTrue(vm.IsFilterActive);
            Assert.AreEqual("WARN|ERROR", vm.FilterRegex);
            StringAssert.Contains(vm.GetContext(), "regex filter");

            var cleared = await setFilter.InvokeAsync(new JsonObject { ["pattern"] = "" }, CancellationToken.None);
            Assert.IsFalse(cleared.IsError);
            Assert.IsFalse(vm.IsFilterActive);

            // ── set_time_filter: computes the bounds the colorizer reads ──
            var setTime = tools.Single(t => t.Name == "set_time_filter");
            var timed = await setTime.InvokeAsync(
                new JsonObject { ["start"] = "2024-01-01T00:00:01", ["end"] = "2024-01-01T00:00:02" },
                CancellationToken.None);
            Assert.IsFalse(timed.IsError);
            Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 1), vm.FilterStart);
            Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 2), vm.FilterEnd);

            // ── highlight_levels: toggles exactly the requested level flags ──
            var highlight = tools.Single(t => t.Name == "highlight_levels");
            var hl = await highlight.InvokeAsync(new JsonObject { ["levels"] = "error,warning" }, CancellationToken.None);
            Assert.IsFalse(hl.IsError);
            Assert.IsTrue(vm.HighlightError);
            Assert.IsTrue(vm.HighlightWarning);
            Assert.IsFalse(vm.HighlightInfo);

            // ── set_tail_paused: pause then resume the live tail ──
            var pause = tools.Single(t => t.Name == "set_tail_paused");
            await pause.InvokeAsync(new JsonObject { ["paused"] = true }, CancellationToken.None);
            Assert.IsTrue(vm.IsPaused);
            await pause.InvokeAsync(new JsonObject { ["paused"] = false }, CancellationToken.None);
            Assert.IsFalse(vm.IsPaused);
        }
        finally { File.Delete(path); }
    });
}
