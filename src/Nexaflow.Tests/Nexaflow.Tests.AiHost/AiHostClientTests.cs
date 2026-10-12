using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The client's side of the host's life: starting it on demand, the handshake, requests and their end, and the
/// watchdogs that replace a host that dies or stops answering.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class AiHostClientTests
{
    /// <summary>Watchdog limits short enough for a test to cross.</summary>
    private static readonly AiHostTimings Fast = new(
        HeartbeatInterval: TimeSpan.FromMilliseconds(50),
        HeartbeatTimeout:  TimeSpan.FromMilliseconds(500),
        CancelGrace:       TimeSpan.FromMilliseconds(500),
        CrashLimit:        3,
        CrashWindow:       TimeSpan.FromSeconds(2));

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static async Task<List<HostReply>> Describe(AiHostClient client, TimeSpan? stall = null, CancellationToken ct = default)
    {
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), stall ?? Generous, ct))
            replies.Add(reply);
        return replies;
    }

    [TestMethod]
    public async Task TheHostStartsOnTheFirstRequest_AndServesTheNext()
    {
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast);
        Assert.IsFalse(client.IsRunning, "nothing is started before it is needed");

        var first  = await Describe(client);
        var second = await Describe(client);

        Assert.AreEqual(1, host.Starts);
        Assert.IsTrue(client.IsRunning);
        Assert.AreEqual("fake", ((HostDescription)first.Single()).RuntimeVersion);
        Assert.AreEqual(1, second.Count);
    }

    [TestMethod]
    public async Task AHostWithTheWrongToken_IsRefused()
    {
        var host = new FakeHost { WrongToken = new string('0', 64) };
        await using var client = new AiHostClient(host, Fast);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));

        Assert.AreEqual(AiHostFailure.Unavailable, ex.Failure);
        Assert.IsTrue(host.Last!.WasKilled, "a host that cannot prove itself is not left running");
    }

    [TestMethod]
    public async Task ARequestTheHostFails_ThrowsWithItsKind()
    {
        var host = new FakeHost { Serve = (r, c) => c.SendAsync(new Failed(r.RequestId, HostErrorKind.Refused, "over budget")) };
        await using var client = new AiHostClient(host, Fast);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));

        Assert.AreEqual((AiHostFailure.Request, HostErrorKind.Refused, "over budget"), (ex.Failure, ex.Kind, ex.Message));
        Assert.IsTrue(client.IsRunning, "a failed request leaves the host serving");
    }

    [TestMethod]
    public async Task AHostThatStopsAnsweringItsHeartbeat_IsKilled_AndTheNextRequestStartsAnother()
    {
        var host = new FakeHost { AnswerPings = false, Serve = FakeHost.Silent };
        await using var client = new AiHostClient(host, Fast);
        var exited = new TaskCompletionSource<AiHostExitedEventArgs>();
        client.HostExited += (_, e) => exited.TrySetResult(e);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));

        Assert.AreEqual(AiHostFailure.Lost, ex.Failure);
        StringAssert.Contains((await exited.Task.WaitAsync(Generous)).Reason, "stopped answering");
        Assert.IsTrue(host.Last!.WasKilled);

        host.AnswerPings = true;
        host.Serve = FakeHost.Describe;
        await Describe(client);
        Assert.AreEqual(2, host.Starts);
    }

    [TestMethod]
    public async Task ARequestSilentPastItsStallLimit_KillsTheHost()
    {
        var host = new FakeHost { Serve = FakeHost.Silent };
        await using var client = new AiHostClient(host, Fast);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client, TimeSpan.FromMilliseconds(300)));

        Assert.AreEqual(AiHostFailure.Lost, ex.Failure);
        Assert.IsTrue(host.Last!.WasKilled, "pings alone do not prove a request is moving");
    }

    [TestMethod]
    public async Task CancellingARequest_StopsItInTheHost_WhichKeepsServing()
    {
        var host = new FakeHost { Serve = FakeHost.Silent };
        await using var client = new AiHostClient(host, Fast);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(() => Describe(client, ct: cts.Token));

        Assert.AreEqual(1, host.CancelsReceived.Count);
        Assert.IsTrue(client.IsRunning);
        Assert.IsFalse(host.Last!.WasKilled);
    }

    [TestMethod]
    public async Task LeavingARequestEarly_StopsItInTheHost()
    {
        var host = new FakeHost
        {
            Serve = (r, c) => c.SendAsync(new Progress(r.RequestId, "loading", 1, 10)),   // and never ends
        };
        await using var client = new AiHostClient(host, Fast);

        await foreach (var _ in client.SendAsync(id => new DescribeHost(id), Generous))
            break;

        Assert.AreEqual(1, host.CancelsReceived.Count);
    }

    [TestMethod]
    public async Task ACancelTheHostIgnores_KillsItAfterTheGrace()
    {
        var host = new FakeHost { Serve = FakeHost.Silent, HonourCancel = false };
        await using var client = new AiHostClient(host, Fast);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(() => Describe(client, ct: cts.Token));

        Assert.IsTrue(host.Last!.WasKilled, "a host that will not stop is hung");
        Assert.IsFalse(client.IsRunning);
    }

    [TestMethod]
    public async Task AHostThatKeepsCrashing_IsNotRestartedUntilTheWindowPasses()
    {
        var host = new FakeHost { CrashAfterHandshake = true, Serve = FakeHost.Silent };
        await using var client = new AiHostClient(host, Fast);
        int exits = 0;
        client.HostExited += (_, _) => Interlocked.Increment(ref exits);

        for (int i = 1; i <= Fast.CrashLimit; i++)
        {
            await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));
            while (Volatile.Read(ref exits) < i) await Task.Delay(10);   // the exit is seen on the client's own time
        }
        var refused = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));

        Assert.AreEqual(AiHostFailure.Unavailable, refused.Failure);
        Assert.AreEqual(Fast.CrashLimit, host.Starts, "no host is started while it keeps crashing");

        await Task.Delay(Fast.CrashWindow + TimeSpan.FromMilliseconds(200));
        host.CrashAfterHandshake = false;
        host.Serve = FakeHost.Describe;
        await Describe(client);
        Assert.AreEqual(Fast.CrashLimit + 1, host.Starts);
    }
}
