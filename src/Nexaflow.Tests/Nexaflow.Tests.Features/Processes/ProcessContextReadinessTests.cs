using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using static Nexaflow.Tests.Features.Processes.FakeProcessSource;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>
/// The page reports <see cref="ProcessesViewModel.IsContextReady"/> off a constructor-queued bootstrap
/// sample, so a Processes page pinned as an AI context item (never shown as a tab, so its view never Loads
/// and OnActivated never fires) still releases the held send instead of waiting forever.
/// </summary>
[TestClass]
[CoversNode("process-view")]
public class ProcessContextReadinessTests
{
    // Capture the bootstrap task + its completion callback — the faked shell never runs queued work, so the
    // VM stays "still gathering" until the test simulates the gather finishing.
    private static (ProcessesViewModel Vm, IBackgroundTask Task, Action<bool> Complete) Build(FakeProcessSource src)
    {
        IBackgroundTask? task = null;
        Action<bool>? complete = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => { task = ci.Arg<IBackgroundTask>(); complete = ci.Arg<Action<bool>>(); });

        var vm = new ProcessesViewModel(shell, src);
        Assert.IsNotNull(task, "Constructor should queue a bootstrap snapshot.");
        return (vm, task!, complete!);
    }

    [TestMethod]
    public void IsContextReady_BeforeBootstrapCompletes_False()
    {
        var (vm, _, _) = Build(new FakeProcessSource());
        using (vm)
        {
            Assert.IsFalse(vm.IsContextReady);
            StringAssert.Contains(vm.GetContext(), "still gathering");
        }
    }

    [TestMethod]
    public async Task IsContextReady_AfterBootstrapCompletes_True_WithoutActivation()
    {
        var src = new FakeProcessSource { Next = Snap((1, 0, "a.exe")) };
        var (vm, task, complete) = Build(src);
        using (vm)
        {
            var raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessesViewModel.IsContextReady)) raised = true;
            };

            await task.RunAsync(CancellationToken.None);   // populates the snapshot
            complete(true);                                // shell marshals this to the UI thread in prod

            Assert.IsTrue(vm.IsContextReady);
            Assert.IsTrue(raised, "Readiness flip must raise PropertyChanged to wake the held send.");
        }
    }
}
