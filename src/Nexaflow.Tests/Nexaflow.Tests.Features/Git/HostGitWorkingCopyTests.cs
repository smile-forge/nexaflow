using LibGit2Sharp;
using Nexaflow.Features.Git.Services;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// A Git viewlet's working copy on a location, against an in-memory location whose host answers git: every question and
/// every change is a git run on the host, in the folder as the host names it, and what git does not change itself when a
/// worktree is removed goes through the location.
/// </summary>
[TestClass]
[CoversNode("git-viewlet")]
public sealed class HostGitWorkingCopyTests
{
    private HostedLocation _box = null!;

    [TestInitialize]
    public void Setup() => _box = new HostedLocation();

    [TestCleanup]
    public async Task Cleanup() => await _box.DisposeAsync();

    private HostGitWorkingCopy Copy(string hostFolder)
    {
        var folder = HostedLocation.PathOf(hostFolder);
        return new HostGitWorkingCopy(_box.Files.GetProgramHost(folder)!, _box.Files, folder);
    }

    private const string Status = "status --porcelain=v2 --branch -z --untracked-files=normal --ignore-submodules=all";
    private const string Tip    = "log -1 --format=%H%x00%s%x00%aI";
    private const string Locals = "for-each-ref --format=%(refname:short) refs/heads/";

    [TestMethod]
    [CoversNode("git-status-line")]
    public async Task TheStatus_IsWhatTheHostsGitSays_ReadWithoutLocksInTheFolderAsTheHostNamesIt()
    {
        _box.Folder("/home/adam/my repo");
        _box.Answer("git", Status, 0, "# branch.head main\0# branch.upstream origin/main\0# branch.ab +1 -0\0"
                                    + "1 .M N... 100755 100755 100755 a a run.sh\0? new.txt\0");
        _box.Answer("git", Tip, 0, "1234567890abcdef1234567890abcdef12345678\0Ship it\02026-11-01T10:00:00Z\n");
        _box.Answer("git", Locals, 0, "main\nfeature\n");

        var s = await Copy("/home/adam/my repo").GetStatusAsync(CancellationToken.None);

        Assert.AreEqual("main", s.Branch);
        Assert.AreEqual(1, s.Ahead);
        Assert.AreEqual(1, s.ModifiedCount);
        Assert.AreEqual(1, s.UntrackedCount);
        Assert.AreEqual("1234567", s.LastCommitHash);
        CollectionAssert.AreEqual(new[] { "feature", "main" }, s.LocalBranches.ToArray());
        foreach (var run in _box.Runs)
        {
            Assert.AreEqual("/home/adam/my repo", run.WorkingDirectory);
            Assert.AreEqual("0", run.Environment!["GIT_OPTIONAL_LOCKS"], "a read takes no lock a person's own git may wait on");
        }
    }

    [TestMethod]
    public async Task AFolderGitCannotOpen_IsNotARepository_AsTheInProcessStatusSaysIt()
    {
        _box.Folder("/src/remnant");
        _box.Answer("git", Status, 128, errors: "fatal: not a git repository (or any of the parent directories): .git\n");
        _box.Answer("git", Tip, 128);
        _box.Answer("git", Locals, 128);

        await Assert.ThrowsExactlyAsync<RepositoryNotFoundException>(() => Copy("/src/remnant").GetStatusAsync(CancellationToken.None));
    }

    [TestMethod]
    [CoversNode("git-branch-picker")]
    public async Task SwitchingBranch_ChecksItOutOnTheHost_AndARefusalIsThrown()
    {
        _box.Folder("/src/repo");
        _box.Answer("git", "checkout feature --", 0);
        _box.Answer("git", "checkout dirty --", 1, errors: "error: Your local changes would be overwritten by checkout.\nAborting\n");

        await Copy("/src/repo").SwitchBranchAsync("feature", CancellationToken.None);
        var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Copy("/src/repo").SwitchBranchAsync("dirty", CancellationToken.None));

