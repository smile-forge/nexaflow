using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// The warm graph: what a reader is handed, and how it is kept describing the tree.
/// <para>
/// This is where the resident process earns its keep and where it can most easily be wrong. Every way it has been wrong
/// is here — a stamp written for a file nobody read, a change merged in by git that the graph only ever reported, a
/// reader handed a graph another command was half-way through changing.
/// </para>
/// </summary>
[TestClass]
[CoversNode("graph-build")]
public class GraphWorkspaceTests
{
    private string _root = "";
    private ProductStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = TestTemp.CreateSubdirectory("nexa-workspace-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        _store = new ProductStore(_root);
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_root, recursive: true); } catch { } }

    /// <summary>Source files under <paramref name="codeRoot"/> plus the archive in <paramref name="store"/> that claims to
    /// describe them, stamped as they are right now.</summary>
    private void Seed(int files, bool withStamps = true, string? codeRoot = null, ProductStore? store = null)
    {
        codeRoot ??= _root;
        Directory.CreateDirectory(Path.Combine(codeRoot, "src"));

        var cache  = new GraphCache();
        var stamps = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        var graph  = new KnowledgeGraph();

        for (var i = 0; i < files; i++)
        {
            var rel  = $"src/File{i}.cs";
            var full = Path.Combine(codeRoot, "src", $"File{i}.cs");
            File.WriteAllText(full, $"namespace N;\r\n\r\npublic class File{i}\r\n{{\r\n    public int A() => {i};\r\n}}\r\n");

            cache.Files[rel] = new FileContribution { Hash = "h" + i };
            graph.Nodes.Add(new GraphNode { Id = $"code:{rel}#T:File{i}", Type = "type", Label = $"File{i}", FilePath = rel, Source = rel });

            var info = new FileInfo(full);
            if (withStamps) stamps[rel] = new FileStamp("h" + i, info.Length, info.LastWriteTimeUtc);
        }

        GraphArchive.Write((store ?? _store).GraphFilePath, new GraphSnapshot { Graph = graph, Cache = cache, Files = stamps });
    }

    private GraphWorkspace Workspace(bool watch = false) => new(_store, _root, null, watch: watch);

    private void Touch(int i, string? codeRoot = null)
    {
        var full = Path.Combine(codeRoot ?? _root, "src", $"File{i}.cs");
        File.WriteAllText(full, $"namespace N;\r\n\r\npublic class File{i}\r\n{{\r\n    public int A() => {i};\r\n    public int B() => 1;\r\n}}\r\n");
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddSeconds(5));
    }

    private static bool Declares(KnowledgeGraph graph, string rel, string member) =>
        graph.Nodes.Exists(n => n.FilePath == rel && n.Label == member);

    [TestMethod]
    public void AnArchiveThatWasNeverBuilt_ReadsAsNothingRatherThanAnEmptyGraph()
    {
        Assert.IsNull(Workspace().Graph, "no graph is a state the caller has to be told about, not an empty one");
        Assert.IsFalse(Workspace().Exists);
    }

    [TestMethod]
    public void AFileThatHasNotChanged_IsNotReadAgain()
    {
        Seed(3);

        var reading = Workspace().Read();

        Assert.IsNotNull(reading.Snapshot);
        Assert.IsTrue(reading.CatchUp.IsEmpty, "every stamp holds, so there is nothing to read");
    }

    /// <summary>The point of holding it warm: an edit made since the archive was written is folded in, so the next answer
    /// is about the file as it is now.</summary>
    [TestMethod]
    public void AChangedFile_IsFoldedIn()
    {
        Seed(5);
        Touch(2);

        var reading = Workspace().Read();

        CollectionAssert.AreEqual(new[] { "src/File2.cs" }, reading.CatchUp.Read.ToList());
        Assert.IsTrue(Declares(reading.Snapshot!.Graph, "src/File2.cs", "B"));
    }

    /// <summary>
    /// A merge, a pull, a branch switched under a session: that is the session's code now, however much of it there is. A
    /// graph that only reported drift past some count left every query describing the code as it was, and the agents
    /// asking had no way to tell.
    /// </summary>
    [TestMethod]
    public void EveryChangedFile_IsFoldedIn_HoweverMany()
    {
        Seed(260);
        for (var i = 0; i < 260; i++) Touch(i);

        var reading = Workspace().Read();

        Assert.AreEqual(260, reading.CatchUp.Read.Count);
        Assert.IsTrue(Declares(reading.Snapshot!.Graph, "src/File259.cs", "B"));
    }

    /// <summary>An archive written before stamps has none, and a stamp is the only thing that says the graph describes a
    /// file — so each is read once, and the stamps that reading takes are what the next process trusts.</summary>
    [TestMethod]
    public void AFileNeverStamped_IsReadOnce_AndTrustedFromThen()
    {
        Seed(5, withStamps: false);

        var first = Workspace();
        Assert.AreEqual(5, first.Read().CatchUp.Read.Count, "the archive's material is not known to be this content");
        first.Flush();

        Assert.IsTrue(Workspace().Read().CatchUp.IsEmpty, "and having been read, every file is stamped");
    }

    [TestMethod]
    public void AChangeMadeThroughTheWorkspace_ReachesDiskOnFlush()
    {
        Seed(2);
        var workspace = Workspace();

        workspace.Mutate(snapshot =>
        {
            snapshot.Graph.Nodes.Add(new GraphNode { Id = "code:src/New.cs#T:New", Type = "type", Label = "New" });
            return 0;
        });
        workspace.Flush();

        var back = GraphArchive.ReadGraph(_store.GraphFilePath);
        Assert.IsNotNull(back);
        Assert.IsTrue(back!.Nodes.Exists(n => n.Id == "code:src/New.cs#T:New"));
    }

    [TestMethod]
    public void ReadingAFile_RecordsWhatItLookedLike()
    {
        Seed(3, withStamps: false);
        var workspace = Workspace();

        Assert.IsNotNull(workspace.Current());
        workspace.Flush();

        var stamps = GraphArchive.ReadFileIndex(_store.GraphFilePath);
        Assert.IsNotNull(stamps);
        Assert.IsTrue(stamps!["src/File0.cs"].IsKnown, "a read turns an unknown stamp into a real one");
        Assert.AreEqual(new FileInfo(Path.Combine(_root, "src", "File0.cs")).Length, stamps["src/File0.cs"].Length);
    }

    /// <summary>
    /// A stamp is a claim that the graph describes the file as it now stands, so only reading the file can make one. A flush
    /// that stamped every file from its directory entry vouched for files nobody had read: a change landed outside nfi —
    /// a merge, a checkout — became invisible the moment anything else on the tree was saved, and stayed invisible until a
    /// full build.
    /// </summary>
    [TestMethod]
    public void AFlush_NeverVouchesForAFileItDidNotRead()
    {
        Seed(3);
        Touch(0);

        var workspace = Workspace();
        Assert.IsNotNull(workspace.Current());         // File0 is read and folded in, so there is something to save
        Touch(1);                                      // and File1 changes after that, outside nfi
        workspace.Flush();

        var saved = GraphArchive.Read(_store.GraphFilePath)!;
        foreach (var i in new[] { 0, 1 })
        {
            var rel  = $"src/File{i}.cs";
            var full = Path.Combine(_root, "src", $"File{i}.cs");
            var info = new FileInfo(full);
            if (!saved.Files[rel].Matches(info.Length, info.LastWriteTimeUtc)) continue;   // reads as changed: nothing was claimed

            Assert.AreEqual(Hashing.Md5(SnaplinkTargets.ReadText(full)!), saved.Cache.Files[rel].Hash,
                $"{rel} is stamped as current, so the graph must describe what it now holds");
        }
    }

    /// <summary>
    /// The main checkout's graph is also read on other trees' behalf, so a file it knows and this tree does not proves
    /// nothing — it is left alone. A tree's own graph is the opposite: a file gone from the tree is gone from the graph.
    /// </summary>
    [TestMethod]
    public void AFileGone_IsLeftInASharedGraph_AndDroppedFromATreesOwn()
    {
        Seed(3);
        File.Delete(Path.Combine(_root, "src", "File1.cs"));

        var shared = Workspace().Read();
        Assert.AreEqual(0, shared.CatchUp.Dropped.Count);
        Assert.IsTrue(shared.Snapshot!.Graph.Nodes.Exists(n => n.Id == "code:src/File1.cs#T:File1"));

        var tree  = TestTemp.CreateSubdirectory("nexa-workspace-tree-").FullName;
        var store = new ProductStore(_root, "tree");
        try
        {
            Seed(3, codeRoot: tree, store: store);
            File.Delete(Path.Combine(tree, "src", "File1.cs"));

            var own = new GraphWorkspace(store, _root, tree).Read();
            CollectionAssert.AreEqual(new[] { "src/File1.cs" }, own.CatchUp.Dropped.ToList());
            Assert.IsFalse(own.Snapshot!.Graph.Nodes.Exists(n => n.Id == "code:src/File1.cs#T:File1"));
        }
        finally { try { Directory.Delete(tree, recursive: true); } catch { } }
    }

    /// <summary>
    /// The failure this pair exists for. The snapshot used to be read once and kept for the life of the
    /// process, so a build run anywhere else wrote a correct archive that was then never looked at again.
    /// It did not present as staleness: queries answered "no nodes match" for types that were plainly
    /// there, and the freshness line reported identical drift after every rebuild, because the build fixed
    /// the file and the answer came from memory.
    /// </summary>
    [TestMethod]
    public void AnArchiveRebuiltByAnotherProcess_IsPickedUp()
    {
        Seed(3);
        var workspace = Workspace();
        Assert.AreEqual(3, workspace.Graph!.Nodes.Count, "the archive as it stood when first read");

        Seed(6);   // another process rebuilds it underneath

        Assert.AreEqual(6, workspace.Graph!.Nodes.Count,
            "the archive on disk is no longer the one this version came from, so it is re-read");
    }

    /// <summary>
    /// The other half, and why this is a stamp rather than a reload-every-time: a workspace that wrote the
    /// archive itself already holds exactly what is in it. Re-reading there is pure cost on the common
    /// path — one daemon, doing its own work.
    /// </summary>
    [TestMethod]
    public void AWorkspaceThatWroteTheArchiveItself_DoesNotReReadIt()
    {
        Seed(3);
        var workspace = Workspace();
        Assert.IsNotNull(workspace.Graph);

        var built = GraphBuilder.BuildSnapshot(new ProductState(), _root, new GraphBuildOptions { GeneratedAt = "T" }, null);
        workspace.Replace(built);

        Assert.AreSame(built.Graph, workspace.Graph,
            "what it just built is what it holds — a re-read here would swap it for a copy of itself");
    }

    /// <summary>
    /// Two processes holding the same tree's graph — the app beside the resident process, or two builds of nfi. One saves
    /// while the other has unsaved refreshes of its own; the later save used to write its whole snapshot over the archive,
    /// dropping what the first had added. It makes its own changes again on the newer archive instead.
    /// </summary>
    [TestMethod]
    public void ASaveAfterAnotherProcessSaved_KeepsWhatThatProcessAdded()
    {
        Seed(2);
        var mine   = Workspace();
        var theirs = Workspace();

        File.WriteAllText(Path.Combine(_root, "src", "Mine.cs"), "namespace N;\n\npublic class Mine\n{\n}\n");
        CollectionAssert.AreEqual(new[] { "src/Mine.cs" }, mine.Update(["src/Mine.cs"]).Read.ToList());   // changed, not yet saved

        File.WriteAllText(Path.Combine(_root, "src", "Theirs.cs"), "namespace N;\n\npublic class Theirs\n{\n}\n");
        CollectionAssert.AreEqual(new[] { "src/Theirs.cs" }, theirs.Update(["src/Theirs.cs"]).Read.ToList());
        theirs.Flush();                                        // they save first

        mine.Flush();

        var saved = GraphArchive.ReadGraph(_store.GraphFilePath)!;
        Assert.IsTrue(saved.Nodes.Exists(n => n.Id == "code:src/Theirs.cs#T:Theirs"), "what the other process added survives");
        Assert.IsTrue(saved.Nodes.Exists(n => n.Id == "code:src/Mine.cs#T:Mine"), "and so does what this one added");
    }

    /// <summary>
    /// What makes reading without a lock safe: a version handed out is never changed. A command that is half-way through
    /// one while the tree moves on finishes on exactly what it started with, rather than on a graph whose lists another
    /// command is rewriting under it.
    /// </summary>
    [TestMethod]
    public void AReader_KeepsTheVersionItWasHanded_WhileTheGraphMovesOn()
    {
        Seed(3);
        var workspace = Workspace();
        var before    = workspace.Current()!;
        var nodes     = before.Graph.Nodes.Count;
        var held      = before.Graph.Nodes[1];

        Touch(1);
        var after = workspace.Current()!;

        Assert.AreNotSame(before, after);
        Assert.IsTrue(Declares(after.Graph, "src/File1.cs", "B"));
        Assert.AreEqual(nodes, before.Graph.Nodes.Count, "the version a reader holds keeps its own lists");
        Assert.IsFalse(Declares(before.Graph, "src/File1.cs", "B"));
        Assert.AreEqual("h1", before.Cache.Files["src/File1.cs"].Hash, "and its own file table");
        Assert.AreSame(held, before.Graph.Nodes[1]);
    }

    /// <summary>A worktree's first question is answered from the main checkout's graph brought onto the branch, not from
    /// nothing — most of a branch is the same code, and a build is minutes.</summary>
    [TestMethod]
    public void AWorktreeWithNoGraph_StartsFromTheMainCheckouts()
    {
        Seed(3);
        var worktree = new GraphWorkspace(new ProductStore(_root, "branch"), _root, null,
                                          seed: () => GraphArchive.Read(_store.GraphFilePath));

        var reading = worktree.Read();

        Assert.IsTrue(reading.CatchUp.Seeded);
        Assert.AreEqual(3, reading.Snapshot!.Graph.Nodes.Count);

        worktree.Flush();
        Assert.IsTrue(File.Exists(new ProductStore(_root, "branch").GraphFilePath), "and from then on it is the worktree's own");
    }

    /// <summary>
    /// The case watching is for: the change is folded in a moment after it lands, so the reader that comes later finds the
    /// graph current and reads nothing — the cost of a question is a field read, not a look at ten thousand files.
    /// </summary>
    [TestMethod]
    public void AWatchingWorkspace_HasAChangeFoldedInBeforeAnyoneAsks()
    {
        Seed(3);
        using var workspace = Workspace(watch: true);
        Assert.IsNotNull(workspace.Current());

        Touch(2);
        Thread.Sleep(3_000);                           // well past the quiet period and the fold, on a busy test machine too

        var reading = workspace.Read();
        Assert.IsTrue(reading.CatchUp.IsEmpty, "the watcher's catch-up got there first");
        Assert.IsTrue(Declares(reading.Snapshot!.Graph, "src/File2.cs", "B"));
    }
}
