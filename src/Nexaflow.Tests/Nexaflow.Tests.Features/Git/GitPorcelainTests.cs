using Nexaflow.Features.Git.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// What a location host's git prints, read as the viewlet reads a status: the porcelain formats, and the rules that pick
/// a linked worktree's own branch, its remote and its mainline exactly as the in-process reading picks them.
/// </summary>
[TestClass]
[CoversNode("git-viewlet")]
public sealed class GitPorcelainTests
{
    [TestMethod]
    public void AStatus_SortsStagedFromModified_AndReadsTheBranchAndItsTracking()
    {
        const string printed =
            "# branch.oid 1234567890abcdef1234567890abcdef12345678\0"
          + "# branch.head main\0"
          + "# branch.upstream origin/main\0"
          + "# branch.ab +2 -1\0"
          + "1 M. N... 100644 100644 100644 aaaa bbbb src/staged file.cs\0"
          + "1 .M N... 100755 100755 100755 aaaa aaaa run.sh\0"
          + "1 MM N... 100644 100644 100644 aaaa bbbb both.txt\0"
          + "2 R. N... 100644 100644 100644 aaaa aaaa R100 new name.txt\0old name.txt\0"
          + "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflicted.txt\0"
          + "? notes/\0"
          + "! ignored.log\0";

        var s = GitPorcelain.ParseStatus(printed);

        Assert.AreEqual("main", s.Branch);
        Assert.IsFalse(s.IsDetached);
        Assert.AreEqual("origin/main", s.Upstream);
        Assert.AreEqual(2, s.Ahead);
        Assert.AreEqual(1, s.Behind);
        CollectionAssert.AreEqual(new[] { "src/staged file.cs:modified", "both.txt:modified", "new name.txt:renamed" },
                                  s.Staged.Select(c => $"{c.Path}:{c.Change}").ToArray(), "a path with a space is kept whole");
        CollectionAssert.AreEqual(new[] { "run.sh:modified" }, s.Modified.Select(c => $"{c.Path}:{c.Change}").ToArray(),
                                  "a path staged is listed staged only; an unmerged one nowhere");
        CollectionAssert.AreEqual(new[] { "notes/" }, s.Untracked.ToArray());
    }

    [TestMethod]
    public void ADetachedHead_ReadsAsTheInProcessStatusNamesIt_WithNoTracking()
    {
        var s = GitPorcelain.ParseStatus("# branch.oid abc\0# branch.head (detached)\0");

        Assert.IsTrue(s.IsDetached);
        Assert.AreEqual(GitPorcelain.DetachedBranch, s.Branch);
        Assert.IsNull(s.Upstream);
        Assert.IsNull(s.Ahead);
    }

    [TestMethod]
    public void ChangesAreCountedStagedAndModified_APathInBothCountingTwice()
    {
        var (staged, modified) = GitPorcelain.CountChanges(
            "1 M. N... 1 1 1 a b x\0" + "1 .D N... 1 1 1 a b y\0" + "1 AM N... 1 1 1 a b z\0" + "2 R. N... 1 1 1 a b R100 new\0old\0");

        Assert.AreEqual(3, staged);
        Assert.AreEqual(2, modified);
    }

    [TestMethod]
    public void TheLastCommit_IsItsShortHashSubjectAndTime_AndAnEmptyRepositoryHasNone()
    {
        var last = GitPorcelain.ParseLastCommit("1234567890abcdef1234567890abcdef12345678\0Fix the: thing\02026-11-01T10:00:00+01:00\n");

        Assert.IsNotNull(last);
        Assert.AreEqual("1234567", last.Value.Hash);
        Assert.AreEqual("Fix the: thing", last.Value.Subject);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(1)), last.Value.When);
        Assert.IsNull(GitPorcelain.ParseLastCommit(string.Empty));
    }

    [TestMethod]
    public void AWorktreesRefs_PickItsCheckedOutBranch_ItsUpstream_AndTheMainline()
    {
        var refs = GitPorcelain.ParseRefs(
            "refs/heads/main\0m1\0refs/remotes/origin/main\n"
          + "refs/heads/feature\0f1\0refs/remotes/origin/feature\n"
          + "refs/remotes/origin/main\0m0\0\n"
          + "refs/remotes/origin/feature\0f0\0\n");

        var (own, remote, target) = GitPorcelain.ResolveWorktreeRefs(refs, headBranch: "feature", tipSha: "f1", folderName: "wt");

        Assert.AreEqual("feature", own);
        Assert.AreEqual("origin/feature", remote?.Name);
        Assert.AreEqual("main", target?.Name);
    }

    [TestMethod]
    public void ADetachedWorktree_OwnsTheBranchNamedAfterItsFolder_OnlyWhereThatBranchIsAtItsCommit()
    {
        var refs = GitPorcelain.ParseRefs("refs/heads/main\0m1\0\nrefs/heads/claude/wt\0t1\0\nrefs/remotes/origin/master\0o1\0\n");

        var atTip = GitPorcelain.ResolveWorktreeRefs(refs, headBranch: null, tipSha: "t1", folderName: "wt");
        var moved = GitPorcelain.ResolveWorktreeRefs(refs, headBranch: null, tipSha: "t2", folderName: "wt");

        Assert.AreEqual("claude/wt", atTip.OwnBranch);
        Assert.IsNull(moved.OwnBranch, "a same-named branch that moved on is not this worktree's to delete");
        Assert.AreEqual("main", atTip.MergeTarget?.Name);
    }

    [TestMethod]
    public void TheMainline_IsALocalOneOtherThanTheWorktreesOwn_ElseTheRemoteOne()
    {
        var refs = GitPorcelain.ParseRefs("refs/heads/main\0m1\0\nrefs/remotes/origin/main\0m0\0\n");

        var (_, _, target) = GitPorcelain.ResolveWorktreeRefs(refs, headBranch: "main", tipSha: "m1", folderName: "main");

        Assert.AreEqual("origin/main", target?.Name, "a worktree on main measures against the remote main");
    }

    [TestMethod]
    public void TheWorktreeList_IsEveryLinkedWorktree_ThePrunableMarked()
    {
        var listed = GitPorcelain.ParseWorktreeList(
            "worktree /src/main\nHEAD aaa\nbranch refs/heads/main\n\n"
          + "worktree /src/wt one\nHEAD bbb\nbranch refs/heads/wt\n\n"
          + "worktree /src/gone\nHEAD ccc\ndetached\nprunable gitdir file points to non-existent location\n\n");

        CollectionAssert.AreEqual(new[] { "/src/wt one:False", "/src/gone:True" },
                                  listed.Select(w => $"{w.Path}:{w.IsPrunable}").ToArray(), "the main working tree is not a linked one");
    }

    [TestMethod]
    public void AWorktreesPointer_NamesItsRegistration_AbsoluteOrRelative()
    {
        Assert.AreEqual("/src/main/.git/worktrees/wt", GitPorcelain.AdminFolderOf("gitdir: /src/main/.git/worktrees/wt\n", "/src/wt"));
        Assert.AreEqual("/src/main/.git/worktrees/wt", GitPorcelain.AdminFolderOf("gitdir: ../main/.git/worktrees/wt", "/src/wt"));
        Assert.IsNull(GitPorcelain.AdminFolderOf("not a pointer", "/src/wt"));
        Assert.AreEqual("/src/main/.git", GitPorcelain.ResolvePosix("/src/main/.git/worktrees/wt", "../.."));
    }
}
