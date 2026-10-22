using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// Git vouching for files so they need not be read: what it may vouch for, and the case it exists for — a worktree whose
/// every file was written by a checkout, starting from the main checkout's graph.
/// </summary>
[TestClass]
[CoversNode("graph-anchor")]
public class GraphAnchorTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);   // git leaves its objects read-only
                Directory.Delete(dir, recursive: true);
            }
            catch { }
    }

    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.Ordinal);

    private static readonly GraphAnchor Anchor = new("c0", Set());

    // ── What git may vouch for ──────────────────────────────────────────────

    [TestMethod]
    public void ATrackedFile_CleanAndUnchangedSinceTheAnchor_IsVouchedFor()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set(), Set());

        Assert.IsTrue(look.Vouches("src/A.cs", Anchor));
    }

    [TestMethod]
    public void AFileWithChangesInTheWorkingTree_IsNot()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set("src/A.cs"), Set());

        Assert.IsFalse(look.Vouches("src/A.cs", Anchor));
    }

    [TestMethod]
    public void AFileChangedBetweenTheAnchorAndHead_IsNot()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set(), Set("src/A.cs"));

        Assert.IsFalse(look.Vouches("src/A.cs", Anchor));
    }

    /// <summary>Git can only say a file is what a commit holds; whether the graph holds what that commit holds is the
    /// anchor's to say, and a file it departs at is one the graph took from the working tree.</summary>
    [TestMethod]
    public void AFileTheAnchorDepartsAt_IsNot()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set(), Set());

        Assert.IsFalse(look.Vouches("src/A.cs", new GraphAnchor("c0", Set("src/A.cs"))));
    }

    [TestMethod]
    public void AnUntrackedFile_IsNot_ForGitKnowsNothingOfIt()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set(), Set());

        Assert.IsFalse(look.Vouches("src/B.cs", Anchor));
    }

    /// <summary>A submodule's files are not in the index one by one — the submodule is, as one entry — so the entry vouches
    /// for what lies in it, and only while the submodule is clean and where the anchor's commit had it.</summary>
    [TestMethod]
    public void AFileInASubmodule_IsVouchedForByTheSubmodule()
    {
        var clean = new GitLook("c1", Set("external/Lib"), Set(), Set());
        var moved = new GitLook("c1", Set("external/Lib"), Set(), Set("external/Lib"));
        var dirty = new GitLook("c1", Set("external/Lib"), Set("external/Lib"), Set());

        Assert.IsTrue(clean.Vouches("external/Lib/src/Thing.cs", Anchor));
        Assert.IsFalse(moved.Vouches("external/Lib/src/Thing.cs", Anchor), "its pinned commit moved");
        Assert.IsFalse(dirty.Vouches("external/Lib/src/Thing.cs", Anchor), "its checkout has changes of its own");
    }

    [TestMethod]
    public void AnAnchorOnACommitThisRepositoryLacks_VouchesForNothing()
    {
        var look = new GitLook("c1", Set("src/A.cs"), Set(), ChangedSince: null);

        Assert.IsFalse(look.Vouches("src/A.cs", Anchor));
    }

    // ── A worktree from the main checkout's graph ───────────────────────────

    /// <summary>
    /// What the anchor is for. A worktree's checkout gives every file a new write time, so every stamp the main checkout's
    /// graph carries disagrees with it — and reading every file to find that almost none changed is the minute a fresh
    /// worktree's first question used to cost. Git says which ones did: the one changed since the commit the graph
    /// describes is read, the rest are vouched for, and the graph is anchored on the worktree's own HEAD from then on.
    /// </summary>
    [TestMethod]
    public void AWorktreeStartedFromTheMainGraph_ReadsOnlyWhatGitSaysDiffers()
    {
        const int files = 300;   // past the point where a catch-up asks git rather than read
        var main = Path.Combine(TestTemp.Root, "nexa-anchor-" + Guid.NewGuid().ToString("N"));
        var tree = main + "-wt";
        _temp.Add(main);
        _temp.Add(tree);

        Directory.CreateDirectory(Path.Combine(main, "src"));
        Repository.Init(main);
        for (var i = 0; i < files; i++)
            File.WriteAllText(Path.Combine(main, "src", $"F{i}.cs"), $"public class F{i}\n{{\n    public int A() => {i};\n}}\n");
        using (var repo = new Repository(main))
        {
            Commands.Stage(repo, "src/*");
            repo.Commit("files", Sig, Sig);
        }

        // The main checkout's graph, built and anchored on the commit it describes.
        var mainStore = new ProductStore(main);
        var built     = GraphBuilder.BuildSnapshot(new ProductState(), main, new GraphBuildOptions { GeneratedAt = "T" }, null);
        new GraphWorkspace(mainStore, main, null).Replace(built);
        var described = GraphArchive.Read(mainStore.GraphFilePath)!.Anchor;
        Assert.IsNotNull(described, "a build is anchored on what it read");

        // One file moves on in a later commit, and a worktree is checked out there.
        File.WriteAllText(Path.Combine(main, "src", "F1.cs"), "public class F1\n{\n    public int A() => 1;\n    public int Later() => 2;\n}\n");
        string head;
        using (var repo = new Repository(main))
        {
            Commands.Stage(repo, "src/F1.cs");
            head = repo.Commit("later", Sig, Sig).Sha;
            repo.Worktrees.Add("feature", tree, isLocked: false);
        }

        var worktree = new GraphWorkspace(new ProductStore(main, "feature"), main, tree,
                                          seed: () => GraphArchive.Read(mainStore.GraphFilePath));
        var reading  = worktree.Read();

        Assert.IsTrue(reading.CatchUp.Seeded);
        CollectionAssert.AreEqual(new[] { "src/F1.cs" }, reading.CatchUp.Read.ToList(), "only what git says differs is read");
        Assert.IsTrue(reading.Snapshot!.Graph.Nodes.Exists(n => n.FilePath == "src/F1.cs" && n.Label == "Later"));
        Assert.AreEqual(head, reading.Snapshot.Anchor?.Commit, "anchored on the worktree's own HEAD — git was asked");
        Assert.IsTrue(worktree.Read().CatchUp.IsEmpty, "and every file vouched for is stamped, so the next look finds nothing");
    }
}
