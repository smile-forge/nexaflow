using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What one card's turns give work nobody is watching when several apps share a host. The scheduler gives each step to the most
/// urgent request waiting, so one reply leaves a turn between its tokens for work below it; two replies hand the card to each
/// other, and work below them still has one turn in <see cref="GpuScheduler.ShareOfTurns"/>, so it moves for as long as they run.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class SharedHostSchedulingTests
{
    private const int Card = 0;

    /// <summary>A reply's tokens, one step each, until <paramref name="stop"/>; how many it took.</summary>
    private static async Task<int> ReplyAsync(GpuScheduler gpu, CancellationToken stop)
    {
        int steps = 0;
        try
        {
            while (!stop.IsCancellationRequested)   // a free card is given without looking at the token
            {
                using (await gpu.NextAsync(RequestPriority.Interactive, Card, stop))
                {
                    steps++;
                    await Task.Delay(2, CancellationToken.None);   // the token's step on the card
                }
            }
        }
        catch (OperationCanceledException) { }
        return steps;
    }

    [TestMethod]
    public async Task OneReply_LeavesATurnBetweenItsTokens_ForWorkNobodyWatches()
    {
        var gpu = new GpuScheduler();
        using var stop = new CancellationTokenSource();
        var reply = ReplyAsync(gpu, stop.Token);
        await Task.Delay(50);

        var thinking = gpu.NextAsync(RequestPriority.Background, Card, default).AsTask();

        (await thinking.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        stop.Cancel();
        await reply;
    }

    [TestMethod]
    public async Task TwoReplies_HandTheCardToEachOther_AndStillLeaveWorkNobodyWatchesATurn()
    {
        var gpu = new GpuScheduler();
        using var stop = new CancellationTokenSource();
        var replies = new[] { ReplyAsync(gpu, stop.Token), ReplyAsync(gpu, stop.Token) };
        await Task.Delay(50);

        var thinking = gpu.NextAsync(RequestPriority.Background, Card, default).AsTask();

        (await thinking.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.IsFalse(replies.Any(r => r.IsCompleted), "the replies were still running");
        stop.Cancel();
        int steps = (await Task.WhenAll(replies)).Sum();
        Assert.IsTrue(steps > 0, "the replies ran meanwhile");
    }
}
