using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// <see cref="ProjectsViewModel"/> list/selection/context, bucket switching, and the archive/shelf/
/// reactivate move commands (driven through <see cref="FakeShellServices"/>, which performs the real
/// safe move). Projects here carry no backlog items, so the brush code paths (which need a live WPF
/// theme) are never hit.
/// </summary>
[TestClass]
public class ProjectsViewModelTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-projvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string ActiveDir  => Path.Combine(_root, "active");
    private string ShelfDir    => Path.Combine(_root, "shelf");
    private string ArchiveDir  => Path.Combine(_root, "archive");

    private ProjectsConfig Config(bool enabled = true)
    {
        Directory.CreateDirectory(ActiveDir);
        Directory.CreateDirectory(ShelfDir);
        Directory.CreateDirectory(ArchiveDir);
        return new ProjectsConfig
        {
            EnableProjects   = enabled,
            ProjectDirectory = ActiveDir,
            ShelfDirectory   = ShelfDir,
            ArchiveDirectory = ArchiveDir,
        };
    }

    private void Folder(string root, string name) => Directory.CreateDirectory(Path.Combine(root, name));

    /// <summary>Seeds one active-bucket project with a name, description, a completion criterion and a
    /// backlog item — written through <see cref="ProjectOperations"/> at schema v2 so the detail VM opens
    /// editable (not the legacy read-only path).</summary>
    private void Seed(ProjectsConfig cfg, string folder, string name)
    {
        Directory.CreateDirectory(Path.Combine(ActiveDir, folder));
        var ops = new ProjectOperations(cfg, ActiveDir);
        ops.ModifyProjectHeader(folder, name, "The alpha project description.");
        ops.SetCompletionCriteria(folder,
            [new CompletionCriterion { Text = "Ships to production", Status = CompletionStatus.Should }]);
        ops.AddToDo(folder, "Wire the widget", "Markdown notes for wiring the widget.");
    }

    // ── AI integration: enriched context + the read-only tool surface (list + detail pages) ──

    [TestMethod]
    [CoversNode("projects-ai-act")]
    [CoversNode("projects-ai-context")]
    public async Task AiTools_ListAndReadProjects_ThroughToolSurface()
    {
        var cfg = Config();
        Seed(cfg, "alpha", "Alpha");

        // ── List page ──
        var list = new ProjectsViewModel(cfg);

        // aspect 4: scope is the current bucket root so two Projects tabs stay distinguishable when pinned
        Assert.AreEqual(ActiveDir, list.GetSecurityContext());

        // enriched context names the actual project + its backlog summary (not just a bare count)
        StringAssert.Contains(list.GetContext(), "Alpha");
        StringAssert.Contains(list.GetContext(), "1 project");

        var listTools = list.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "list_projects", "read_project" },
            listTools.Select(t => t.Name).ToArray(),
            "the Projects list AI act surface changed — update the tree's projects-ai-act leaves to match");
        Assert.IsTrue(listTools.All(t => t.Safety == ToolSafety.SafeOperation), "all Projects act tools are read-only");

        // list_projects enumerates the loaded projects
        var lp = await listTools.Single(t => t.Name == "list_projects")
                                .InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(lp.IsError);
        StringAssert.Contains(lp.ModelText, "Alpha");

        // read_project (no arg → selected project) returns description + criteria + backlog item detail
        var readTool = listTools.Single(t => t.Name == "read_project");
        var rSel = await readTool.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(rSel.IsError);
        StringAssert.Contains(rSel.ModelText, "The alpha project description.");
        StringAssert.Contains(rSel.ModelText, "Ships to production");
        StringAssert.Contains(rSel.ModelText, "Wire the widget");

        // read_project by folder name works too
        var rByName = await readTool.InvokeAsync(new JsonObject { ["project"] = "alpha" }, CancellationToken.None);
        Assert.IsFalse(rByName.IsError);
        StringAssert.Contains(rByName.ModelText, "Wire the widget");

        // an unknown project is a recoverable error, not an exception
        var rMiss = await readTool.InvokeAsync(new JsonObject { ["project"] = "ghost" }, CancellationToken.None);
        Assert.IsTrue(rMiss.IsError);

        // ── Detail page ──
        var detail = new ProjectDetailViewModel(new ProjectOperations(cfg, ActiveDir), cfg, "alpha");

        // aspect 4: scope is this project's folder path
        Assert.AreEqual(Path.Combine(ActiveDir, "alpha"), detail.GetSecurityContext());

        // enriched context carries the real description, criteria and backlog item — not just a count
        var dctx = detail.GetContext();
        StringAssert.Contains(dctx, "Alpha");
        StringAssert.Contains(dctx, "alpha project description");
        StringAssert.Contains(dctx, "Ships to production");
        StringAssert.Contains(dctx, "Wire the widget");

        var detailTools = detail.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "read_project", "read_backlog_item" },
            detailTools.Select(t => t.Name).ToArray(),
            "the Project Detail AI act surface changed — update the tree's projects-ai-act leaves to match");
        Assert.IsTrue(detailTools.All(t => t.Safety == ToolSafety.SafeOperation), "all Projects act tools are read-only");

        // read_project reads THIS project
        var dRead = await detailTools.Single(t => t.Name == "read_project")
                                     .InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(dRead.IsError);
        StringAssert.Contains(dRead.ModelText, "Wire the widget");

        // read_backlog_item (no arg → first item) returns the item's markdown detail
        var biTool = detailTools.Single(t => t.Name == "read_backlog_item");
        var bi = await biTool.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(bi.IsError);
        StringAssert.Contains(bi.ModelText, "Wire the widget");
        StringAssert.Contains(bi.ModelText, "Markdown notes for wiring the widget.");

        // read_backlog_item by title works too
        var biByTitle = await biTool.InvokeAsync(new JsonObject { ["item"] = "Wire the widget" }, CancellationToken.None);
        Assert.IsFalse(biByTitle.IsError);
        StringAssert.Contains(biByTitle.ModelText, "Markdown notes");
    }

    [TestMethod]
    [CoversNode("projects-list-rows")]
    public void Disabled_ShowsNoProjects()
    {
        var vm = new ProjectsViewModel(Config(enabled: false));
        Assert.IsFalse(vm.IsEnabled);
        Assert.AreEqual(0, vm.ProjectCount);
        StringAssert.Contains(vm.GetContext(), "no projects");
    }

    [TestMethod]
    [CoversNode("projects-list-rows")]
    public void Enabled_LoadsProjects_AndSelectsFirst()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        Folder(ActiveDir, "beta");

        var vm = new ProjectsViewModel(cfg);

        Assert.IsTrue(vm.IsEnabled);
        Assert.AreEqual(2, vm.ProjectCount);
        Assert.IsNotNull(vm.SelectedProject);
        StringAssert.Contains(vm.GetContext(), "2 project");
    }

    [TestMethod]
    [CoversNode("projects-list-rows")]
    public void Enabled_DisplayName_FallsBackToFolderName()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        var vm = new ProjectsViewModel(cfg);
        Assert.AreEqual("alpha", vm.Projects.Single().DisplayName);
    }

    [TestMethod]
    [CoversNode("projects-open")]
    public void OpenProjectCommand_RaisesRequestWithAbsolutePath()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        var vm = new ProjectsViewModel(cfg);
        string? requested = null;
        vm.OpenProjectRequested += p => requested = p;

        vm.OpenProjectCommand.Execute(vm.Projects.Single());

        Assert.AreEqual(Path.Combine(ActiveDir, "alpha"), requested);
    }

    [TestMethod]
    [CoversNode("projects-open")]
    public void OpenFilesCommand_RaisesRequestWithAbsolutePath()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        var vm = new ProjectsViewModel(cfg);
        string? requested = null;
        vm.OpenFilesRequested += p => requested = p;

        vm.OpenFilesCommand.Execute(vm.Projects.Single());

        Assert.AreEqual(Path.Combine(ActiveDir, "alpha"), requested);
    }

    [TestMethod]
    [CoversNode("projects-buckets-tabs")]
    public void SelectedBucket_Shelf_LoadsFromShelfDirectory()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        Folder(ShelfDir, "gamma");
        var vm = new ProjectsViewModel(cfg) { SelectedBucket = ProjectBucket.Shelf };

        Assert.AreEqual(1, vm.ProjectCount);
        Assert.AreEqual("gamma", vm.Projects.Single().DisplayName);
        Assert.IsFalse(vm.CanArchiveOrShelf);
        Assert.IsTrue(vm.CanReactivate);
    }

    [TestMethod]
    [CoversNode("projects-bucket-actions")]
    [CoversNode("projects-buckets")]
    public void ArchiveCommand_MovesFolderToArchive_AndReloads()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        var shell = new FakeShellServices();
        var vm = new ProjectsViewModel(cfg, shell);

        vm.ArchiveCommand.Execute(vm.Projects.Single());

        Assert.AreEqual(1, shell.Moves.Count);
        Assert.IsTrue(Directory.Exists(Path.Combine(ArchiveDir, "alpha")), "folder should now be under archive");
        Assert.IsFalse(Directory.Exists(Path.Combine(ActiveDir, "alpha")), "source should be gone after a safe move");
        Assert.IsFalse(vm.IsMoving);
        Assert.AreEqual(0, vm.ProjectCount, "the active bucket reloads without the moved project");
    }

    [TestMethod]
    [CoversNode("projects-bucket-actions")]
    [CoversNode("projects-buckets")]
    public void ReactivateCommand_MovesFromShelfToProjects()
    {
        var cfg = Config();
        Folder(ShelfDir, "gamma");
        var shell = new FakeShellServices();
        var vm = new ProjectsViewModel(cfg, shell) { SelectedBucket = ProjectBucket.Shelf };

        vm.ReactivateCommand.Execute(vm.Projects.Single());

        Assert.IsTrue(Directory.Exists(Path.Combine(ActiveDir, "gamma")));
        Assert.IsFalse(Directory.Exists(Path.Combine(ShelfDir, "gamma")));
    }

    // ── ProjectSummaryItem text helper (pure) ───────────────────────────────

    [TestMethod]
    [CoversNode("projects-list-rows")]
    public void DescriptionPreview_TakesFirstTwoNonEmptyLines()
    {
        var item = new ProjectSummaryItem { Description = "line one\n\nline two\nline three" };
        Assert.AreEqual("line one line two", item.DescriptionPreview);
    }
}
