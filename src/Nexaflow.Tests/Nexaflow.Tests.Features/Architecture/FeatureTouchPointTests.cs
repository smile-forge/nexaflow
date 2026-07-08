using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Turns the silent steps of "add a feature" into red tests. Forgetting the ProjectReference in
/// Core (the DLL never ships, discovery never sees it) or in this test project (tests can't see
/// it), or the file-type mapping for a new viewer, produces a green build and a broken feature —
/// these assertions make the build say what was missed.
/// </summary>
[TestClass]
public class FeatureTouchPointTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>Names of every feature project directory under src/Nexaflow.Features.</summary>
    private static IReadOnlyList<string> FeatureProjectDirs()
        => Directory.GetDirectories(Path.Combine(Root, "src", "Nexaflow.Features"), "Nexaflow.Features.*")
                    .Select(p => Path.GetFileName(p)!)
                    .ToList();

    /// <summary>Feature project names referenced by <paramref name="csprojPath"/>.</summary>
    private static HashSet<string> ReferencedFeatures(string csprojPath)
        => Regex.Matches(File.ReadAllText(csprojPath), @"Include=""[^""]*[\\/](Nexaflow\.Features\.[^\\/""]+)\.csproj""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Every_feature_project_ships_via_a_Core_ProjectReference()
    {
        var referenced = ReferencedFeatures(Path.Combine(Root, "src", "Nexaflow.Core", "Nexaflow.Core.csproj"));
        var missing = FeatureProjectDirs().Where(d => !referenced.Contains(d)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Feature DLLs ship (and are discovered) only through Nexaflow.Core.csproj ProjectReferences — " +
            $"without one the feature silently never loads. Missing: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void Every_feature_project_is_referenced_by_this_test_project()
    {
        var referenced = ReferencedFeatures(Path.Combine(
            Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features", "Nexaflow.Tests.Features.csproj"));
        var missing = FeatureProjectDirs().Where(d => !referenced.Contains(d)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Nexaflow.Tests.Features must reference every feature project, or its tests (and the " +
            $"architecture guards) can't see it. Missing: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void Every_viewer_sample_extension_routes_through_the_bundled_filemap()
    {
        // The per-file UI smoke (SampleFileViewerTests) opens each fixture via the default-open
        // route, which only works if the bundled filemap maps the extension. A fixture/ViewerMap
        // row without a filemap entry means the viewer silently isn't the default-open target.
        var filemap = File.ReadAllText(Path.Combine(Root,
            "src", "Nexaflow.Features", "Nexaflow.Features.WindowsFileSystem", "FileActions", "default-filemap.json"));

        var missing = new List<string>();
        foreach (var (subDir, viewerId) in ViewerMap.BySet)
            foreach (var ext in TestSampleData.Files(subDir)
                         .Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
                         .Where(e => e.Length > 0)
                         .Distinct())
                if (!filemap.Contains($"*.{ext}", StringComparison.OrdinalIgnoreCase))
                    missing.Add($"{subDir}/*.{ext} (→ {viewerId})");

        Assert.AreEqual(0, missing.Count,
            "Every sample-set extension in ViewerMap must be mapped in default-filemap.json or the " +
            $"default-open route won't reach the viewer. Missing: {string.Join(", ", missing)}");
    }
}
