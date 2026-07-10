using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
[CoversNode("sysinfoview")]
public class SystemInfoViewModelTests
{
    // Capture the onComplete the VM hands to QueueBackgroundTask so a test can simulate the gather
    // finishing — the faked shell never runs the task, so the VM stays "still gathering" until invoked.
    private static (SystemInfoViewModel Vm, Action<bool> Complete) Build()
    {
        Action<bool>? captured = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => captured = ci.Arg<Action<bool>>());

        var vm = new SystemInfoViewModel(shell);
        Assert.IsNotNull(captured, "Constructor should queue a background gather.");
        return (vm, captured!);
    }

    [TestMethod]
    public void IsContextReady_WhileGathering_False()
    {
        var (vm, _) = Build();

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still gathering");
    }

    [TestMethod]
    public void IsContextReady_AfterGatherCompletes_True()
    {
        var (vm, complete) = Build();

        complete(true);

        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    public void IsContextReady_AfterGatherFails_True()
    {
        // Readiness ties to "loading finished", not "data arrived" — a failed scan must still release
        // the send rather than wedge it forever.
        var (vm, complete) = Build();

        complete(false);

        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    public void IsContextReady_Flip_RaisesPropertyChanged()
    {
        // The shell's "hold the send until ready" await keys off this notification; without it the held
        // send would never resume.
        var (vm, complete) = Build();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemInfoViewModel.IsContextReady)) raised = true;
        };

        complete(true);

        Assert.IsTrue(raised);
    }
}
