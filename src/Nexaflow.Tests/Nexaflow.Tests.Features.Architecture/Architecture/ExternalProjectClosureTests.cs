using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A <c>ProjectReference</c> from <c>src/</c> into <c>external/</c> obliges the solution to list not just
/// that project but its whole transitive closure. Two different failures come from listing less, and
/// neither shows up on the command line — which is what makes this worth a test rather than a convention.
///
/// <para>
/// <b>Nothing listed → restore.</b> Visual Studio's solution restore covers the projects the solution
/// LISTS, so a reference to one it never restored fails with NU1105 "unable to find project information".
/// <c>dotnet build</c> follows the reference transitively and never notices, and a warm <c>obj/</c> hides
/// it locally, so it surfaces first on a cold clone.
/// </para>
/// <para>
/// <b>Listed in part → configuration split</b>, which is what shipped in 1.6. DiscUtils' projects share one
/// output directory keyed by configuration (<c>OutputPath = ..\$(Configuration)</c>), so they are a single
/// unit that everyone has to evaluate the same way. With three of its twenty-seven listed, the three took
/// <c>Debug</c> from the solution and the other twenty-four were evaluated with <c>$(Configuration)</c>
/// unset — which the fork defaults to <c>Release</c>, because that is how VS evaluates an out-of-solution
/// reference. MSBuild treats each (project, global properties) pair as its own node, so the command line
/// quietly built BOTH trees and every reference resolved. Visual Studio builds one side, and the other
/// side's reference dangles: <c>Metadata file '...\Library\Release\netstandard2.1\DiscUtils.Xfs.dll' could
/// not be found</c>. Green CLI, uncompilable IDE.
/// </para>
/// <para>
/// So the rule is the closure, not the reference. See docs/externals.md → Wiring rules.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class ExternalProjectClosureTests
{
    private static readonly string Root = RepoRoot.Locate();

    private const string ProjectRefPattern = "ProjectReference\\s+Include\\s*=\\s*\"([^\"]+)\"";
    private const string ListedPattern     = "<Project\\s+Path\\s*=\\s*\"([^\"]+)\"";

    private static readonly Regex ProjectRefRe = new(ProjectRefPattern, RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void Every_external_project_the_solution_reaches_is_listed_in_it()
    {
        var listed = ListedProjects();
        var reachable = ExternalClosureFromSrc();

        var missing = reachable
            .Where(p => !listed.Contains(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} external project(s) are reached by a ProjectReference but not listed in "
          + "Nexaflow.slnx. Add each to the /External/ folder with an AnyCPU platform mapping "
          + "(docs/externals.md → Wiring rules explains both failures this causes):\n"
          + string.Join("\n", missing.Select(Rel)));
    }

    /// <summary>Absolute paths of every project the solution lists, external or not.</summary>
    private static HashSet<string> ListedProjects()
    {
        var solution = File.ReadAllText(Path.Combine(Root, "Nexaflow.slnx"));
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(solution, ListedPattern))
            listed.Add(Absolute(Root, m.Groups[1].Value));
        return listed;
    }

    /// <summary>
    /// Every project under <c>external/</c> reachable from a <c>src/</c> project, following
    /// ProjectReferences all the way down.
    /// </summary>
    private static HashSet<string> ExternalClosureFromSrc()
    {
        var externalRoot = Path.GetFullPath(Path.Combine(Root, "external")) + Path.DirectorySeparatorChar;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var proj in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories))
            if (!IsBuildOutput(proj)) queue.Enqueue(Path.GetFullPath(proj));

        while (queue.Count > 0)
        {
            var proj = queue.Dequeue();
            if (!seen.Add(proj) || !File.Exists(proj)) continue;

            var dir = Path.GetDirectoryName(proj)!;
            foreach (Match m in ProjectRefRe.Matches(File.ReadAllText(proj)))
            {
                var target = Absolute(dir, m.Groups[1].Value);
                if (target.StartsWith(externalRoot, StringComparison.OrdinalIgnoreCase)) found.Add(target);
                queue.Enqueue(target);
            }
        }

        // Only judge what is actually on disk. An uninitialised submodule has no .csproj to walk, and
        // reporting that as a missing solution entry would name the wrong problem — populating it is the
        // EnsureSubmodulesInitialized target's job, not this test's.
        found.RemoveWhere(p => !File.Exists(p));
        return found;
    }

    private static string Absolute(string baseDir, string relative)
        => Path.GetFullPath(Path.Combine(baseDir, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Rel(string absolute) => "  " + Path.GetRelativePath(Root, absolute).Replace('\\', '/');

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
