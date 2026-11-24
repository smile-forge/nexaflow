using System.Diagnostics;
using System.IO.Pipes;

using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>Starts the workers a test's supervisor runs, each made by <paramref name="make"/>, and counts them.</summary>
internal sealed class WorkerLaunches(Func<int, IWorkerProcess> make) : IWorkerLauncher
{
    private int _starts;

    public int Starts => Volatile.Read(ref _starts);

    public IWorkerProcess Start() => make(Interlocked.Increment(ref _starts));
}

/// <summary>
/// A service's worker run in the test process: a listener serving the pipes handed to it — duplicated into this process, as into a
/// worker's — on a line of its own, welcoming clients as this process, the one the test's service is said to run in. It describes
/// itself as <c>worker {n}</c>.
/// </summary>
internal sealed class InProcessWorker : IWorkerProcess
{
    private readonly AnonymousPipeServerStream _line = new(PipeDirection.Out);
    private readonly StreamWriter _control;
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();

    public InProcessWorker(int number)
    {
        Id = number;
        _control = new StreamWriter(_line);
        var handed = new HandedPipes(new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, _line.ClientSafePipeHandle)));
        var log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-worker-").FullName);
        var listener = new HostListener(handed, token: null, [new Describes($"worker {number}")], HostPolicy.Default, log, endsWithLastClient: false,
                                        welcomeAs: Environment.ProcessId);
        _ = Task.Run(async () =>
        {
            await listener.RunAsync();
            _exited.TrySetResult(WorkerExit.Stopped);
        });
    }

    public int Id { get; }

    public Task<int> Exited => _exited.Task;

    public bool WasKilled { get; private set; }

    public void Hand(TakenClient client)
    {
        lock (_gate)
        {
            if (_exited.Task.IsCompleted) throw new InvalidOperationException($"worker {Id} has exited");
            PipeHandover.Hand(client, _self.SafeHandle, _control);
        }
    }

    public void Stop()
    {
        lock (_gate) _control.Dispose();
    }

    /// <summary>Dies as a crashed worker does: its exit is a failure's.</summary>
    public void Kill()
    {
        WasKilled = true;
        Stop();
        _exited.TrySetResult(-1);
    }

    public void Dispose() => _self.Dispose();

    private sealed class Describes(string name) : HostRequestHandler<DescribeHost>
    {
        protected override Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, CancellationToken ct)
            => reply(new HostDescription(request.RequestId, name, ["CPUExecutionProvider"], null, []));
    }
}

/// <summary>A worker that does only what a test says: exits with a code when told, and stops when asked unless it is told not to.</summary>
internal sealed class ScriptedWorker(int id, bool stopsWhenAsked = true) : IWorkerProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id { get; } = id;

    public Task<int> Exited => _exited.Task;

    public bool WasKilled { get; private set; }

    public void ExitWith(int code) => _exited.TrySetResult(code);

    public void Hand(TakenClient client) { }

    public void Stop()
    {
        if (stopsWhenAsked) _exited.TrySetResult(WorkerExit.Stopped);
    }

    public void Kill()
    {
        WasKilled = true;
        _exited.TrySetResult(-1);
    }

    public void Dispose() { }
}

/// <summary>The service as a client finds it: <paramref name="pipeName"/>, run — as the service manager would say — by this process.</summary>
internal sealed class ServiceInThisProcess(string pipeName) : IAiHostService
{
    public string PipeName => pipeName;

    public bool IsInstalled() => true;

    public int? RunningProcessId() => Environment.ProcessId;
}
