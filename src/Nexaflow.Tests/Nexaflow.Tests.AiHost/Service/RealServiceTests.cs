using System.Security.Principal;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The service's supervisor with real workers — the host executable, started as the service starts it: a client taken on the
/// service's pipe is served in another process, welcomed as the supervisor's; a worker that dies is replaced and the client joins
/// the next; and stopping the service ends its worker as asked, not killed.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class RealServiceTests
{
    private static readonly AiHostTimings Timings = AiHostTimings.Default;
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    private static string HostPath => Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe");

    /// <summary>Starts real workers, keeping hold of each.</summary>
    private sealed class Recording(IWorkerLauncher inner) : IWorkerLauncher
    {
        private readonly List<IWorkerProcess> _started = [];

        public IReadOnlyList<IWorkerProcess> Started
        {
            get
            {
                lock (_started) return [.. _started];
            }
        }

        public IWorkerProcess Start()
        {
            var worker = inner.Start();
            lock (_started) _started.Add(worker);
            return worker;
        }
    }

    private sealed record Running(string PipeName, Recording Workers, CancellationTokenSource Stop, Task Supervising) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Stop.CancelAsync();
            await Supervising.WaitAsync(Generous);
            Stop.Dispose();
        }
    }

    /// <summary>A service on a store and a log folder of the test's own, its policy file naming the folder.</summary>
    private static Running Serve()
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-service-").FullName;
        string policy = Path.Combine(folder, "host.json");
        File.WriteAllText(policy, $$"""{ "logFolder": {{System.Text.Json.JsonSerializer.Serialize(Path.Combine(folder, "logs"))}} }""");
        string pipeName = "nexaflow-test-service-" + Guid.NewGuid().ToString("N");
        var log = new HostLog(Path.Combine(folder, "logs"));
        var workers = new Recording(new ProcessWorkerLauncher(HostPath, [AiHostProtocol.StoreArgument, Path.Combine(folder, "store"),
                                                                         AiHostProtocol.PolicyArgument, policy], log));
        var supervisor = new WorkerSupervisor(new ServicePipes(pipeName, ServicePipes.For(WindowsIdentity.GetCurrent().User!)).AcceptAsync,
                                              workers, log, WorkerRestarts.Default, TimeProvider.System);
        var stop = new CancellationTokenSource();
        return new Running(pipeName, workers, stop, Task.Run(() => supervisor.RunAsync(stop.Token)));
    }

    private static async Task<HostDescription> DescribeAsync(AiHostClient client)
    {
        HostDescription? description = null;
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), Generous)) description = reply as HostDescription;
        return description ?? throw new AssertFailedException("the host did not describe itself");
    }

    [TestMethod]
    public async Task AClientOfTheService_IsServedByAWorkerInAProcessOfItsOwn()
    {
        await using var service = Serve();
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Timings, app: "Test", service: new ServiceInThisProcess(service.PipeName));

        var description = await DescribeAsync(client);

        StringAssert.StartsWith(description.RuntimeVersion, "1.");
        Assert.AreEqual(0, host.Starts, "the app started no host of its own");
        Assert.AreNotEqual(Environment.ProcessId, service.Workers.Started.Single().Id, "served in the worker, welcomed as the service");
    }

    [TestMethod]
    public async Task AWorkerThatDies_IsReplaced_AndTheAppJoinsTheNext()
    {
        await using var service = Serve();
        var host = new FakeHost();
        await using var client = new AiHostClient(host, Timings, app: "Test", service: new ServiceInThisProcess(service.PipeName));
        await DescribeAsync(client);

        service.Workers.Started.Single().Kill();
        var until = DateTime.UtcNow + Generous;
        while (service.Workers.Started.Count < 2 && DateTime.UtcNow < until) await Task.Delay(20);

        // The request that finds the connection gone fails; the next joins the service again, and its new worker serves it.
        HostDescription? next = null;
        for (int attempt = 0; attempt < 3 && next is null; attempt++)
        {
            try { next = await DescribeAsync(client); }
            catch (AiHostException ex) when (ex.Failure == AiHostFailure.Lost) { }
        }
        Assert.IsNotNull(next);
        Assert.AreEqual(2, service.Workers.Started.Count);
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public async Task StoppingTheService_EndsItsWorker_AsAsked()
    {
        var service = Serve();
        await using (var client = new AiHostClient(new FakeHost(), Timings, app: "Test", service: new ServiceInThisProcess(service.PipeName)))
            await DescribeAsync(client);
        var worker = service.Workers.Started.Single();

        await service.DisposeAsync();

        Assert.AreEqual(WorkerExit.Stopped, await worker.Exited.WaitAsync(Generous));
    }
}
