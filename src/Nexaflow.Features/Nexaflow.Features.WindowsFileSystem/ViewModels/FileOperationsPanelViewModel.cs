using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Operations;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>
/// The operations panel above the folder tree: what is being copied, moved or deleted, how far it has
/// got, and the buttons that do something about it.
/// <para>
/// It appears only when there is something worth interrupting the layout for. A drop that finishes in
/// 80 ms should never make the tree jump, so the panel waits <see cref="ExpandDelayMs"/> and opens
/// only if the work is still going; when the last operation clears it waits again before collapsing,
/// so a run of small operations does not flicker the panel open and shut.
/// </para>
/// <para>
/// The wait is <see cref="Task.Delay(int, CancellationToken)"/> plus
/// <see cref="IShellServices.RunOnUiAsync(Action)"/>, not a <c>DispatcherTimer</c>: a feature does not
/// own a dispatcher, and this way the view-model is testable off a UI thread.
/// </para>
/// </summary>
public sealed partial class FileOperationsPanelViewModel : ObservableObject
{
    /// <summary>How long work must still be running before the panel is worth showing.</summary>
    internal const int ExpandDelayMs = 600;

    /// <summary>How long the panel lingers after the last operation clears.</summary>
    internal const int CollapseDelayMs = 1500;

    private readonly FileOperationQueue _queue;
    private readonly IShellServices _shell;
    private CancellationTokenSource? _pending;

    public FileOperationsPanelViewModel(FileOperationQueue queue, IShellServices shell)
    {
        _queue = queue;
        _shell = shell;
    }

    /// <summary>The live rows. The same collection every tab in this workspace runtime is showing.</summary>
    public ObservableCollection<FileOperation> Operations => _queue.Operations;

    /// <summary>Whether the panel is on screen. The view animates its height off this.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>Collapsed to a single summary line by the header chevron.</summary>
    [ObservableProperty] private bool _isCollapsed;

    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>Starts watching the queue. Called by the view on load — the queue outlives the tab, so
    /// a permanent subscription would retain this view-model.</summary>
    public void Attach()
    {
        _queue.Changed += OnQueueChanged;
        OnQueueChanged();
    }

    /// <summary>Stops watching, and abandons any pending show or hide.</summary>
    public void Detach()
    {
        _queue.Changed -= OnQueueChanged;
        _pending?.Cancel();
        _pending = null;
    }

    [RelayCommand] private void CancelAll() => _queue.CancelAll();
    [RelayCommand] private void DismissFinished() => _queue.DismissFinished();
    [RelayCommand] private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    [RelayCommand] private static void Cancel(FileOperation? op) => op?.Cancel();
    [RelayCommand] private static void Retry(FileOperation? op) => op?.Retry();
    [RelayCommand] private void Dismiss(FileOperation? op) { if (op is not null) _queue.Dismiss(op); }

    /// <summary>Raised on the UI thread by the queue, so everything here is already on it.</summary>
    private void OnQueueChanged()
    {
        Summary = DescribeQueue();

        bool busy = _queue.IsBusy;

        // Anything still in flight is worth showing at once if the panel is already open; the delay
        // only exists to stop a fast operation opening it at all.
        if (busy && IsVisible) { _pending?.Cancel(); _pending = null; return; }
        if (!busy && !IsVisible) { _pending?.Cancel(); _pending = null; return; }

        ScheduleVisibility(busy, busy ? ExpandDelayMs : CollapseDelayMs);
    }

    /// <summary>Applies <paramref name="visible"/> after <paramref name="delayMs"/>, unless the queue
    /// changes its mind first — a new schedule cancels the one before it.</summary>
    private void ScheduleVisibility(bool visible, int delayMs)
    {
        _pending?.Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delayMs, cts.Token); }
            catch (OperationCanceledException) { return; }

            await _shell.RunOnUiAsync(() =>
            {
                // Re-read rather than trusting the decision made 600 ms ago.
                if (!cts.IsCancellationRequested) IsVisible = _queue.IsBusy;
            });
        });
    }

    private string DescribeQueue()
    {
        int running = 0, waiting = 0, problems = 0;

        foreach (var op in _queue.Operations)
        {
            if (op.State is FileOperationState.Queued) waiting++;
            else if (!op.IsFinished) running++;
            if (op.HasProblems) problems++;
        }

        if (running == 0 && waiting == 0)
            return problems > 0 ? $"{problems} finished with problems" : "Nothing in progress";

        var text = running == 1 ? "1 operation" : $"{running} operations";
        if (waiting > 0) text += $", {waiting} waiting";
        return text;
    }
}
