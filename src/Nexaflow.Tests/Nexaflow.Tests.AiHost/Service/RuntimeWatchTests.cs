using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The service's worker takes up another runtime only once the store holds one and nothing is held or running.</summary>
[TestClass]
[CoversNode("ai-host")]
public class RuntimeWatchTests
{
    private static readonly TimeSpan Often = TimeSpan.FromMilliseconds(20);

    [TestMethod]
    public async Task APackArrivedWhileNothingIsHeldOrRunning_IsWorthRestartingFor()
    {
        string? available = null;
        var watch = new RuntimeWatch(() => available, loaded: null, busy: () => false, Often, TimeProvider.System);
        var worth = watch.UntilWorthRestartingAsync(CancellationToken.None);

        await Task.Delay(200);
        Assert.IsFalse(worth.IsCompleted, "the runtime it loaded is the store's");
        available = @"C:\Store\cuda-pack-13";

        await worth.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public async Task APackArrivedWhileSomethingIsHeld_WaitsTillNothingIs()
    {
        bool busy = true;
        var watch = new RuntimeWatch(() => @"C:\Store\cuda-pack-13", loaded: null, () => busy, Often, TimeProvider.System);
        var worth = watch.UntilWorthRestartingAsync(CancellationToken.None);

        await Task.Delay(200);
        Assert.IsFalse(worth.IsCompleted, "a client's hold or request is never cut short for it");
        busy = false;

        await worth.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public async Task ThePackItRunsFromRemoved_IsWorthRestartingFor_OnTheCpu()
    {
        var watch = new RuntimeWatch(() => null, loaded: @"C:\Store\cuda-pack-13", busy: () => false, Often, TimeProvider.System);

        await watch.UntilWorthRestartingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
    }
}
