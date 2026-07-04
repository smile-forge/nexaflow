using System.IO;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// End-to-end behaviour of <see cref="ProjectOperations"/> over a private temp project directory:
/// project/backlog CRUD plus the transactional file path (which transitively exercises
/// <c>TransactionalFileService</c> — its commit/rollback are not otherwise reachable).
/// </summary>
[TestClass]
public class ProjectOperationsTests
{
    private string _root = "";
    private ProjectOperations _ops = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _ops = new ProjectOperations(new ProjectsConfig { EnableProjects = true, ProjectDirectory = _root });
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string NewFolder(string name = "proj")
    {
        Directory.CreateDirectory(Path.Combine(_root, name));
        return name;
    }

    private string Tx(string folder) => _ops.StartTransaction(folder);

    // ── Listing & loading ────────────────────────────────────────────────────

    [TestMethod]
    public void RootPath_IsConfiguredDirectory()
        => Assert.AreEqual(_root, _ops.RootPath);

    [TestMethod]
    public void GetProjectListTyped_IsEmpty_WhenRootMissing()
    {
        var ops = new ProjectOperations(new ProjectsConfig { ProjectDirectory = Path.Combine(_root, "nope") });
        Assert.AreEqual(0, ops.GetProjectListTyped().Count);
    }

    [TestMethod]
    public void GetProjectListTyped_FallsBackToFolderName_WhenUnnamed()
    {
        NewFolder("alpha");
        var list = _ops.GetProjectListTyped();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(("alpha", "alpha"), list[0]);
    }

    [TestMethod]
    public void GetProjectListTyped_UsesProjectName_WhenSet()
    {
        var f = NewFolder("alpha");
        _ops.ModifyProjectHeader(f, "Cool Project", "desc");
        CollectionAssert.Contains(_ops.GetProjectListTyped(), ("alpha", "Cool Project"));
    }

    [TestMethod]
    public void GetProjectInfo_MissingProjectFile_NamesAfterFolder()
        => Assert.AreEqual("alpha", _ops.GetProjectInfo(NewFolder("alpha")).Name);

