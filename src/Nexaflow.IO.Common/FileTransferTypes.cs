namespace Nexaflow.IO.Common;

/// <summary>What a transfer does to its sources.</summary>
public enum TransferKind
{
    /// <summary>Sources are duplicated; nothing at the source is touched.</summary>
    Copy,

    /// <summary>Sources are relocated. A source item is removed only once its own copy is verified —
    /// see <see cref="FileTransferEngine"/> for why that matters.</summary>
    Move,

    /// <summary>Sources are deleted permanently. Recycling needs shell32 and is not modelled here.</summary>
    Delete,
}

/// <summary>How a destination that already exists is resolved.</summary>
public enum ConflictPolicy
{
    /// <summary>Record a failure and leave both sides alone.</summary>
    Fail,

    /// <summary>Leave the existing item and count the source as done.</summary>
    Skip,

    /// <summary>Replace the existing item.</summary>
    Overwrite,

    /// <summary>Pick a free name — "name (2)", "name (3)" …</summary>
    AutoRename,

    /// <summary>Continue an interrupted run into the same destination: a file already there with the
    /// same length is taken as done and skipped, anything else is rewritten. This is what a retry
    /// after freeing disk space uses, so retrying continues rather than producing a second copy.</summary>
    Resume,
}

/// <summary>Where a run has got to.</summary>
public enum TransferPhase
{
    /// <summary>Measuring the sources; byte totals are not known yet.</summary>
    Scanning,

    /// <summary>Moving bytes.</summary>
    Running,

    /// <summary>Parked, waiting on the caller. Nothing has been deleted and nothing is being written.</summary>
    Paused,

    /// <summary>Nothing more will happen.</summary>
    Finished,
}

/// <summary>Why a run parked itself.</summary>
public enum PauseReason
{
    /// <summary>The destination volume cannot hold what is left.</summary>
    OutOfSpace,
}

/// <summary>What the caller decided about a paused run.</summary>
public enum PauseDecision
{
    /// <summary>Try again from the start of the item that could not be written.</summary>
    Retry,

    /// <summary>Give up. The run ends as cancelled.</summary>
    Cancel,
}

/// <summary>One source and the full path it should end up at — a path, never a folder.</summary>
public sealed record TransferItem(string Source, string Destination);

/// <summary>A unit of work handed to <see cref="FileTransferEngine.RunAsync"/>.</summary>
public sealed record FileTransferRequest(
    TransferKind Kind,
    IReadOnlyList<TransferItem> Items,
    ConflictPolicy Conflicts = ConflictPolicy.AutoRename);

/// <summary>What a walk of the sources found. <paramref name="Partial"/> is set when part of the tree
/// could not be read, so the totals are a floor rather than a count.</summary>
public sealed record TransferScan(long TotalBytes, int TotalFiles, int TotalFolders, bool Partial)
{
    /// <summary>Nothing measured — the shape a rename-only move reports.</summary>
    public static readonly TransferScan Empty = new(0, 0, 0, false);
}

/// <summary>A snapshot of a run. Immutable, so a sink can hand one straight to a UI thread.</summary>
public readonly record struct TransferProgress(
    TransferPhase Phase,
    long BytesDone,
    long BytesTotal,
    int ItemsDone,
    int ItemsTotal,
    string? CurrentItem,
    long BytesPerSecond,
    TimeSpan? Remaining,
    PauseReason? Paused);

/// <summary>One thing that went wrong, named precisely. <paramref name="Win32Code"/> is the low word
/// of the underlying HRESULT so a caller can turn it into its own prose — the engine deliberately
/// does not own user-facing wording.</summary>
public sealed record TransferItemFailure(string Path, string Verb, int Win32Code, string Message);

/// <summary>The outcome of a run.</summary>
/// <param name="Completed">False when the run was cancelled. Failures do not make this false — a run
/// that copied 900 of 1,000 files completed, and the 100 are in <paramref name="Failures"/>.</param>
/// <param name="PartialDestinations">Destinations left half-written by a cancellation or a fault the
/// engine could not clean up. Named so a caller can offer to remove them rather than leaving
/// unexplained bytes on the disk.</param>
public sealed record TransferResult(
    bool Completed,
    long BytesTransferred,
    int ItemsTransferred,
    IReadOnlyList<TransferItemFailure> Failures,
    IReadOnlyList<string> SkippedReparsePoints,
    IReadOnlyList<string> RenamedDestinations,
    IReadOnlyList<string> PartialDestinations);

/// <summary>
/// How a run asks the caller what to do when it parks. A null prompt means "nobody is listening" —
/// the run then records a failure instead of waiting forever, which is what a headless caller wants.
/// </summary>
public interface IFileTransferPrompt
{
    /// <summary>Called on a background thread. <paramref name="detail"/> already reads as a sentence.</summary>
    Task<PauseDecision> OnPausedAsync(PauseReason reason, string detail, CancellationToken ct);
}
