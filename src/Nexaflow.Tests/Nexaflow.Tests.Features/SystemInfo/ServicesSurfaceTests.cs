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
/// The Services page's grid and per-row controls, driven headlessly with the elevation bridge faked — no
/// service on this machine is started, stopped or re-configured. Every control here goes through the
/// bridge, so what matters is that each one asks for the right operation on the right service, and that a
/// declined UAC prompt is silent (the user said no; an error toast would be noise) while a genuine failure
/// is surfaced.
/// </summary>
[TestClass]
public class ServicesSurfaceTests
{
    /// <summary>A view-model with rows already loaded and the bridge faked to <paramref name="result"/>.</summary>
    private static ServicesViewModel Build(out IShellServices shell, ElevatedResult? result = null,
                                           params ServiceRow[] rows)
    {
        var captured = null as Action<bool>;
        var localShell = Substitute.For<IShellServices>();
        localShell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => captured = ci.Arg<Action<bool>>());
        localShell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(result ?? ElevatedResult.Declined()));

        var vm = new ServicesViewModel(localShell);
        Assert.IsNotNull(captured, "the page queues its gather on construction");

        foreach (var row in rows) vm.Services.Add(row);
        shell = localShell;
        return vm;
    }

    private static ServiceRow Row(string name, string display, string status = "Running",
                                  string startMode = ServiceStartModes.Automatic, bool pausable = true)
        => new(name, display, "A service.", pausable, status, startMode);

    // ── Grid + filter ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-services-grid")]
    public void Filter_MatchesEitherTheServiceNameOrItsDisplayName()
    {
        var vm = Build(out _, rows: [Row("Spooler", "Print Spooler"), Row("wuauserv", "Windows Update")]);

        vm.FilterText = "print";
        CollectionAssert.AreEqual(new[] { "Spooler" },
                                  vm.ServicesView.Cast<ServiceRow>().Select(r => r.Name).ToArray());

        vm.FilterText = "wuau";                    // the short service name, not the display name
        CollectionAssert.AreEqual(new[] { "wuauserv" },
                                  vm.ServicesView.Cast<ServiceRow>().Select(r => r.Name).ToArray());

        vm.FilterText = "   ";
        Assert.AreEqual(2, vm.ServicesView.Cast<ServiceRow>().Count(), "a blank filter shows every service");
    }

    [TestMethod]
    [CoversNode("sysinfo-services-grid")]
    public void RowButtons_AreOfferedOnlyWhereTheyMakeSense()
    {
        var running = Row("A", "A", status: "Running");
        var stopped = Row("B", "B", status: "Stopped");
        var paused  = Row("C", "C", status: "Paused");
        var rigid   = Row("D", "D", status: "Running", pausable: false);

        Assert.IsTrue(running.CanStop && running.CanPause && !running.CanStart);
        Assert.IsTrue(stopped.CanStart && !stopped.CanStop);
        Assert.IsTrue(paused.CanResume && paused.CanStop);
        Assert.IsFalse(rigid.CanPause, "a service that doesn't support pause must not offer the button");
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-services-refresh")]
    public void Refresh_WhileAGatherIsInFlight_IsANoOp()
    {
        var vm = Build(out var shell);
        Assert.IsTrue(vm.IsLoading, "the constructor's gather is still running");
        shell.ClearReceivedCalls();

        vm.RefreshCommand.Execute(null);

        shell.DidNotReceiveWithAnyArgs().QueueBackgroundTask(default!, default, default);
    }

    // ── Start / Stop / Restart / Pause / Resume ───────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-services-control")]
    public async Task EachControlButton_AsksTheBridgeForItsOwnOperation_OnThatService()
    {
        var vm = Build(out var shell, rows: [Row("Spooler", "Print Spooler")]);
        var row = vm.Services.Single();

        await vm.StartServiceCommand.ExecuteAsync(row);
        await vm.StopServiceCommand.ExecuteAsync(row);
        await vm.RestartServiceCommand.ExecuteAsync(row);
        await vm.PauseServiceCommand.ExecuteAsync(row);
        await vm.ResumeServiceCommand.ExecuteAsync(row);

        foreach (var op in new[] { ElevatedOps.ServiceStart, ElevatedOps.ServiceStop, ElevatedOps.ServiceRestart,
                                   ElevatedOps.ServicePause, ElevatedOps.ServiceContinue })
        {
            await shell.Received(1).RunElevatedAsync(
                Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                    o => o.Op == op && o.Args[ElevatedArgs.ServiceName] == "Spooler")),
                Arg.Any<CancellationToken>());
        }
    }

    [TestMethod]
    [CoversNode("sysinfo-services-control")]
    public async Task ADeclinedUacPrompt_IsSilent_ButARealFailureIsSurfaced()
    {
        var declined = Build(out var declinedShell, ElevatedResult.Declined(), Row("Spooler", "Print Spooler"));
        await declined.StopServiceCommand.ExecuteAsync(declined.Services.Single());
        declinedShell.DidNotReceiveWithAnyArgs().ShowError(default!);

        var failed = Build(out var failedShell,
            ElevatedResult.Fail(ElevatedErrorKind.Unexpected, "The service did not respond."),
            Row("Spooler", "Print Spooler"));
        await failed.StopServiceCommand.ExecuteAsync(failed.Services.Single());
        failedShell.Received().ShowError(Arg.Is<string>(m => m.Contains("did not respond")));
    }

    // ── Startup type ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-services-startmode")]
    public void StartupPicker_AppliesAChosenMode_ButIgnoresAReSelectionOfTheCurrentOne()
    {
        var vm = Build(out var shell, rows: [Row("Spooler", "Print Spooler",
                                                 startMode: ServiceStartModes.Automatic)]);
        var row = vm.Services.Single();

        vm.OnStartModeSelected(row, ServiceStartModes.Automatic);   // the ComboBox echoing what it already shows
        shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);

        vm.OnStartModeSelected(row, ServiceStartModes.Disabled);
        shell.Received(1).RunElevatedAsync(
            Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                o => o.Op == ElevatedOps.ServiceSetStartMode
                  && o.Args[ElevatedArgs.StartMode] == ServiceStartModes.Disabled)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [CoversNode("sysinfo-services-startmode")]
    public void StartupPicker_OffersTheWindowsStartupTypes()
    {
        var vm = Build(out _);

        CollectionAssert.IsSubsetOf(
            new[] { ServiceStartModes.Automatic, ServiceStartModes.Manual, ServiceStartModes.Disabled },
            vm.StartModes.ToArray());
    }
}
