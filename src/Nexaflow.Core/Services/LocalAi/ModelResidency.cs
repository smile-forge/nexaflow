using System.Threading.Channels;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// Which of the models providers keep loaded stay loaded. A model its policy (<see cref="ModelPreloadConfig"/>) loads
/// ahead is warmed once the first window is up — one at a time, each while no model is in use, so a load never queues
/// in front of someone's request — and then kept. Any other loads with its first request and is let go once it has stood
/// idle. Every use is counted in the <see cref="ModelUseHistory"/> the policy reads next time. One for the app, like the
/// provider pool whose execution instances it follows: <see cref="ProviderManager"/> tracks each it makes and forgets
/// each it tears down.
/// </summary>
public sealed class ModelResidency : IAsyncDisposable
{
    /// <summary>How often models are checked for having stood idle — well inside any idle time the policy sets.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ModelPreloadConfig _config;
    private readonly ModelUseHistory _history;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<IAiProvider, Kept> _kept = new(ReferenceEqualityComparer.Instance);
    private readonly Channel<Kept> _ahead = Channel.CreateUnbounded<Kept>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _life = new();
    private TaskCompletionSource _quiet = Quiet();
    private Task _preloading = Task.CompletedTask;
    private Task _sweeping = Task.CompletedTask;
    private bool _open;

    public ModelResidency(ModelPreloadConfig config, ModelUseHistory history, TimeProvider? time = null)
    {
        _config  = config;
        _history = history;
        _time    = time ?? TimeProvider.System;
    }

    /// <summary>One model a provider keeps loaded: its provider, its use, and whether the policy loads it ahead.</summary>
    private sealed class Kept(IAiProvider provider, ITracksModelUse model, string key)
    {
        public IAiProvider Provider => provider;
        public ITracksModelUse Model => model;
        public string Key => key;
        public bool Ahead;
        public EventHandler? OnBegan;
        public EventHandler? OnIdle;
    }

    private static TaskCompletionSource Quiet()
    {
        var quiet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        quiet.SetResult();
        return quiet;
    }

    /// <summary>Follows <paramref name="provider"/>'s model, named <paramref name="key"/> in the history; a provider whose
    /// model is not kept loaded is not followed.</summary>
    public void Track(IAiProvider provider, string key)
    {
        if (provider is not ITracksModelUse model) return;
        var kept = new Kept(provider, model, key);
        kept.OnBegan = (_, _) => Counted(kept);
        kept.OnIdle  = (_, _) => Settled();
        lock (_gate)
        {
            if (!_kept.TryAdd(provider, kept)) return;
        }
        model.ModelUse.Began += kept.OnBegan;
        model.ModelUse.Idle  += kept.OnIdle;
    }

    /// <summary>Stops following <paramref name="provider"/>: it is being torn down, and its model let go with it.</summary>
    public void Forget(IAiProvider provider)
    {
        Kept? kept;
        lock (_gate)
            if (!_kept.Remove(provider, out kept)) return;
        kept.Model.ModelUse.Began -= kept.OnBegan;
        kept.Model.ModelUse.Idle  -= kept.OnIdle;
        Settled();
    }

    /// <summary>
    /// Weighs each of <paramref name="assigned"/> — a workspace's execution instances — against the policy: one it loads
    /// ahead is queued to warm and then kept; one it no longer loads ahead is let go once it stands idle. Queued warms run
    /// once <see cref="Open"/> has been called.
    /// </summary>
    public void Preload(IEnumerable<IAiProvider> assigned)
    {
        lock (_gate)
            foreach (var provider in assigned)
            {
                if (!_kept.TryGetValue(provider, out var kept)) continue;
                bool ahead = _config.Preloads(_history.DaysUsed(kept.Key));
                if (ahead && !kept.Ahead) _ahead.Writer.TryWrite(kept);
                kept.Ahead = ahead;
            }
    }

    /// <summary>Starts warming what is queued, and letting idle models go — once the first window is up, so nothing a
    /// model load does competes with it. Later calls do nothing.</summary>
    public void Open()
    {
        lock (_gate)
        {
            if (_open) return;
            _open = true;
            _preloading = Task.Run(PreloadAsync);
            _sweeping   = Task.Run(SweepAsync);
        }
    }

    /// <summary>A use began: the day counts toward loading it ahead, and queued warms wait for it.</summary>
    private void Counted(Kept kept)
    {
        _history.Record(kept.Key);
        lock (_gate)
            if (_quiet.Task.IsCompleted) _quiet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A use ended, or a model was forgotten: when no model is in use any more, a queued warm may go.</summary>
    private void Settled()
    {
        lock (_gate)
            if (!_kept.Values.Any(k => k.Model.ModelUse.InUse)) _quiet.TrySetResult();
    }

    /// <summary>Completes when no followed model is in use.</summary>
    private Task QuietAsync()
    {
        lock (_gate)
        {
            if (!_kept.Values.Any(k => k.Model.ModelUse.InUse)) _quiet.TrySetResult();
            return _quiet.Task;
        }
    }

    private async Task PreloadAsync()
    {
        try
        {
            await foreach (var kept in _ahead.Reader.ReadAllAsync(_life.Token).ConfigureAwait(false))
            {
                await QuietAsync().WaitAsync(_life.Token).ConfigureAwait(false);
                lock (_gate)
                    if (!kept.Ahead || !_kept.ContainsKey(kept.Provider)) continue;   // forgotten, or no longer loaded ahead
                await BestEffort(kept.Provider.WarmupAsync(_life.Token)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
    }

    private async Task SweepAsync()
    {
        using var timer = new PeriodicTimer(SweepInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_life.Token).ConfigureAwait(false))
                await ReleaseIdleAsync(_life.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
    }

    /// <summary>Lets go every model the policy does not load ahead that has stood idle for its idle time — none while
    /// that time is unset.</summary>
    internal async Task ReleaseIdleAsync(CancellationToken ct)
    {
        if (_config.IdleRelease is not { } idle) return;
        List<Kept> idleCandidates;
        lock (_gate)
            idleCandidates = [.. _kept.Values.Where(k => !k.Ahead && !k.Model.ReleasesItself)];
        foreach (var kept in idleCandidates)
            await BestEffort(kept.Model.ModelUse.ReleaseIfIdleAsync(idle, kept.Provider.CooldownAsync, ct)).ConfigureAwait(false);
    }

    /// <summary>Warming and letting go are best-effort, as providers promise: one that throws costs its own model, never
    /// the models after it.</summary>
    private static async Task BestEffort(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _life.Cancel();
        _ahead.Writer.TryComplete();
        await Task.WhenAll(_preloading, _sweeping).ConfigureAwait(false);
        _life.Dispose();
    }
}
