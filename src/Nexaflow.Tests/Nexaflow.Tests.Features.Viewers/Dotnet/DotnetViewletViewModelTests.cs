using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dotnet.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dotnet;

[TestClass]
[CoversNode("dotnet-viewlet")]
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
    [CoversNode("dotnet-ai-context")]
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
    [CoversNode("dotnet-verb-run")]
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
    [CoversNode("dotnet-verb-run")]
    public void ProjectTarget_IsItsOwnRunTarget()
    {
        var vm = Vm(TempDir("Foo.csproj"));

        // A project needs no startup-project resolution — it *is* the run target.
        Assert.IsTrue(vm.RunCommand.CanExecute(null));
        Assert.IsFalse(vm.ShowStartupPicker);
        StringAssert.Contains(vm.RunTooltip, "Foo.csproj");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    // ── Verb toolbar: one leaf per button ─────────────────────────────────────
    // Restore / Build / Test / Clean act on the selected target itself, so they share one enablement rule
    // (Run is different — it needs a runnable *project*, covered above).

    [TestMethod]
    [CoversNode("dotnet-verb-restore")]
    [CoversNode("dotnet-verb-build")]
    [CoversNode("dotnet-verb-test")]
    [CoversNode("dotnet-verb-clean")]
    public void TargetVerbs_NeedATarget_AndAreDisabledWhileAVerbRuns()
    {
        var empty = Vm(TempDir());
        Assert.IsNull(empty.SelectedTarget, "precondition: a folder with no .NET files has no target");
        foreach (var (name, command) in Verbs(empty))
            Assert.IsFalse(command.CanExecute(null), $"{name} should be disabled with no target");

        var vm = Vm(TempDir("App.csproj"));
        Assert.IsNotNull(vm.SelectedTarget);
        foreach (var (name, command) in Verbs(vm))
            Assert.IsTrue(command.CanExecute(null), $"{name} should be enabled once a target is selected");

        vm.IsBusy = true;
        foreach (var (name, command) in Verbs(vm))
            Assert.IsFalse(command.CanExecute(null), $"{name} should be disabled while another verb runs");
    }

    private static (string Name, System.Windows.Input.ICommand Command)[] Verbs(DotnetViewletViewModel vm) =>
    [
        ("Restore", vm.RestoreCommand), ("Build", vm.BuildCommand),
        ("Test", vm.TestCommand),       ("Clean", vm.CleanCommand),
    ];

    [TestMethod]
    [CoversNode("dotnet-verb-restore")]
    [CoversNode("dotnet-verb-build")]
    [CoversNode("dotnet-verb-test")]
    [CoversNode("dotnet-verb-clean")]
    public async Task RunVerbAsync_WithNoTarget_DoesNothing()
    {
        // No target means no dotnet process is ever launched — the guard, not the CLI, is what's under test.
        Assert.IsNull(await Vm(TempDir()).RunVerbAsync("build"));
    }

    // ── Open the target in its default application ────────────────────────────

    [TestMethod]
    [CoversNode("dotnet-open-target")]
    public void OpenTarget_IsOfferedOnlyOnceATargetIsResolved()
    {
        Assert.IsFalse(Vm(TempDir()).OpenTargetCommand.CanExecute(null), "nothing to open in a folder with no target");
        Assert.IsTrue(Vm(TempDir("App.csproj")).OpenTargetCommand.CanExecute(null));
    }

    // ── Run status indicator ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dotnet-progress")]
    public void ProgressLabel_IsTheVerbsGerund()
    {
        Assert.AreEqual("Restoring", DotnetViewletViewModel.Gerund("restore"));
        Assert.AreEqual("Building",  DotnetViewletViewModel.Gerund("build"));
        Assert.AreEqual("Running",   DotnetViewletViewModel.Gerund("run"));
        Assert.AreEqual("Testing",   DotnetViewletViewModel.Gerund("test"));
        Assert.AreEqual("Cleaning",  DotnetViewletViewModel.Gerund("clean"));
        Assert.AreEqual("Publish",   DotnetViewletViewModel.Gerund("publish"), "an unknown verb is merely capitalised");
    }

    [TestMethod]
    [CoversNode("dotnet-progress")]
    public void ProgressDetail_ClipsALongOutputLineWithAnEllipsis()
    {
        Assert.AreEqual("short line", DotnetViewletViewModel.Truncate("  short line  "), "trimmed, not clipped");

        var clipped = DotnetViewletViewModel.Truncate(new string('x', 200));
        Assert.AreEqual(80, clipped.Length);
        StringAssert.EndsWith(clipped, "…");
    }

    [TestMethod]
    [CoversNode("dotnet-progress")]
    public void FailureOutput_IsTailedSoANoisyBuildCannotFloodTheToast()
    {
        var output = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var tail = DotnetViewletViewModel.Tail(output, maxLines: 5);

        Assert.AreEqual(5, tail.Split(Environment.NewLine).Length);
        StringAssert.EndsWith(tail, "line 100");
        Assert.AreEqual("only line", DotnetViewletViewModel.Tail("only line", maxLines: 5), "short output passes through");
    }

    [TestMethod]
    [CoversNode("dotnet-progress")]
    public void StartsIdle_WithNoGlyphAndNoRunningLabel()
    {
        var vm = Vm(TempDir("App.csproj"));
        Assert.IsFalse(vm.IsBusy);
        Assert.AreEqual(string.Empty, vm.StatusGlyph);
        Assert.AreEqual(string.Empty, vm.RunningLabel);
    }

    // ── NuGet update caution ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dotnet-nuget-caution")]
    public void NugetCaution_ListsEachPackageCurrentToLatest()
    {
        var vm = Vm(TempDir("App.csproj"));
        vm.ApplyUpdates([new("Serilog", "2.0.0", "4.1.0"), new("Xunit", "2.4.1", "2.9.0")]);

        Assert.IsTrue(vm.HasUpdates);
        StringAssert.Contains(vm.UpdatesTooltip, "Serilog: 2.0.0 > 4.1.0");
        StringAssert.Contains(vm.UpdatesTooltip, "Xunit: 2.4.1 > 2.9.0");
        StringAssert.Contains(vm.GetContext(), "2 package update(s) available");
    }

    [TestMethod]
    [CoversNode("dotnet-nuget-caution")]
    public void NugetCaution_ClearsWhenNothingIsOutdated()
    {
        var vm = Vm(TempDir("App.csproj"));
        vm.ApplyUpdates([new("Serilog", "2.0.0", "4.1.0")]);
        vm.ApplyUpdates([]);

        Assert.IsFalse(vm.HasUpdates, "an up-to-date target must not keep showing a stale warning");
        Assert.AreEqual(string.Empty, vm.UpdatesTooltip);
        Assert.IsFalse(vm.GetContext().Contains("package update"));
    }

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
