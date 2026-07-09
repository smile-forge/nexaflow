using System.IO;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.ViewModels;

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

    [TestMethod]
    public void Disabled_ShowsNoProjects()
    {
        var vm = new ProjectsViewModel(Config(enabled: false));
        Assert.IsFalse(vm.IsEnabled);
        Assert.AreEqual(0, vm.ProjectCount);
        StringAssert.Contains(vm.GetContext(), "no projects");
    }

    [TestMethod]
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
    public void Enabled_DisplayName_FallsBackToFolderName()
    {
        var cfg = Config();
        Folder(ActiveDir, "alpha");
        var vm = new ProjectsViewModel(cfg);
        Assert.AreEqual("alpha", vm.Projects.Single().DisplayName);
    }

    [TestMethod]
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
    public void DescriptionPreview_TakesFirstTwoNonEmptyLines()
    {
        var item = new ProjectSummaryItem { Description = "line one\n\nline two\nline three" };
        Assert.AreEqual("line one line two", item.DescriptionPreview);
    }
}
