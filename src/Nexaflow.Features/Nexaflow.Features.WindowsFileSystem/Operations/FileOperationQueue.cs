using Nexaflow.Features.Common;

using Nexaflow.IO.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Operations;

/// <summary>
/// Every copy, move and delete the file browser starts, and the only thing that starts one.
/// <para>
/// The enqueue is synchronous and does no IO: it plans destinations, makes a
/// <see cref="FileOperation"/>, and hands the work to the shell's background queue. That is the
/// whole fix for a drop that used to run its copy inside the OLE <c>IDropTarget::Drop</c> callback —
/// a 200 GB folder froze the app, and the window having no message pump meant Windows could not
/// deliver the next drop at all. Dropping three folders now enqueues three operations.
/// </para>
/// <para>
/// One instance per <see cref="IShellServices"/>, i.e. per workspace runtime, so an operation
/// outlives the tab that started it and is visible from every file-browser tab in that runtime.
/// (A workspace rebuild makes a new shell and orphans this entry, exactly as it does for
/// <see cref="Services.FileSystemFeatureRegistry"/>; anything already running finishes unwatched
/// rather than being cancelled, because a copy should survive a tab closing.)
/// </para>
/// </summary>
public sealed class FileOperationQueue : IFileOperationHost
{
    private static readonly ConditionalWeakTable<IShellServices, FileOperationQueue> _instances = new();
    private static readonly object _instancesLock = new();

    /// <summary>The queue for <paramref name="shell"/>, made on first ask.</summary>
    public static FileOperationQueue For(IShellServices shell)
    {
        lock (_instancesLock)
        {
            if (!_instances.TryGetValue(shell, out var q))
                _instances.Add(shell, q = new FileOperationQueue(shell));
            return q;
        }
    }

    private readonly IShellServices _shell;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _volumeGates = new(StringComparer.OrdinalIgnoreCase);

    private FileOperationQueue(IShellServices shell) => _shell = shell;

    /// <summary>Live and recently-finished operations, newest last. Only ever touched on the UI thread.</summary>
    public ObservableCollection<FileOperation> Operations { get; } = [];

    /// <summary>Raised on the UI thread whenever anything about the list or a row changes.</summary>
    public event Action? Changed;

    /// <summary>True while anything is queued, scanning, running, paused or unwinding.</summary>
    public bool IsBusy => Operations.Any(op => !op.IsFinished);

    // ── Starting work ─────────────────────────────────────────────────────────

    /// <summary>Files dropped onto <paramref name="destinationFolder"/> from outside the app.</summary>
    public FileOperation? EnqueueDrop(IReadOnlyList<string> sources, string destinationFolder, bool move)
        => EnqueueTransfer(sources, destinationFolder, move);

    /// <summary>A clipboard paste. <paramref name="onCutSucceeded"/> runs on the UI thread once a cut
    /// has actually landed, so the clipboard is only consumed when there was something to consume.</summary>
    public FileOperation? EnqueuePaste(IReadOnlyList<string> sources, string destinationFolder, bool isCut,
                                       Action? onCutSucceeded = null)
        => EnqueueTransfer(sources, destinationFolder, isCut, isCut ? onCutSucceeded : null);

    private FileOperation? EnqueueTransfer(IReadOnlyList<string> sources, string destinationFolder, bool move,
                                           Action? onSucceeded = null)
    {
        if (sources.Count == 0 || string.IsNullOrEmpty(destinationFolder)) return null;

        destinationFolder = Services.ShellPath.RealForMutation(destinationFolder);
        var items = FileOperationDestinations.Plan(sources, destinationFolder, move, out var refusals);

        if (items.Count == 0)
        {
            if (refusals.Count > 0) _shell.ShowError(string.Join(Environment.NewLine, refusals));
            return null;
        }

        var request = new FileTransferRequest(
            move ? TransferKind.Move : TransferKind.Copy, items, ConflictPolicy.AutoRename);

        var op = new FileOperation(move ? "Moving" : "Copying", Describe(items),
                                   LabelFor(destinationFolder), items.Count);
        if (refusals.Count > 0) _shell.ShowError(string.Join(Environment.NewLine, refusals));

        Start(op, new FileTransferTask(op, this, request, recycle: false, VolumeKey(destinationFolder)),
              onSucceeded);
        return op;
    }

