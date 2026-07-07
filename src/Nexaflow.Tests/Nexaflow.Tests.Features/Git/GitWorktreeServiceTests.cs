using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.Git.Services;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Covers <see cref="GitWorktreeService"/>: worktree detection, merge/push state, and full removal.
/// Each test builds a real repository + linked worktree on disk under the temp folder.
/// </summary>
[TestClass]
public class GitWorktreeServiceTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string NewTempPath(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nexawt_{tag}_" + Guid.NewGuid().ToString("N"));
        _temp.Add(dir);
        return dir;
    }

    /// <summary>Creates a repo with one commit and a "main" branch at that commit, and returns its path.</summary>
    private string InitRepoWithMain()
    {
        var dir = NewTempPath("main");
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "one");

        using var repo = new Repository(dir);
        Commands.Stage(repo, "*");
        repo.Commit("init", Sig, Sig);
        if (repo.Branches["main"] is null) repo.CreateBranch("main");
        return dir;
    }

    /// <summary>Adds a linked worktree checked out to a fresh branch off HEAD; returns its folder path.</summary>
    private string AddWorktree(string mainDir, string branch, string worktreeName)
    {
        var wtPath = NewTempPath(worktreeName);
        using var repo = new Repository(mainDir);
        if (repo.Branches[branch] is null) repo.CreateBranch(branch);
        repo.Worktrees.Add(branch, worktreeName, wtPath, isLocked: false);
        return wtPath;
    }

    private static void CommitInto(string dir, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(dir, file), content);
        using var repo = new Repository(dir);
        Commands.Stage(repo, "*");
        repo.Commit(message, Sig, Sig);
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

    // ── Detection ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsWorktree_MainCheckout_False()
    {
        var main = InitRepoWithMain();
        Assert.IsFalse(new GitWorktreeService(main).IsWorktree());
        Assert.IsNull(new GitWorktreeService(main).GetInfo());
    }

    [TestMethod]
    public void IsWorktree_LinkedWorktree_True()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");

        Assert.IsTrue(new GitWorktreeService(wt).IsWorktree());

        var info = new GitWorktreeService(wt).GetInfo();
        Assert.IsNotNull(info);
        Assert.AreEqual("feat", info!.Branch);
        Assert.AreEqual("feat-wt", info.WorktreeName);
        Assert.IsFalse(info.IsDetached);
    }

    // ── Merge state ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetInfo_BranchAtMainTip_IsMerged()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");   // feat starts at main's tip

        var info = new GitWorktreeService(wt).GetInfo();

        Assert.IsNotNull(info);
        Assert.IsNotNull(info!.MergeTargetBranch);
        Assert.IsTrue(info.IsMerged, "A branch sitting at the mainline tip is contained in it → merged.");
        Assert.IsTrue(info.CanRemoveWithoutConfirmation);
    }

    [TestMethod]
    public void GetInfo_BranchAheadOfMain_NotMerged()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");
        CommitInto(wt, "b.txt", "work", "feature work");   // feat now ahead of main

        var info = new GitWorktreeService(wt).GetInfo();

        Assert.IsNotNull(info);
        Assert.IsNotNull(info!.MergeTargetBranch);
        Assert.IsFalse(info.IsMerged);
        Assert.IsFalse(info.CanRemoveWithoutConfirmation);
    }

    [TestMethod]
    public void GetInfo_NoRemote_ReportsNoUpstream()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");

        var info = new GitWorktreeService(wt).GetInfo();

        Assert.IsNotNull(info);
        Assert.IsFalse(info!.HasUpstream);
        Assert.IsFalse(info.IsPushed);
    }

    [TestMethod]
    public void GetInfo_UncommittedChange_CountsAsDirty()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");
        File.WriteAllText(Path.Combine(wt, "a.txt"), "changed");   // modify a tracked file

        var info = new GitWorktreeService(wt).GetInfo();

        Assert.IsNotNull(info);
        Assert.IsTrue(info!.HasUncommittedChanges);
        Assert.IsFalse(info.CanRemoveWithoutConfirmation, "Even a merged worktree prompts when it's dirty.");
    }

    // ── Removal ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Remove_DeletesFolderPrunesWorktreeAndBranch()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");

        var result = new GitWorktreeService(wt).Remove();

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(Directory.Exists(wt), "worktree folder should be gone");
        Assert.IsFalse(Directory.Exists(Path.Combine(main, ".git", "worktrees", "feat-wt")),
                       "worktree registration should be pruned");

        using var repo = new Repository(main);
        Assert.IsNull(repo.Branches["feat"], "branch should be deleted");
    }

    [TestMethod]
    public void Remove_NavigateToIsWorktreeParent()
    {
        var main = InitRepoWithMain();
        var wt   = AddWorktree(main, "feat", "feat-wt");
        var expectedParent = Directory.GetParent(wt)!.FullName;

        var result = new GitWorktreeService(wt).Remove();

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(expectedParent, result.NavigateTo);
    }

    [TestMethod]
    public void Remove_OnMainCheckout_Fails()
    {
        var main = InitRepoWithMain();

        var result = new GitWorktreeService(main).Remove();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(Directory.Exists(main), "a non-worktree must not be touched");
    }
}
