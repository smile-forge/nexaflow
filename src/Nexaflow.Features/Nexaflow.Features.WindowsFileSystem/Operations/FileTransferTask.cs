using Nexaflow.Features.Common;

using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Operations;

/// <summary>
/// Runs one <see cref="FileOperation"/> on the shell's background queue. Everything the UI sees is
/// written through <see cref="FileOperationQueue.PublishOnUi"/>, so this never touches an observable
/// property directly and the feature never touches a dispatcher.
/// </summary>
internal sealed class FileTransferTask(
    FileOperation op,
    FileOperationQueue queue,
    string volumeKey) : IBackgroundTask
{
    public string Description => op.Title;

    public async Task RunAsync(CancellationToken ct)
    {
        var gate = queue.GateFor(volumeKey);

        try { await gate.WaitAsync(ct); }
        catch (OperationCanceledException)
        {
            // Cancelled before it ever started. Report it like any other outcome rather than throwing:
            // QueueBackgroundTask swallows a cancellation and skips onComplete, which would leave the
            // row stuck at "Queued" forever.
            queue.PublishOnUi(() => op.Finish(Cancelled()));
            return;
        }

        try
        {
            var sources = op.Request.Items.Select(i => i.Source).ToList();

            queue.PublishOnUi(() => op.SetState(FileOperationState.Scanning));
            var measured = await FileTransferEngine.ScanAsync(sources, ct);

            // A delete keeps the counts but not the byte total: it never moves a byte, so a progress bar
            // measured against the size of what it is removing would sit at nought until the moment it ended.
            TransferScan? scan = op.Kind == TransferKind.Delete
                ? measured with { TotalBytes = 0 }
                : measured;

            if (op.Kind == TransferKind.Delete && op.Recycle)
            {
                await RecycleAsync(sources, scan, ct);
                return;
            }

            queue.PublishOnUi(() => op.SetState(FileOperationState.Running));

            var result = await FileTransferEngine.RunAsync(
                op.Request, scan, new FileOperationProgressSink(op, queue), op, ct);

            queue.PublishOnUi(() => op.Finish(result));
        }
        catch (Exception ex)
        {
            // The engine does not throw; anything arriving here is a genuine bug, and swallowing it
            // would leave a row spinning forever. Report it as the operation's own failure.
            queue.PublishOnUi(() => op.Finish(Faulted(ex)));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The Recycle Bin is shell32's alone, so this one path stays a blocking P/Invoke — but on a
    /// background thread, with a row in the panel, instead of freezing the window as it used to.
    /// </summary>
    private async Task RecycleAsync(IReadOnlyList<string> paths, TransferScan scan, CancellationToken ct)
    {
        queue.PublishOnUi(() =>
        {
            op.SetState(FileOperationState.Running);
            op.Publish(new TransferProgress(TransferPhase.Running, 0, 0, 0, scan.TotalFiles,
                                            null, 0, null, null));
        });

        var failures = new List<TransferItemFailure>();

        await Task.Run(() =>
        {
            try
            {
                if (!NativeMethods.RecycleFiles(paths))
                    failures.Add(new TransferItemFailure(paths[0], "delete", 0,
                        "Windows could not send everything to the Recycle Bin."));
            }
            catch (Exception ex)
            {
                failures.Add(new TransferItemFailure(paths[0], "delete", 0, ex.Message));
            }
        }, ct);

        var result = new TransferResult(
            Completed: !ct.IsCancellationRequested, BytesTransferred: 0,
            ItemsTransferred: failures.Count == 0 ? scan.TotalFiles : 0,
            Failures: failures, SkippedReparsePoints: [], RenamedDestinations: [], PartialDestinations: []);

        queue.PublishOnUi(() => op.Finish(result));
    }

    private static TransferResult Cancelled()
        => new(false, 0, 0, [], [], [], []);

    private static TransferResult Faulted(Exception ex)
        => new(true, 0, 0, [new TransferItemFailure(string.Empty, "run", 0, ex.Message)], [], [], []);
}

/// <summary>
/// Carries engine progress to the UI at a rate a UI can use. The engine already rate-limits, and this
/// gates again at 150 ms because two layers of throttle cost nothing and a 400,000-file copy that
/// posts one continuation per file does not.
/// <para>
/// Deliberately not <see cref="Progress{T}"/>: that captures a synchronisation context and posts
/// every single report to it.
/// </para>
/// </summary>
internal sealed class FileOperationProgressSink(FileOperation op, FileOperationQueue queue)
    : IProgress<TransferProgress>
{
    private static readonly long GateTicks = TimeSpan.FromMilliseconds(150).Ticks;
    private long _lastPublishedTicks;

    public void Report(TransferProgress value)
    {
        long now  = DateTime.UtcNow.Ticks;
        long last = Interlocked.Read(ref _lastPublishedTicks);

        bool always = value.Phase is TransferPhase.Paused or TransferPhase.Finished;
        if (!always && now - last < GateTicks) return;

        Interlocked.Exchange(ref _lastPublishedTicks, now);
        queue.PublishOnUi(() => op.Publish(value));
    }
}
