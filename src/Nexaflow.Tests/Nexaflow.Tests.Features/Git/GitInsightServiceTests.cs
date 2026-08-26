using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// The composite queries — each replaces four to seven primitive calls, so what matters is that they gather
/// the right things and that their derived judgements ("safe to delete", "which PRs merged") are correct.
/// </summary>
[TestClass]
public class GitInsightServiceTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    private string InitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexagiti_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        _temp.Add(dir);
        return dir;
    }

    private static void Write(string dir, string name, string content)
    {
        var full = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void CommitAll(string dir, string message)
    {
        using var repo = new Repository(dir);
        Commands.Stage(repo, "*");
        repo.Commit(message, Sig, Sig);
    }

    private static void Tag(string dir, string name)
    {
        using var repo = new Repository(dir);
        repo.ApplyTag(name);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
            }
            catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>Four commits, <c>v1</c> on the second — the release-boundary shape used throughout.</summary>
    private string TaggedHistory()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");   CommitAll(dir, "first");
        Write(dir, "b.txt", "two");   CommitAll(dir, "second");
        Tag(dir, "v1");
        Write(dir, "c.txt", "three"); CommitAll(dir, "third");
        Write(dir, "d.txt", "four");  CommitAll(dir, "fourth");
        return dir;
    }

    // ── git_compare ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-compare")]
    public void Compare_GathersDivergenceStatAndCommits_InOneCall()
    {
        var c = new GitInsightService(TaggedHistory()).Compare("v1", "HEAD");

        Assert.AreEqual(0, c.Divergence.Ahead,  "v1 holds nothing HEAD lacks");
        Assert.AreEqual(2, c.Divergence.Behind, "HEAD has two commits v1 lacks");
        StringAssert.Contains(c.Stat, "2 file(s) changed");
        CollectionAssert.AreEquivalent(new[] { "third", "fourth" }, c.Commits.Select(x => x.Subject).ToArray());
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-compare")]
    public void Compare_UnknownRevision_Throws()
    {
        var svc = new GitInsightService(TaggedHistory());
        Assert.ThrowsExactly<ArgumentException>(() => svc.Compare("v1", "no-such-ref"));
    }

    // ── git_changelog ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-changelog")]
    public void Changelog_ListsCommitsContributorsAndStat()
    {
        var log = new GitInsightService(TaggedHistory()).Changelog("v1", "HEAD");

        CollectionAssert.AreEquivalent(new[] { "third", "fourth" }, log.Commits.Select(c => c.Subject).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Tester" }, log.Contributors.ToArray());
        StringAssert.Contains(log.Stat, "2 file(s) changed");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-changelog")]
    public void Changelog_RecoversPullRequestTitlesFromMergeCommits_WithoutANetwork()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");
        Tag(dir, "v1");

        string trunk;
        using (var repo = new Repository(dir))
        {
            trunk = repo.Head.FriendlyName;
            repo.CreateBranch("feature");
            Commands.Checkout(repo, "feature");
        }
        Write(dir, "f.txt", "work"); CommitAll(dir, "do the work");

        using (var repo = new Repository(dir))
        {
            Commands.Checkout(repo, trunk);
            // The message shape a forge writes: header line, blank, then the PR title.
            repo.Merge(repo.Branches["feature"], Sig,
                       new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward,
                                          CommitOnSuccess = false });
            Commands.Stage(repo, "*");
            repo.Commit("Merge pull request #42 from smile-forge/feature\n\nAdd the feature everyone wanted",
                        Sig, Sig);
        }

        var log = new GitInsightService(dir).Changelog("v1", "HEAD");

        Assert.AreEqual(1, log.PullRequests.Count);
        Assert.AreEqual(42, log.PullRequests[0].Number);
        Assert.AreEqual("Add the feature everyone wanted", log.PullRequests[0].Title);
        Assert.IsFalse(log.Commits.Any(c => c.Subject.StartsWith("Merge pull request")),
                       "the merge commit itself is excluded from the commit list");
    }

    // ── git_branch_audit ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-branch-audit")]
    public void AuditBranches_MarksAMergedBranchSafe_AndAnUnmergedOneNot()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        string trunk;
        using (var repo = new Repository(dir))
        {
            trunk = repo.Head.FriendlyName;
            repo.CreateBranch("merged-already");     // points at the same commit → contained in trunk
            repo.CreateBranch("has-own-work");
            Commands.Checkout(repo, "has-own-work");
        }
        Write(dir, "b.txt", "extra"); CommitAll(dir, "unmerged work");

        using (var repo = new Repository(dir)) Commands.Checkout(repo, trunk);

        var rows = new GitInsightService(dir).AuditBranches(trunk);

        var merged   = rows.Single(r => r.Name == "merged-already");
        var unmerged = rows.Single(r => r.Name == "has-own-work");

        Assert.IsTrue(merged.MergedIntoMainline);
        Assert.IsTrue(merged.SafeToDelete);

        Assert.IsFalse(unmerged.MergedIntoMainline);
        Assert.IsFalse(unmerged.SafeToDelete, "a branch with its own commits must never read as safe");
        Assert.AreEqual(1, unmerged.AheadOfMainline);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-branch-audit")]
    public void AuditBranches_TheCurrentBranchIsNeverSafeToDelete()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        var rows    = new GitInsightService(dir).AuditBranches();
        var current = rows.Single(r => r.IsCurrent);

        Assert.IsFalse(current.SafeToDelete, "git refuses to delete the checked-out branch, and so must we");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-branch-audit")]
    public void AuditBranches_FlagsABranchHeldByAWorktree()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        var wtPath = Path.GetFullPath(Path.Combine(dir, "..", "wt_" + Guid.NewGuid().ToString("N")));
        using (var repo = new Repository(dir)) repo.Worktrees.Add("held", wtPath, isLocked: false);
        _temp.Add(wtPath);

        var rows = new GitInsightService(dir).AuditBranches();

        Assert.IsTrue(rows.Any(r => r.HeldByWorktree is not null),
                      "a branch checked out in a worktree must be reported as held");
        Assert.IsFalse(rows.Single(r => r.HeldByWorktree is not null).SafeToDelete);
    }

    // ── git_find_work ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-find-work")]
    public void FindWork_FindsABranchByName_EvenWithNoWorktreeForIt()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");
        using (var repo = new Repository(dir)) repo.CreateBranch("claude/local-llm-provider");

        var hits = new GitInsightService(dir).FindWork("llm");

        Assert.IsTrue(hits.Any(h => h.Kind == "branch" && h.Name == "claude/local-llm-provider"),
                      "this is the 'my work vanished' case — the branch outlives any worktree");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-find-work")]
    public void FindWork_SearchesTagsAndCommitSubjectsToo_AndIsEmptyWhenAbsent()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "add the widget subsystem");
        Tag(dir, "widget-milestone");

        var svc = new GitInsightService(dir);

        var hits = svc.FindWork("widget");
        Assert.IsTrue(hits.Any(h => h.Kind == "tag"));
        Assert.IsTrue(hits.Any(h => h.Kind == "commit"));

        Assert.AreEqual(0, svc.FindWork("nothing-matches-this").Count);
    }

    // ── git_file_history ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-file-history")]
    public void FileHistory_ListsEveryCommitTouchingThePath()
    {
        var dir = InitRepo();
        Write(dir, "f.txt", "one");   CommitAll(dir, "create f");
        Write(dir, "other.txt", "x"); CommitAll(dir, "unrelated");
        Write(dir, "f.txt", "two");   CommitAll(dir, "change f");

        var history = new GitInsightService(dir).FileHistory("f.txt");

        CollectionAssert.AreEquivalent(
            new[] { "create f", "change f" },
            history.Select(h => h.Commit.Subject).ToArray());
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-file-history")]
    public void FileHistory_UnknownPath_IsEmpty()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        Assert.AreEqual(0, new GitInsightService(dir).FileHistory("never-existed.txt").Count);
    }
}
