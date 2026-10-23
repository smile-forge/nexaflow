using System.Runtime.CompilerServices;

namespace Nexaflow.Providers.Common;

/// <summary>
/// When a provider's model is in use, and how long it has stood idle since: what lets the app keep a model loaded only
/// while it is wanted. A provider begins a use before it reaches for its model and ends it when the request — or the
/// session — is done; the app counts each use toward what it loads ahead next time, and releases a model that has stood
/// idle long enough. A release and a use never overlap: a use begun while the model is being released waits for that
/// release, then loads it again.
/// </summary>
public sealed class ModelUseTracker(TimeProvider? time = null)
{
    private readonly TimeProvider  _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _releasing = new(1, 1);
    private readonly Lock _gate = new();
    private int  _inUse;
    private bool _loaded;
    private long _idleSince;

    /// <summary>A use began. Raised on the thread that began it.</summary>
    public event EventHandler? Began;

    /// <summary>The last use in progress ended. Raised on the thread that ended it.</summary>
    public event EventHandler? Idle;

    /// <summary>Whether a request or a session is using the model now.</summary>
    public bool InUse
    {
        get { lock (_gate) return _inUse > 0; }
    }

    /// <summary>Begins a use, once any release in progress is done; disposing the returned value ends it.</summary>
    public async ValueTask<IDisposable> BeginAsync(CancellationToken ct = default)
    {
        await _releasing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _inUse++;
                _loaded = true;
            }
        }
        finally { _releasing.Release(); }
        Began?.Invoke(this, EventArgs.Empty);
        return new Use(this);
    }

    /// <summary>Yields <paramref name="source"/> as one use: begun before its first item is asked for, ended when it ends
    /// or is left.</summary>
    public async IAsyncEnumerable<T> During<T>(IAsyncEnumerable<T> source, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using (await BeginAsync(ct).ConfigureAwait(false))
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
                yield return item;
    }

    /// <summary>The model was loaded with no use — warmed ahead of one: it counts as idle from now.</summary>
    public void Loaded()
    {
        lock (_gate)
        {
            _loaded = true;
            if (_inUse == 0) _idleSince = _time.GetTimestamp();
        }
    }

    /// <summary>
    /// Runs <paramref name="release"/> when the model is loaded, nothing is using it, and it has stood idle for at least
    /// <paramref name="idleFor"/>; a use begun meanwhile waits for it. Returns whether it ran.
    /// </summary>
    public async Task<bool> ReleaseIfIdleAsync(TimeSpan idleFor, Func<CancellationToken, Task> release, CancellationToken ct = default)
    {
        await _releasing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (!_loaded || _inUse > 0 || _time.GetElapsedTime(_idleSince) < idleFor) return false;
                _loaded = false;
            }
            await release(ct).ConfigureAwait(false);
            return true;
        }
        finally { _releasing.Release(); }
    }

    private void End()
    {
        bool idle;
        lock (_gate)
        {
            idle = --_inUse == 0;
            if (idle) _idleSince = _time.GetTimestamp();
        }
        if (idle) Idle?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Use(ModelUseTracker tracker) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) tracker.End();
        }
    }
}