    /// <summary>A delete. <paramref name="permanent"/> false goes to the Recycle Bin, which only
    /// shell32 can do — that path still runs here so it stops blocking the UI thread and shows up in
    /// the panel like everything else.</summary>
    public FileOperation? EnqueueDelete(IReadOnlyList<string> paths, bool permanent)
    {
        var real = Services.ShellPath.RealForMutation(paths);
        if (real.Length == 0) return null;

        var request = new FileTransferRequest(
            TransferKind.Delete, [.. real.Select(p => new TransferItem(p, p))], ConflictPolicy.Fail);

        // A delete says nothing when it works: the files being gone is its own confirmation.
        var op = new FileOperation(permanent ? "Deleting" : "Recycling", Describe(request.Items),
                                   targetLabel: string.Empty, request.Items.Count, announceOnSuccess: false);
        Start(op, new FileTransferTask(op, this, request, recycle: !permanent, VolumeKey(real[0])),
              onSucceeded: null);
        return op;
    }

    private void Start(FileOperation op, IBackgroundTask task, Action? onSucceeded)
    {
        op.Marshal = PublishOnUi;
        Operations.Add(op);
        Changed?.Invoke();

        _shell.QueueBackgroundTask(
            task,
            onComplete: _ =>
            {
                if (op.State is FileOperationState.Completed) onSucceeded?.Invoke();
                Announce(op);
                Retire(op);
            },
            ct: op.Token);
    }

    /// <summary>
    /// Shows and runs work the queue did not plan itself — an archive being built or unpacked. The row,
    /// the cancel button, the volume gate, the completion notice and the auto-retire are all the ones a
    /// copy already gets; only the work differs, so only the work is passed in.
    /// </summary>
    public Task Run(FileOperationRequest request,
                    Func<IProgress<TransferProgress>, CancellationToken, Task<TransferResult>> work)
    {
        var op = new FileOperation(request.Verb, request.Subject, request.TargetLabel);
        Start(op, new FileWorkTask(op, this, VolumeKey(request.DestinationPath), work), onSucceeded: null);
        return op.Completion;
    }

    /// <summary>What the row calls the thing being worked on.</summary>
    private static string Describe(IReadOnlyList<TransferItem> items)
        => items.Count == 1
            ? Path.GetFileName(items[0].Source.TrimEnd(Path.DirectorySeparatorChar))
            : $"{items.Count} items";

    // ── Controls ──────────────────────────────────────────────────────────────

    /// <summary>Stops everything still in flight.</summary>
    public void CancelAll()
    {
        foreach (var op in Operations.Where(o => !o.IsFinished).ToList()) op.Cancel();
        Changed?.Invoke();
    }

    /// <summary>Clears finished rows the user has read.</summary>
    public void DismissFinished()
    {
        foreach (var op in Operations.Where(o => o.IsFinished).ToList()) Operations.Remove(op);
        Changed?.Invoke();
    }

    /// <summary>Removes one row.</summary>
    public void Dismiss(FileOperation op)
    {
        Operations.Remove(op);
        Changed?.Invoke();
    }

    // ── Internals the task and the sink use ───────────────────────────────────

    /// <summary>Publishes a change from a background thread. The marshal is here so no caller has to
    /// think about it, and so <see cref="Changed"/> is only ever raised on the UI thread.</summary>
    internal void PublishOnUi(Action apply)
        => _ = _shell.RunOnUiAsync(() => { apply(); Changed?.Invoke(); });

    /// <summary>
    /// One gate per destination volume. Two large copies onto the same disk interleave into a slower
    /// pair rather than a faster one, so they queue; copies to different disks run together. A waiting
    /// operation is visible as <see cref="FileOperationState.Queued"/>, which is what makes "all three
    /// folders are going to arrive" something the user can see rather than take on trust.
    /// </summary>
    internal SemaphoreSlim GateFor(string volumeKey)
        => _volumeGates.GetOrAdd(volumeKey, _ => new SemaphoreSlim(1, 1));

    private static string VolumeKey(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? path; }
        catch { return path; }
    }

    private static string LabelFor(string folder)
    {
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name    = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>One notification per operation, as it finishes. The old code collected every failure
    /// and reported once at the very end — after hours, or never if the app was closed first.</summary>
    private void Announce(FileOperation op)
    {
        if (op.State is FileOperationState.Cancelled) return;

        if (op.Problems.Count == 0)
        {
            if (op.AnnounceOnSuccess) _shell.ShowNotification($"{op.Title} — done.");
            _shell.RequestRefresh();
            return;
        }

        // A 40,000-file copy that goes wrong must not produce 40,000 messages; the row keeps the rest.
        var shown = op.Problems.Take(5).ToList();
        if (op.Problems.Count > shown.Count) shown.Add($"…and {op.Problems.Count - shown.Count} more.");
        _shell.ShowError(string.Join(Environment.NewLine, shown));
        _shell.RequestRefresh();
    }

    /// <summary>A clean run clears itself out of the way; one with something to say stays until it is
    /// dismissed.</summary>
    private void Retire(FileOperation op)
    {
        if (op.State is not FileOperationState.Completed) return;

        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            PublishOnUi(() => Operations.Remove(op)), TaskScheduler.Default);
    }
}
