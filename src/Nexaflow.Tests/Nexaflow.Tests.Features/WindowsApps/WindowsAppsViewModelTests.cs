using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsApps;

[TestClass]
[CoversNode("windowsapps-ai-context")]
public class WindowsAppsViewModelTests
{
    // Capture the pass-1 onComplete the VM hands to QueueBackgroundTask so a test can simulate the scan
    // finishing without running it (which would hit the real registry).
    private static (WindowsAppsViewModel Vm, Action<bool> Complete) Build()
    {
        Action<bool>? captured = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => captured = ci.Arg<Action<bool>>());

        var vm = new WindowsAppsViewModel(shell);
        Assert.IsNotNull(captured, "Constructor should queue a background scan.");
        return (vm, captured!);
    }

    [TestMethod]
    public void IsContextReady_WhileScanning_False()
    {
        var (vm, _) = Build();

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still scanning");
    }

    [TestMethod]
    public void IsContextReady_AfterScanFinishes_True()
    {
        // Ready once pass 1 finishes — here via the failure path, which also proves a failed scan
        // releases the gate instead of wedging it.
        var (vm, complete) = Build();

        complete(false);

        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    public void IsContextReady_Flip_RaisesPropertyChanged()
    {
        var (vm, complete) = Build();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WindowsAppsViewModel.IsContextReady)) raised = true;
        };

        complete(false);

        Assert.IsTrue(raised);
    }
}
