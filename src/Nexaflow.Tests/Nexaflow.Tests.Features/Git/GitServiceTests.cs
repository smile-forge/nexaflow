using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

[TestClass]
[CoversNode("git-read-service")]
public class GitServiceTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    // ── Repo helpers ──────────────────────────────────────────────────────────

    private string InitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexagit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        _temp.Add(dir);
        return dir;
    }

    private static void Write(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content);

    /// <summary>Stages everything and commits — for building up repo history in a test's arrange phase.</summary>
    private static void CommitAll(string dir, string message)
    {
        using var repo = new Repository(dir);
        Commands.Stage(repo, "*");
        repo.Commit(message, Sig, Sig);
    }

    private static void Stage(string dir, string path)
    {
        using var repo = new Repository(dir);
        Commands.Stage(repo, path);
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

    // ── GetStatus ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetStatus_AfterCommit_CleanWithTip()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        var s = new GitService(dir).GetStatus();

        Assert.AreEqual(0, s.StagedCount);
        Assert.AreEqual(0, s.ModifiedCount);
        Assert.AreEqual(0, s.UntrackedCount);
        Assert.IsFalse(string.IsNullOrEmpty(s.Branch));
        Assert.IsNotNull(s.LastCommitHash);
        Assert.AreEqual("init", s.LastCommitSubject);
    }

    [TestMethod]
    public void GetStatus_NewFile_CountsUntracked()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        Write(dir, "b.txt", "new");

        var s = new GitService(dir).GetStatus();

        Assert.AreEqual(1, s.UntrackedCount);
        CollectionAssert.Contains(s.Untracked.ToList(), "b.txt");
    }

    [TestMethod]
    public void GetStatus_StagedNewFile_CountsStaged()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        Write(dir, "b.txt", "new");
        Stage(dir, "b.txt");

        var s = new GitService(dir).GetStatus();

        Assert.AreEqual(1, s.StagedCount);
        Assert.IsTrue(s.Staged.Any(f => f.Path == "b.txt"));
    }

    [TestMethod]
    public void GetStatus_ModifiedTrackedFile_CountsModified()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        Write(dir, "a.txt", "two");   // changed, not staged

        var s = new GitService(dir).GetStatus();

        Assert.AreEqual(1, s.ModifiedCount);
        Assert.AreEqual(0, s.StagedCount);
        Assert.IsTrue(s.Modified.Any(f => f.Path == "a.txt"));
    }

    // ── GetLog ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetLog_ReturnsCommitsMostRecentFirst()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "first");
        Write(dir, "a.txt", "two");
        CommitAll(dir, "second");

        var log = new GitService(dir).GetLog(10);

        Assert.AreEqual(2, log.Count);
        Assert.AreEqual("second", log[0].Subject);
        Assert.AreEqual("first", log[1].Subject);
    }

    // ── GetDiff ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetDiff_WorkingTree_ShowsModifiedContent()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one\n");
        CommitAll(dir, "init");
        Write(dir, "a.txt", "one\ntwo\n");

        var diff = new GitService(dir).GetDiff();

        StringAssert.Contains(diff, "two");
    }

    [TestMethod]
    public void GetDiff_Staged_ShowsStagedFileOnly()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        Write(dir, "b.txt", "staged-content");
        Stage(dir, "b.txt");

        var diff = new GitService(dir).GetDiff(staged: true);

        StringAssert.Contains(diff, "staged-content");
    }

    // ── GetBranches / Show / GetRemotes ───────────────────────────────────────

    [TestMethod]
    public void GetBranches_IncludesCurrentLocalBranch()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        var branches = new GitService(dir).GetBranches();

        Assert.IsTrue(branches.Any(b => b.IsCurrent && !b.IsRemote));
    }

    [TestMethod]
    public void Show_ReturnsMessageAndDiff()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "hello-content");
        CommitAll(dir, "hello commit");

        string sha;
        using (var repo = new Repository(dir)) sha = repo.Head.Tip.Sha;

        var detail = new GitService(dir).Show(sha);

        Assert.IsNotNull(detail);
        StringAssert.Contains(detail!.Message, "hello commit");
        StringAssert.Contains(detail.Diff, "hello-content");   // first commit diffs against an empty tree
    }

    [TestMethod]
    public void Show_UnknownHash_ReturnsNull()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        Assert.IsNull(new GitService(dir).Show("0000000000000000000000000000000000000000"));
    }

    [TestMethod]
    public void GetRemotes_NoneConfigured_Empty()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        Assert.AreEqual(0, new GitService(dir).GetRemotes().Count);
    }
}
