using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// What a request being served has been doing since it last said anything, for the server to speak for it while it says nothing
/// itself (<see cref="HostServer"/>). The client takes a request silent past its stall limit for a hung host, and work below people's
/// priority can wait that long — for a turn on its device, a slot to answer in, room in a context, a model loading — or move on a
/// step at a time between other work's steps, saying nothing until it has something to show. Waits mark themselves on the request
/// they are for (<see cref="WaitAsync{T}"/>); the server then says <see cref="Progress.Queued"/> for a request waiting now and
/// <see cref="Progress.Working"/> for one that has waited and gone on since it last spoke. A request stuck inside one step does neither,
/// so it stays silent and the watchdog still hears a hang.
/// </summary>
internal sealed class RequestActivity(TimeProvider clock)
{
    private static readonly AsyncLocal<RequestActivity?> Serving = new();

    /// <summary>The request the code running now serves; null outside any — a model loaded again after a picture, a warm-up.</summary>
    public static RequestActivity? Current => Serving.Value;

    private long _saidAt = clock.GetTimestamp();
    private int _waiting;
    private int _movedOn;

    /// <summary>Makes this the request everything from here on in the caller's flow serves.</summary>
    public void Enter() => Serving.Value = this;

    /// <summary>The request sent something: it has spoken for itself.</summary>
    public void Said()
    {
        Interlocked.Exchange(ref _saidAt, clock.GetTimestamp());
        Interlocked.Exchange(ref _movedOn, 0);
    }

    /// <summary>What to say for the request now that it has been silent for <paramref name="quiet"/>: queued while it waits, working
    /// once it has gone on from a wait since it last spoke; null while it spoke more recently, or has done neither.</summary>
    public string? Due(TimeSpan quiet)
    {
        if (clock.GetElapsedTime(Interlocked.Read(ref _saidAt)) < quiet) return null;
        if (Volatile.Read(ref _waiting) > 0) return Progress.Queued;
        return Volatile.Read(ref _movedOn) != 0 ? Progress.Working : null;
    }

    /// <summary><paramref name="wait"/>, marked on the request it is for (<see cref="Current"/>) while it lasts.</summary>
    public static async Task<T> WaitAsync<T>(Task<T> wait)
    {
        if (wait.IsCompleted || Current is not { } activity) return await wait;
        activity.Waiting();
        try { return await wait; }
        finally { activity.Waited(); }
    }

    /// <inheritdoc cref="WaitAsync{T}"/>
    public static async Task WaitAsync(Task wait)
    {
        if (wait.IsCompleted || Current is not { } activity)
        {
            await wait;
            return;
        }
        activity.Waiting();
        try { await wait; }
        finally { activity.Waited(); }
    }

    /// <inheritdoc cref="WaitAsync{T}"/>
    public static ValueTask<T> WaitAsync<T>(ValueTask<T> wait) => wait.IsCompleted ? wait : new ValueTask<T>(WaitAsync(wait.AsTask()));

    private void Waiting() => Interlocked.Increment(ref _waiting);

    private void Waited()
    {
        Interlocked.Decrement(ref _waiting);
        Interlocked.Exchange(ref _movedOn, 1);
    }
}
