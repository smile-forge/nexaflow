using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dotnet.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dotnet;

[TestClass]
[CoversNode("viewlet")]
public class DotnetViewletViewModelTests
{
    private readonly List<string> _temp = [];

    private string TempDir(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexadotnet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "<Project/>");
        _temp.Add(dir);
        return dir;
    }

    /// <summary>A folder holding a real <c>.slnx</c> plus the projects it names — enough for the VM to
    /// resolve a startup project. <paramref name="projects"/> maps a relative project path to its content.</summary>
    private string TempSolution(string solutionName, params (string Path, string Content)[] projects)
    {
        var dir = TempDir();
        foreach (var (rel, content) in projects)
        {
            var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        var entries = string.Concat(projects.Select(p => $"""<Project Path="{p.Path}" />"""));
        File.WriteAllText(Path.Combine(dir, solutionName), $"<Solution>{entries}</Solution>");
        return dir;
    }

    private const string GuiApp = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup></Project>";
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup/></Project>";

    // Constructs the VM and immediately cancels the debounced NuGet check it kicks off on target select.
    private static DotnetViewletViewModel Vm(string dir)
    {
        var vm = new DotnetViewletViewModel(Substitute.For<IShellServices>(), dir);
        vm.CancelPending();
        return vm;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    // ── GetContext ────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetContext_WithTarget_NamesTarget()
        => StringAssert.Contains(Vm(TempDir("Foo.csproj")).GetContext(), "Foo.csproj");

    [TestMethod]
    public void GetContext_NoTarget_ReportsNone()
        => StringAssert.Contains(Vm(TempDir()).GetContext(), "no buildable target");

    // ── ResolveTarget ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveTarget_KnownName_ReturnsThatTarget()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        Assert.AreEqual("Foo.csproj", vm.ResolveTarget("Foo.csproj")!.DisplayName);
    }

    [TestMethod]
    public void ResolveTarget_UnknownName_FallsBackToSelected()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        Assert.AreSame(vm.SelectedTarget, vm.ResolveTarget("nope.csproj"));
    }

    [TestMethod]
    public void ResolveTarget_Null_ReturnsSelected()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        Assert.AreSame(vm.SelectedTarget, vm.ResolveTarget(null));
    }

    // ── GetClientTools ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dotnet-ai-act")]
    public void GetClientTools_ExposesVerbsAndOutdatedCheck()
    {
        var names = Vm(TempDir("Foo.csproj")).GetClientTools().Select(t => t.Name).ToList();

        CollectionAssert.Contains(names, "dotnet_build");
        CollectionAssert.Contains(names, "dotnet_test");
        CollectionAssert.Contains(names, "dotnet_restore");
        CollectionAssert.Contains(names, "dotnet_clean");
        CollectionAssert.Contains(names, "dotnet_check_outdated_packages");
    }

    [TestMethod]
    [CoversNode("dotnet-ai-act")]
    public void GetClientTools_DoesNotExposeRun()
    {
        var names = Vm(TempDir("Foo.csproj")).GetClientTools().Select(t => t.Name).ToList();

        CollectionAssert.DoesNotContain(names, "dotnet_run");
    }

    // ── Run target: a solution can't be `dotnet run`, so it resolves to a startup project ──────

    [TestMethod]
    [CoversNode("dotnet-startup-picker")]
    public void SolutionWithOneRunnableProject_AutoSelectsItAndEnablesRun()
    {
        var vm = Vm(TempSolution("My.slnx", ("App/App.csproj", GuiApp), ("Lib/Lib.csproj", Library)));

        Assert.IsTrue(vm.SelectedTarget!.IsSolution, "a solution is preferred as the selected target");
        Assert.AreEqual("App.csproj", vm.StartupProject?.DisplayName);
        Assert.IsTrue(vm.RunCommand.CanExecute(null));
        StringAssert.Contains(vm.RunTooltip, "App.csproj");
    }

    [TestMethod]
    [CoversNode("dotnet-startup-picker")]
    public void SolutionWithOneRunnableProject_HidesTheStartupPicker()
    {
        var vm = Vm(TempSolution("My.slnx", ("App/App.csproj", GuiApp), ("Lib/Lib.csproj", Library)));

        Assert.IsFalse(vm.ShowStartupPicker, "no choice to make — don't offer a caret");
    }

    [TestMethod]
    [CoversNode("dotnet-startup-picker")]
    public void SolutionWithSeveralRunnableProjects_ShowsTheStartupPicker()
    {
        var vm = Vm(TempSolution("My.slnx", ("App/App.csproj", GuiApp), ("Other/Other.csproj", GuiApp)));

        Assert.IsTrue(vm.ShowStartupPicker);
        Assert.AreEqual(2, vm.RunnableProjects.Count);
    }

    [TestMethod]
    [CoversNode("run")]
    public void SolutionWithNoRunnableProject_DisablesRunAndSaysWhy()
    {
        // The bug this fixes: Run used to shell out to a bare `dotnet run` in the folder, which finds no
        // project and fails with "cannot find the file".
        var vm = Vm(TempSolution("My.slnx", ("Lib/Lib.csproj", Library)));

        Assert.IsNull(vm.StartupProject);
        Assert.IsFalse(vm.RunCommand.CanExecute(null));
        StringAssert.Contains(vm.RunTooltip, "No runnable project");
    }

    [TestMethod]
    [CoversNode("run")]
    public void ProjectTarget_IsItsOwnRunTarget()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        // A project needs no startup-project resolution — it *is* the run target.
        Assert.IsTrue(vm.RunCommand.CanExecute(null));
        Assert.IsFalse(vm.ShowStartupPicker);
        StringAssert.Contains(vm.RunTooltip, "Foo.csproj");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dotnet-cancel")]
    public void CancelVerb_IsDisabledWhenIdle()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        Assert.IsFalse(vm.CancelVerbCommand.CanExecute(null), "nothing to stop until a verb is running");
    }

    // ── Quiesce ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task QuiesceAsync_NoRunningCheck_CompletesWithoutThrowing()
    {
        // Idle path (the common case: the scan already finished / was a cache hit) — no process to kill,
        // so quiesce must be a safe no-op rather than null-ref.
        var vm = Vm(TempDir("Foo.csproj"));

        await vm.QuiesceAsync(CancellationToken.None);
    }
}
