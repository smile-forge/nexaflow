using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Visuals.Common.Localization;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The jobs a window's workspace has running in the background, as its AI bar shows them: a pill that counts them, opening a
/// list of each — what it is doing and for how long — with Stop. Drawn from the app's background tasks: those the workspace's
/// assistant set going that are still running. The list's clocks tick while there is something on it. It listens to the
/// app-wide list weakly, so a window that closes leaves nothing behind.
/// </summary>
public sealed partial class WorkspaceBackgroundTasks : ObservableObject
{
    private readonly BackgroundActivityManager _tasks;
    private readonly Func<WorkspaceRuntime?> _workspace;
    private readonly DispatcherTimer _clock;

    /// <param name="tasks">The app's background tasks.</param>
    /// <param name="workspace">The window's workspace now.</param>
    public WorkspaceBackgroundTasks(BackgroundActivityManager tasks, Func<WorkspaceRuntime?> workspace)
    {
        _tasks     = tasks;
        _workspace = workspace;
        _clock     = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Tick();

        CollectionChangedEventManager.AddHandler(tasks.Tasks, OnTasksChanged);
        foreach (var task in tasks.Tasks) Watch(task);
        Refresh();
    }

    /// <summary>The workspace's jobs still running, newest first.</summary>
    public ObservableCollection<BackgroundTask> Running { get; } = [];

    /// <summary>Whether the list is open.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>Whether the workspace has any job running: the pill shows only then.</summary>
    public bool Any => Running.Count > 0;

    /// <summary>What the pill says.</summary>
    public string Label => Running.Count == 1
        ? Str.Get("Shell.BackgroundTasks.CountOne")
        : Str.Format("Shell.BackgroundTasks.CountFormat", Running.Count);

    /// <summary>Takes the list again: a task started, ended, or the window's workspace changed.</summary>
    public void Refresh()
    {
        var workspace = _workspace();
        var had       = Any;
        Running.Clear();
        if (workspace is not null)
            foreach (var task in _tasks.Tasks)
                if (ReferenceEquals(task.Workspace, workspace) && task.Status == TaskStatus.Running)
                    Running.Add(task);

        if (Running.Count == 0) IsOpen = false;
        Tick();
        _clock.IsEnabled = Running.Count > 0;
        if (Any != had) OnPropertyChanged(nameof(Any));
        OnPropertyChanged(nameof(Label));
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen && Any;

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var task in Running) task.Tick(now);
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { } added)
            foreach (BackgroundTask task in added) Watch(task);
        Refresh();
    }

    /// <summary>Follows a job's status, so the list drops it when it ends. Work the app does of its own accord is never listed.</summary>
    private void Watch(BackgroundTask task)
    {
        if (task.Workspace is not null)
            PropertyChangedEventManager.AddHandler(task, OnStatusChanged, nameof(BackgroundTask.Status));
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
}
