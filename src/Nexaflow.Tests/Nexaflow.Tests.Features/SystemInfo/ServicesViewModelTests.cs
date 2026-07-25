using System;
using System.Threading;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
[CoversNode("sysinfo-ai-context")]
public class ServicesViewModelTests
{
    // Capture the onComplete handed to QueueBackgroundTask so a test can simulate the gather finishing
    // (the faked shell never actually runs the background task).
    private static (ServicesViewModel Vm, Action<bool> Complete) Build()
    {
        Action<bool>? captured = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => captured = ci.Arg<Action<bool>>());

        var vm = new ServicesViewModel(shell);
        Assert.IsNotNull(captured, "Constructor should queue a background gather.");
        return (vm, captured!);
    }

    [TestMethod]
    public void WhileGathering_IsNotReady()
    {
        var (vm, _) = Build();

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still gathering");
    }

    [TestMethod]
    public void AfterGather_IsReady()
    {
        var (vm, complete) = Build();
        complete(true);
        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    public void ReadinessFlip_RaisesPropertyChanged()
    {
        var (vm, complete) = Build();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServicesViewModel.IsContextReady)) raised = true;
        };

        complete(true);

        Assert.IsTrue(raised);
    }
}
