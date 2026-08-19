using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Nexaflow.Features.Common;
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
[NoCoverage("architecture guard")]
public class FeatureTouchPointTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>Names of every feature project under src/Nexaflow.Features. A feature is defined by its
    /// <c>.csproj</c>, not by the folder: removing a feature leaves gitignored bin/obj behind, and a
    /// name-only match would then report that corpse as a shipped feature missing every reference.</summary>
    private static IReadOnlyList<string> FeatureProjectDirs()
        => Directory.GetDirectories(Path.Combine(Root, "src", "Nexaflow.Features"), "Nexaflow.Features.*")
                    .Where(IsProjectDir)
                    .Select(p => Path.GetFileName(p)!)
                    .ToList();

    /// <summary>True when the directory actually holds its own <c>&lt;name&gt;.csproj</c>.</summary>
    internal static bool IsProjectDir(string dir)
        => File.Exists(Path.Combine(dir, Path.GetFileName(dir) + ".csproj"));

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
    public void Every_feature_project_is_referenced_by_one_of_the_test_suites()
    {
        // The union, because the suites are split by subject: a viewer belongs to Viewers, a Windows
        // integration to WindowsOS, and neither should have to reference the other's features. What still
        // has to hold is that SOME suite references each one — otherwise its tests, and the reflection
        // guards that load every feature assembly from this project's output, silently can't see it.
        var referenced = FeatureTestSuites.ProjectFiles(Root)
            .SelectMany(ReferencedFeatures)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = FeatureProjectDirs().Where(d => !referenced.Contains(d)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Every feature project must be referenced by one of the Nexaflow.Tests.Features* suites, or " +
            "its tests (and the architecture guards) can't see it. Add it to whichever suite matches its " +
            $"subject. Missing: {string.Join(", ", missing)}");
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

    [TestMethod]
    public void Every_internal_viewer_action_is_reachable_from_the_bundled_filemap()
    {
        // The "this is a file viewer" marker is IFileAction.OpensViewer — the same flag the
        // Define-New wizard filters on when listing internal viewers. Every viewer action's
        // ExperienceId (or a descendant of it — child ids satisfy parent experiences) must be
        // mapped by default-filemap.json, or no file type can ever default-open in that viewer.
        var filemap = File.ReadAllText(Path.Combine(Root,
            "src", "Nexaflow.Features", "Nexaflow.Features.WindowsFileSystem", "FileActions", "default-filemap.json"));
        var mappedIds = Regex.Matches(filemap, @"""ExperienceId"":\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var asm in Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
                     .Select(p => Assembly.Load(Path.GetFileNameWithoutExtension(p))))
            foreach (var type in ActionTypes(asm))
            {
                // Identity properties (OpensViewer / ExperienceId statics) are constants by
                // contract — read them without running a constructor. A type whose identity
                // depends on ctor state (ShellVerbAction, CustomAction) reports OpensViewer=false
                // via the default and is out of scope here.
                IFileAction action;
                bool opensViewer;
                try
                {
                    action = (IFileAction)RuntimeHelpers.GetUninitializedObject(type);
                    opensViewer = action.OpensViewer;
                }
                catch { continue; }   // identity not readable without ctor state → not a mappable viewer
                if (!opensViewer) continue;

                var expId = type.GetProperty("StaticExperienceId", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;
                if (string.IsNullOrEmpty(expId)) { missing.Add($"{type.Name}: no StaticExperienceId"); continue; }

                var reachable = mappedIds.Contains(expId) ||
                                mappedIds.Any(id => id.StartsWith(expId + "/", StringComparison.OrdinalIgnoreCase));
                if (!reachable) missing.Add($"{type.Name} ({expId})");
            }

        Assert.AreEqual(0, missing.Count,
            "Every viewer-opening IFileAction must have its ExperienceId (or a descendant) mapped in " +
            $"default-filemap.json. Missing: {string.Join(", ", missing)}");
    }

    private static IEnumerable<Type> ActionTypes(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        return types.Where(t => t is { IsAbstract: false, IsInterface: false }
                                && typeof(IFileAction).IsAssignableFrom(t));
    }
}
