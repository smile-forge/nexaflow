using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The AI host service's supervisor: clients taken on the service's pipe are served by its worker as the service's own process; a
/// worker that fails is started again, and one that keeps failing only after a pause; one that exits to take up a new runtime at
/// once; stopping the service stops the worker, killing one that will not; and the pipe is the service's alone.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ServiceSupervisorTests
{
    private static readonly AiHostTimings Fast = new(
        HeartbeatInterval: TimeSpan.FromMilliseconds(50),
        HeartbeatTimeout:  TimeSpan.FromSeconds(5),
        CancelGrace:       TimeSpan.FromMilliseconds(500),
        CrashLimit:        3,
        CrashWindow:       TimeSpan.FromSeconds(2),
        ExitWait:          TimeSpan.FromSeconds(5));

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static readonly WorkerRestarts Quick = new(Limit: 3, Window: TimeSpan.FromSeconds(30), Pause: TimeSpan.FromMinutes(10),
                                                       StopGrace: TimeSpan.FromMilliseconds(300));

    private sealed record Supervising(string PipeName, WorkerLaunches Launches, CancellationTokenSource Stop, Task Running) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Stop.CancelAsync();
            try { await Running.WaitAsync(Generous); }
            catch (Exception ex) when (ex is Win32Exception or OperationCanceledException) { }
            Stop.Dispose();
        }
    }

    private static Supervising Supervise(Func<int, IWorkerProcess> worker, WorkerRestarts? restarts = null, string? pipeName = null)
    {
        pipeName ??= "nexaflow-test-service-" + Guid.NewGuid().ToString("N");
        var pipes = new ServicePipes(pipeName, ServicePipes.For(WindowsIdentity.GetCurrent().User!));
        var launches = new WorkerLaunches(worker);
        var log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-service-").FullName);
        var supervisor = new WorkerSupervisor(pipes.AcceptAsync, launches, log, restarts ?? Quick, TimeProvider.System);
        var stop = new CancellationTokenSource();
        return new Supervising(pipeName, launches, stop, Task.Run(() => supervisor.RunAsync(stop.Token)));
    }

    private static async Task<string> DescribeAsync(string pipeName)
    {
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Fast, app: "Test", service: new ServiceInThisProcess(pipeName));
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), Generous)) replies.Add(reply);
        Assert.AreEqual(0, host.Starts, "the service served it: the app started no host of its own");
        return ((HostDescription)replies.Single()).RuntimeVersion;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Generous;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(10);
        Assert.IsTrue(condition(), "it never came to pass");
    }

    [TestMethod]
    public async Task AClientOfTheService_IsServedByItsWorker_AsTheServicesOwnProcess()
    {
        await using var service = Supervise(n => new InProcessWorker(n));

        Assert.AreEqual("worker 1", await DescribeAsync(service.PipeName));
        Assert.AreEqual("worker 1", await DescribeAsync(service.PipeName), "every client, by the one worker");
        Assert.AreEqual(1, service.Launches.Starts);
    }

    [TestMethod]
    public async Task AWorkerThatFails_IsStartedAgain_AndServesTheNextClient()
    {
        var workers = new List<InProcessWorker>();
        await using var service = Supervise(n =>
        {
            var worker = new InProcessWorker(n);
            lock (workers) workers.Add(worker);
            return worker;
        });
        Assert.AreEqual("worker 1", await DescribeAsync(service.PipeName));

        lock (workers) workers[0].Kill();
        await UntilAsync(() => service.Launches.Starts == 2);

        Assert.AreEqual("worker 2", await DescribeAsync(service.PipeName));
    }

    [TestMethod]
    public async Task AWorkerFailingAgainAndAgain_IsStartedAgainOnlyAfterAPause()
    {
        var restarts = Quick with { Limit = 2, Pause = TimeSpan.FromMilliseconds(800) };
        await using var service = Supervise(n =>
        {
            var worker = new ScriptedWorker(n);
            worker.ExitWith(1);
            return worker;
        }, restarts);

        await UntilAsync(() => service.Launches.Starts == 2);
        await Task.Delay(300);
        Assert.AreEqual(2, service.Launches.Starts, "two failures in the window: the next waits out the pause");
        await UntilAsync(() => service.Launches.Starts >= 3);
    }

    [TestMethod]
    public async Task AWorkerExitingToTakeUpANewRuntime_IsStartedAgainAtOnce_AndThatIsNoFailure()
    {
        int exits = 0;
        await using var service = Supervise(n =>
        {
            var worker = new ScriptedWorker(n);
            if (Interlocked.Increment(ref exits) <= 5) worker.ExitWith(WorkerExit.RuntimeChanged);
            return worker;
        });

        await UntilAsync(() => service.Launches.Starts == 6);
    }

    [TestMethod]
    public async Task StoppingTheService_StopsItsWorker()
    {
        ScriptedWorker? worker = null;
        var service = Supervise(n => worker = new ScriptedWorker(n));
        await UntilAsync(() => worker is not null);

        await service.DisposeAsync();

        Assert.AreEqual(WorkerExit.Stopped, await worker!.Exited);
        Assert.IsFalse(worker.WasKilled);
    }

    [TestMethod]
    public async Task AWorkerThatWillNotStop_IsKilledAfterTheGrace()
    {
        ScriptedWorker? worker = null;
        var service = Supervise(n => worker = new ScriptedWorker(n, stopsWhenAsked: false));
        await UntilAsync(() => worker is not null);

        await service.DisposeAsync();

        Assert.IsTrue(worker!.WasKilled);
    }

    [TestMethod]
    public async Task TheServicesPipe_TakenAlreadyByAnother_IsRefused_AndTheServiceStops()
    {
        string name = "nexaflow-test-service-" + Guid.NewGuid().ToString("N");
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);
        var service = Supervise(n => new ScriptedWorker(n), pipeName: name);

        await Assert.ThrowsExactlyAsync<Win32Exception>(() => service.Running.WaitAsync(Generous));
        await service.DisposeAsync();
    }

    [TestMethod]
    public void TheServicesPipe_LetsASignedInAccountReadAndWrite_NeverOpenAnInstance_AndNoOneAcrossTheNetwork()
    {
        var service = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
        var acl = ServicePipes.For(service).DiscretionaryAcl!.Cast<CommonAce>().ToList();

        var signedIn = acl.Single(a => a.SecurityIdentifier.IsWellKnown(WellKnownSidType.AuthenticatedUserSid));
        Assert.AreEqual(AceType.AccessAllowed, signedIn.AceType);
        Assert.AreEqual(0, signedIn.AccessMask & (int)PipeAccessRights.CreateNewInstance, "no client opens an instance to answer on");
        Assert.AreEqual((int)PipeAccessRights.ReadWrite, signedIn.AccessMask & (int)PipeAccessRights.ReadWrite);
        Assert.AreEqual(AceType.AccessDenied, acl.Single(a => a.SecurityIdentifier.IsWellKnown(WellKnownSidType.NetworkSid)).AceType);
        Assert.IsTrue(acl.Any(a => a.SecurityIdentifier == service && a.AceType == AceType.AccessAllowed), "the service's own account");
    }

    [TestMethod]
    [DataRow("not a handle")]
    [DataRow("1234")]
    [DataRow("1234 not-an-account")]
    public async Task AHandedLineNamingNoPipeAndAccount_IsRefused(string line)
    {
        var pipes = new HandedPipes(new StringReader(line + "\n"));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => pipes.NextAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task HandedPipes_End_WhenTheSupervisorStopsHandingThem()
    {
        Assert.IsNull(await new HandedPipes(new StringReader("")).NextAsync(CancellationToken.None));
    }
}
