using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Services;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>
/// The escalation path shared by kill, set-priority and handle inspection: try non-elevated first, and only
/// on an access denial go through the privilege bridge for one UAC prompt.
/// <para>
/// The <i>trigger</i> — a Win32 access denial — needs a protected process, which a test must not go hunting
/// for. Everything either side of it is asserted here: the paths that must <b>never</b> reach the bridge
/// (a process that already exited, an unparseable priority), the exact request each escalation sends (a
/// drifted op name or arg key breaks elevation silently, and only on the branch no test can reach), and the
/// rule for reporting what came back — a declined prompt is the user's own answer and must not raise an
/// error toast, while a real failure must. The inspect leg runs end-to-end in <see cref="HandlesTests"/>,
/// which fakes the bridge result directly.
/// </para>
/// </summary>
[TestClass]
[CoversNode("process-elevation")]
public class ProcessElevationTests
{
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunElevatedAsync(Arg.Any<ElevatedRequest>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(ElevatedResult.Declined()));
        return shell;
    }

    // ── The request each escalation sends ─────────────────────────────────────

    [TestMethod]
    public void KillRequest_NamesTheKillOperation_WithThePidAndWhetherTheTreeGoesToo()
    {
        var op = ProcessActions.KillRequest(pid: 4242, tree: true).Operations.Single();

        Assert.AreEqual(ElevatedOps.ProcessKill, op.Op);
        Assert.AreEqual("4242", op.Args[ElevatedArgs.ProcessId]);
        Assert.AreEqual("true", op.Args[ElevatedArgs.ProcessKillTree]);

        var single = ProcessActions.KillRequest(pid: 7, tree: false).Operations.Single();
        Assert.AreEqual("false", single.Args[ElevatedArgs.ProcessKillTree],
                        "a single-process kill must not ask the bridge to take the whole tree");
    }

    [TestMethod]
    public void SetPriorityRequest_NamesThePriorityOperation_WithThePidAndClass()
    {
        var op = ProcessActions.SetPriorityRequest(4242, ProcessPriorityClass.High).Operations.Single();

        Assert.AreEqual(ElevatedOps.ProcessSetPriority, op.Op);
        Assert.AreEqual("4242", op.Args[ElevatedArgs.ProcessId]);
        Assert.AreEqual("High", op.Args[ElevatedArgs.ProcessPriority]);
    }

    [TestMethod]
    public async Task InspectRequest_NamesTheInspectOperation_WithThePidAndWhatToRead()
    {
        var shell = Shell();

        await ProcessInspect.InspectAsync(shell, pid: 4242, what: "all");

        await shell.Received(1).RunElevatedAsync(
            Arg.Is<ElevatedRequest>(r => r.Operations.Any(
                o => o.Op == ElevatedOps.ProcessInspect
                  && o.Args[ElevatedArgs.ProcessId] == "4242"
                  && o.Args[ElevatedArgs.InspectWhat] == "all")),
            Arg.Any<CancellationToken>());
    }

    // ── Reporting the bridge's answer ─────────────────────────────────────────

    [TestMethod]
    public void DeclinedApproval_IsReportedBack_ButNotRaisedAsAnError()
    {
        var shell = Shell();

        var message = ProcessActions.Outcome(shell, ElevatedResult.Declined(), "notepad was not terminated");

        StringAssert.Contains(message, "declined");
        StringAssert.Contains(message, "notepad was not terminated",
                              "the message says what didn't happen, not just that approval was refused");
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    [TestMethod]
    public void ARealFailure_IsSurfacedToTheUser()
    {
        var shell = Shell();

        var message = ProcessActions.Outcome(shell,
            ElevatedResult.Fail(ElevatedErrorKind.Unexpected, "The bridge could not open the process."),
            "notepad was not terminated");

        Assert.AreEqual("The bridge could not open the process.", message);
        shell.Received().ShowError("The bridge could not open the process.");
    }

    [TestMethod]
    public void Success_PassesTheBridgesOwnMessageThrough_Quietly()
    {
        var shell = Shell();
        var ok = ElevatedResult.FromOperations(
            [ElevatedOperationResult.Ok(ElevatedOps.ProcessKill, "Terminated notepad (PID 42).")]);

        var message = ProcessActions.Outcome(shell, ok, "notepad was not terminated");

        StringAssert.Contains(message, "Terminated notepad");
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    // ── Paths that must never escalate ────────────────────────────────────────

    [TestMethod]
    public async Task KillingAProcessThatAlreadyExited_SaysSo_WithoutAUacPrompt()
    {
        var shell = Shell();

        // A PID that cannot be running: Process.GetProcessById throws ArgumentException, which is "gone",
        // not "denied" — prompting for admin here would be pure noise.
        var message = await ProcessActions.KillAsync(shell, pid: -1, name: "ghost.exe", tree: false);

        StringAssert.Contains(message, "no longer running");
        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    public async Task SettingAnUnknownPriorityClass_IsRejectedLocally_WithoutAUacPrompt()
    {
        var shell = Shell();

        var message = await ProcessActions.SetPriorityAsync(shell, pid: -1, name: "ghost.exe",
                                                            priority: "Turbo");

        StringAssert.Contains(message, "Unknown priority class");
        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    public async Task SettingAPriorityOnAProcessThatAlreadyExited_SaysSo_WithoutAUacPrompt()
    {
        var shell = Shell();

        var message = await ProcessActions.SetPriorityAsync(shell, pid: -1, name: "ghost.exe",
                                                            priority: "High");

        StringAssert.Contains(message, "no longer running");
        await shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }
}
