using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The test suites these guards hold, as assemblies and as project files.
/// <para>
/// The guards here used to read <c>typeof(…).Assembly</c> and one <c>Nexaflow.Tests.Features.csproj</c>,
/// because there was exactly one of each. Once the suite was split by subject (Viewers / WindowsOS /
/// Architecture / the rest) that reading silently narrowed every guard to whichever assembly happened to
/// hold it — a feature tested only in Viewers would have looked untested. Both accessors here deliberately
/// <em>discover</em> the suites rather than list them, so adding another needs no edit and, more
/// importantly, cannot be forgotten.
/// </para>
/// <para>
/// <see cref="Patterns"/> is the one thing that is spelled out, because a suite's name is the only thing
/// that says it belongs. <c>Nexaflow.Tests.Initiatives</c> is here for the same reason the coverage rule
/// exists at all: when its 265 tests moved out of <c>Tests.Features\ProductManager\</c> a glob of
/// <c>Nexaflow.Tests.Features*</c> stopped seeing them, and they would have quietly dropped out of the
/// <c>[CoversNode]</c> guard on the way. Add a row when a new suite is created; the guards do the rest.
/// </para>
/// </summary>
internal static class FeatureTestSuites
{
    /// <summary>Assembly/project name prefixes that identify a suite these guards apply to.</summary>
    private static readonly string[] Patterns = ["Nexaflow.Tests.Features*", "Nexaflow.Tests.Initiatives*"];

    /// <summary>Every suite assembly beside this one. They land here because this project references them
    /// (see the csproj) — which is also what puts the feature DLLs in reach of the reflection rules.</summary>
    public static IReadOnlyList<Assembly> Assemblies()
    {
        var found = Patterns
            .SelectMany(p => Directory.GetFiles(AppContext.BaseDirectory, p + ".dll"))
            .Distinct()
            .Select(TryLoad)
            .OfType<Assembly>()
            .ToList();

        Assert.IsTrue(found.Count > 0,
            $"No suite DLL beside the guards in '{AppContext.BaseDirectory}' (looked for "
            + $"{string.Join(", ", Patterns)}). The suites reach the guards through this project's "
            + "ProjectReferences; without them every rule below passes over an empty set, which is worse "
            + "than failing.");
        return found;
    }

    /// <summary>Every suite csproj under src/Nexaflow.Tests.</summary>
    public static IReadOnlyList<string> ProjectFiles(string repoRoot)
        => [.. Patterns
                 .SelectMany(p => Directory.GetFiles(Path.Combine(repoRoot, "src", "Nexaflow.Tests"),
                                                     p + ".csproj", SearchOption.AllDirectories))
                 .Distinct()
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                          && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];

    private static Assembly? TryLoad(string path)
    {
        try { return Assembly.Load(Path.GetFileNameWithoutExtension(path)); }
        catch { return null; }   // a native/unmanaged neighbour, not a suite
    }
}
