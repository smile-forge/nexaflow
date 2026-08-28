using System.IO;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// Which repository a git-reading verb runs in.
///
/// <para>
/// This is one line of code and it was wrong in the way that costs the most: <c>remap --from-git</c> ran git
/// against the <b>product root</b>. From a linked worktree that is the main checkout, whose HEAD has never
/// seen your commits — so a range ending at <c>HEAD</c> came back empty, the verb printed "git recorded no
/// renames", exited 0, and rewrote nothing on the exact branch that had just moved fourteen files.
/// </para>
/// <para>
/// Nothing downstream caught it. <c>validate</c> resolves a snaplink against the product root when the
/// working tree does not have the file, so every moved-away path still resolved <em>in the main checkout</em>
/// and the tree reported clean while sixty-six links were stale. A verb that silently does nothing is worse
/// than one that fails, and the only cheap guard is to pin the decision itself.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Repository resolution for the headless CLI — infrastructure, not a product-tree node.")]
public class GitRepoResolutionTests
{
    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup() => _tmp = Directory.CreateTempSubdirectory("nexa-gitrepo-").FullName;

    [TestCleanup]
    public void Teardown() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    /// <summary>A directory carrying a <c>.git</c> entry — a directory for a checkout, a file for a worktree.</summary>
    private string Repo(string name, bool linkedWorktree = false)
    {
        var dir = Path.Combine(_tmp, name);
        Directory.CreateDirectory(dir);
        if (linkedWorktree) File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: ../main/.git/worktrees/" + name);
        else Directory.CreateDirectory(Path.Combine(dir, ".git"));
        return dir;
    }

    [TestMethod]
    public void AWorktreeCaller_WinsOverTheProductRoot()
    {
        // The regression. The product tree lives in the main checkout even while you work in a worktree, so
        // these two are routinely different directories and the caller's is the one holding the renames.
        var main     = Repo("main");
        var worktree = Repo("feature-branch", linkedWorktree: true);

        Assert.AreEqual(worktree, Program.GitRepoFor(worktree, productRoot: main),
            "git must run where the caller stands — the main checkout does not have the caller's commits");
    }

    [TestMethod]
    public void ADeeperCallerResolvesToItsOwnWorktreeRoot_NotJustItsCwd()
    {
        var worktree = Repo("feature-branch", linkedWorktree: true);
        var nested   = Path.Combine(worktree, "src", "Nexaflow.Features");
        Directory.CreateDirectory(nested);

        Assert.AreEqual(worktree, Program.GitRepoFor(nested, productRoot: Repo("main")));
    }

    [TestMethod]
    public void APlainCheckout_IsUnaffected_BecauseBothAnswersAreTheSameDirectory()
    {
        // CI and the release gate run here: caller and product root are one checkout, so the fix is a no-op.
        var checkout = Repo("checkout");

        Assert.AreEqual(checkout, Program.GitRepoFor(checkout, productRoot: checkout));
    }

    [TestMethod]
    public void ACallerOutsideAnyRepository_FallsBackToTheProductRoot()
    {
        var main    = Repo("main");
        var nowhere = Path.Combine(_tmp, "not-a-repo");
        Directory.CreateDirectory(nowhere);

        Assert.AreEqual(main, Program.GitRepoFor(nowhere, productRoot: main),
            "with no caller repository the product root is the only candidate left");
    }

    [TestMethod]
    public void NeitherInARepository_StillReturnsTheProductRoot_RatherThanNull()
    {
        // The caller reports git's own error in this case; it must get a path to name, not a crash.
        var nowhere = Path.Combine(_tmp, "loose");
        Directory.CreateDirectory(nowhere);

        Assert.AreEqual(nowhere, Program.GitRepoFor(nowhere, productRoot: nowhere));
    }
}