    [TestMethod]
    public void GetProjectInfo_EmptyFolderName_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _ops.GetProjectInfo(""));

    [TestMethod]
    public void GetProjectInfo_NonexistentFolder_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _ops.GetProjectInfo("ghost"));

    // ── Metadata ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ModifyProjectHeader_Persists_AndStampsLastUpdate()
    {
        var f = NewFolder();
        _ops.ModifyProjectHeader(f, "Name", "Description");
        var info = _ops.GetProjectInfo(f);
        Assert.AreEqual("Name", info.Name);
        Assert.AreEqual("Description", info.Description);
        Assert.IsNotNull(info.LastUpdate);
    }

    [TestMethod]
    public void Objectives_AddThenClear()
    {
        var f = NewFolder();
        _ops.AddObjectives(f, ["o1", "o2"]);
        CollectionAssert.AreEqual(new[] { "o1", "o2" }, _ops.GetProjectInfo(f).Objectives);
        _ops.ClearObjectives(f);
        Assert.AreEqual(0, _ops.GetProjectInfo(f).Objectives.Count);
    }

    // ── Backlog ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddToDo_AppearsInBacklog()
    {
        var f = NewFolder();
        var id = _ops.AddToDo(f, "Write tests", "now");
        var item = _ops.GetProjectInfo(f).Backlog.Single();
        Assert.AreEqual(id, item.Id);
        Assert.AreEqual("Write tests", item.Title);
        Assert.AreEqual(BacklogStatus.NotStarted, item.Status);
    }

    [TestMethod]
    public void ProgressToDo_AdvancesThroughTheChain()
    {
        var f = NewFolder();
        var id = _ops.AddToDo(f, "x");
        Assert.AreEqual("AwaitingDesign", _ops.ProgressToDo(f, id));
        Assert.AreEqual("AwaitingDesignReview", _ops.ProgressToDo(f, id));
    }

    [TestMethod]
    public void ProgressToDo_MissingId_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _ops.ProgressToDo(NewFolder(), Guid.NewGuid()));

    [TestMethod]
    public void CancelToDo_SetsCancelled()
    {
        var f = NewFolder();
        var id = _ops.AddToDo(f, "x");
        _ops.CancelToDo(f, id);
        Assert.AreEqual(BacklogStatus.Cancelled, _ops.GetProjectInfo(f).Backlog.Single().Status);
    }

    [TestMethod]
    public void ModifyToDoPlans_Persist()
    {
        var f = NewFolder();
        var id = _ops.AddToDo(f, "x");
        _ops.ModifyToDoImpPlan(f, id, "imp");
        _ops.ModifyToDoTestPlan(f, id, "test");
        var item = _ops.GetProjectInfo(f).Backlog.Single();
        Assert.AreEqual("imp", item.ImpPlan);
        Assert.AreEqual("test", item.TestPlan);
    }

    [TestMethod]
    public void GetToDos_Placeholder_WhenEmpty()
        => StringAssert.Contains(_ops.GetToDos(NewFolder()), "No backlog items");

    [TestMethod]
    public void GetProjectDetails_IncludesNameAndBacklogSummary()
    {
        var f = NewFolder();
        _ops.ModifyProjectHeader(f, "Proj", "");
        _ops.AddToDo(f, "task");
        var md = _ops.GetProjectDetails(f);
        StringAssert.Contains(md, "# Proj");
        StringAssert.Contains(md, "active item");
    }

    // ── Transactional file operations ────────────────────────────────────────

    [TestMethod]
    public void StartTransaction_ReturnsScopedId()
    {
        var f = NewFolder("proj");
        StringAssert.StartsWith(Tx(f), "FT_proj_");
    }

    [TestMethod]
    public void StartTransaction_NonexistentFolder_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _ops.StartTransaction("ghost"));

    [TestMethod]
    public void WriteNewProjectFile_CreatesFile_OnCommit()
    {
        var f = NewFolder();
        var tx = Tx(f);
        _ops.WriteNewProjectFile(tx, f, "docs/readme.md", "hello");
        _ops.CompleteTransaction(tx);

        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(_root, f, "docs", "readme.md")));
    }

    [TestMethod]
    public void WriteNewProjectFile_ExistingFile_Throws()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "exists.txt"), "x");
        var tx = Tx(f);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ops.WriteNewProjectFile(tx, f, "exists.txt", "y"));
    }

    [TestMethod]
    public void ReplaceProjectFileContents_UniqueMatch_Replaces()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "a.txt"), "the quick brown fox");
        var tx = Tx(f);
        _ops.ReplaceProjectFileContents(tx, f, "a.txt", "quick", "slow");
        _ops.CompleteTransaction(tx);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_root, f, "a.txt")), "the slow brown fox");
    }

    [TestMethod]
    public void ReplaceProjectFileContents_NoMatch_Throws()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "a.txt"), "abc");
        var tx = Tx(f);
        Assert.ThrowsExactly<ArgumentException>(() => _ops.ReplaceProjectFileContents(tx, f, "a.txt", "zzz", "q"));
    }

    [TestMethod]
    public void ReplaceProjectFileContents_AmbiguousMatch_Throws()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "a.txt"), "dup dup");
        var tx = Tx(f);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ops.ReplaceProjectFileContents(tx, f, "a.txt", "dup", "q"));
    }

    [TestMethod]
    public void DeleteProjectFile_RemovesIt()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "gone.txt"), "x");
        var tx = Tx(f);
        _ops.DeleteProjectFile(tx, f, "gone.txt");
        _ops.CompleteTransaction(tx);
        Assert.IsFalse(File.Exists(Path.Combine(_root, f, "gone.txt")));
    }

    [TestMethod]
    public void MoveProjectFile_ExistingDestination_Throws()
    {
        var f = NewFolder();
        File.WriteAllText(Path.Combine(_root, f, "src.txt"), "x");
        File.WriteAllText(Path.Combine(_root, f, "dst.txt"), "y");
        var tx = Tx(f);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ops.MoveProjectFile(tx, f, "src.txt", "dst.txt"));
    }

    [TestMethod]
    public void Abandon_RestoresModifiedFile()
    {
        var f = NewFolder();
        var path = Path.Combine(_root, f, "a.txt");
        File.WriteAllText(path, "original");
        var tx = Tx(f);
        _ops.ReplaceProjectFileContents(tx, f, "a.txt", "original", "changed");
        Assert.AreEqual("changed", File.ReadAllText(path));

        _ops.AbandonTransaction(tx);
        Assert.AreEqual("original", File.ReadAllText(path), "rollback should restore the backup");
    }

    [TestMethod]
    public void Abandon_RemovesNewlyWrittenFile()
    {
        var f = NewFolder();
        var tx = Tx(f);
        _ops.WriteNewProjectFile(tx, f, "fresh.txt", "data");
        var path = Path.Combine(_root, f, "fresh.txt");
        Assert.IsTrue(File.Exists(path));

        _ops.AbandonTransaction(tx);
        Assert.IsFalse(File.Exists(path), "rollback should remove files created in the transaction");
    }

    [TestMethod]
    public void WriteNewProjectFile_PathTraversal_Throws()
    {
        var f = NewFolder();
        var tx = Tx(f);
        Assert.ThrowsExactly<ArgumentException>(() => _ops.WriteNewProjectFile(tx, f, @"..\..\escape.txt", "x"));
    }

    [TestMethod]
    public void GetProjectFileContents_Missing_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => _ops.GetProjectFileContents(NewFolder(), "absent.txt"));
}
