namespace Nexaflow.AiHost.Service;

/// <summary>
/// Watches, in the service's worker, for the store to hold another runtime than the one the worker loaded — a CUDA pack fetched since
/// it started, or the one it runs from removed — and says when it may exit to take it up (<see cref="WorkerExit.RuntimeChanged"/>):
/// once nothing is held or running, so no client loses more than its connection. Native libraries load once per process, so another
/// runtime is taken up only by another worker.
/// </summary>
/// <param name="available">The runtime the store holds now: the CUDA pack's folder, null for the install's CPU runtime.</param>
/// <param name="loaded">The one the worker loaded.</param>
/// <param name="busy">Whether anything is held or running.</param>
internal sealed class RuntimeWatch(Func<string?> available, string? loaded, Func<bool> busy, TimeSpan every, TimeProvider time)
{
    /// <summary>How often the worker looks: a pack arrives once in a long while, and a minute's wait is no one's.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    /// <summary>Completes once another runtime is in the store and nothing is held or running.</summary>
    public async Task UntilWorthRestartingAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(every, time);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            if (!string.Equals(available(), loaded, StringComparison.OrdinalIgnoreCase) && !busy()) return;
    }
}
