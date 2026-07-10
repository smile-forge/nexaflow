using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>
/// Headless command/state coverage for <see cref="SystemInfoViewModel"/> not already exercised by
/// <c>SystemInfoViewModelTests</c> (which covers <c>IsContextReady</c>/<c>GetContext</c> notification).
/// Here: the <c>Refresh</c> re-entrancy guard — while a gather is in flight, a second Refresh must be a
/// no-op so the dashboard never double-queues the (expensive) WMI probe.
/// </summary>
[TestClass]
[CoversNode("sysinfo-refresh")]
public class SystemInfoViewModelCommandTests
{
    // Build a VM over a faked shell that records every QueueBackgroundTask call and captures the last
    // onComplete — the fake never runs the task, so the VM stays IsLoading (mid-gather) until we invoke it.
    private static (SystemInfoViewModel Vm, System.Func<int> QueueCount, System.Func<Action<bool>?> LastComplete) Build()
    {
        var count = 0;
        Action<bool>? captured = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => { count++; captured = ci.Arg<Action<bool>>(); });

        var vm = new SystemInfoViewModel(shell);
        return (vm, () => count, () => captured);
    }

    [TestMethod]
    public void Ctor_QueuesInitialGather_AndIsLoading()
    {
        var (vm, queueCount, _) = Build();

        Assert.AreEqual(1, queueCount(), "Constructor should queue exactly one background gather.");
        Assert.IsTrue(vm.IsLoading, "The VM should be loading until the initial gather completes.");
    }

    [TestMethod]
    public void Refresh_WhileGathering_IsNoOp()
    {
        // Initial gather is queued by the ctor and left in flight (fake never completes it), so IsLoading
        // is still true. A second Refresh must bail on the guard rather than queue another probe.
        var (vm, queueCount, _) = Build();
        Assert.AreEqual(1, queueCount());

        vm.RefreshCommand.Execute(null);

        Assert.AreEqual(1, queueCount(), "Refresh while already loading must not queue a second gather.");
    }

    [TestMethod]
    public void Refresh_AfterGatherCompletes_QueuesAgain()
    {
        // Once the in-flight gather finishes (IsLoading clears), Refresh is allowed to re-queue.
        var (vm, queueCount, lastComplete) = Build();
        lastComplete()!.Invoke(true);      // simulate the initial gather completing
        Assert.IsFalse(vm.IsLoading);

        vm.RefreshCommand.Execute(null);

        Assert.AreEqual(2, queueCount(), "Refresh after the gather finished should queue a fresh gather.");
        Assert.IsTrue(vm.IsLoading, "A fresh gather puts the VM back into the loading state.");
    }
}
