using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Every submodule this repository declares is checked out here.
/// <para>
/// The build does not need them all, and deliberately does not fetch them all: <c>Directory.Build.targets</c>
/// runs <c>tools/ensure-submodules.ps1</c>, which reads <c>tools/tree-sitter-grammars.props</c> and populates
/// only the nested grammars that get compiled. That keeps the inner loop fast and it is why a fresh worktree
/// builds cleanly with a tenth of the files. What it also means is that two checkouts of the <i>same commit</i>
/// legitimately contain different files — so anything derived from "every file in the repository" differs
/// between them. The knowledge graph is exactly that: node counts, <c>graph grep</c> results and the freshness
/// report all change with which optional submodules happen to be present, and a finding one developer can
/// reproduce another cannot.
/// </para>
/// <para>
/// So the completeness is asserted here rather than in the build: develop against the subset, and let the
/// gate that runs before a PR insist on the whole. At that point the graph describes the same repository for
/// everyone.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class SubmoduleCompletenessTests
{
    /// <summary>What to run when this fails. The build's own script populates the required subset by design,
    /// so it is not the fix for the optional ones — git is.</summary>
    private const string Remedy = "git submodule update --init --recursive";

    [TestMethod]
    public void EverySubmoduleThisRepoDeclares_IsCheckedOutHere()
    {
        var root    = RepoRoot.Locate();
        var missing = new List<string>();

        foreach (var path in DeclaredUnder(root, ""))
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full) || !Populated(full)) missing.Add(path);
        }

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} declared submodule(s) are not checked out, so anything derived from the whole "
          + $"repository — the knowledge graph above all — describes a different repository here than it does "
          + $"for anyone with them. Run: {Remedy}\n  " + string.Join("\n  ", missing.Take(20))
          + (missing.Count > 20 ? $"\n  … and {missing.Count - 20} more" : ""));
    }

    /// <summary>
    /// Every submodule path <c>.gitmodules</c> declares at <paramref name="relative"/>, and recursively those
    /// its populated submodules declare in turn — the nested grammar set is a submodule of a submodule, and
    /// it is the half that goes missing.
    /// </summary>
    private static IEnumerable<string> DeclaredUnder(string root, string relative)
    {
        var dir  = string.IsNullOrEmpty(relative) ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var file = Path.Combine(dir, ".gitmodules");
        if (!File.Exists(file)) yield break;

        foreach (var path in Paths(file))
        {
            var child = string.IsNullOrEmpty(relative) ? path : relative + "/" + path;
            yield return child;

            // Only descend into one that is actually there: an absent submodule has no .gitmodules to read,
            // and reporting it once is more use than reporting it and then silently missing its children.
            foreach (var nested in DeclaredUnder(root, child)) yield return nested;
        }
    }

    /// <summary>The <c>path = …</c> of each submodule stanza. Parsed rather than shelled out to git, so the
    /// guard reports the same thing on a machine where the test host has no git on PATH.</summary>
    private static IEnumerable<string> Paths(string gitmodules) =>
        File.ReadLines(gitmodules)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("path", StringComparison.Ordinal) && l.Contains('='))
            .Select(l => l[(l.IndexOf('=') + 1)..].Trim())
            .Where(p => p.Length > 0);

    /// <summary>
    /// Whether a submodule directory holds anything but its own git metadata. The directory exists either
    /// way — git creates an empty one for a submodule it has not fetched — so existence alone would pass for
    /// precisely the case this guard is about.
    /// </summary>
    private static bool Populated(string dir) =>
        Directory.EnumerateFileSystemEntries(dir)
                 .Any(e => !Path.GetFileName(e).Equals(".git", StringComparison.OrdinalIgnoreCase));
}
