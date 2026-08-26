using System.IO;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

/// <summary>
/// Snaplinks and coverage records must name the repo's own copy of a file, never the copy inside a linked git
/// worktree (<c>.claude/worktrees/&lt;name&gt;/…</c>). Such a path resolves while the branch is checked out and
/// dies the moment the worktree is removed, so it is broken on arrival — but a naive "is it under the root?"
/// test passes it, because the worktrees sit <em>inside</em> the main checkout.
/// </summary>
[TestClass]
[CoversNode("product-snaplinks")]
public class GitWorktreePathTests
{
    private string _root = string.Empty;
    private string _worktree = string.Empty;

    /// <summary>A main checkout with one linked worktree nested under <c>.claude/worktrees/</c> — the real shape.</summary>
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"wtpath_{Guid.NewGuid():N}");
        _worktree = Path.Combine(_root, ".claude", "worktrees", "feature-x");
        Directory.CreateDirectory(Path.Combine(_worktree, "src"));

        // git's own metadata: <main>/.git/worktrees/<name>/gitdir points at the worktree's ".git" file.
        var meta = Path.Combine(_root, ".git", "worktrees", "feature-x");
        Directory.CreateDirectory(meta);
        File.WriteAllText(Path.Combine(meta, "gitdir"), Path.Combine(_worktree, ".git").Replace('\\', '/'));

        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Widget.cs"), "namespace Demo; public class Widget { }");
        File.WriteAllText(Path.Combine(_worktree, "src", "Widget.cs"), "namespace Demo; public class Widget { }");
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string WorktreeDoc = ".claude/worktrees/feature-x/src/Widget.cs";

    private static ProductState TreeWith(params Snaplink[] links) => new()
    {
        Nodes = new Dictionary<string, ProductNode> { ["n"] = new() { Title = "Node", Snaplinks = [.. links] } }
    };

    private static Snaplink Code(string doc) => new() { Type = "code", Doc = doc };

    [TestMethod]
    public void Roots_ReadsEveryLinkedWorktreeFromGitMetadata()
    {
        var roots = GitWorktrees.Roots(_root);

        Assert.AreEqual(1, roots.Count);
        Assert.AreEqual(_worktree.Replace('\\', '/'), roots[0]);
    }

    [TestMethod]
    public void Roots_IsEmpty_ForACheckoutWithNoWorktrees()
    {
        var plain = Path.Combine(Path.GetTempPath(), $"wtplain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(plain);
        try { Assert.AreEqual(0, GitWorktrees.Roots(plain).Count); }
        finally { Directory.Delete(plain, recursive: true); }
    }

    [TestMethod]
    public void TryReRoot_RewritesAWorktreePath_AndLeavesARepoPathAlone()
    {
        var roots = GitWorktrees.Roots(_root);

        Assert.IsTrue(GitWorktrees.TryReRoot(WorktreeDoc, _root, roots, out var reRooted));
        Assert.AreEqual("src/Widget.cs", reRooted);

        Assert.IsFalse(GitWorktrees.TryReRoot("src/Widget.cs", _root, roots, out var untouched),
            "the repo's own path is already canonical");
        Assert.AreEqual("src/Widget.cs", untouched);
    }

    [TestMethod]
    public void TryReRoot_HandlesAnAbsolutePath_AsAPdbRecordsIt()
    {
        var abs = Path.Combine(_worktree, "src", "Widget.cs");

        Assert.IsTrue(GitWorktrees.TryReRoot(abs, _root, GitWorktrees.Roots(_root), out var reRooted));
        Assert.AreEqual("src/Widget.cs", reRooted);
    }

    [TestMethod]
    public void Validator_FlagsAWorktreeDoc_EvenThoughTheFileResolves()
    {
        var report = SnaplinkValidator.Validate(TreeWith(Code(WorktreeDoc)), _root);

        Assert.AreEqual(1, report.IssueCount, "the file exists today — the point is that it will not tomorrow");
        Assert.AreEqual(IntegrityKind.WorktreePath, report.Issues[0].Kind);
        StringAssert.Contains(report.Issues[0].Detail, "src/Widget.cs", "the message names the path to use instead");
    }

    [TestMethod]
    public void Validator_LeavesARepoPathClean()
    {
        Assert.IsTrue(SnaplinkValidator.Validate(TreeWith(Code("src/Widget.cs")), _root).IsClean);
    }

    [TestMethod]
    public void NormalizeWorktreePaths_ReRootsNodeAndConcernLinks_AndReportsEachChange()
    {
        var state = TreeWith(Code(WorktreeDoc), Code("src/Widget.cs"));
        state.Nodes["n"].Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [Code(WorktreeDoc)] }];

        var changes = SnaplinkRemapper.NormalizeWorktreePaths(state, _root);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual("src/Widget.cs", state.Nodes["n"].Snaplinks![0].Doc);
        Assert.AreEqual("src/Widget.cs", state.Nodes["n"].Snaplinks![1].Doc, "an already-canonical link is untouched");
        Assert.AreEqual("src/Widget.cs", state.Nodes["n"].Concerns![0].Snaplinks![0].Doc);
        Assert.IsTrue(SnaplinkValidator.Validate(state, _root).IsClean, "the repair leaves the tree valid");
    }

    [TestMethod]
    public void NormalizeWorktreePaths_ChangesNothing_WhenThereAreNoWorktrees()
    {
        var plain = Path.Combine(Path.GetTempPath(), $"wtplain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(plain);
        try
        {
            var state = TreeWith(Code(WorktreeDoc));
            Assert.AreEqual(0, SnaplinkRemapper.NormalizeWorktreePaths(state, plain).Count);
            Assert.AreEqual(WorktreeDoc, state.Nodes["n"].Snaplinks![0].Doc);
        }
        finally { Directory.Delete(plain, recursive: true); }
    }
}
