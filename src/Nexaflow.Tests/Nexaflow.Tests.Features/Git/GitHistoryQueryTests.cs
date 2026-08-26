using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// History queries that reach beyond the working tree: revision ranges, diffs between two revisions, tags, and
/// a file's contents at a revision. Kept apart from <see cref="GitServiceTests"/> (which covers the basic
/// working-tree reads) because each test here backs a <em>different</em> leaf of the AI tool surface.
/// </summary>
/// <remarks>
/// The shape under test throughout is the one that defeated the old surface: "what changed between release
/// v1 and now". Every assertion is anchored on a tagged commit part-way through a linear history.
/// </remarks>
[TestClass]
public class GitHistoryQueryTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    // ── Repo helpers ──────────────────────────────────────────────────────────

    private string InitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexagitq_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Four commits with a <c>v1</c> tag on the second — the release-boundary shape.</summary>
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

    // ── Revision ranges & filters ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_WithARange_ReturnsOnlyWhatFollowsTheTag()
    {
        var log = new GitService(TaggedHistory())
            .GetLog(50, filter: new GitLogFilter(Range: new GitRange("v1", "HEAD")));

        // 'from' is exclusive, matching git's own v1..HEAD, so the tagged commit is not itself included.
        CollectionAssert.AreEquivalent(new[] { "third", "fourth" }, log.Select(c => c.Subject).ToArray());
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_RangeWithAnUnknownEndpoint_Throws()
    {
        var svc = new GitService(TaggedHistory());
        Assert.ThrowsExactly<ArgumentException>(() =>
            svc.GetLog(50, filter: new GitLogFilter(Range: new GitRange("v1", "no-such-ref"))));
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_RangeAndPath_Compose()
    {
        var log = new GitService(TaggedHistory())
            .GetLog(50, path: "c.txt", filter: new GitLogFilter(Range: new GitRange("v1", "HEAD")));

        Assert.AreEqual(1, log.Count);
        Assert.AreEqual("third", log[0].Subject);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_GrepAndAuthor_NarrowTheWalk()
    {
        var svc = new GitService(TaggedHistory());

        Assert.AreEqual(1, svc.GetLog(50, filter: new GitLogFilter(Grep: "third")).Count);
        Assert.AreEqual(4, svc.GetLog(50, filter: new GitLogFilter(Author: "Tester")).Count);
        Assert.AreEqual(0, svc.GetLog(50, filter: new GitLogFilter(Author: "nobody")).Count);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_SinceAndUntil_BoundByAuthorDate()
    {
        var svc  = new GitService(TaggedHistory());
        var hour = TimeSpan.FromHours(1);

        Assert.AreEqual(4, svc.GetLog(50, filter: new GitLogFilter(Since: DateTimeOffset.Now - hour)).Count);
        Assert.AreEqual(0, svc.GetLog(50, filter: new GitLogFilter(Until: DateTimeOffset.Now - hour)).Count);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-log-ranges")]
    public void GetLog_NoMerges_SkipsTheMergeCommit()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        string trunk;
        using (var repo = new Repository(dir))
        {
            trunk = repo.Head.FriendlyName;
            repo.CreateBranch("side");
            Commands.Checkout(repo, "side");
        }
        Write(dir, "side.txt", "s"); CommitAll(dir, "on side");

        using (var repo = new Repository(dir)) Commands.Checkout(repo, trunk);
        Write(dir, "main.txt", "m"); CommitAll(dir, "on trunk");

        using (var repo = new Repository(dir))
            repo.Merge(repo.Branches["side"], Sig,
                       new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });

        var svc = new GitService(dir);
        var all      = svc.GetLog(50);
        var noMerges = svc.GetLog(50, filter: new GitLogFilter(NoMerges: true));

        Assert.AreEqual(all.Count - 1, noMerges.Count, "exactly the merge commit should drop out");
        Assert.IsFalse(noMerges.Any(c => c.Subject.StartsWith("Merge", StringComparison.OrdinalIgnoreCase)));
    }

    // ── Two-ref diff & output format ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-diff-refs")]
    public void GetDiffBetween_Stat_NamesTheFilesAndTotals()
    {
        var stat = new GitService(TaggedHistory()).GetDiffBetween("v1", "HEAD");

        StringAssert.Contains(stat, "c.txt");
        StringAssert.Contains(stat, "d.txt");
        StringAssert.Contains(stat, "2 file(s) changed");
        Assert.IsFalse(stat.Contains("+++"), "a stat summarises; it must not embed the patch");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-diff-refs")]
    public void GetDiffBetween_NameOnly_ListsPathsAlone()
    {
        var names = new GitService(TaggedHistory())
            .GetDiffBetween("v1", "HEAD", format: GitDiffFormat.NameOnly)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        CollectionAssert.AreEquivalent(new[] { "c.txt", "d.txt" }, names);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-diff-refs")]
    public void GetDiffBetween_Patch_CarriesTheContent_AndAPathScopesIt()
    {
        var svc = new GitService(TaggedHistory());

        StringAssert.Contains(svc.GetDiffBetween("v1", "HEAD", format: GitDiffFormat.Patch), "three");
        Assert.AreEqual("c.txt", svc.GetDiffBetween("v1", "HEAD", "c.txt", GitDiffFormat.NameOnly).Trim());
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-diff-refs")]
    public void GetDiffBetween_UnknownRevision_Throws()
    {
        var svc = new GitService(TaggedHistory());
        Assert.ThrowsExactly<ArgumentException>(() => svc.GetDiffBetween("v1", "no-such-ref"));
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-diff-refs")]
    public void GetDiffBetween_IdenticalRevisions_IsEmpty()
    {
        Assert.AreEqual(string.Empty, new GitService(TaggedHistory()).GetDiffBetween("v1", "v1"));
    }

    // ── Tags ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-tags")]
    public void GetTags_CarryTargetAndDate_AndFilterByPattern()
    {
        var dir = TaggedHistory();
        Tag(dir, "beta-1");
        var svc = new GitService(dir);

        var all = svc.GetTags();
        CollectionAssert.AreEquivalent(new[] { "v1", "beta-1" }, all.Select(t => t.Name).ToArray());
        Assert.IsTrue(all.All(t => t.TargetHash.Length == 7), "each tag names the commit it points at");

        var filtered = svc.GetTags("v1");
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("v1", filtered[0].Name);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-tags")]
    public void GetTags_NoTags_IsEmpty()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        Assert.AreEqual(0, new GitService(dir).GetTags().Count);
    }

    // ── File at a revision ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-file-at")]
    public void GetFileAt_ReturnsTheContentAsOfThatRevision()
    {
        var dir = InitRepo();
        Write(dir, "cfg.json", "{ \"old\": true }"); CommitAll(dir, "first");
        Tag(dir, "v1");
        Write(dir, "cfg.json", "{ \"new\": true }"); CommitAll(dir, "second");

        var svc = new GitService(dir);
        StringAssert.Contains(svc.GetFileAt("v1", "cfg.json")!, "old");
        StringAssert.Contains(svc.GetFileAt("HEAD", "cfg.json")!, "new");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-file-at")]
    public void GetFileAt_AcceptsABackslashPath()
    {
        var dir = InitRepo();
        Write(dir, Path.Combine("sub", "f.txt"), "nested");
        CommitAll(dir, "init");

        // A Windows caller will naturally pass a backslash path; it must resolve the same as a forward slash.
        Assert.AreEqual("nested", new GitService(dir).GetFileAt("HEAD", @"sub\f.txt"));
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-file-at")]
    public void GetFileAt_MissingPathAndBadRevision_AreDistinguishable()
    {
        var svc = new GitService(TaggedHistory());

        // c.txt only appears after v1 — absent there, present at HEAD.
        Assert.IsNull(svc.GetFileAt("v1", "c.txt"));
        Assert.IsNotNull(svc.GetFileAt("HEAD", "c.txt"));

        // The caller needs to tell "you named a bad revision" from "that file wasn't there yet".
        Assert.IsTrue(svc.RevisionExists("v1"));
        Assert.IsFalse(svc.RevisionExists("no-such-ref"));
    }

    // ── Divergence between arbitrary refs ─────────────────────────────────────

    /// <summary>A trunk and a diverged <c>side</c> branch: 1 commit each side of a shared base.</summary>
    private string ForkedHistory(out string trunk)
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "base");

        using (var repo = new Repository(dir))
        {
            trunk = repo.Head.FriendlyName;
            repo.CreateBranch("side");
            Commands.Checkout(repo, "side");
        }
        Write(dir, "side.txt", "s"); CommitAll(dir, "on side");

        var trunkName = trunk;
        using (var repo = new Repository(dir)) Commands.Checkout(repo, trunkName);
        Write(dir, "trunk.txt", "t"); CommitAll(dir, "on trunk");
        return dir;
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-merge-base")]
    public void GetDivergence_ReportsCommonAncestorAndBothCounts()
    {
        var dir = ForkedHistory(out var trunk);

        var d = new GitService(dir).GetDivergence("side", trunk);

        Assert.IsNotNull(d.MergeBaseHash, "the two branches share the base commit");
        Assert.AreEqual(1, d.Ahead,  "side has one commit trunk lacks");
        Assert.AreEqual(1, d.Behind, "trunk has one commit side lacks");
        Assert.IsFalse(d.IsAMergedIntoB);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-merge-base")]
    public void GetDivergence_AnAncestorIsReportedAsMerged()
    {
        var d = new GitService(TaggedHistory()).GetDivergence("v1", "HEAD");

        Assert.AreEqual(0, d.Ahead, "v1 is an ancestor of HEAD, so it holds nothing HEAD lacks");
        Assert.IsTrue(d.IsAMergedIntoB, "this is the 'safe to delete' signal");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-merge-base")]
    public void GetDivergence_UnknownRef_Throws()
    {
        var svc = new GitService(TaggedHistory());
        Assert.ThrowsExactly<ArgumentException>(() => svc.GetDivergence("HEAD", "no-such-ref"));
    }

    // ── Containment ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-contains")]
    public void GetRefsContaining_ListsTheBranchesAndTagsThatReachIt()
    {
        var dir  = TaggedHistory();
        var refs = new GitService(dir).GetRefsContaining("v1");

        // The tag itself, and the trunk branch whose history passes through it.
        Assert.IsTrue(refs.Any(r => r.IsTag && r.Name == "v1"));
        Assert.IsTrue(refs.Any(r => !r.IsTag && !r.IsRemote), "the current branch contains the tagged commit");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-contains")]
    public void GetRefsContaining_ACommitOnlyOnASideBranch_IsNotClaimedByTrunk()
    {
        var dir = ForkedHistory(out var trunk);

        string sideTip;
        using (var repo = new Repository(dir)) sideTip = repo.Branches["side"].Tip.Sha;

        var names = new GitService(dir).GetRefsContaining(sideTip).Select(r => r.Name).ToList();

        CollectionAssert.Contains(names, "side");
        CollectionAssert.DoesNotContain(names, trunk, "trunk must not claim work that never merged into it");
    }

    // ── Blame ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-blame")]
    public void GetBlame_AttributesEachLineToItsLastCommit()
    {
        var dir = InitRepo();
        Write(dir, "f.txt", "alpha\n");          CommitAll(dir, "add alpha");
        Write(dir, "f.txt", "alpha\nbeta\n");    CommitAll(dir, "add beta");

        var lines = new GitService(dir).GetBlame("f.txt");

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("alpha", lines[0].Text);
        Assert.AreEqual("beta",  lines[1].Text);
        Assert.AreNotEqual(lines[0].Hash, lines[1].Hash, "the two lines arrived in different commits");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-blame")]
    public void GetBlame_RespectsALineRange_AndRejectsAMissingFile()
    {
        var dir = InitRepo();
        Write(dir, "f.txt", "one\ntwo\nthree\n"); CommitAll(dir, "init");
        var svc = new GitService(dir);

        var lines = svc.GetBlame("f.txt", startLine: 2, endLine: 2);
        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("two", lines[0].Text);
        Assert.AreEqual(2, lines[0].Line);

        Assert.ThrowsExactly<ArgumentException>(() => svc.GetBlame("nope.txt"));
    }

    // ── History search (pickaxe) ──────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-search-history")]
    public void SearchHistory_FindsWhereAStringArrivedAndWhereItLeft()
    {
        var dir = InitRepo();
        Write(dir, "f.txt", "harmless\n");            CommitAll(dir, "init");
        Write(dir, "f.txt", "harmless\nMAGIC_TOKEN\n"); CommitAll(dir, "introduce token");
        Write(dir, "f.txt", "harmless\n");            CommitAll(dir, "remove token");

        var matches = new GitService(dir).SearchHistory("MAGIC_TOKEN");

        Assert.AreEqual(2, matches.Count, "one commit added the line, one removed it");
        CollectionAssert.AreEquivalent(
            new[] { "introduce token", "remove token" },
            matches.Select(m => m.Commit.Subject).ToArray());
        Assert.IsTrue(matches.Any(m => m.Added   > 0));
        Assert.IsTrue(matches.Any(m => m.Removed > 0));
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-search-history")]
    public void SearchHistory_UnknownPattern_IsEmpty_AndPathScopes()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "TOKEN\n"); CommitAll(dir, "in a");
        Write(dir, "b.txt", "TOKEN\n"); CommitAll(dir, "in b");
        var svc = new GitService(dir);

        Assert.AreEqual(0, svc.SearchHistory("NOT_PRESENT_ANYWHERE").Count);

        var scoped = svc.SearchHistory("TOKEN", path: "b.txt");
        Assert.AreEqual(1, scoped.Count);
        Assert.AreEqual("in b", scoped[0].Commit.Subject);
    }

    // ── Recovery: stashes & reflog ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-recovery")]
    public void GetStashes_ListsWhatWasStashed()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");
        Write(dir, "a.txt", "dirty");

        using (var repo = new Repository(dir)) repo.Stashes.Add(Sig, "work in progress");

        var stashes = new GitService(dir).GetStashes();

        Assert.AreEqual(1, stashes.Count);
        StringAssert.Contains(stashes[0].Message, "work in progress");
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-recovery")]
    public void GetReflog_ShowsHeadMovements_AndIsEmptyWhenThereIsNoStash()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "first");
        Write(dir, "a.txt", "two"); CommitAll(dir, "second");

        var svc = new GitService(dir);

        Assert.AreEqual(0, svc.GetStashes().Count);
        Assert.IsTrue(svc.GetReflog().Count >= 2, "each commit moved HEAD, so the reflog records them");
    }

    // ── Worktrees ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("git-ai-act-git-worktrees")]
    public void GetWorktrees_NoneLinked_IsEmpty()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        Assert.AreEqual(0, new GitService(dir).GetWorktrees().Count);
    }

    [TestMethod]
    [CoversNode("git-ai-act-git-worktrees")]
    public void GetWorktrees_ListsALinkedWorktreeWithItsBranchAndState()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one"); CommitAll(dir, "init");

        var wtPath = Path.Combine(dir, "..", "wt_" + Guid.NewGuid().ToString("N"));
        using (var repo = new Repository(dir))
            repo.Worktrees.Add("feature-wt", Path.GetFullPath(wtPath), isLocked: false);
        _temp.Add(Path.GetFullPath(wtPath));

        var worktrees = new GitService(dir).GetWorktrees();

        Assert.AreEqual(1, worktrees.Count);
        Assert.AreEqual("feature-wt", worktrees[0].Name);
        Assert.IsFalse(worktrees[0].IsBroken);
        Assert.IsFalse(worktrees[0].HasUncommittedChanges);
    }
}
