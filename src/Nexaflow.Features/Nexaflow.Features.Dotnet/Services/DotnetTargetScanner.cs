using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Dotnet.Models;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Enumerates the buildable .NET targets directly inside a folder. Solutions are listed
/// first so callers can default to "solution preferred".
/// </summary>
public static class DotnetTargetScanner
{
    private static readonly string[] SolutionGlobs = ["*.slnx", "*.sln"];
    private static readonly string[] ProjectGlobs  = ["*.csproj", "*.fsproj", "*.vbproj"];

    public static IReadOnlyList<DotnetTarget> Scan(string folderPath)
    {
        var result = new List<DotnetTarget>();
        try
        {
            foreach (var path in Enumerate(folderPath, SolutionGlobs))
                result.Add(new DotnetTarget(Path.GetFileName(path), path, IsSolution: true));

            foreach (var path in Enumerate(folderPath, ProjectGlobs))
                result.Add(new DotnetTarget(Path.GetFileName(path), path, IsSolution: false));
        }
        catch
        {
            // Unreadable folder — treat as no targets.
        }
        return result;
    }

    private static IEnumerable<string> Enumerate(string folderPath, string[] globs)
        => globs.SelectMany(g => Directory.EnumerateFiles(folderPath, g, SearchOption.TopDirectoryOnly))
                .OrderBy(p => p);
}
