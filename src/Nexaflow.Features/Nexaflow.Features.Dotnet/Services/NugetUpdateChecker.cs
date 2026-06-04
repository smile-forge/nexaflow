using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Dotnet.Models;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Checks a target for outdated NuGet packages via <c>dotnet list … package --outdated</c>.
///
/// Deliberately NOT an <c>IBackgroundTask</c>: the shell's background-activity manager drives the
/// AI input bar's busy/read-only state, and this passive check runs on navigation and on every
/// target switch — it must never seize that flag. The viewlet runs it off the UI thread itself.
///
/// Best-effort: if the target hasn't been restored (no assets file) the command fails and an empty
/// list is returned — no caution is shown.
/// </summary>
public static class NugetUpdateChecker
{
    public sealed record PackageUpdate(string Name, string Current, string Latest);

    public static async Task<IReadOnlyList<PackageUpdate>> CheckAsync(
        DotnetTarget target, string workingDir, CancellationToken ct)
    {
        var result = await DotnetCli.RunListOutdatedAsync(target, workingDir, ct);
        if (!result.Succeeded)
            return [];

        var found = new List<PackageUpdate>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in result.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("> "))
                continue;

            // "> Name   Requested   Resolved   Latest"
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                continue;

            var name    = parts[1];
            var current = parts[3]; // Resolved
            var latest  = parts[4]; // Latest

            if (seen.Add(name))
                found.Add(new PackageUpdate(name, current, latest));
        }

        return found;
    }
}
