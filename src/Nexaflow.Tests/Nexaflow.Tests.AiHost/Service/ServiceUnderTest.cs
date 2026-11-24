using System.Security.Principal;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The AI host service run in a test: its supervisor in this process, on a pipe of its own, and its workers the host executable as
/// the service starts them, keeping <c>store</c>, logging to a folder of the test's own. Apps join it by name (<see cref="Join"/>), as
/// they join the machine's service, and every one of them is served by the one worker: one broker, many clients.
/// </summary>
internal sealed class ServiceUnderTest : IAsyncDisposable
{
    private readonly Recording _workers;
    private readonly CancellationTokenSource _stop;
    private readonly Task _supervising;

    private ServiceUnderTest(string pipeName, Recording workers, CancellationTokenSource stop, Task supervising)
    {
        PipeName = pipeName;
        _workers = workers;
        _stop = stop;
        _supervising = supervising;
    }

    /// <summary>How long stopping the service may take, its worker's grace included.</summary>
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(60);

    public string PipeName { get; }

    /// <summary>The workers it started, in order.</summary>
    public IReadOnlyList<IWorkerProcess> Workers => _workers.Started;

    /// <summary>A service whose worker keeps <paramref name="store"/> — a folder of the test's own where none is given — and reads a
    /// policy file of the test's own, naming its log folder.</summary>
    public static ServiceUnderTest Start(string? store = null)
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-service-").FullName;
        string logs = Path.Combine(folder, "logs");
        string policy = Path.Combine(folder, "host.json");
        File.WriteAllText(policy, $$"""{ "logFolder": {{System.Text.Json.JsonSerializer.Serialize(logs)}} }""");
        string pipeName = "nexaflow-test-service-" + Guid.NewGuid().ToString("N");
        var log = new HostLog(logs, modelCalls: true);
        var workers = new Recording(new ProcessWorkerLauncher(TestStore.HostPath, [AiHostProtocol.StoreArgument, store ?? Path.Combine(folder, "store"),
                                                                                    AiHostProtocol.PolicyArgument, policy], log));
        var pipes = new ServicePipes(pipeName, ServicePipes.For(WindowsIdentity.GetCurrent().User!));
        var supervisor = new WorkerSupervisor(pipes.AcceptAsync, workers, log, WorkerRestarts.Default, TimeProvider.System);
        var stop = new CancellationTokenSource();
        return new ServiceUnderTest(pipeName, workers, stop, Task.Run(() => supervisor.RunAsync(stop.Token)));
    }

    /// <summary>An app joining the service as <paramref name="app"/>. It never starts a host of its own: a join that fails fails its
    /// request, saying so.</summary>
    public AiHostClient Join(string app, AiHostTimings? timings = null)
        => new(new NoPrivateHost(), timings ?? AiHostTimings.Default, app: app, service: new ServiceInThisProcess(PipeName));

    /// <summary>Stops the service — its worker as asked, or killed past the grace.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _supervising.WaitAsync(StopWait);
        _stop.Dispose();
    }

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

    /// <summary>What an app joining the test's service starts in its place: nothing.</summary>
    private sealed class NoPrivateHost : IAiHostLauncher
    {
        public IAiHostProcess Start(string pipeName, string token, AiHostEnvironment environment)
            => throw new AiHostException(AiHostFailure.Unavailable, "The test's service was not joined, and its apps start no host of their own.");

        public HostMemory Memory => HostMemory.Unbounded;
    }
}
