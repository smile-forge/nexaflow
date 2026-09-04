using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Operations;

/// <summary>Where one operation has got to, as the panel shows it.</summary>
public enum FileOperationState
{
    /// <summary>Waiting for another operation on the same disk to finish.</summary>
    Queued,

    /// <summary>Measuring, so the bar is still indeterminate.</summary>
    Scanning,

    /// <summary>Moving bytes.</summary>
    Running,

    /// <summary>Parked and waiting on the user — the disk filled up.</summary>
    Paused,

    /// <summary>Cancelled, but the current file has not finished unwinding yet.</summary>
    Cancelling,

    /// <summary>Everything landed.</summary>
    Completed,

    /// <summary>Finished, but something did not land. The row stays until it is dismissed.</summary>
    Failed,

    /// <summary>Stopped on request.</summary>
    Cancelled,
}

/// <summary>
/// One copy, move or delete, as the UI sees it. Everything observable here is written on the UI
/// thread only — <see cref="Publish"/> is the single door in, and the queue's progress sink is what
/// marshals through it. The background run touches nothing on this object directly.
/// </summary>
public sealed partial class FileOperation : ObservableObject, IFileTransferPrompt
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<PauseDecision>? _pauseAnswer;

    internal FileOperation(TransferKind kind, FileTransferRequest request, string targetLabel, bool recycle)
    {
        Kind        = kind;
        Request     = request;
        TargetLabel = targetLabel;
        Recycle     = recycle;
        ItemsTotal  = request.Items.Count;

        Verb = kind switch
        {
            TransferKind.Move   => "Moving",
            TransferKind.Delete => recycle ? "Recycling" : "Deleting",
            _                   => "Copying",
        };

        SourceSummary = request.Items.Count == 1
            ? Path.GetFileName(request.Items[0].Source.TrimEnd(Path.DirectorySeparatorChar))
            : $"{request.Items.Count} items";
    }

    public Guid Id { get; } = Guid.NewGuid();
    public TransferKind Kind { get; }
    public string Verb { get; }
    public string SourceSummary { get; }

    /// <summary>The destination folder's name, or empty for a delete.</summary>
    public string TargetLabel { get; }

    /// <summary>A delete that goes to the Recycle Bin rather than being permanent.</summary>
    internal bool Recycle { get; }

    internal FileTransferRequest Request { get; }
    internal CancellationToken Token => _cts.Token;

    /// <summary>How this object gets onto the UI thread. Set by the queue, which owns the marshal —
    /// a feature never reaches for a dispatcher of its own.</summary>
    internal Action<Action>? Marshal { get; set; }

    /// <summary>Completes when the run has finished, whatever the outcome. Tests await this instead
    /// of sleeping, and nothing else needs it.</summary>
    public Task Completion => _completion.Task;

    /// <summary>What the row says: "Moving 3 items to Archive".</summary>
    public string Title => string.IsNullOrEmpty(TargetLabel)
        ? $"{Verb} {SourceSummary}"
        : $"{Verb} {SourceSummary} to {TargetLabel}";

    [ObservableProperty] private FileOperationState _state = FileOperationState.Queued;

    /// <summary>0–1, or -1 while there is no total to measure against.</summary>
    [ObservableProperty] private double _fraction = -1;

    [ObservableProperty] private long   _bytesDone;
    [ObservableProperty] private long   _bytesTotal;
    [ObservableProperty] private int    _itemsDone;
    [ObservableProperty] private int    _itemsTotal;
    [ObservableProperty] private string _currentItem     = string.Empty;
    [ObservableProperty] private string _detail          = string.Empty;
    [ObservableProperty] private string _pauseDetail     = string.Empty;
    [ObservableProperty] private bool   _hasProblems;

    /// <summary>Every failure, in the engine's own words. The row shows these behind an expander;
    /// the shell notification shows the first few.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>Destinations left half-written, so the panel can offer to clear them up rather than
    /// leaving unexplained bytes on the disk.</summary>
    public IReadOnlyList<string> PartialDestinations { get; private set; } = [];

    public bool IsFinished => State is FileOperationState.Completed
                                    or FileOperationState.Failed
                                    or FileOperationState.Cancelled;

    // ── Driving it ────────────────────────────────────────────────────────────

    /// <summary>Stops the run. Safe from any thread and safe to call twice.</summary>
    public void Cancel()
    {
        _pauseAnswer?.TrySetResult(PauseDecision.Cancel);
        if (!IsFinished) State = FileOperationState.Cancelling;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Answers a pause: carry on. Does nothing unless the run is actually parked.</summary>
    public void Retry() => _pauseAnswer?.TrySetResult(PauseDecision.Retry);

    /// <summary>
    /// The engine parked. Publishing the reason and waiting for an answer is what turns "half the
    /// folder arrived and the rest vanished" into a row with a Retry button — and nothing has been
    /// deleted while this is pending.
    /// </summary>
    Task<PauseDecision> IFileTransferPrompt.OnPausedAsync(PauseReason reason, string detail, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<PauseDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pauseAnswer = answer;
        Marshal?.Invoke(() => PublishPause(detail));
        ct.Register(() => answer.TrySetResult(PauseDecision.Cancel));
        return answer.Task;
    }

    // ── State, written on the UI thread only ──────────────────────────────────

    internal void Publish(TransferProgress p)
    {
        State = p.Phase switch
        {
            TransferPhase.Scanning => FileOperationState.Scanning,
            TransferPhase.Paused   => FileOperationState.Paused,
            _ when State is FileOperationState.Cancelling => FileOperationState.Cancelling,
            _                      => FileOperationState.Running,
        };

        BytesDone   = p.BytesDone;
        BytesTotal  = p.BytesTotal;
        ItemsDone   = p.ItemsDone;
        ItemsTotal  = p.ItemsTotal;
        CurrentItem = p.CurrentItem ?? string.Empty;
        Fraction    = p.BytesTotal > 0 ? Math.Clamp((double)p.BytesDone / p.BytesTotal, 0, 1) : -1;
        Detail      = DescribeProgress(p);
    }

    internal void SetState(FileOperationState state) => State = state;

    internal void PublishPause(string detail)
    {
        PauseDetail = detail;
        State       = FileOperationState.Paused;
    }

    internal void Finish(TransferResult result)
    {
        Problems = [.. result.Failures.Select(f => f.Message)];
        PartialDestinations = result.PartialDestinations;
        HasProblems = Problems.Count > 0;

        State = !result.Completed ? FileOperationState.Cancelled
              : HasProblems       ? FileOperationState.Failed
                                  : FileOperationState.Completed;

        Detail = State switch
        {
            FileOperationState.Cancelled => "Stopped.",
            FileOperationState.Failed    => Problems.Count == 1 ? "1 problem" : $"{Problems.Count} problems",
            _                            => "Done.",
        };

        if (State is FileOperationState.Completed) Fraction = 1;
        PauseDetail = string.Empty;
        _completion.TrySetResult();
    }

    /// <summary>Reports a refusal that never reached the engine (a plan with nothing left to do).</summary>
    internal void FinishRefused(IReadOnlyList<string> refusals)
    {
        Problems    = refusals;
        HasProblems = refusals.Count > 0;
        State       = HasProblems ? FileOperationState.Failed : FileOperationState.Completed;
        Detail      = HasProblems ? refusals[0] : "Nothing to do.";
        _completion.TrySetResult();
    }

    private static string DescribeProgress(TransferProgress p)
    {
        if (p.Phase == TransferPhase.Scanning) return "Working out how much there is…";
        if (p.BytesTotal <= 0) return p.ItemsTotal > 0 ? $"{p.ItemsDone} of {p.ItemsTotal}" : string.Empty;

        var text = $"{SizeFormatter.FormatBytes(p.BytesDone)} of {SizeFormatter.FormatBytes(p.BytesTotal)}";
        if (p.BytesPerSecond > 0) text += $" · {SizeFormatter.FormatBytes(p.BytesPerSecond)}/s";
        if (p.Remaining is { } left && left > TimeSpan.FromSeconds(2))
            text += $" · {DurationFormatter.FormatEta(left)} left";
        return text;
    }
}
