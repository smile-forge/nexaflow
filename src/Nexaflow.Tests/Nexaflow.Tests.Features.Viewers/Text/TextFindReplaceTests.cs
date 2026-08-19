using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// The Notepad-parity editor affordances layered on top of the existing search engine: the Find &amp; Replace
/// bar (open/close, match-case, regex, replace), Go-to-line, editor zoom, and the undo-stack reset that keeps
/// Ctrl+Z from reverting programmatic composition. Cases that touch the AvalonEdit <c>TextDocument</c> run
/// under <see cref="AsyncPump"/>; the two that mutate a live find field drain the ~250 ms live-search debounce
/// before the pump completes so no continuation is posted after it closes.
/// </summary>
[TestClass]
public class TextFindReplaceTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"textfr_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>A shell whose RunOnUiAsync runs the delegate inline — the substitute default swallows it, which
    /// would no-op the UI-marshalled replace path.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static TextViewModel Make(IShellServices? shell = null)
        => new("nonexistent.txt", shell ?? Substitute.For<IShellServices>()) { IsMonitoring = false };

    private static TextViewModel Make(string path, IShellServices? shell = null)
        => new(path, shell ?? Substitute.For<IShellServices>()) { IsMonitoring = false };

    // ── Find bar open / close ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-searchbar")]
    public void OpenFind_And_OpenReplace_ToggleBarState()
    {
        using var vm = Make();
        Assert.IsFalse(vm.IsFindBarOpen);

        vm.OpenFindCommand.Execute(null);
        Assert.IsTrue(vm.IsFindBarOpen);
        Assert.IsFalse(vm.IsReplaceVisible, "Ctrl+F opens the bar in find-only mode");

        vm.OpenReplaceCommand.Execute(null);
        Assert.IsTrue(vm.IsFindBarOpen);
        Assert.IsTrue(vm.IsReplaceVisible, "Ctrl+H reveals the replace row");

        vm.CloseFindBarCommand.Execute(null);
        Assert.IsFalse(vm.IsFindBarOpen);
        Assert.IsFalse(vm.IsReplaceVisible);
        Assert.IsFalse(vm.IsSearchActive, "closing the bar clears the active search");
    }

    [TestMethod]
    [CoversNode("text-viewer-searchbar")]
    public void ToggleFind_OpensThenCloses()
    {
        using var vm = Make();
        Assert.IsFalse(vm.IsFindBarOpen);

        vm.ToggleFindCommand.Execute(null);
        Assert.IsTrue(vm.IsFindBarOpen, "the toolbar Find button opens the bar");

        vm.ToggleFindCommand.Execute(null);
        Assert.IsFalse(vm.IsFindBarOpen, "clicking Find again closes the bar");
    }

    // ── Match case ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-match-case")]
    public void MatchCase_MakesSearchCaseSensitive() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("Foo\nfoo\nFOO\nbar");
        try
        {
            using var vm = Make(path);
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("foo");
            Assert.AreEqual(3, vm.SearchMatchCount, "case-insensitive by default matches all three");

            vm.MatchCase = true;
            await vm.SearchConventionalAsync("foo");
            Assert.AreEqual(1, vm.SearchMatchCount, "match-case narrows to the exact-case line");

            await Task.Delay(300); // drain the live-search debounce the MatchCase change scheduled
        }
        finally { File.Delete(path); }
    });

    // ── Regex ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-regex")]
    public void RegexSearch_Matches_AndSurfacesInBar() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("foo\nfooo\nbar\nfoooo");
        try
        {
            using var vm = Make(path);
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchRegexAsync("fo{3,}");   // 3+ o's → "fooo", "foooo"
            Assert.AreEqual(2, vm.SearchMatchCount);
            Assert.IsTrue(vm.IsFindBarOpen, "running a search opens the bar");
            Assert.IsTrue(vm.UseRegex, "the bar reflects that this was a regex search");
            Assert.AreEqual("fo{3,}", vm.FindText, "the bar's find field shows the pattern");
        }
        finally { File.Delete(path); }
    });

    // ── Replace ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-replace")]
    public void ReplaceAll_ReplacesEveryMatch_AutoEntersEditing() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("foo\nbar\nfoo\nfoo");
        try
        {
            using var vm = Make(path, RunningShell());
            await vm.LoadAsync(CancellationToken.None);

            vm.FindText    = "foo";
            vm.ReplaceText = "X";
            await vm.ReplaceAllCommand.ExecuteAsync(null);

            Assert.IsTrue(vm.IsEditing, "Replace All auto-enters editing on an editable file");
            Assert.IsTrue(vm.IsDirty);
            Assert.AreEqual("X\nbar\nX\nX", vm.Document.Text);

            await Task.Delay(300); // drain the live-search debounce the FindText set scheduled
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    [CoversNode("text-viewer-replace")]
    public void ReplaceCurrent_ReplacesOnlyTheCurrentMatch() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("foo\nbar\nfoo");
        try
        {
            using var vm = Make(path);
            await vm.LoadAsync(CancellationToken.None);

            await vm.SearchConventionalAsync("foo");   // matches lines 1 and 3; current = line 1
            Assert.AreEqual(2, vm.SearchMatchCount);

            vm.ReplaceText = "X";
            await vm.ReplaceCurrentCommand.ExecuteAsync(null);

            Assert.AreEqual("X\nbar\nfoo", vm.Document.Text, "only the current match is replaced");
            Assert.IsTrue(vm.IsDirty);
            Assert.AreEqual(1, vm.SearchMatchCount, "the remaining-match count updates");
        }
        finally { File.Delete(path); }
    });

    // ── Go to line ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-goto-line")]
    public void GoToLine_ScrollsToRequestedLine_AndRejectsBadInput() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("one\ntwo\nthree\nfour\nfive");
        try
        {
            var shell = Substitute.For<IShellServices>();
            Action<string>? confirm = null;
            shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                         Arg.Any<Action<string>>(), Arg.Any<Action>()))
                 .Do(ci => confirm = ci.Arg<Action<string>>());

            using var vm = new TextViewModel(path, shell) { IsMonitoring = false };
            await vm.LoadAsync(CancellationToken.None);

            vm.GoToLineCommand.Execute(null);
            Assert.IsNotNull(confirm, "Go to line opens a shell text prompt");

            confirm!("3");
            Assert.AreEqual(3, vm.Document.GetLineByOffset(vm.ScrollToOffset).LineNumber,
                "the requested line is scrolled into view");

            confirm!("not-a-number");
            shell.Received().ShowError(Arg.Any<string>());
        }
        finally { File.Delete(path); }
    });

    // ── Zoom ────────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-zoom")]
    public void Zoom_StepsAndClampsWithinBounds()
    {
        using var vm = Make();
        Assert.AreEqual(100, vm.ZoomPercent);
        CollectionAssert.Contains(vm.ZoomPresets.ToArray(), 130, "130% is an offered preset");

        vm.ZoomInCommand.Execute(null);
        Assert.AreEqual(110, vm.ZoomPercent);

        vm.ResetZoomCommand.Execute(null);
        Assert.AreEqual(100, vm.ZoomPercent);

        vm.ZoomPercent = 1000;  // clamps to the ceiling
        Assert.AreEqual(400, vm.ZoomPercent);

        vm.ZoomPercent = 5;     // clamps to the floor
        Assert.AreEqual(50, vm.ZoomPercent);

        for (var i = 0; i < 100; i++) vm.ZoomOutCommand.Execute(null);
        Assert.AreEqual(50, vm.ZoomPercent, "Zoom Out never drops below the floor");
    }

    // ── Undo / redo ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("text-viewer-undo-redo")]
    public void Undo_IsClearedAfterLoad_ThenTracksUserEdits() => AsyncPump.Run(async () =>
    {
        var path = WriteTemp("alpha\nbeta");
        try
        {
            using var vm = Make(path);
            await vm.LoadAsync(CancellationToken.None);
            Assert.IsFalse(vm.Document.UndoStack.CanUndo, "the initial load is the baseline, not an undoable edit");

            vm.IsEditing = true;
            vm.Document.Insert(0, "X");
            Assert.IsTrue(vm.Document.UndoStack.CanUndo, "a user edit is undoable");

            vm.Document.UndoStack.Undo();
            Assert.AreEqual("alpha\nbeta", vm.Document.Text, "undo reverts the edit, not the load composition");
            Assert.IsTrue(vm.Document.UndoStack.CanRedo, "redo is now available");
        }
        finally { File.Delete(path); }
    });
}
