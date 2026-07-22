using System.IO;
using LibGit2Sharp;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Git;
using Nexaflow.Features.Git.Services;
using Nexaflow.Features.Git.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Per-control unit tests for the Git viewlet — one method (or a small group) per leaf of the tree's
/// <c>git-viewlet</c> panel, driving the view-model rather than the UI. The integrated interaction is covered
/// once by <c>GitJourneyTests</c>; these are where the per-control assertions live.
/// </summary>
[TestClass]
public class GitViewletViewModelTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    private string InitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexagitvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        _temp.Add(dir);
        return dir;
    }

    private GitViewletViewModel Vm(string? dir = null, string gitManagerPath = "")
        => new(new GitOptions { GitManagerPath = gitManagerPath },
               Substitute.For<IShellServices>(), dir ?? InitRepo());

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);   // .git/objects are read-only
                Directory.Delete(dir, recursive: true);
            }
            catch { /* best-effort temp cleanup */ }
        }
    }

    // ── Fixture builders ──────────────────────────────────────────────────────

    private static GitStatus Status(
        string branch = "main", int staged = 0, int modified = 0, int untracked = 0,
        int? ahead = null, int? behind = null,
        string? hash = null, string? subject = null, DateTimeOffset? when = null,
        params string[] localBranches) =>
        new(branch, null, ahead, behind,
            [.. Enumerable.Range(0, staged).Select(i => new GitFileChange($"s{i}", "M"))],
            [.. Enumerable.Range(0, modified).Select(i => new GitFileChange($"m{i}", "M"))],
            [.. Enumerable.Range(0, untracked).Select(i => $"u{i}")],
            hash, subject, when,
            localBranches.Length > 0 ? localBranches : [branch]);

    private static GitWorktreeInfo Worktree(
        bool merged = true, bool hasUpstream = true, bool pushed = true, int aheadOfRemote = 0,
        int staged = 0, int modified = 0, bool broken = false, bool detached = false) =>
        new("feature-x", "feature-x", "feature/x", detached, hasUpstream ? "origin/feature/x" : null,
            hasUpstream, aheadOfRemote, pushed, "main", merged, staged, modified, broken);

    // ── Status Line (git-status-line) ─────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-status-line")]
    public void StatusLine_CleanTree_ShowsASingleGoodCleanSegment()
    {
        var vm = Vm();
        vm.ApplyStatus(Status());

        Assert.AreEqual(1, vm.StatusSegments.Count);
        Assert.AreEqual("clean", vm.StatusSegments[0].Text);
        Assert.AreEqual(GitTone.Good, vm.StatusSegments[0].Tone);
    }

    [TestMethod]
    [CoversNode("git-status-line")]
    public void StatusLine_CountsAreTonedBySeverity_StagedGood_ModifiedCaution()
    {
        var vm = Vm();
        vm.ApplyStatus(Status(staged: 2, modified: 3, untracked: 1));

        CollectionAssert.AreEqual(
            new[] { "2 staged", "3 modified", "1 untracked" },
            vm.StatusSegments.Select(s => s.Text).ToArray());
        CollectionAssert.AreEqual(
            new[] { GitTone.Good, GitTone.Caution, GitTone.Normal },
            vm.StatusSegments.Select(s => s.Tone).ToArray());
    }

    [TestMethod]
    [CoversNode("git-status-line")]
    public void StatusLine_AheadBehind_RenderAsMutedArrows_AndAreOmittedWhenInSync()
    {
        var vm = Vm();

        vm.ApplyStatus(Status(ahead: 2, behind: 1));
        var arrows = vm.StatusSegments.Single(s => s.Tone == GitTone.Muted);
        Assert.AreEqual("↑2 ↓1", arrows.Text);

        vm.ApplyStatus(Status(ahead: 0, behind: 0));
        Assert.IsFalse(vm.StatusSegments.Any(s => s.Tone == GitTone.Muted), "in sync — no arrows");
    }

    // ── Last Commit Line (git-last-commit) ────────────────────────────────────

    [TestMethod]
    [CoversNode("git-last-commit")]
    public void LastCommitLine_CombinesHashSubjectAndAge_AndIsEmptyWithNoCommit()
    {
        var vm = Vm();

        vm.ApplyStatus(Status(hash: "abc1234", subject: "Fix the thing", when: DateTimeOffset.Now.AddHours(-3)));
        StringAssert.Contains(vm.LastCommitLine, "abc1234");
        StringAssert.Contains(vm.LastCommitLine, "Fix the thing");
        StringAssert.Contains(vm.LastCommitLine, "3h ago");

        vm.ApplyStatus(Status());
        Assert.AreEqual(string.Empty, vm.LastCommitLine, "a repo with no commits shows nothing");
    }

    [TestMethod]
    [CoversNode("git-last-commit")]
    public void FormatTimeAgo_StepsThroughEachUnit()
    {
        var now = DateTimeOffset.Now;
        Assert.AreEqual("just now", GitViewletViewModel.FormatTimeAgo(now.AddSeconds(-5)));
        Assert.AreEqual("5m ago",   GitViewletViewModel.FormatTimeAgo(now.AddMinutes(-5)));
        Assert.AreEqual("2h ago",   GitViewletViewModel.FormatTimeAgo(now.AddHours(-2)));
        Assert.AreEqual("3d ago",   GitViewletViewModel.FormatTimeAgo(now.AddDays(-3)));
        Assert.AreEqual("2mo ago",  GitViewletViewModel.FormatTimeAgo(now.AddDays(-70)));
        Assert.AreEqual("2y ago",   GitViewletViewModel.FormatTimeAgo(now.AddDays(-800)));
    }

    // ── Branch Picker (git-branch-picker) ─────────────────────────────────────

    [TestMethod]
    [CoversNode("git-branch-picker")]
    public void BranchPicker_IsFedByTheStatus_AndNamesTheCurrentBranch()
    {
        var vm = Vm();
        vm.ApplyStatus(Status(branch: "main", localBranches: ["main", "dev", "release"]));

        Assert.AreEqual("main", vm.BranchName);
        CollectionAssert.AreEqual(new[] { "main", "dev", "release" }, vm.LocalBranches.ToArray());
    }

    [TestMethod]
    [CoversNode("git-branch-picker")]
    public void SwitchBranch_IsRefusedForTheCurrentBranch_AndWhileBusy()
    {
        var vm = Vm();
        vm.ApplyStatus(Status(branch: "main", localBranches: ["main", "dev"]));

        Assert.IsFalse(vm.SwitchBranchCommand.CanExecute("main"), "switching to the branch you're on is a no-op");
        Assert.IsTrue(vm.SwitchBranchCommand.CanExecute("dev"));

        vm.IsBusy = true;
        Assert.IsFalse(vm.SwitchBranchCommand.CanExecute("dev"), "no switching mid-operation");
        Assert.IsFalse(vm.IsInteractive);
    }

    // ── Pull Button (git-pull) ────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-pull")]
    public void Pull_IsDisabledWhileBusy_AndTheCaptionReportsProgress()
    {
        var vm = Vm();
        Assert.IsTrue(vm.PullCommand.CanExecute(null));
        Assert.AreEqual("Pull", vm.PullCaption);

        vm.IsBusy = true;
        Assert.IsFalse(vm.PullCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("git-pull")]
    public void IsAuthFailure_RecognisesTheFailuresWorthRetryingWithAToken()
    {
        Assert.IsTrue(GitViewletViewModel.IsAuthFailure(new Exception("too many redirects or authentication replays")));
        Assert.IsTrue(GitViewletViewModel.IsAuthFailure(new Exception("request failed with status code: 401")));
        Assert.IsTrue(GitViewletViewModel.IsAuthFailure(new Exception("no credential store")));
        Assert.IsFalse(GitViewletViewModel.IsAuthFailure(new Exception("cannot fast-forward, branch has diverged")));
    }

    // ── Pull Result line (git-pull-result) ────────────────────────────────────

    [TestMethod]
    [CoversNode("git-pull-result")]
    public void ActionResult_CarriesTheMessageAndWhetherItFailed()
    {
        var vm = Vm();
        Assert.AreEqual(string.Empty, vm.ActionResult, "nothing is shown until an action reports");

        vm.SetActionResult("Already up to date", success: true);
        Assert.AreEqual("Already up to date", vm.ActionResult);
        Assert.IsFalse(vm.ActionResultIsError);

        vm.SetActionResult("Pull failed: nope", success: false);
        Assert.IsTrue(vm.ActionResultIsError);
    }

    // ── Open in Git Manager (git-open-manager) ────────────────────────────────

    [TestMethod]
    [CoversNode("git-open-manager")]
    public void OpenGitManager_IsHiddenUntilAnApplicationIsConfigured()
    {
        Assert.IsFalse(Vm().ShowGitManager, "no configured git GUI — the button isn't offered");
        Assert.IsTrue(Vm(gitManagerPath: @"C:\Tools\gitui.exe").ShowGitManager);
    }

    [TestMethod]
    [CoversNode("git-open-manager")]
    public void OpenGitManager_WithNoConfiguredPath_IsASafeNoOp()
    {
        var vm = Vm();
        vm.OpenGitManagerCommand.Execute(null);   // must not throw — an unset path is simply nothing to launch
    }

    // ── Worktree badge (git-worktree-badge) ───────────────────────────────────

    [TestMethod]
    [CoversNode("git-worktree-badge")]
    public void WorktreeBadge_IsHiddenForAMainCheckout_AndShownForALinkedWorktree()
    {
        var vm = Vm();

        vm.ApplyStatus(Status());
        vm.ApplyWorktree(null);
        Assert.IsFalse(vm.IsWorktree);
        Assert.AreEqual(string.Empty, vm.WorktreeTooltip);

        vm.ApplyWorktree(Worktree());
        Assert.IsTrue(vm.IsWorktree);
        StringAssert.Contains(vm.WorktreeTooltip, "Linked git worktree");
    }

    [TestMethod]
    [CoversNode("git-worktree-badge")]
    public void WorktreeState_IsAppendedToTheStatusLine_MergedAndPushedReadAsGood()
    {
        var vm = Vm();
        vm.ApplyStatus(Status());
        vm.ApplyWorktree(Worktree(merged: true, pushed: true));

        var texts = vm.StatusSegments.Select(s => s.Text).ToArray();
        CollectionAssert.Contains(texts, "merged into main");
        CollectionAssert.Contains(texts, "pushed");
        Assert.AreEqual(GitTone.Good, vm.StatusSegments.Single(s => s.Text == "merged into main").Tone);
    }

    [TestMethod]
    [CoversNode("git-worktree-badge")]
    public void WorktreeState_UnmergedAndUnpushed_ReadAsCaution()
    {
        var vm = Vm();
        vm.ApplyStatus(Status());
        vm.ApplyWorktree(Worktree(merged: false, pushed: false, aheadOfRemote: 3));

        Assert.AreEqual(GitTone.Caution, vm.StatusSegments.Single(s => s.Text == "unmerged vs main").Tone);
        Assert.AreEqual(GitTone.Caution, vm.StatusSegments.Single(s => s.Text == "unpushed ↑3").Tone);
    }

    [TestMethod]
    [CoversNode("git-worktree-badge")]
    public void BrokenRemnant_ReadsAsBad_AndSaysHowToCleanItUp()
    {
        var vm = Vm();
        vm.ApplyStatus(Status());
        vm.ApplyWorktree(Worktree(broken: true));

        var segment = vm.StatusSegments.Single(s => s.Tone == GitTone.Bad);
        StringAssert.Contains(segment.Text, "broken remnant");
        StringAssert.Contains(vm.WorktreeTooltip, "dangling");
    }

    // ── Remove worktree (git-worktree-remove) ─────────────────────────────────

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public void RemoveWorktree_IsOfferedOnlyForAWorktree()
    {
        var vm = Vm();
        Assert.IsFalse(vm.RemoveWorktreeCommand.CanExecute(null), "a main checkout has nothing to remove");

        vm.ApplyWorktree(Worktree());
        Assert.IsTrue(vm.RemoveWorktreeCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public void RemovalPrompt_SpellsOutEverythingThatWouldBeLost()
    {
        var prompt = GitViewletViewModel.BuildRemovalPrompt(
            Worktree(merged: false, hasUpstream: true, pushed: false, aheadOfRemote: 2, staged: 1, modified: 3));

        StringAssert.Contains(prompt, "not merged into main");
        StringAssert.Contains(prompt, "2 commit(s) are not on the remote");
        StringAssert.Contains(prompt, "4 uncommitted change(s)");
        StringAssert.Contains(prompt, "the branch 'feature/x'");
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public void RemovalPrompt_NeverPushed_AndDetached_AreCalledOutDistinctly()
    {
        var neverPushed = GitViewletViewModel.BuildRemovalPrompt(Worktree(merged: false, hasUpstream: false));
        StringAssert.Contains(neverPushed, "never been pushed");

        var detached = GitViewletViewModel.BuildRemovalPrompt(Worktree(detached: true));
        StringAssert.Contains(detached, "This deletes the folder.");
        Assert.IsFalse(detached.Contains("and the branch"), "there is no branch to delete when detached");
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public void RemovalPrompt_ForABrokenRemnant_ExplainsTheDanglingLink()
    {
        var prompt = GitViewletViewModel.BuildRemovalPrompt(Worktree(broken: true));
        StringAssert.Contains(prompt, "dangling");
        StringAssert.Contains(prompt, "leftover folder");
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public void AMergedCleanWorktree_RemovesWithoutConfirmation()
    {
        Assert.IsTrue(Worktree(merged: true, staged: 0, modified: 0).CanRemoveWithoutConfirmation);
        Assert.IsFalse(Worktree(merged: false).CanRemoveWithoutConfirmation);
        Assert.IsFalse(Worktree(merged: true, modified: 1).CanRemoveWithoutConfirmation);
        Assert.IsFalse(Worktree(broken: true).CanRemoveWithoutConfirmation);
    }

    // ── AI surface (git-ai-context / git-ai-act) ──────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act")]
    public void GetClientTools_ExposesTheEightReadOnlyGitTools()
    {
        // Read-only by design: the user can pull / switch branch / remove a worktree, the AI cannot.
        // If this set changes, update the git-ai-act-* leaves in the product tree to match.
        CollectionAssert.AreEquivalent(
            new[] { "git_status", "git_log", "git_diff", "git_branches", "git_show", "git_remotes",
                    "git_tags", "git_file_at" },
            Vm().GetClientTools().Select(t => t.Name).ToArray());
    }

    [TestMethod]
    [CoversNode("git-ai-act")]
    public void GetClientTools_AreAllReadOnly()
    {
        // The whole surface is SafeOperation — a mutating git tool would be a deliberate, separately-gated
        // addition, so a stray one appearing here should fail loudly rather than ship quietly.
        foreach (var tool in Vm().GetClientTools())
            Assert.AreEqual(ToolSafety.SafeOperation, tool.Safety, $"{tool.Name} must stay read-only");
    }

    [TestMethod]
    [CoversNode("git-ai-context")]
    public void GetContext_OnAFreshRepo_DescribesTheBranch()
    {
        var vm = Vm();
        StringAssert.Contains(vm.GetContext() ?? "", "master", "a freshly-init'd repo reports its default branch");
    }

    [TestMethod]
    [CoversNode("git-ai-context")]
    public void GetContext_OnANonRepo_IsNullRatherThanThrowing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexagitvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _temp.Add(dir);

        // One un-openable folder must not break the whole file-browser AI context.
        Assert.IsNull(Vm(dir).GetContext());
    }
}
