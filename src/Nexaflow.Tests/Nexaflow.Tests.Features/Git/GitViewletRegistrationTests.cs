using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Git;
using Nexaflow.Features.Git.Viewlets;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// The folder-match rules that decide which of the two Git viewlets a folder gets. A main checkout has a
/// <c>.git</c> <em>directory</em>; a linked worktree has a <c>.git</c> <em>file</em> (a <c>gitdir:</c>
/// pointer). They are matched by different globs and must stay mutually exclusive — otherwise a worktree
/// gets no Git bar at all, or both fire on one folder.
/// </summary>
[TestClass]
[CoversNode("git-viewlet-match")]
public class GitViewletRegistrationTests
{
    // The globs are interface members with defaults, so the match rules are read through IFolderViewlet —
    // exactly how the registry sees them.
    private static IFolderViewlet MainCheckout()     => new GitViewlet(new GitOptions(), Substitute.For<IShellServices>());
    private static IFolderViewlet Worktree() => new GitWorktreeViewlet(new GitOptions(), Substitute.For<IShellServices>());

    [TestMethod]
    public void MainCheckout_MatchesTheGitFolder_NotTheGitFile()
    {
        var viewlet = MainCheckout();
        CollectionAssert.AreEqual(new[] { ".git" }, viewlet.ContainsFolderGlobs);
        Assert.IsNull(viewlet.ContainsFileGlobs, "a main checkout must not also match the worktree pointer file");
    }

    [TestMethod]
    public void LinkedWorktree_MatchesTheGitFile_NotTheGitFolder()
    {
        var viewlet = Worktree();
        CollectionAssert.AreEqual(new[] { ".git" }, viewlet.ContainsFileGlobs);
        Assert.IsNull(viewlet.ContainsFolderGlobs, "a worktree must not also match a main checkout's .git directory");
    }

    [TestMethod]
    public void BothRenderAsASingleBar_AndNeitherAppliesToDrives()
    {
        foreach (var viewlet in new[] { MainCheckout(), Worktree() })
        {
            Assert.AreEqual("Git", viewlet.DisplayName);
            Assert.AreEqual(ViewletDisplayMode.SingleBar, viewlet.DefaultDisplayMode);
            CollectionAssert.AreEqual(new[] { ViewletDisplayMode.SingleBar }, viewlet.SupportedModes);
            Assert.IsFalse(viewlet.AppliesToDrives, "a drive root is never a repository");
        }
    }
}
