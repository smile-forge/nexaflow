using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The feature test suites, as assemblies and as project files.
/// <para>
/// The guards here used to read <c>typeof(…).Assembly</c> and one <c>Nexaflow.Tests.Features.csproj</c>,
/// because there was exactly one of each. Once the suite was split by subject (Viewers / WindowsOS /
/// Architecture / the rest) that reading silently narrowed every guard to whichever assembly happened to
/// hold it — a feature tested only in Viewers would have looked untested. Both accessors here deliberately
/// <em>discover</em> the suites rather than list them, so adding a fifth needs no edit and, more
/// importantly, cannot be forgotten.
/// </para>
/// </summary>
internal static class FeatureTestSuites
{
    /// <summary>Every suite assembly beside this one. They land here because this project references them
    /// (see the csproj) — which is also what puts the feature DLLs in reach of the reflection rules.</summary>
    public static IReadOnlyList<Assembly> Assemblies()
    {
        var found = Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Tests.Features*.dll")
            .Select(TryLoad)
            .OfType<Assembly>()
            .ToList();

        Assert.IsTrue(found.Count > 0,
            $"No Nexaflow.Tests.Features*.dll beside the guards in '{AppContext.BaseDirectory}'. The suites "
            + "reach the guards through this project's ProjectReferences; without them every rule below "
            + "passes over an empty set, which is worse than failing.");
        return found;
    }

    /// <summary>Every <c>Nexaflow.Tests.Features*.csproj</c> under src/Nexaflow.Tests.</summary>
    public static IReadOnlyList<string> ProjectFiles(string repoRoot)
        => [.. Directory.GetFiles(Path.Combine(repoRoot, "src", "Nexaflow.Tests"),
                                  "Nexaflow.Tests.Features*.csproj", SearchOption.AllDirectories)
                        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];

    private static Assembly? TryLoad(string path)
    {
        try { return Assembly.Load(Path.GetFileNameWithoutExtension(path)); }
        catch { return null; }   // a native/unmanaged neighbour, not a suite
    }
}
