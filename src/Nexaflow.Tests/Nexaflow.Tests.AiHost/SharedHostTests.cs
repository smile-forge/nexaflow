using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The client and the machine's AI host service: joined where it runs, speaks this app's protocol and is answered by the service's
/// own process, else a host of the app's own started; and, once joined, never killed — a stalled request fails alone, and a missed
/// heartbeat lets the connection go for the next request to join again.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class SharedHostTests
{
    /// <summary>Watchdog limits short enough for a test to cross.</summary>
    private static readonly AiHostTimings Fast = new(
        HeartbeatInterval: TimeSpan.FromMilliseconds(50),
        HeartbeatTimeout:  TimeSpan.FromMilliseconds(500),
        CancelGrace:       TimeSpan.FromMilliseconds(500),
        CrashLimit:        3,
        CrashWindow:       TimeSpan.FromSeconds(2),
        ExitWait:          TimeSpan.FromSeconds(5));

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static async Task<List<HostReply>> Describe(AiHostClient client, TimeSpan? stall = null)
    {
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), stall ?? Generous))
            replies.Add(reply);
        return replies;
    }

    [TestMethod]
    public async Task WhereTheServiceRuns_TheAppJoinsIt_AndStartsNoHostOfItsOwn()
    {
        await using var service = new FakeService();
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, app: "Aria", service: service);

        await Describe(client);
        await Describe(client);

        Assert.AreEqual((1, 0), (service.Joins, host.Starts), "one connection, serving both requests");
        var hello = service.Greetings.Single();
        Assert.AreEqual(("Aria", null, AiHostProtocol.Version), (hello.App, hello.Token, hello.ProtocolVersion));
        Assert.IsTrue(client.IsRunning);
    }

    [TestMethod]
    public async Task WhereTheServiceDoesNotRun_TheAppStartsAHostOfItsOwn()
    {
        await using var service = new FakeService { Running = false };
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, service: service);

        await Describe(client);

        Assert.AreEqual(1, host.Starts);
        Assert.IsTrue(service.Greetings.IsEmpty, "nothing is said to a service that is not running");
    }

    [TestMethod]
    public async Task APipeAnsweredByAProcessNotTheService_IsNeverSpokenTo()
    {
        await using var service = new FakeService { ReportsProcessId = System.Environment.ProcessId + 1 };
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, service: service);

        await Describe(client);

        Assert.IsTrue(service.Greetings.IsEmpty, "no hello reaches what took the service's pipe");
        Assert.AreEqual(1, host.Starts);
    }

    [TestMethod]
    public async Task AServiceOfAnotherProtocol_LeavesTheAppToAHostOfItsOwn()
    {
        await using var service = new FakeService { SpeaksProtocol = AiHostProtocol.Version - 1 };
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, service: service);

        await Describe(client);

        Assert.AreEqual((0, 1), (service.Joins, host.Starts));
    }

    [TestMethod]
    public async Task AServiceThatTurnsTheAppAway_IsUnavailable_SayingWhy()
    {
        await using var service = new FakeService { RefusesWith = "not granted" };
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, service: service);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));

        Assert.AreEqual(AiHostFailure.Unavailable, ex.Failure);
        StringAssert.Contains(ex.Message, "not granted");
        Assert.AreEqual(0, host.Starts, "turned away is not absent: the app does not go round the service");
    }

    [TestMethod]
    public async Task ARequestTheServiceLeavesSilent_FailsAlone_AndTheConnectionServesTheNext()
    {
        int served = 0;
        await using var service = new FakeService
        {
            Serve = (request, channel) => Interlocked.Increment(ref served) == 1 ? FakeHost.Silent(request, channel) : FakeHost.Describe(request, channel),
        };
        await using var client = new AiHostClient(new FakeHost(), Fast, service: service);
        int exits = 0;
        client.HostExited += (_, _) => Interlocked.Increment(ref exits);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client, TimeSpan.FromMilliseconds(300)));
        await Describe(client);

        Assert.AreEqual(AiHostFailure.Stalled, ex.Failure);
        Assert.AreEqual(1, service.CancelsReceived.Count, "the request is cancelled in the host, which goes on");
        Assert.AreEqual((1, 0), (service.Joins, exits), "the same connection, and no host's end");
    }

    [TestMethod]
    public async Task AServiceMissingItsHeartbeat_IsLetGo_AndTheNextRequestJoinsItAgain()
    {
        await using var service = new FakeService { AnswerPings = false, Serve = FakeHost.Silent };
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, service: service);
        int exits = 0;
        client.HostExited += (_, _) => Interlocked.Increment(ref exits);

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(() => Describe(client));
        Assert.AreEqual(AiHostFailure.Lost, ex.Failure);

        service.AnswerPings = true;
        service.Serve = FakeHost.Describe;
        await Describe(client);

        Assert.AreEqual((2, 0, 0), (service.Joins, host.Starts, exits), "joined again, nothing started, nothing counted against a host");
    }

    [TestMethod]
    public async Task TheServiceLastJoined_RunsOnForTheApp_TillItStops()
    {
        await using var service = new FakeService();
        await using var client = new AiHostClient(new FakeHost(), Fast, service: service);
        client.RetireWhenIdle(TimeSpan.FromMilliseconds(100), holding: () => false);
        await Describe(client);

        // Let go for standing idle: what the service keeps of this app is kept by its name, not by its connection.
        var letGo = DateTime.UtcNow + Generous;
        while (service.Left == 0 && DateTime.UtcNow < letGo) await Task.Delay(20);
        Assert.AreEqual(1, service.Left, "the idle connection was let go");

        Assert.IsTrue(client.IsRunning);
        service.Running = false;
        Assert.IsFalse(client.IsRunning);
    }
}
