using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// The warm graph, and the one judgement it has to get right: how much drift to fold in before folding in
/// more is a rebuild.
/// <para>
/// This is where the resident process earns its keep and where it can most easily be wrong. Refresh nothing
/// and it answers from stale data; refresh everything and asking for one node re-parses the repository —
/// which is exactly what shipped first, because a worktree seeded from the main checkout has every file
/// stamped at checkout and an archive built before that, so all six thousand read as changed. The rule this
/// codebase already had for that case is the right one, and these hold it: drift is reported, never acted on.
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
        _root = Directory.CreateTempSubdirectory("nexa-workspace-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        _store = new ProductStore(_root);
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_root, recursive: true); } catch { } }

    /// <summary>A source file plus the archive that claims to describe it, stamped as it is right now.</summary>
    private void Seed(int files, bool withStamps = true)
    {
        var cache  = new GraphCache();
        var stamps = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        var graph  = new KnowledgeGraph();

        for (var i = 0; i < files; i++)
        {
            var rel  = $"src/File{i}.cs";
            var full = Path.Combine(_root, "src", $"File{i}.cs");
            File.WriteAllText(full, $"namespace N;\r\n\r\npublic class File{i}\r\n{{\r\n    public int A() => {i};\r\n}}\r\n");

            cache.Files[rel] = new FileContribution { Hash = "h" + i };
            graph.Nodes.Add(new GraphNode { Id = $"code:{rel}#T:File{i}", Type = "type", Label = $"File{i}", FilePath = rel });

            var info = new FileInfo(full);
            stamps[rel] = withStamps
                ? new FileStamp("h" + i, info.Length, info.LastWriteTimeUtc)
                : new FileStamp("h" + i, 0, default);
        }

        GraphArchive.Write(_store.GraphFilePath, new GraphSnapshot { Graph = graph, Cache = cache, Files = stamps });
    }

    private GraphWorkspace Workspace() => new(_store, _root, null);

    private void Touch(int i)
    {
        var full = Path.Combine(_root, "src", $"File{i}.cs");
        File.WriteAllText(full, $"namespace N;\r\n\r\npublic class File{i}\r\n{{\r\n    public int A() => {i};\r\n    public int B() => 1;\r\n}}\r\n");
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddSeconds(5));
    }

    [TestMethod]
    public void AnArchiveThatWasNeverBuilt_ReadsAsNothingRatherThanAnEmptyGraph()
    {
        Assert.IsNull(Workspace().Graph, "no graph is a state the caller has to be told about, not an empty one");
        Assert.IsFalse(Workspace().Exists);
    }

    [TestMethod]
    public void AFileThatHasNotChanged_IsNotReParsed()
    {
        Seed(3);
        var workspace = Workspace();

        Assert.IsNotNull(workspace.Graph);
        Assert.AreEqual(0, workspace.Drifted, "nothing moved, so nothing is behind");
    }

    /// <summary>The point of holding it warm: an edit made since the archive was written is folded in, so
    /// the next answer is about the file as it is now.</summary>
    [TestMethod]
    public void AHandfulOfChangedFiles_AreFoldedIn()
    {
        Seed(5);
        Touch(2);

        var workspace = Workspace();
        Assert.IsNotNull(workspace.Graph);

        Assert.AreEqual(0, workspace.Drifted, "a working session's worth of drift is absorbed, not reported");
    }

    /// <summary>
    /// The regression that matters. A seeded worktree has everything looking changed, and refreshing it all
    /// is a build nobody asked for — minutes, triggered by a query. It must report instead.
    /// </summary>
    [TestMethod]
    public void MoreDriftThanASession_IsReportedRatherThanRebuilt()
    {
        Seed(260);
        for (var i = 0; i < 260; i++) Touch(i);

        var workspace = Workspace();
        var graph     = workspace.Graph;

        Assert.IsNotNull(graph);
        Assert.AreEqual(260, workspace.Drifted,
            "every file moved and none was folded in — that is a rebuild, and it is the caller's decision");
    }

    /// <summary>
    /// An archive written before stamps were recorded has none, and treating unknown as changed is how the
    /// rebuild-by-accident happened. Unknown falls back to the archive's own write time, which is what the
    /// freshness report has always used.
    /// </summary>
    [TestMethod]
    public void AnArchiveWithNoStamps_JudgesByItsOwnWriteTime_NotByAssumingTheWorst()
    {
        Seed(5, withStamps: false);

        var workspace = Workspace();
        Assert.IsNotNull(workspace.Graph);

        Assert.AreEqual(0, workspace.Drifted,
            "files older than the archive are current, however little the archive says about them");
    }

    [TestMethod]
    public void AChangeRecordedOnTheWorkspace_ReachesDiskOnFlush()
    {
        Seed(2);
        var workspace = Workspace();

        var snapshot = workspace.Current();
        Assert.IsNotNull(snapshot);
        snapshot!.Graph.Nodes.Add(new GraphNode { Id = "code:src/New.cs#T:New", Type = "type", Label = "New" });

        workspace.MarkChanged();
        workspace.Flush();

        var back = GraphArchive.ReadGraph(_store.GraphFilePath);
        Assert.IsNotNull(back);
        Assert.IsTrue(back!.Nodes.Exists(n => n.Id == "code:src/New.cs#T:New"));
    }

    /// <summary>Written on every flush, so the next process can tell in one stat apiece what to re-read —
    /// the thing that stops the fallback above being needed twice.</summary>
    [TestMethod]
    public void FlushingRecordsWhatEachFileLookedLike()
    {
        Seed(3, withStamps: false);
        var workspace = Workspace();

        Assert.IsNotNull(workspace.Current());
        workspace.MarkChanged();
        workspace.Flush();

        var stamps = GraphArchive.ReadFileIndex(_store.GraphFilePath);
        Assert.IsNotNull(stamps);
        Assert.IsTrue(stamps!["src/File0.cs"].IsKnown, "a flush turns an unknown stamp into a real one");
        Assert.AreEqual(new FileInfo(Path.Combine(_root, "src", "File0.cs")).Length,
                        stamps["src/File0.cs"].Length);
    }

    /// <summary>
    /// A file the archive knows and this tree does not is another checkout's — a submodule, a branch that has
    /// not merged — and the archive is shared. Forgetting it on sight would have each tree quietly delete the
    /// others' code from a graph they all read.
    /// </summary>
    [TestMethod]
    public void AFileThisTreeDoesNotHave_IsLeftAlone()
    {
        Seed(3);
        File.Delete(Path.Combine(_root, "src", "File1.cs"));

        var workspace = Workspace();
        var graph     = workspace.Graph;

        Assert.IsNotNull(graph);
        Assert.IsTrue(graph!.Nodes.Exists(n => n.Id == "code:src/File1.cs#T:File1"),
                      "absent is reported, never acted on");
        Assert.AreEqual(0, workspace.Drifted);
    }
}
