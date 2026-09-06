using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// What one branch's unmerged snaplinks are allowed to do to the tree every other worktree reads.
/// <para>
/// The shared tree is one file, gitignored, shared by the main checkout and every linked worktree at once.
/// A snaplink recorded there from a branch is a claim about files that exist nowhere else, so every other
/// agent's <c>validate</c> reports it broken — work in progress arriving as a wall of red in a worktree that
/// never touched it. That is what the pending set exists to prevent.
/// </para>
/// <para>
/// It leaked because the deferral was only half a mechanism. The overlay went on at load, whole; taking it
/// off was left to each write, which peeled off only the sets the command's own arguments named. So a verb
/// that names no link at all named nothing to peel, and wrote the branch's entire set into the shared tree.
/// The tests below are one per verb shape that did it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("initiatives-pending")]
public class PendingIsolationTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private const string Branch = "feature-wt";

    private readonly List<string> _temp = [];
    private string _main = "";
    private string _worktree = "";

    [TestInitialize]
    public void Setup()
    {
        _main = NewDir("nfi-isolation-main-");
        Repository.Init(_main);
        Directory.CreateDirectory(Path.Combine(_main, "src"));
        File.WriteAllText(Path.Combine(_main, "src", "Shared.cs"), "namespace D;\npublic class Shared { }\n");
        File.WriteAllText(Path.Combine(_main, ".gitignore"), "/.product/\n");

        var store = new ProductStore(_main);
        store.Initialize("Isolation");
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["root"] = new() { Title = "Root", Children = ["one", "two"] },
            ["one"] = new()
            {
                Title = "One", Parent = "root",
                Concerns = [new ConcernLink { Tag = "tests", Status = Status.Should }],
            },
            ["two"] = new() { Title = "Two", Parent = "root" },
        });

        using (var repo = new Repository(_main))
        {
            Commands.Stage(repo, "*");
            repo.Commit("init", Sig, Sig, new CommitOptions { AllowEmptyCommit = true });
        }

        // A real linked worktree, because that is what the branch detection reads: a .git *file* pointing at
        // <main>/.git/worktrees/<name>. A second independent repository would not resolve back to this
        // product root, and the code paths under test all key off that relationship.
        _worktree = Path.Combine(NewDir("nfi-isolation-wt-"), "tree");
        using (var repo = new Repository(_main))
            repo.Worktrees.Add(Branch, _worktree, isLocked: false);

        // Files only this branch has — the exact claim a snaplink made from a worktree is making.
        Directory.CreateDirectory(Path.Combine(_worktree, "src"));
        File.WriteAllText(Path.Combine(_worktree, "src", "Alpha.cs"), "namespace D;\npublic class Alpha { }\n");
        File.WriteAllText(Path.Combine(_worktree, "src", "Beta.cs"), "namespace D;\npublic class Beta { }\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
            }
            catch { /* best-effort */ }
    }

    private string NewDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _temp.Add(dir);
        return dir;
    }

    /// <summary>Runs a verb the way the daemon does — with the caller's directory carried on the request,
    /// since that is what decides whether this is the main checkout or somebody's branch.</summary>
    private static int Run(string from, params string[] args)
    {
        using (RequestScope.Begin(Console.Out, Console.Error, from))
            return Program.Execute(args);
    }

    private int FromBranch(params string[] args) => Run(_worktree, args);

    /// <summary>The tree as every other worktree reads it — the file, with no overlay of anyone's.</summary>
    private ProductState Shared => new ProductStore(_main).Load();

    private IReadOnlyList<Snaplink> SharedLinks(string nodeId) => Shared.Nodes[nodeId].Snaplinks ?? [];

    private IReadOnlyList<Snaplink> SharedConcernLinks(string nodeId, string tag) =>
        Shared.Nodes[nodeId].Concerns?.FirstOrDefault(c => c.Tag == tag)?.Snaplinks ?? [];

    private PendingSnaplinks Deferred => new PendingStore(_worktree, "docs/product").Load(Branch);

    private void AddLink(string nodeId, string file, string cls, string? concern = null) =>
        Assert.AreEqual(0, FromBranch([
            "add-snaplink", nodeId, _main, "--type", "code", "--doc", $"src/{file}", "--class", cls,
            .. concern is null ? Array.Empty<string>() : ["--concern", concern]]),
            $"add-snaplink {nodeId} should have succeeded");

    // ── the leak, one test per verb shape that had it ───────────────────────

    /// <summary>
    /// The one that did the most damage. A status change names no snaplink, so the old write had nothing to
    /// restore and wrote the state it was handed — which the load had overlaid with the branch's whole set.
    /// </summary>
    [TestMethod]
    public void AVerbThatNamesNoLink_DoesNotWriteTheBranchesDeferredLinksToTheSharedTree()
    {
        AddLink("one", "Alpha.cs", "Alpha");
        Assert.AreEqual(0, SharedLinks("one").Count, "the link is deferred, so the shared tree never sees it");

        Assert.AreEqual(0, FromBranch("set-status", "two", "done", _main));

        Assert.AreEqual(0, SharedLinks("one").Count,
        "set-status must not carry the branch's deferred links into the tree every other worktree reads");
        Assert.AreEqual(Status.Done, Shared.Nodes["two"].Status, "the status itself is a plan, and does land");
    }

    /// <summary>A snaplink verb restored the set its own arguments named — and only that one, so every other
    /// node the branch had already deferred rode along.</summary>
    [TestMethod]
    public void ASecondLinkVerb_DoesNotCarryTheFirstNodesLinksAlong()
    {
        AddLink("one", "Alpha.cs", "Alpha");
        AddLink("two", "Beta.cs", "Beta");

        Assert.AreEqual(0, SharedLinks("two").Count, "the set this command named was always restored");
        Assert.AreEqual(0, SharedLinks("one").Count,
            "and so must every other set the branch has deferred — the overlay went on whole");
    }

    /// <summary>A concern's links are a separate set on the same node, and the overlay covers them too.</summary>
    [TestMethod]
    public void AConcernsLinks_AreDeferredTheSameWay()
    {
        AddLink("one", "Alpha.cs", "Alpha", concern: "tests");

        Assert.AreEqual(0, FromBranch("set-status", "two", "done", _main));

        Assert.AreEqual(0, SharedConcernLinks("one", "tests").Count,
            "a concern link set is deferred like any other, and comes off the write like any other");
    }

    /// <summary>Doctor repairs structure and writes the tree directly, nowhere near the snaplink verbs. It never
    /// went near the restore either, so from a worktree it published the branch's whole set on its way past.</summary>
    [TestMethod]
    public void DoctorFix_DoesNotPublishTheBranchesDeferredLinks()
    {
        AddLink("one", "Alpha.cs", "Alpha");

        // Doctor writes only when it has something to repair, so give it one: a parent whose children list has
        // lost a child that still names it.
        var store = new ProductStore(_main);
        var shared = store.Load();
        shared.Nodes["root"].Children.Remove("two");
        store.SaveTree(shared.Nodes);

        Assert.AreEqual(0, FromBranch("doctor", _main, "--fix"));

        CollectionAssert.Contains(Shared.Nodes["root"].Children.ToArray(), "two", "the repair itself did happen");
        Assert.AreEqual(0, SharedLinks("one").Count,
            "doctor --fix writes the tree, so it is subject to the same rule as every other write");
    }

    // ── and the branch still gets its own view ──────────────────────────────

    /// <summary>Keeping the links out of the shared tree is only half of it: the branch has to go on seeing
    /// its own, or the deferral would just be a way of losing them.</summary>
    [TestMethod]
    public void TheBranchStillSeesEverythingItHasDeferred()
    {
        AddLink("one", "Alpha.cs", "Alpha");
        AddLink("two", "Beta.cs", "Beta");
        Assert.AreEqual(0, FromBranch("set-status", "two", "done", _main));

        var deferred = Deferred;
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, deferred.TouchedNodes.ToArray(),
            "both sets stay recorded against the branch");
        Assert.AreEqual(2, deferred.Targets.Count(), "and Targets names each one, which is what the write peels off");

        Assert.AreEqual(0, FromBranch("validate", _main),
            "validate from the branch overlays them and resolves them against the branch's own files");
    }

    /// <summary>The main checkout is where the links are true once they merge — and until then it must not be
    /// reporting on them at all, cleanly or otherwise.</summary>
    [TestMethod]
    public void TheMainCheckoutSeesNoneOfIt()
    {
        AddLink("one", "Alpha.cs", "Alpha");
        AddLink("two", "Beta.cs", "Beta");
        Assert.AreEqual(0, FromBranch("set-status", "two", "done", _main));

        Assert.AreEqual(0, Run(_main, "validate", _main),
            "src/Alpha.cs exists only on the branch, so a leaked link would fail here — and that failure is "
          + "the noise every other agent was reading as a broken build");
    }
}
