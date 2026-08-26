using System.IO;
using Nexaflow.Features.Common.Dependencies;

namespace Nexaflow.Features.Dotnet.Dependencies;

/// <summary>
/// The .NET viewlet shells out to the <c>dotnet</c> CLI to build, test and list packages. Optional: the
/// rest of Nexaflow is unaffected, and the viewlet only appears for a folder that looks like a .NET
/// project — but when it is absent every command in it fails, so it is worth naming on the About page.
/// </summary>
public sealed class DotnetCliDependency : IExternalDependency
{
    public const string DependencyId = "dotnet-cli";

    public string Id          => DependencyId;
    public string DisplayName => ".NET SDK (dotnet CLI)";

    public string Description =>
        "Runs build, test and package commands for the .NET folder viewlet. Only that viewlet needs it.";

    public ExternalDependencyKind Kind => ExternalDependencyKind.Optional;

    public string? InstallUrl => "https://dotnet.microsoft.com/download";

    public ExternalDependencyStatus Probe()
        => ResolveOnPath("dotnet.exe") is { } path
            ? new ExternalDependencyStatus(ExternalDependencyState.Present, null, path)
            : new ExternalDependencyStatus(ExternalDependencyState.Missing, null,
                "No dotnet.exe on PATH.");

    /// <summary>
    /// First match for <paramref name="exeName"/> on PATH, or null. Deliberately a PATH walk rather than
    /// launching the tool with <c>--version</c>: probes run for every declared component whenever the About
    /// page is opened, and spawning processes to answer "is it installed" is both slower and noisier.
    /// </summary>
    private static string? ResolveOnPath(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim(' ', '"'), exeName); }
            catch { continue; }   // a malformed PATH entry is not worth failing the whole probe over

            try { if (File.Exists(candidate)) return candidate; }
            catch { /* unreadable directory — keep looking */ }
        }
        return null;
    }
}
