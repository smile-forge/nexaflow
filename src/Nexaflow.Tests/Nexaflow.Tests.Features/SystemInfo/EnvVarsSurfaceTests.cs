using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>
/// The Environment Variables page's chrome: the scope selector and name filter, the value editor's
/// read-only gate, and the three write actions. Nothing here changes this machine's environment — a User
/// write goes in-process and a Machine write goes through the elevation bridge, and both are faked, so
/// what is asserted is which route each scope takes and that Delete asks first.
///
/// The PATH-entry list editing (add / remove / reorder) is covered by
/// <see cref="EnvironmentVariablesViewModelTests"/>.
/// </summary>
[TestClass]
public class EnvVarsSurfaceTests
{
    private static EnvironmentVariablesViewModel Build(out IShellServices shell)
    {
        shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(ElevatedResult.Declined()));
        return new EnvironmentVariablesViewModel(shell);
    }

    private static EnvVarRow Var(string name, string value, EnvScope scope = EnvScope.User)
        => new(name, value, scope);

    // ── Scope selector + filter ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-envvars-scope")]
    public void Scope_OffersBothPersistentScopes_AndStartsOnTheUsersOwn()
    {
        var vm = Build(out _);

        CollectionAssert.AreEqual(new[] { EnvScope.User, EnvScope.Machine }, vm.Scopes.ToArray());
        Assert.AreEqual(EnvScope.User, vm.SelectedScope, "the no-UAC scope is the safe default");
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-scope")]
    public void Filter_NarrowsTheNameListCaseInsensitively_AndClearingItRestoresEverything()
    {
        var vm = Build(out _);
        foreach (var v in new[] { Var("PATH", "a;b"), Var("TEMP", "t"), Var("PATHEXT", ".EXE") })
            vm.Variables.Add(v);

        vm.FilterText = "path";
        CollectionAssert.AreEquivalent(new[] { "PATH", "PATHEXT" },
                                       vm.VariablesView.Cast<EnvVarRow>().Select(v => v.Name).ToArray());

        vm.FilterText = "  ";
        Assert.AreEqual(3, vm.VariablesView.Cast<EnvVarRow>().Count());
    }

    // ── Value editor ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-envvars-editor")]
    public void Editor_IsReadOnlyUntilAVariableIsSelected()
    {
        var vm = Build(out _);
        Assert.IsTrue(vm.IsEditorReadOnly);
        Assert.IsFalse(vm.CanEditSelection);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null), "there is nothing to save yet");
        Assert.IsFalse(vm.DeleteCommand.CanExecute(null));

        vm.SelectedVariable = Var("TEMP", @"C:\temp");

        Assert.IsFalse(vm.IsEditorReadOnly);
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        Assert.IsTrue(vm.DeleteCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-editor")]
    public void Editor_SwitchesToTheEntryListForADelimitedValue()
    {
        var vm = Build(out _);

        vm.EditValue = @"C:\one";
        Assert.IsFalse(vm.IsEditingList, "a single value edits as plain text");

        vm.EditValue = @"C:\one;C:\two";
        Assert.IsTrue(vm.IsEditingList, "a ';'-delimited value edits as a PATH-style entry list");
        CollectionAssert.AreEqual(new[] { @"C:\one", @"C:\two" }, vm.EditEntries.ToArray());
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-envvars-save")]
    public async Task Save_AUserVariable_GoesInProcess_WithNoUacPrompt()
    {
        var vm = Build(out var shell);
        vm.SelectedVariable = Var("NEXAFLOW_TEST_UNUSED", "old", EnvScope.User);
        vm.EditValue = "new";

        await vm.SaveCommand.ExecuteAsync(null);

        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-save")]
    public async Task Save_AMachineVariable_GoesThroughTheElevationBridge()
    {
        var vm = Build(out var shell);
        vm.SelectedVariable = Var("SOME_MACHINE_VAR", "old", EnvScope.Machine);
        vm.EditValue = "new value";

        await vm.SaveCommand.ExecuteAsync(null);

        await shell.Received(1).RunElevatedAsync(
            Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                o => o.Op == ElevatedOps.EnvSet
                  && o.Args[ElevatedArgs.EnvName] == "SOME_MACHINE_VAR"
                  && o.Args[ElevatedArgs.EnvValue] == "new value"
                  && o.Args[ElevatedArgs.EnvTarget] == "Machine")),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-save")]
    public async Task Save_WithNothingSelected_IsANoOp()
    {
        var vm = Build(out var shell);

        await vm.SaveCommand.ExecuteAsync(null);

        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-envvars-delete")]
    public async Task Delete_AsksFirst_AndDeclineRemovesNothing()
    {
        var vm = Build(out var shell);
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));
        vm.SelectedVariable = Var("SOME_MACHINE_VAR", "v", EnvScope.Machine);

        await vm.DeleteCommand.ExecuteAsync(null);

        await shell.Received().ConfirmAsync("Delete variable",
            Arg.Is<string>(m => m.Contains("SOME_MACHINE_VAR")), Arg.Any<CancellationToken>());
        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-delete")]
    public async Task Delete_Confirmed_ForAMachineVariable_GoesThroughTheBridge()
    {
        var vm = Build(out var shell);
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(true));
        vm.SelectedVariable = Var("SOME_MACHINE_VAR", "v", EnvScope.Machine);

        await vm.DeleteCommand.ExecuteAsync(null);

        await shell.Received(1).RunElevatedAsync(
            Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                o => o.Op == ElevatedOps.EnvDelete && o.Args[ElevatedArgs.EnvName] == "SOME_MACHINE_VAR")),
            Arg.Any<CancellationToken>());
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-envvars-add")]
    public void Add_PromptsForAName_AndABlankOneCreatesNothing()
    {
        var vm = Build(out var shell);
        Action<string>? confirm = null;
        shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                     Arg.Any<Action<string>>(), Arg.Any<Action>()))
             .Do(ci => confirm = ci.Arg<Action<string>>());

        vm.AddNewCommand.Execute(null);

        shell.Received().ShowPrompt("New variable", "Name", "", Arg.Any<Action<string>>(), Arg.Any<Action>());
        Assert.IsNotNull(confirm);

        confirm!("   ");                      // the user pressed OK on an empty box
        shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    [CoversNode("sysinfo-envvars-add")]
    public void Add_CreatesTheVariableInTheScopeOnScreen()
    {
        var vm = Build(out var shell);
        vm.SelectedScope = EnvScope.Machine;
        Action<string>? confirm = null;
        shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                     Arg.Any<Action<string>>(), Arg.Any<Action>()))
             .Do(ci => confirm = ci.Arg<Action<string>>());

        vm.AddNewCommand.Execute(null);
        confirm!("  NEW_MACHINE_VAR  ");       // trimmed before use

        shell.Received(1).RunElevatedAsync(
            Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                o => o.Op == ElevatedOps.EnvSet
                  && o.Args[ElevatedArgs.EnvName] == "NEW_MACHINE_VAR"
                  && o.Args[ElevatedArgs.EnvTarget] == "Machine")),
            Arg.Any<CancellationToken>());
    }
}
