using Nexaflow.Core.Models;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;
using Nexaflow.Visuals.Common.Localization;

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

    /// <summary>The app UI dispatcher, captured at construction (built on the UI thread in App.InitializeApp).</summary>
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

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
        // Work done for a job is told on the job's row, not a row of its own.
        if (s_job.Value is { } job)
        {
            Dispatch(() => job.Detail = description);
            return new JobDetail(this, job);
        }

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

    // ── Jobs ────────────────────────────────────────────────────────────────

    /// <summary>The job the work in this flow is done for, if any.</summary>
    private static readonly AsyncLocal<BackgroundTask?> s_job = new();

    /// <summary>
    /// Starts a job the assistant of <paramref name="workspace"/> set going: a task of its own, shown with that workspace's, that
    /// <paramref name="stopping"/> stops.
    /// </summary>
    public BackgroundTask StartJob(string description, WorkspaceRuntime workspace, CancellationTokenSource stopping)
    {
        var job = new BackgroundTask { Description = description, Workspace = workspace, Stopping = stopping };
        Dispatch(() =>
        {
            RemoveIdlePlaceholder();
            Tasks.Insert(0, job);
        });

        Interlocked.Increment(ref _activeCount);
        SetIsActive(true);
        return job;
    }

    /// <summary>Has every activity started from here on, in the calling flow and what it awaits, told as <paramref name="job"/>'s
    /// detail rather than as a task of its own.</summary>
    public static void Within(BackgroundTask job) => s_job.Value = job;

    /// <summary>Ends a job as <paramref name="status"/>, saying how in <paramref name="detail"/>.</summary>
    public void EndJob(BackgroundTask job, TaskStatus status, string? detail)
    {
        Dispatch(() =>
        {
            job.Detail = detail;
            job.Status = status;
        });
        DecrementActive();
    }

    /// <summary>Stops every job the assistant of <paramref name="workspace"/> has running: the models they run on go with it.</summary>
    public void StopJobsOf(WorkspaceRuntime workspace) => Dispatch(() =>
    {
        foreach (var task in Tasks.ToList())
            if (ReferenceEquals(task.Workspace, workspace) && task.CanStop)
                task.Stopping!.Cancel();
    });

    /// <summary>Inserts a task that was created outside the activity system.</summary>
    public void AddTask(BackgroundTask task) => Dispatch(() => Tasks.Insert(0, task));

    // ── Idle placeholder ───────────────────────────────────────────────────

    private void InsertIdlePlaceholder()
    {
        _idlePlaceholder = new BackgroundTask
        {
            Description = Str.Get("Shell.BackgroundTasks.Idle"),
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

    private void Dispatch(Action action)
    {
        if (_ui.CheckAccess())
            action();
        else
            _ui.Invoke(action);
    }

    // ── ActivityHandle ─────────────────────────────────────────────────────

    private sealed class ActivityHandle(BackgroundActivityManager manager, BackgroundTask task)
        : IActivityHandle
    {
        public void Complete()                   => manager.OnComplete(task);
        public void Fail(string? reason)         => manager.OnFail(task, reason);
        public void Update(string description)   => manager.Dispatch(() => task.Description = description);
    }

    /// <summary>An activity started for a job: what it says is the job's detail, and its end is the job's to tell.</summary>
    private sealed class JobDetail(BackgroundActivityManager manager, BackgroundTask job) : IActivityHandle
    {
        public void Complete() { }
        public void Fail(string? reason)       { if (reason is not null) manager.Dispatch(() => job.Detail = reason); }
        public void Update(string description) => manager.Dispatch(() => job.Detail = description);
    }
}
