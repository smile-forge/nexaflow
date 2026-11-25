using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The one thread the shell's windows run on, as the session and its workspaces see it — whatever UI toolkit draws them.
/// Everything that has to reach that thread from elsewhere — a file watch, a finished background task, a forwarded launch —
/// goes through this rather than capturing a dispatcher of its own, so there is one, given by the app at start.
/// </summary>
public interface IUiThread
{
    /// <summary>Whether the calling thread is the UI thread.</summary>
    bool IsCurrent { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread and returns once it has run — at once when called there.</summary>
    void Run(Action action);

    /// <summary>Runs <paramref name="func"/> on the UI thread and returns what it returns — at once when called there.</summary>
    T Run<T>(Func<T> func);

    /// <summary>Runs <paramref name="action"/> on the UI thread: at once when called there, otherwise queued. The task ends
    /// when it has run.</summary>
    Task RunAsync(Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread — at once when called there — and ends with what it ends with.</summary>
    Task<T> RunAsync<T>(Func<Task<T>> action);

    /// <summary>Queues <paramref name="action"/> behind what is already waiting on the UI thread, even when called there.</summary>
    void Post(Action action);

    /// <summary>Queues <paramref name="action"/> for when the UI thread has drawn and handled its input — work that must not
    /// hold up a frame.</summary>
    void PostInBackground(Action action);

    /// <summary>
    /// A timer whose tick runs on the UI thread, made stopped: every <paramref name="interval"/>, or once each time it is
    /// started when <paramref name="once"/>.
    /// </summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once);
}
