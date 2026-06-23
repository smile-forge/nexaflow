using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Text.Services;

/// <summary>
/// Runs a streaming <see cref="FileSplitter"/> split off the UI thread via the shell's background-activity
/// area. The result is captured on <see cref="Result"/> so the queueing code can report it once the task
/// completes (the shell's <c>onComplete</c> callback only carries success/failure).
/// </summary>
internal sealed class SplitFileBackgroundTask(string sourcePath, SplitOptions options) : IBackgroundTask
{
    public string Description => $"Splitting {Path.GetFileName(sourcePath)}";

    public SplitResult? Result { get; private set; }

    public async Task RunAsync(CancellationToken ct)
        => Result = await FileSplitter.SplitAsync(sourcePath, options, ct);
}
