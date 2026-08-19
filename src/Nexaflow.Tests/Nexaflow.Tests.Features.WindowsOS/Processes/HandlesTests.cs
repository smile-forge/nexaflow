using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.Services;
using Nexaflow.Features.Processes.ViewModels;
using NSubstitute;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

[TestClass]
[CoversNode("handles")]
public class HandlesTests
{
    private static ProcessDetailViewModel NewVm(IShellServices shell) =>
        new(shell, 123, new FakeProcessSource());

    /// <summary>Builds an <see cref="ElevatedResult"/> carrying one process.inspect op with the given Data.</summary>
    private static ElevatedResult InspectResult(params (string key, string value)[] data)
    {
        var op = ElevatedOperationResult.Ok(ElevatedOps.ProcessInspect, "ok");
        foreach (var (k, v) in data) op.Data[k] = v;
        return ElevatedResult.FromOperations([op]);
    }

    private const string TwoHandlesJson =
        """[{"Type":"File","Name":"\\Device\\Foo","HandleValue":"0x10","Access":"0x00120089"},""" +
        """{"Type":"Key","Name":"\\REGISTRY\\Machine","HandleValue":"0x24","Access":"0x000F003F"}]""";

    // ── LoadHandles command (full path through ProcessInspect) ──────────────────
    [TestMethod]
    public async Task LoadHandles_PopulatesHandles_FromElevatedJson()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(InspectResult(("handles", TwoHandlesJson))));
        using var vm = NewVm(shell);

        await vm.LoadHandlesCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HandlesLoaded);
        Assert.IsNull(vm.HandlesError);
        Assert.AreEqual(2, vm.Handles.Count);
        Assert.AreEqual("File", vm.Handles[0].Type);
        Assert.AreEqual("\\Device\\Foo", vm.Handles[0].Name);
        Assert.AreEqual("0x10", vm.Handles[0].HandleValue);
        Assert.AreEqual("0x00120089", vm.Handles[0].Access);
    }

    [TestMethod]
    public async Task LoadHandles_Declined_SetsError_NotLoaded()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(ElevatedResult.Declined()));
        using var vm = NewVm(shell);

        await vm.LoadHandlesCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HandlesLoaded);
        Assert.AreEqual(0, vm.Handles.Count);
        Assert.IsFalse(string.IsNullOrEmpty(vm.HandlesError));
    }

    [TestMethod]
    public async Task LoadHandles_Failure_SetsError_AndSurfaces()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(ElevatedResult.Fail(ElevatedErrorKind.OperationFailed, "boom")));
        using var vm = NewVm(shell);

        await vm.LoadHandlesCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HandlesLoaded);
        Assert.AreEqual("boom", vm.HandlesError);
        shell.Received(1).ShowError("boom");
    }

    [TestMethod]
    public async Task LoadHandles_AllMode_ClearsModulesDenied_AndFillsCommandLine()
    {
        var shell = Substitute.For<IShellServices>();
        const string modulesJson = """[{"Name":"a.dll","Path":"C:\\a.dll","Version":"1.0","Company":"x","Size":2048}]""";
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(InspectResult(
                 ("handles", TwoHandlesJson),
                 ("modules", modulesJson),
                 ("commandLine", "\"a.exe\" --flag"))));
        using var vm = NewVm(shell);
        vm.ApplyDetail(new ProcessDetail { Pid = 123, Name = "a.exe", ModulesDenied = true });

        await vm.LoadHandlesCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.ModulesDenied, "an all-mode inspect must clear the modules denial");
        Assert.AreEqual(1, vm.Modules.Count);
        Assert.AreEqual("a.dll", vm.Modules[0].Name);
        Assert.AreEqual("\"a.exe\" --flag", vm.Detail!.CommandLine);
    }

    // ── ApplyInspect reconcile + degradation (no shell) ─────────────────────────
    [TestMethod]
    public void ApplyInspect_Reconcile_ReusesUnchangedHandleInstances()
    {
        using var vm = NewVm(Substitute.For<IShellServices>());
        IReadOnlyList<HandleInfo> Same() =>
        [
            new() { Type = "File", Name = "n", HandleValue = "0x10", Access = "0x1" },
            new() { Type = "Key",  Name = "m", HandleValue = "0x24", Access = "0x2" },
        ];

        vm.ApplyInspect(new ProcessInspect.InspectResult(true, false, "", Same(), null, null));
        var first = vm.Handles[0];
        vm.ApplyInspect(new ProcessInspect.InspectResult(true, false, "", Same(), null, null));

        Assert.AreSame(first, vm.Handles[0], "an unchanged handle (same key) must keep its instance");
        Assert.AreEqual(2, vm.Handles.Count);
    }

    [TestMethod]
    public async Task LoadHandles_MalformedJson_DegradesToEmpty_NoThrow()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(InspectResult(("handles", "{ not valid json"))));
        using var vm = NewVm(shell);

        await vm.LoadHandlesCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HandlesLoaded);   // op succeeded; payload just couldn't be parsed
        Assert.AreEqual(0, vm.Handles.Count);
    }
}
