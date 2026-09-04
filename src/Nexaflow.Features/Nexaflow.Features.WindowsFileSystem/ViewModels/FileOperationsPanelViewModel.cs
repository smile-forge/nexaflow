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
    private bool? _pendingTarget;

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

    /// <summary>
    /// Starts watching the queue. Called by the view on load — the queue outlives the tab, so a permanent
    /// subscription would retain this view-model.
    /// <para>
    /// Work that is already in flight is shown at once rather than after another countdown. The debounce
    /// exists so that work too brief to care about never flashes the panel open; a copy still running
    /// when this tab arrives has already outlived that concern, and making someone wait a further 600 ms
    /// to find out what their machine is busy with is the opposite of the point. It is why a tab opened
    /// or switched to mid-copy could look as though it had no panel at all.
    /// </para>
    /// </summary>
    public void Attach()
    {
        _queue.Changed += OnQueueChanged;

        Summary = DescribeQueue();

        if (_queue.IsBusy)
        {
            CancelPending();
            IsVisible = true;
            return;
        }

        OnQueueChanged();
    }

    /// <summary>Stops watching, and abandons any pending show or hide.</summary>
    public void Detach()
    {
        _queue.Changed -= OnQueueChanged;
        CancelPending();
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

        // Already where we want to be — and abandon any countdown to the opposite.
        if (busy == IsVisible) { CancelPending(); return; }

        ScheduleVisibility(busy, busy ? ExpandDelayMs : CollapseDelayMs);
    }

    /// <summary>
    /// Applies <paramref name="visible"/> after <paramref name="delayMs"/>, unless the queue changes its
    /// mind first.
    /// <para>
    /// A countdown that is already heading for this state is left alone. That is the whole subtlety: the
    /// queue reports progress several times a second, and restarting the countdown on each report means
    /// it never elapses — the panel would only ever appear for an operation that had gone quiet, which is
    /// the opposite of the one worth showing.
    /// </para>
    /// </summary>
    private void ScheduleVisibility(bool visible, int delayMs)
    {
        if (_pendingTarget == visible) return;

        CancelPending();
        var cts = new CancellationTokenSource();
        _pending       = cts;
        _pendingTarget = visible;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delayMs, cts.Token); }
            catch (OperationCanceledException) { return; }

            await _shell.RunOnUiAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                _pendingTarget = null;

                // Re-read rather than trusting the decision made when the countdown started.
                IsVisible = _queue.IsBusy;
            });
        });
    }

    private void CancelPending()
    {
        _pending?.Cancel();
        _pending       = null;
        _pendingTarget = null;
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
