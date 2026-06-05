using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dotnet.ViewModels;

namespace Nexaflow.Tests.Features.Dotnet;

[TestClass]
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
    public void GetClientTools_DoesNotExposeRun()
    {
        var names = Vm(TempDir("Foo.csproj")).GetClientTools().Select(t => t.Name).ToList();

        CollectionAssert.DoesNotContain(names, "dotnet_run");
    }

    // ── Tail ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Tail_LongOutput_KeepsLastLines()
    {
        var tail = DotnetViewletViewModel.Tail("a\nb\nc\nd", maxLines: 2);

        Assert.AreEqual("c\nd", tail.Replace("\r\n", "\n"));
    }
}
