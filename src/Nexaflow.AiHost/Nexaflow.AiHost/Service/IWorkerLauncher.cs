

namespace Nexaflow.AiHost.Service;

/// <summary>Starts the service's worker: the broker, in a process of its own.</summary>
internal interface IWorkerLauncher
{
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be started.</exception>
    IWorkerProcess Start();
}

/// <summary>A worker, as its supervisor watches it.</summary>
internal interface IWorkerProcess : IDisposable
{
    int Id { get; }

    /// <summary>Completes when the process has exited, with its exit code (<see cref="WorkerExit"/>).</summary>
    Task<int> Exited { get; }

    /// <summary>Hands the worker a client taken on the service's pipe. The worker takes a handle of its own to its pipe, so the caller
    /// closes its own.</summary>
    /// <exception cref="ObjectDisposedException">The worker was asked to stop.</exception>
    /// <exception cref="InvalidOperationException">The worker has exited.</exception>
    /// <exception cref="IOException">The worker is going, and cannot be told.</exception>
    void Hand(TakenClient client);

    /// <summary>Asks the worker to stop: it lets its clients go and exits (<see cref="WorkerExit.Stopped"/>).</summary>
    void Stop();

    void Kill();
}
