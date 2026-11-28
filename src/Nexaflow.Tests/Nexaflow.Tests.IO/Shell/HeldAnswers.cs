using System.Collections.Concurrent;
using Nexaflow.IO.Common.Shell;

namespace Nexaflow.Tests.IO.Shell;

/// <summary>
/// A test's own thread as the shell's answers are handed to it: what is posted waits until the test pumps, as a window's
/// thread runs what is posted to it once it is free.
/// </summary>
internal sealed class HeldAnswers : IAnswerThread
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly SemaphoreSlim _arrived = new(0);

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        _arrived.Release();
    }

    /// <summary>Runs what is posted until <paramref name="done"/>, failing after ten seconds.</summary>
    public void PumpUntil(Func<bool> done, string what = "the answers did not arrive")
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _arrived.Wait(TimeSpan.FromMilliseconds(20));
            while (_posted.TryDequeue(out var action)) action();
        }
        Assert.IsTrue(done(), what);
    }

    /// <summary>Runs what is posted for <paramref name="time"/>.</summary>
    public void PumpFor(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        PumpUntil(() => DateTime.UtcNow >= until);
    }

    /// <summary>Runs <paramref name="test"/> on a thread of the shell's own apartment, as the shell's callers are.</summary>
    public static void OnSta(Action test)
    {
        Exception? failed = null;
        var thread = new Thread(() => { try { test(); } catch (Exception e) { failed = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failed is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
    }
}
