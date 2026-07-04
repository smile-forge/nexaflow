using System.IO;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// <see cref="ProjectsViewModel"/> list/selection/context and the row's text helpers. Projects here carry
/// no backlog items, so the brush/pie code paths (which need a live WPF theme) are never hit.
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

    private ProjectOperations Ops() =>
        new(new ProjectsConfig { EnableProjects = true, ProjectDirectory = _root });

    private void Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name));

    [TestMethod]
    public void Disabled_ShowsNoProjects()
    {
        var vm = new ProjectsViewModel(Ops(), isEnabled: false);
        Assert.IsFalse(vm.IsEnabled);
        Assert.AreEqual(0, vm.ProjectCount);
        StringAssert.Contains(vm.GetContext(), "no projects");
    }

    [TestMethod]
    public void Enabled_LoadsProjects_AndSelectsFirst()
    {
        Folder("alpha");
        Folder("beta");

        var vm = new ProjectsViewModel(Ops(), isEnabled: true);

        Assert.IsTrue(vm.IsEnabled);
        Assert.AreEqual(2, vm.ProjectCount);
        Assert.IsNotNull(vm.SelectedProject);
        StringAssert.Contains(vm.GetContext(), "2 project");
    }

    [TestMethod]
    public void Enabled_DisplayName_FallsBackToFolderName()
    {
        Folder("alpha");
        var vm = new ProjectsViewModel(Ops(), isEnabled: true);
        Assert.AreEqual("alpha", vm.Projects.Single().DisplayName);
    }

    [TestMethod]
    public void OpenProjectCommand_RaisesRequestWithFolder()
    {
        Folder("alpha");
        var vm = new ProjectsViewModel(Ops(), isEnabled: true);
        string? requested = null;
        vm.OpenProjectRequested += f => requested = f;

        vm.OpenProjectCommand.Execute(vm.Projects.Single());

        Assert.AreEqual("alpha", requested);
    }

    [TestMethod]
    public void OpenFilesCommand_RaisesRequestWithFullPath()
    {
        Folder("alpha");
        var vm = new ProjectsViewModel(Ops(), isEnabled: true);
        string? requested = null;
        vm.OpenFilesRequested += p => requested = p;

        vm.OpenFilesCommand.Execute(vm.Projects.Single());

        Assert.AreEqual(Path.Combine(_root, "alpha"), requested);
    }

    // ── ProjectSummaryItem text helpers (pure) ───────────────────────────────

    [TestMethod]
    public void DescriptionPreview_TakesFirstTwoNonEmptyLines()
    {
        var item = new ProjectSummaryItem { Description = "line one\n\nline two\nline three" };
        Assert.AreEqual("line one line two", item.DescriptionPreview);
    }

    [TestMethod]
    public void DescriptionDisplay_UsesPlaceholder_WhenEmpty()
    {
        Assert.AreEqual("This project does not have a description",
            new ProjectSummaryItem { Description = "" }.DescriptionDisplay);
        Assert.AreEqual("real", new ProjectSummaryItem { Description = "real" }.DescriptionDisplay);
    }
}
