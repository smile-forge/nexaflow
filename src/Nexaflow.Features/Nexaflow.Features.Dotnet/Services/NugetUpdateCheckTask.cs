using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dotnet.Models;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Background check for outdated NuGet packages — reported through the shell's background-activity
/// ticker. (This no longer locks the AI input bar: that coupling was removed in the shell.)
/// Results land in <see cref="Updates"/> for the queueing view to read in its completion callback;
/// <see cref="Target"/> lets the caller discard a stale result if the selection has since changed.
/// </summary>
public sealed class NugetUpdateCheckTask(DotnetTarget target, string workingDir) : IBackgroundTask
{
    public DotnetTarget Target => target;

    public IReadOnlyList<NugetUpdateChecker.PackageUpdate> Updates { get; private set; } = [];

    /// <summary>True when the check actually ran (vs. failing because the target wasn't restored).</summary>
    public bool Checked { get; private set; }

    public string Description => $"Checking NuGet updates: {target.DisplayName}";

    public async Task RunAsync(CancellationToken ct)
    {
        var result = await NugetUpdateChecker.CheckAsync(target, workingDir, ct);
        Checked = result.Checked;
        Updates = result.Updates;
    }
}
