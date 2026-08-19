using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// The git viewlet's honest AI-context line (<c>git-ai-context</c>). GitContextFormatter renders it from a
/// <see cref="GitStatus"/> (+ optional worktree), so these assert the wording against constructed records —
/// no repository, no UI control. The status-reading itself is covered by <see cref="GitServiceTests"/>.
/// </summary>
[TestClass]
[CoversNode("git-ai-context")]
public class GitContextFormatterTests
{
    private static GitStatus Status(
        string branch = "main",
        string? upstream = null,
        int? ahead = null,
        int? behind = null,
        int staged = 0,
        int modified = 0,
        int untracked = 0,
        string? lastHash = null,
        string? lastSubject = null)
        => new(
            Branch:            branch,
            Upstream:          upstream,
            Ahead:             ahead,
            Behind:            behind,
            Staged:            Enumerable.Range(0, staged).Select(i => new GitFileChange($"s{i}.cs", "modified")).ToList(),
            Modified:          Enumerable.Range(0, modified).Select(i => new GitFileChange($"m{i}.cs", "modified")).ToList(),
            Untracked:         Enumerable.Range(0, untracked).Select(i => $"u{i}.cs").ToList(),
            LastCommitHash:    lastHash,
            LastCommitSubject: lastSubject,
            LastCommitWhen:    lastHash is null ? null : DateTimeOffset.Now,
            LocalBranches:     [branch]);

    private static GitWorktreeInfo Worktree(
        bool hasUpstream = false,
        int aheadOfRemote = 0,
        bool isPushed = false,
        string? mergeTarget = null,
        bool isMerged = false,
        bool isBroken = false)
        => new(
            DisplayName:       "wt",
            WorktreeName:      "wt",
            Branch:            "feature",
            IsDetached:        false,
            Upstream:          hasUpstream ? "origin/feature" : null,
            HasUpstream:       hasUpstream,
            AheadOfRemote:     aheadOfRemote,
            IsPushed:          isPushed,
            MergeTargetBranch: mergeTarget,
            IsMerged:          isMerged,
            StagedCount:       0,
            ModifiedCount:     0,
            IsBroken:          isBroken);

    [TestMethod]
    public void Clean_tree_no_upstream()
        => Assert.AreEqual("Git: on 'main'. Working tree clean.",
            GitContextFormatter.Describe(Status(), worktree: null));

    [TestMethod]
    public void Ahead_and_behind_render_against_upstream()
    {
        var line = GitContextFormatter.Describe(
            Status(branch: "feature", upstream: "origin/feature", ahead: 2, behind: 1), worktree: null);
        StringAssert.Contains(line, "2↑");
        StringAssert.Contains(line, "1↓");
        StringAssert.Contains(line, "vs origin/feature");
    }

    [TestMethod]
    public void Ahead_behind_omitted_when_in_sync_with_upstream()
        => Assert.AreEqual("Git: on 'main'. Working tree clean.",
            GitContextFormatter.Describe(Status(upstream: "origin/main", ahead: 0, behind: 0), worktree: null));

    [TestMethod]
    public void Working_tree_changes_are_summarised()
    {
        var line = GitContextFormatter.Describe(Status(staged: 3, modified: 2, untracked: 1), worktree: null);
        StringAssert.Contains(line, "3 staged, 2 modified, 1 untracked.");
        Assert.IsFalse(line.Contains("Working tree clean"));
    }

    [TestMethod]
    public void Last_commit_is_appended()
    {
        var line = GitContextFormatter.Describe(Status(lastHash: "abc1234", lastSubject: "Fix the thing"), worktree: null);
        StringAssert.Contains(line, "Last commit abc1234 \"Fix the thing\".");
    }

    [TestMethod]
    public void Worktree_merged_and_pushed()
    {
        var line = GitContextFormatter.Describe(
            Status(), Worktree(hasUpstream: true, isPushed: true, mergeTarget: "main", isMerged: true));
        StringAssert.Contains(line, "This is a linked worktree, merged into main, pushed to its remote.");
    }

    [TestMethod]
    public void Worktree_unmerged_and_never_pushed()
    {
        var line = GitContextFormatter.Describe(
            Status(), Worktree(hasUpstream: false, mergeTarget: "main", isMerged: false));
        StringAssert.Contains(line, "not yet merged into main");
        StringAssert.Contains(line, "never pushed.");
    }

    [TestMethod]
    public void Worktree_with_unpushed_commits()
    {
        var line = GitContextFormatter.Describe(
            Status(), Worktree(hasUpstream: true, aheadOfRemote: 3, isPushed: false));
        StringAssert.Contains(line, "3 commit(s) unpushed.");
    }

    [TestMethod]
    public void Broken_worktree_remnant()
    {
        var line = GitContextFormatter.Describe(Status(), Worktree(isBroken: true));
        StringAssert.Contains(line, "broken worktree remnant (dangling .git link).");
    }
}
