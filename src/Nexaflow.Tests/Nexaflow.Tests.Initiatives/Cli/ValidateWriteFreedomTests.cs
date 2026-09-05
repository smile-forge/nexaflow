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
/// What a verb is allowed to do to the repository it is asked about.
/// <para>
/// <c>validate</c> is the verb everything runs — the installer's release gate, the Product page, every
/// agent, several times a session. It used to fold merged link sets into the shared tree and commit the
/// removal on the way past, so asking a question moved whatever branch the caller happened to be standing
/// on. In the main checkout that is main, and nobody who typed "validate" was expecting a commit.
/// </para>
/// <para>
/// Folding in is <c>promote</c>: named for what it does, run when you mean it, and refused from a working
/// tree that is not the product root, where a pending set is somebody's own work rather than something
/// that has merged.
/// </para>
/// </summary>
[TestClass]
[CoversNode("initiatives-pending")]
public class ValidateWriteFreedomTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    private string _main = "";

    [TestInitialize]
    public void Setup()
    {
        _main = NewDir("nfi-writefree-");
        Repository.Init(_main);
        Directory.CreateDirectory(Path.Combine(_main, "src"));
        Directory.CreateDirectory(Path.Combine(_main, "docs", "product", "pending"));
        File.WriteAllText(Path.Combine(_main, "src", "Alpha.cs"), "namespace D;\npublic class Alpha { }\n");
        File.WriteAllText(Path.Combine(_main, "src", "Beta.cs"), "namespace D;\npublic class Beta { }\n");
        File.WriteAllText(Path.Combine(_main, ".gitignore"), "/.product/\n");

        var store = new ProductStore(_main);
        store.Initialize("WriteFree");
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["root"] = new() { Title = "Root", Children = ["leaf"] },
            ["leaf"] = new()
            {
                Title = "Leaf", Parent = "root",
                Snaplinks = [new Snaplink { Type = "code", Doc = "src/Alpha.cs", Class = "Alpha" }],
            },
        });

        CommitAll("init");
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

    private void CommitAll(string message)
    {
        using var repo = new Repository(_main);
        Commands.Stage(repo, "*");
        repo.Commit(message, Sig, Sig, new CommitOptions { AllowEmptyCommit = true });
    }

    private string Head()
    {
        using var repo = new Repository(_main);
        return repo.Head.Tip.Sha;
    }

    private PendingStore Pending => new(_main, "docs/product");

    /// <summary>A set that has arrived in the main checkout — which is what a merge delivers, and the only
    /// signal there is that its links are now true.</summary>
    private void ArrivedPendingSet(string branch)
    {
        var set = new PendingSnaplinks { Branch = branch };
        set.Capture("leaf", null,
            [new Snaplink { Type = "code", Doc = "src/Alpha.cs", Class = "Alpha" },
             new Snaplink { Type = "code", Doc = "src/Beta.cs", Class = "Beta" }]);
        Pending.Save(set);
        CommitAll("merge " + branch);
    }

    /// <summary>Runs a verb the way the daemon does — with the caller's directory carried on the request,
    /// since that is what decides whether this is the main checkout or somebody's branch.</summary>
    private static int Run(string from, params string[] args)
    {
        using (RequestScope.Begin(Console.Out, Console.Error, from))
            return Program.Execute(args);
    }

    // ── validate reads ──────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_WithAMergedSetWaiting_LeavesTheRepositoryExactlyAsItFoundIt()
    {
        ArrivedPendingSet("feature");
        var before = Head();

        Run(_main, "validate", _main);

        Assert.AreEqual(before, Head(), "validate must not commit — it is the verb every gate and agent runs");
        Assert.AreEqual(1, Pending.All().Count, "and it must not consume the pending set either");
    }

    /// <summary>Not folding them in must not mean not seeing them: the verdict has to be the one the tree
    /// gives once they are promoted, or a read-only validate would just be a less useful one.</summary>
    [TestMethod]
    public void Validate_StillAnswersForTheLinksThatAreWaiting()
    {
        ArrivedPendingSet("feature");
        Assert.AreEqual(1, new ProductStore(_main).Load().Nodes["leaf"].Snaplinks!.Count,
            "the shared tree has not been told yet");

        // The set names src/Beta.cs, which is there, so the overlay resolves and the verdict stays clean.
        Assert.AreEqual(0, Run(_main, "validate", _main), "an overlaid set that resolves is still clean");

        // And a set naming a file that is not there has to fail, or the overlay would be decoration.
        File.Delete(Path.Combine(_main, "src", "Beta.cs"));
        Assert.AreNotEqual(0, Run(_main, "validate", _main), "the waiting links are really being checked");
    }

    // ── promote writes ──────────────────────────────────────────────────────

    [TestMethod]
    public void Promote_IsTheVerbThatFoldsInAndCommits()
    {
        ArrivedPendingSet("feature");
        var before = Head();

        Assert.AreEqual(0, Run(_main, "promote", _main));

        Assert.AreNotEqual(before, Head(), "promote is named for the write, so it makes it");
        Assert.AreEqual(0, Pending.All().Count);
        Assert.AreEqual(2, new ProductStore(_main).Load().Nodes["leaf"].Snaplinks!.Count,
            "and the shared tree now holds what merged");
    }

    [TestMethod]
    public void Promote_DryRun_WritesNothing()
    {
        ArrivedPendingSet("feature");
        var before = Head();

        Assert.AreEqual(0, Run(_main, "promote", _main, "--dry-run"));

        Assert.AreEqual(before, Head());
        Assert.AreEqual(1, Pending.All().Count);
    }

    /// <summary>A pending set reached from another working tree is that branch's own unmerged work. Folding
    /// it into the shared tree from there is the mistake the whole split exists to prevent, and it would
    /// commit the deletion onto the branch as well.</summary>
    [TestMethod]
    public void Promote_FromAnotherWorkingTree_IsRefused()
    {
        ArrivedPendingSet("feature");
        var before = Head();

        var elsewhere = NewDir("nfi-writefree-wt-");
        Repository.Init(elsewhere);

        Assert.AreNotEqual(0, Run(elsewhere, "promote", _main), "the caller is not standing in the product root");
        Assert.AreEqual(before, Head());
        Assert.AreEqual(1, Pending.All().Count);
    }

    // ── the option that described the behaviour is gone with it ─────────────

    [TestMethod]
    public void Validate_NoLongerTakesTheOptionThatTurnedThePromotingOff()
    {
        Assert.AreNotEqual(0, Run(_main, "validate", _main, "--no-promote"),
            "arguments are strict: an option that no longer describes anything is an error, not a no-op");
    }
}
