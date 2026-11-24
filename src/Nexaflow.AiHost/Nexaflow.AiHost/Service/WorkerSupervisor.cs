using System.ComponentModel;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// The AI host service: a thin supervisor that takes clients on the service's pipe (<paramref name="accept"/>) and hands each to its
/// worker — the broker, in a process of its own — so a native crash in llama.cpp or ONNX Runtime costs the worker's requests, never
/// the service. A worker runs only while clients want one: started for the first client, it exits once no client has been connected
/// and nothing held for a while (<see cref="WorkerExit.Idle"/>) — each app deciding, by how long it stays connected while idle, how
/// long that is — and the next client starts another; idle, the service holds no runtime, on the GPU or in memory. A worker that
/// exits to take up a new runtime (<see cref="WorkerExit.RuntimeChanged"/>) is started again for the next client too; one that fails
/// is, unless it has failed <see cref="WorkerRestarts.Limit"/> times within <see cref="WorkerRestarts.Window"/>, when the next start
/// waits <see cref="WorkerRestarts.Pause"/>. A client taken while no worker runs waits for the one it starts; one handed to a worker
/// that then fails goes with it, and joins the next.
/// </summary>
internal sealed class WorkerSupervisor(Func<CancellationToken, Task<TakenClient>> accept, IWorkerLauncher launcher, HostLog log,
                                       WorkerRestarts restarts, TimeProvider time)
{
    private readonly Lock _gate = new();

    /// <summary>A client is waiting for a worker: one is started for it.</summary>
    private readonly SemaphoreSlim _demand = new(0);
    private TaskCompletionSource<IWorkerProcess> _live = Vacant();

    /// <summary>Supervises until <paramref name="ct"/> is cancelled, then stops the worker — killed where it has not stopped within
    /// <see cref="WorkerRestarts.StopGrace"/>.</summary>
    /// <exception cref="Win32Exception">The service's pipe could not be opened: its name is taken.</exception>
    public async Task RunAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = Task.Run(() => KeepWorkerAsync(stop.Token), CancellationToken.None);
        try
        {
            await TakeClientsAsync(stop.Token).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await workers.ConfigureAwait(false);
        }
    }

    private async Task TakeClientsAsync(CancellationToken ct)
    {
        while (true)
        {
            TakenClient client;
            try { client = await accept(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (IOException ex)
            {
                log.Write(ex.Message);
                continue;
            }
            using (client)
            {
                try { await HandAsync(client, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    /// <summary>Hands <paramref name="client"/> to the live worker — one started for it where none runs; where that one is going as it
    /// is handed the client, to the next, once the going one's exit is seen.</summary>
    private async Task HandAsync(TakenClient client, CancellationToken ct)
    {
        while (true)
        {
            var live = LiveWorker();
            if (!live.IsCompleted) _demand.Release();   // none runs: one is started for this client
            var worker = await live.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                worker.Hand(client);
                return;
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or ObjectDisposedException or InvalidOperationException)
            {
                log.Write($"worker {worker.Id} is going as a client comes: {ex.Message}; the client waits for the next");
            }
            await worker.Exited.WaitAsync(ct).ConfigureAwait(false);
            while (LiveWorker() == live) await Task.Delay(10, ct).ConfigureAwait(false);   // its going taken in
        }
    }

    private Task<IWorkerProcess> LiveWorker()
    {
        lock (_gate) return _live.Task;
    }

    private async Task KeepWorkerAsync(CancellationToken ct)
    {
        var failures = new Queue<DateTimeOffset>();
        while (await DemandedAsync(ct).ConfigureAwait(false) && await WaitOutFailuresAsync(failures, ct).ConfigureAwait(false))
        {
            IWorkerProcess worker;
            try { worker = launcher.Start(); }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                log.Write($"the worker could not be started: {ex.Message}");
                failures.Enqueue(time.GetUtcNow());
                _demand.Release();   // the client that wanted it still waits
                continue;
            }
            using (worker)
            {
                log.Write($"worker {worker.Id} started");
                lock (_gate) _live.TrySetResult(worker);
                int code;
                try
                {
                    code = await worker.Exited.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Withdraw();
                    await StopAsync(worker).ConfigureAwait(false);
                    return;
                }
                Withdraw();
                switch (code)
                {
                    case WorkerExit.Idle:
                        log.Write($"worker {worker.Id} exited with no client to serve; the next client starts another");
                        break;
                    case WorkerExit.RuntimeChanged:
                        log.Write($"worker {worker.Id} exited to take up a new runtime; the next client starts another");
                        break;
                    default:
                        log.Write($"worker {worker.Id} exited with 0x{code:X8}");
                        failures.Enqueue(time.GetUtcNow());
                        break;
                }
            }
        }
    }

    /// <summary>Waits for a client to want a worker — those that wanted one together are one want; false once the service is stopping.</summary>
    private async Task<bool> DemandedAsync(CancellationToken ct)
    {
        try
        {
            await _demand.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        while (_demand.Wait(0)) { }
        return true;
    }

    /// <summary>The live worker has gone: clients taken from now on wait for the next.</summary>
    private void Withdraw()
    {
        lock (_gate)
            if (_live.Task.IsCompleted) _live = Vacant();
    }

    /// <summary>Waits out <see cref="WorkerRestarts.Pause"/> where the worker failed <see cref="WorkerRestarts.Limit"/> times within
    /// the window; false once the service is stopping.</summary>
    private async Task<bool> WaitOutFailuresAsync(Queue<DateTimeOffset> failures, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        while (failures.TryPeek(out var at) && time.GetUtcNow() - at > restarts.Window) failures.Dequeue();
        if (failures.Count < restarts.Limit) return true;
        log.Write($"the worker failed {failures.Count} times in {restarts.Window.TotalMinutes:0.#} minutes; the next starts in {restarts.Pause.TotalSeconds:0} s");
        try
        {
            await Task.Delay(restarts.Pause, time, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        failures.Clear();
        return true;
    }

    private async Task StopAsync(IWorkerProcess worker)
    {
        worker.Stop();
        try
        {
            await worker.Exited.WaitAsync(restarts.StopGrace, time).ConfigureAwait(false);
            log.Write($"worker {worker.Id} stopped");
        }
        catch (TimeoutException)
        {
            worker.Kill();
            log.Write($"worker {worker.Id} did not stop within {restarts.StopGrace.TotalSeconds:0} s, and was killed");
        }
    }

    private static TaskCompletionSource<IWorkerProcess> Vacant() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>How the supervisor restarts its worker: after <see cref="Limit"/> failures within <see cref="Window"/> the next start waits
/// <see cref="Pause"/>; a worker asked to stop that has not within <see cref="StopGrace"/> is killed.</summary>
internal sealed record WorkerRestarts(int Limit, TimeSpan Window, TimeSpan Pause, TimeSpan StopGrace)
{
    public static WorkerRestarts Default { get; } = new(3, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
}

/// <summary>How a worker's exit reads to its supervisor; any other code is a failure.</summary>
internal static class WorkerExit
{
    /// <summary>Asked to stop, it stopped.</summary>
    public const int Stopped = 0;

    /// <summary>A CUDA pack its runtime is not loaded from arrived — or the one it is went — while nothing was held or running: the next
    /// client starts it again, on what the store now holds.</summary>
    public const int RuntimeChanged = 10;

    /// <summary>No client was connected, and nothing held, for <see cref="IdleWatch.Linger"/>: it is started again for the next client.</summary>
    public const int Idle = 11;
}
