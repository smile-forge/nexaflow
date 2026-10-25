using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Which files a graph no longer describes — the one judgement a full scan and a watcher's report both rest on. A file
/// is judged by the stamp its last read recorded, so a scan costs a stat per file and reads nothing; the cases here are
/// the ones where getting that judgement wrong either reads the repository for nothing or answers from code as it was.
/// </summary>
[TestClass]
[CoversNode("graph-archive")]
public class GraphFreshnessTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(TestTemp.Root, "nexaflow-freshness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "Proj"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Writes a file and returns its repo-relative path.</summary>
    private string File_(string name, string text = "public class C { }\n")
    {
        var rel = $"src/Proj/{name}";
        File.WriteAllText(Full(rel), text);
        return rel;
    }

    private string Full(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// A graph that read <paramref name="read"/> just now — each with material and a stamp of the file as it stands — and
    /// lists <paramref name="listed"/> by name only, the way it lists a project file or an asset.
    /// </summary>
    private GraphSnapshot Read(string[] read, params string[] listed)
    {
        var snapshot = new GraphSnapshot();
        foreach (var rel in read)
        {
            snapshot.Cache.Files[rel] = new FileContribution { Hash = "h:" + rel };
            if (File.Exists(Full(rel)))
            {
                var info = new FileInfo(Full(rel));
                snapshot.Files[rel] = new FileStamp("h:" + rel, info.Length, info.LastWriteTimeUtc);
            }
        }
        foreach (var rel in listed)
            snapshot.Graph.Nodes.Add(new GraphNode { Id = "file:" + rel, Type = NodeType.File, FilePath = rel, Source = rel });
        return snapshot;
    }

    private void TouchedSince(string rel) => File.SetLastWriteTimeUtc(Full(rel), DateTime.UtcNow.AddMinutes(5));

    [TestMethod]
    public void NothingMoved_FindsNothing()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");

        Assert.IsTrue(GraphFreshness.Scan(Read([proj, code]), _root).IsEmpty);
    }

    [TestMethod]
    public void AFileWrittenSinceItWasRead_IsStale_WithItsEntryAsTheScanFoundIt()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        var snapshot = Read([proj, code]);
        TouchedSince(code);

        var drift = GraphFreshness.Scan(snapshot, _root);

        CollectionAssert.AreEqual(new[] { code }, drift.Stale.Keys.ToList());
        Assert.AreEqual(File.GetLastWriteTimeUtc(Full(code)), drift.Stale[code].ModifiedUtc,
            "the entry as found is what a file vouched for without reading is stamped with");
    }

    /// <summary>
    /// A stamp is the only evidence that the graph describes a file. A code file with none has never been read — an archive
    /// from before stamps, or a read the file moved under — so it is read once, and trusted from then on.
    /// </summary>
    [TestMethod]
    public void ACodeFileNeverStamped_IsStale()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var code     = File_("C.cs");
        var snapshot = Read([proj, code]);
        snapshot.Files.Clear();

        CollectionAssert.Contains(GraphFreshness.Scan(snapshot, _root).Stale.Keys.ToList(), code);
    }

    /// <summary>An asset is listed by name and never read, so there is no stamp to want: judging it stale would read every
    /// image in the repository on every scan, to learn nothing the graph uses.</summary>
    [TestMethod]
    public void AFileOnlyListed_IsNeverStale()
    {
        var proj  = File_("Proj.csproj", "<Project />\n");
        var image = File_("logo.png", "not really a png");

        Assert.IsTrue(GraphFreshness.Scan(Read([proj], image), _root).IsEmpty);
    }

    [TestMethod]
    public void AFileInAProjectOnlyTheSolutionNames_IsFoundAdded()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        Directory.CreateDirectory(Path.Combine(_root, "src", "New"));
        File.WriteAllText(Path.Combine(_root, "src", "New", "New.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(_root, "src", "New", "Fresh.cs"), "public class Fresh { }\n");
        File.WriteAllText(Path.Combine(_root, "App.slnx"),
            "<Solution>\n  <Project Path=\"src/Proj/Proj.csproj\" />\n  <Project Path=\"src/New/New.csproj\" />\n</Solution>\n");

        var drift = GraphFreshness.Scan(Read([proj, code]), _root);

        CollectionAssert.Contains(drift.Added.ToList(), "src/New/Fresh.cs",
                                  "the graph has never read New.csproj, and the solution names it");
    }

    /// <summary>
    /// A file the graph knows and this tree does not is reported as absent, never as stale: re-reading a file that is not
    /// there can only prune it, and whether that is right — a tree's own graph, or one read on another tree's behalf — is
    /// the graph's owner's call.
    /// </summary>
    [TestMethod]
    public void AFileNotInThisTree_IsAbsent_NotStale()
    {
        var proj  = File_("Proj.csproj", "<Project />\n");
        var mine  = File_("C.cs");
        var other = "src/Nexaflow.Maths/Latex/TexNode.cs";

        var drift = GraphFreshness.Scan(Read([proj, mine, other]), _root);

        CollectionAssert.AreEqual(new[] { other }, drift.Absent.ToList());
        Assert.AreEqual(0, drift.Stale.Count);
    }

    [TestMethod]
    public void ANewFileBesideAProject_IsAdded()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var code     = File_("C.cs");
        var snapshot = Read([proj, code]);
        var added    = File_("New.cs");

        CollectionAssert.Contains(GraphFreshness.Scan(snapshot, _root).Added.ToList(), added);
    }

    /// <summary>The walk for additions is scoped to the directories the solution's projects live in — a pinned submodule
    /// cannot gain a file without a deliberate bump, and scanning for one costs more than everything else together.</summary>
    [TestMethod]
    public void ANewFileFarFromAnyProject_IsNotLookedFor()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var code     = File_("C.cs");
        var snapshot = Read([proj, code]);

        Directory.CreateDirectory(Path.Combine(_root, "external", "vendor"));
        File.WriteAllText(Path.Combine(_root, "external", "vendor", "Vendor.cs"), "class V { }\n");

        Assert.AreEqual(0, GraphFreshness.Scan(snapshot, _root).Added.Count);
    }

    /// <summary>What a watcher reported is all that is judged: the point of watching is not looking at the rest.</summary>
    [TestMethod]
    public void Of_JudgesOnlyThePathsItIsGiven()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var one      = File_("One.cs");
        var two      = File_("Two.cs");
        var snapshot = Read([proj, one, two]);
        TouchedSince(one);
        TouchedSince(two);

        var drift = GraphFreshness.Of(snapshot, _root, [Full(one)]);

        CollectionAssert.AreEqual(new[] { one }, drift.Stale.Keys.ToList());
    }

    /// <summary>A folder deleted or renamed whole is one notification, by the folder's name — which has to stand for every
    /// file the graph knows beneath it.</summary>
    [TestMethod]
    public void Of_AFolderThatWentWhole_TakesEveryFileBeneathIt()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        Directory.CreateDirectory(Path.Combine(_root, "src", "Proj", "Inner"));
        var a = File_("Inner/A.cs");
        var b = File_("Inner/B.cs");
        var snapshot = Read([proj, a, b]);
        Directory.Delete(Path.Combine(_root, "src", "Proj", "Inner"), recursive: true);

        var drift = GraphFreshness.Of(snapshot, _root, [Path.Combine(_root, "src", "Proj", "Inner")]);

        CollectionAssert.AreEquivalent(new[] { a, b }, drift.Absent.ToList());
    }

    [TestMethod]
    public void Of_ANewCodeFileReported_IsAdded_AndAnythingElseIsNot()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var snapshot = Read([proj]);
        var code     = File_("Fresh.cs");
        var notes    = File_("notes.txt", "not code");

        var drift = GraphFreshness.Of(snapshot, _root, [Full(code), Full(notes)]);

        CollectionAssert.AreEqual(new[] { code }, drift.Added.ToList());
    }

    /// <summary>
    /// A file saved in a folder makes the folder report a write too, and walking the folder for each such report is what made
    /// the graph's own edits cost a look at their whole directory. Written to, a folder stands for nothing — its entries come
    /// by their own names; created or renamed in, it is the only report there is of what came with it.
    /// </summary>
    [TestMethod]
    public void Of_ABatch_TakesAFolderOnlyWrittenTo_ForNothing_AndOneThatArrived_ForWhatCame()
    {
        var proj     = File_("Proj.csproj", "<Project />\n");
        var snapshot = Read([proj]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "Proj", "Moved"));
        var came     = File_("Moved/Came.cs");
        var folder   = Path.Combine(_root, "src", "Proj", "Moved");

        var written = GraphFreshness.Of(snapshot, _root, new TreeChangeBatch([folder], false, new HashSet<string>()));
        var arrived = GraphFreshness.Of(snapshot, _root,
                                        new TreeChangeBatch([folder], false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder }));

        Assert.IsTrue(written.IsEmpty, "a folder written to says nothing its entries do not say themselves");
        CollectionAssert.AreEqual(new[] { came }, arrived.Added.ToList());
    }
}