        Assert.AreEqual("Aborting", refused.Message);
        Assert.AreEqual("0", _box.Runs.First().Environment!["GIT_TERMINAL_PROMPT"], "git never waits on a question nobody sees");
    }

    [TestMethod]
    [CoversNode("git-pull")]
    public async Task APull_IsUpToDate_OrComplete_ByWhetherHeadMoved_AndAFailureSaysWhatGitSaid()
    {
        _box.Folder("/src/repo");
        var heads = new Queue<string>(["a1\n", "a1\n", "a1\n", "b2\n"]);
        _box.Answer("git", "rev-parse -q --verify HEAD", _ => new ProgramExit(0, heads.Dequeue(), ""));
        _box.Answer("git", "pull --ff-only", 0);

        var upToDate = await Copy("/src/repo").PullAsync(CancellationToken.None);
        var complete = await Copy("/src/repo").PullAsync(CancellationToken.None);
        _box.Answer("git", "pull --ff-only", 128, errors: "hint: diverged\nfatal: Not possible to fast-forward, aborting.\n");
        heads = new Queue<string>(["b2\n"]);
        var failed = await Copy("/src/repo").PullAsync(CancellationToken.None);

        Assert.AreEqual(GitPullOutcome.UpToDate, upToDate.Outcome);
        Assert.AreEqual(GitPullOutcome.Ok, complete.Outcome);
        Assert.AreEqual(GitPullOutcome.Failed, failed.Outcome, "a location's pull is never offered a Windows token");
        StringAssert.Contains(failed.Message, "Not possible to fast-forward");
    }

    [TestMethod]
    public async Task TheUncommittedDiff_IsTheWorkingTreeAgainstHead_OrTheIndex_OfAPathAsGitNamesIt()
    {
        _box.Folder("/src/repo");
        _box.Answer("git", "diff --no-color --no-ext-diff HEAD -- src/a.txt", 0, "+tree\n");
        _box.Answer("git", "diff --no-color --no-ext-diff --cached", 0, "+index\n");

        Assert.AreEqual("+tree\n", await Copy("/src/repo").GetDiffAsync(@"src\a.txt", staged: false, CancellationToken.None));
        Assert.AreEqual("+index\n", await Copy("/src/repo").GetDiffAsync(null, staged: true, CancellationToken.None));
    }

    [TestMethod]
    public async Task ADiffBetweenRevisions_IsTheHostsGit_InTheFormatAsked_AndAnUnknownRevisionIsAnArgumentError()
    {
        _box.Folder("/src/repo");
        _box.Answer("git", "diff --no-color --no-ext-diff --stat=200 v1 HEAD -- docs", 0, " docs/a.md | 2 +-\n 1 file changed\n");
        _box.Answer("git", "diff --no-color --no-ext-diff --name-only v1 nope", 128, errors: "fatal: bad revision 'nope'\n");

        var stat = await Copy("/src/repo").GetDiffBetweenAsync("v1", "HEAD", "docs", GitDiffFormat.Stat, CancellationToken.None);
        var bad  = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => Copy("/src/repo").GetDiffBetweenAsync("v1", "nope", null, GitDiffFormat.NameOnly, CancellationToken.None));

        StringAssert.Contains(stat, "docs/a.md");
        StringAssert.Contains(bad.Message, "bad revision");
    }

    /// <summary>A linked worktree at <c>/src/wt</c> of the repository at <c>/src/main</c>, on its own branch <c>wt</c>,
    /// merged into <c>main</c> and with nothing uncommitted.</summary>
    private void LinkedWorktree(bool merged = true)
    {
        _box.File("/src/wt/.git", "gitdir: /src/main/.git/worktrees/wt\n");
        _box.File("/src/wt/readme.md", "hello");
        _box.File("/src/main/.git/worktrees/wt/commondir", "../..\n");
        _box.Answer("git", "symbolic-ref -q --short HEAD", 0, "wt\n");
        _box.Answer("git", "rev-parse -q --verify HEAD^{commit}", 0, "t1\n");
        _box.Answer("git", "for-each-ref --format=%(refname)%00%(objectname)%00%(upstream) refs/heads/ refs/remotes/", 0,
                    "refs/heads/main\0m1\0\nrefs/heads/wt\0t1\0\n");
        _box.Answer("git", "status --porcelain=v2 -z --untracked-files=no --ignore-submodules=all", 0, "");
        _box.Answer("git", "rev-list --count m1..t1", 0, merged ? "0\n" : "2\n");
    }

    [TestMethod]
    [CoversNode("git-worktree-badge")]
    public async Task ALinkedWorktree_IsReadByTheHostsGit_ItsPointerThroughTheLocation()
    {
        LinkedWorktree(merged: false);

        var info = await Copy("/src/wt").GetWorktreeAsync(CancellationToken.None);

        Assert.IsNotNull(info);
        Assert.AreEqual("wt", info.DisplayName);
        Assert.AreEqual("wt", info.Branch);
        Assert.AreEqual("main", info.MergeTargetBranch);
        Assert.IsFalse(info.IsMerged);
        Assert.IsFalse(info.HasUpstream);
        Assert.IsFalse(info.CanRemoveWithoutConfirmation);
        Assert.IsNull(await Copy("/src/main").GetWorktreeAsync(CancellationToken.None), "a main checkout's .git is a folder");
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public async Task RemovingAWorktree_MovesItAsideAndDeletesItThroughTheLocation_AndTheHostsGitDeletesItsBranch()
    {
        LinkedWorktree();
        _box.Answer("git", "--git-dir /src/main/.git branch -D wt", 0, "Deleted branch wt (was t1).\n");

        var result = await Copy("/src/wt").RemoveWorktreeAsync(CancellationToken.None);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(_box.Exists("/src/wt"), "the worktree's folder is gone");
        Assert.IsFalse(_box.Exists("/src/main/.git/worktrees/wt"), "its registration is gone");
        Assert.IsFalse(_box.Paths().Any(p => p.Contains(".removing-", StringComparison.Ordinal)), "nothing is left aside");
        var branchDelete = _box.Runs.Single(r => r.Arguments.Contains("branch"));
        Assert.AreEqual("/src", branchDelete.WorkingDirectory);
    }

    [TestMethod]
    [CoversNode("git-worktree-remove")]
    public async Task ABrokenWorktree_IsStillRemoved_WithNoBranchToDelete()
    {
        _box.File("/src/wt/.git", "gitdir: /src/main/.git/worktrees/wt\n");
        _box.Folder("/src/main/.git");
        _box.Answer("git", "symbolic-ref -q --short HEAD", 128, errors: "fatal: not a git repository: /src/main/.git/worktrees/wt\n");

        var info   = await Copy("/src/wt").GetWorktreeAsync(CancellationToken.None);
        var result = await Copy("/src/wt").RemoveWorktreeAsync(CancellationToken.None);

        Assert.IsTrue(info!.IsBroken);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(_box.Exists("/src/wt"));
        Assert.IsFalse(_box.Runs.Any(r => r.Arguments.Contains("branch")), "a remnant owns no branch it can name");
    }

    [TestMethod]
    public async Task TheWorktreeList_ReadsEachLinkedWorktreeOnTheHost_AndAPrunableOneIsBroken()
    {
        LinkedWorktree();
        _box.Folder("/src/main");
        _box.Answer("git", "worktree list --porcelain", 0,
                    "worktree /src/main\nHEAD m1\nbranch refs/heads/main\n\nworktree /src/wt\nHEAD t1\nbranch refs/heads/wt\n\n"
                  + "worktree /src/gone\nHEAD g1\ndetached\nprunable gitdir file points to non-existent location\n\n");

        var listed = await Copy("/src/main").GetWorktreesAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "wt:wt:merged", "gone:(unknown):broken" },
                                  listed.Select(w => $"{w.Name}:{w.Branch}:{(w.IsBroken ? "broken" : w.IsMerged ? "merged" : "unmerged")}").ToArray());
    }

    [TestMethod]
    public async Task TheAssistantsLine_DescribesWhatTheHostLastSaid_WithoutAskingAgain()
    {
        _box.Folder("/src/repo");
        _box.Answer("git", Status, 0, "# branch.head main\0");
        _box.Answer("git", Tip, 0, "");
        _box.Answer("git", Locals, 0, "main\n");
        var copy = Copy("/src/repo");

        Assert.IsNull(copy.DescribeForAssistant(), "nothing read yet, nothing to say");
        await copy.GetStatusAsync(CancellationToken.None);
        var runs = _box.Runs.Count();

        StringAssert.Contains(copy.DescribeForAssistant(), "main");
        Assert.AreEqual(runs, _box.Runs.Count(), "the line is answered without waiting on the host");
    }
}
