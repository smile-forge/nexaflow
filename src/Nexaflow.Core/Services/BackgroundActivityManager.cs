using Nexaflow.Core.Models;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Windows;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Core.Services;

/// <summary>
/// Owns the shell's background task collection and exposes
/// <see cref="IBackgroundActivityManager"/> so providers can report activity
/// without knowing about WPF.  Thread-safe: any thread can call
/// <see cref="StartActivity"/>; all collection mutations are marshalled to
/// the UI dispatcher.
/// Starts with an "Idle" placeholder; replaces it when work begins and
/// restores it when all in-flight activities complete.
/// </summary>
public sealed class BackgroundActivityManager : IBackgroundActivityManager
{
    public ObservableCollection<BackgroundTask> Tasks { get; } = [];

    private int _activeCount;
    private BackgroundTask? _idlePlaceholder;

    /// <summary>True while at least one activity is in the Running state.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Fired (on the UI thread) whenever <see cref="IsActive"/> changes.</summary>
    public event EventHandler<bool>? IsActiveChanged;

    public BackgroundActivityManager() => Dispatch(InsertIdlePlaceholder);

    // ── IBackgroundActivityManager ─────────────────────────────────────────

    public IActivityHandle StartActivity(string description)
    {
        var task = new BackgroundTask { Description = description };

        Dispatch(() =>
        {
            RemoveIdlePlaceholder();
            Tasks.Insert(0, task);
        });

        Interlocked.Increment(ref _activeCount);
        SetIsActive(true);

        return new ActivityHandle(this, task);
    }

    // ── Called by ActivityHandle ───────────────────────────────────────────

    private void OnComplete(BackgroundTask task)
    {
        Dispatch(() => task.Status = TaskStatus.Completed);
        DecrementActive();
    }

    private void OnFail(BackgroundTask task, string? reason)
    {
        Dispatch(() =>
        {
            if (reason is not null)
                task.Description = $"{task.Description}: {reason}";
            task.Status = TaskStatus.Failed;
        });
        DecrementActive();
    }

    private void DecrementActive()
    {
        if (Interlocked.Decrement(ref _activeCount) == 0)
        {
            SetIsActive(false);
            Dispatch(InsertIdlePlaceholder);
        }
    }

    // ── Shell helpers (used by ShellViewModel.AddBackgroundTask) ──────────

    /// <summary>Inserts a task that was created outside the activity system.</summary>
    public void AddTask(BackgroundTask task) => Dispatch(() => Tasks.Insert(0, task));

    // ── Idle placeholder ───────────────────────────────────────────────────

    private void InsertIdlePlaceholder()
    {
        _idlePlaceholder = new BackgroundTask
        {
            Description = "Idle",
            Status      = TaskStatus.Completed
        };
        Tasks.Insert(0, _idlePlaceholder);
    }

    private void RemoveIdlePlaceholder()
    {
        if (_idlePlaceholder is null) return;
        Tasks.Remove(_idlePlaceholder);
        _idlePlaceholder = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetIsActive(bool value)
    {
        if (IsActive == value) return;
        IsActive = value;
        Dispatch(() => IsActiveChanged?.Invoke(this, value));
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    // ── ActivityHandle ─────────────────────────────────────────────────────

    private sealed class ActivityHandle(BackgroundActivityManager manager, BackgroundTask task)
        : IActivityHandle
    {
        public void Complete()              => manager.OnComplete(task);
        public void Fail(string? reason)    => manager.OnFail(task, reason);
    }
}
