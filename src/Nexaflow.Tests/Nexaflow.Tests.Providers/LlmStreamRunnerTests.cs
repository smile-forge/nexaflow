using System.Runtime.CompilerServices;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The skeleton every SDK provider streams through: what a reader of the stream sees when the vendor's stream
/// succeeds, fails before it has said anything, fails part-way, or is cancelled — and what the activity says.
/// </summary>
[TestClass]
public class LlmStreamRunnerTests
{
    private sealed class Activities : IBackgroundActivityManager
    {
        public List<string> Ends { get; } = [];

        public IActivityHandle StartActivity(string label) => new Handle(this);

        private sealed class Handle(Activities owner) : IActivityHandle
        {
            public void Complete()             => owner.Ends.Add("complete");
            public void Fail(string? message)  => owner.Ends.Add("fail: " + message);
        }
    }

    private static async IAsyncEnumerable<LlmStreamEvent> Texts(
        IEnumerable<string> pieces, Exception? after = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var p in pieces)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new LlmTextDelta(p);
        }
        if (after is not null) throw after;
    }

    private static async Task<List<string>> ReadAsync(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var read = new List<string>();
        await foreach (var e in stream)
            if (e is LlmTextDelta d) read.Add(d.Text);
        return read;
    }

    [TestMethod]
    public async Task TheReaderSeesEveryPiece_InOrder_AndTheActivityCompletes()
    {
        var activities = new Activities();

        var read = await ReadAsync(LlmStreamRunner.RunAsync(activities, "x", "P", ct => Texts(["a", "b", "c"], ct: ct)));

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, read);
        CollectionAssert.AreEqual(new[] { "complete" }, activities.Ends);
    }

    [TestMethod]
    public async Task AFailureBeforeAnythingWasSaid_IsRetried()
    {
        var opened = 0;
        IAsyncEnumerable<LlmStreamEvent> Open(CancellationToken ct) =>
            ++opened == 1 ? Texts([], new HttpRequestException("503 Service Unavailable"), ct) : Texts(["ok"], ct: ct);

        var read = await ReadAsync(LlmStreamRunner.RunAsync(new Activities(), "x", "P", Open));

        Assert.AreEqual(2, opened);
        CollectionAssert.AreEqual(new[] { "ok" }, read);
    }

    [TestMethod]
    public async Task AFailurePartWay_IsNotRetried_TheReaderHasAlreadySeenPartOfTheReply()
    {
        var opened     = 0;
        var activities = new Activities();
        IAsyncEnumerable<LlmStreamEvent> Open(CancellationToken ct)
        {
            opened++;
            return Texts(["half"], new HttpRequestException("503 Service Unavailable"), ct);
        }

        var read = new List<string>();
        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(async () =>
        {
            await foreach (var e in LlmStreamRunner.RunAsync(activities, "x", "P", Open))
                if (e is LlmTextDelta d) read.Add(d.Text);
        });

        Assert.AreEqual(1, opened);
        CollectionAssert.AreEqual(new[] { "half" }, read);
        StringAssert.StartsWith(ex.Message, "P request failed");
        StringAssert.StartsWith(activities.Ends.Single(), "fail:");
    }

    [TestMethod]
    public async Task TheCallersCancellation_ArrivesUnwrapped()
    {
        using var cts  = new CancellationTokenSource();
        var activities = new Activities();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in LlmStreamRunner.RunAsync(activities, "x", "P", ct => Texts(["a", "b", "c"], ct: ct), cts.Token))
                cts.Cancel();
        });

        CollectionAssert.AreEqual(new[] { "fail: canceled" }, activities.Ends);
    }

    [TestMethod]
    public async Task AReaderThatStopsEarly_StillEndsTheActivity()
    {
        var activities = new Activities();

        await foreach (var _ in LlmStreamRunner.RunAsync(activities, "x", "P", ct => Texts(["a", "b"], ct: ct)))
            break;

        CollectionAssert.AreEqual(new[] { "complete" }, activities.Ends);
    }

    [TestMethod]
    public async Task CompleteAsync_IsTheWholeReply_OrNullForNone()
    {
        var provider = new Provider(["Hello", ", ", "world."]);
        Assert.AreEqual("Hello, world.", (await provider.CompleteAsync([]))?.RawText);
        Assert.IsNull(await new Provider([]).CompleteAsync([]));
    }

    private sealed class Provider(string[] pieces) : ILlmProvider
    {
        public string Name => "p";
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => Texts(pieces, ct: ct);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
