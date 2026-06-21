using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// Takes a single process snapshot off the UI thread via the shell's background queue, so the page can
/// become AI-context-ready even when it is only pinned as a context item and never shown as a tab (the
/// live 1s polling loop is gated on the view loading, which never happens for a pinned chip). The snapshot
/// is read from <see cref="Result"/> in the completion callback.
/// </summary>
public sealed class LoadProcessSnapshotTask(IProcessSource source) : IBackgroundTask
{
    public string Description => "Gathering process list";

    public ProcessSnapshot? Result { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        Result = source.Snapshot();
    }, ct);
}
