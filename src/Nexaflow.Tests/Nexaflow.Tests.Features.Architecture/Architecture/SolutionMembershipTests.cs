using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Guards that every project actually lives in the right solution. A project can be ProjectReferenced (so
/// <c>dotnet build Nexaflow.slnx</c> compiles it transitively and CI stays green) yet be absent from the
/// <c>.slnx</c> — in which case an IDE, which only loads the solution's listed projects, reports it as a missing
/// reference and won't build. These turn that silent gap into a red test for both solutions.
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class SolutionMembershipTests
{
    private static readonly string Root = RepoRoot.Locate();

    private static readonly string[] SkipSegments =
        [".claude", "bin", "obj", ".git", ".vs", "node_modules", "test-samples", "packages"];

    /// <summary>Project paths a <c>.slnx</c> lists (its <c>Path="…"</c> attributes), repo-relative, forward slashes.</summary>
    private static HashSet<string> SolutionProjects(string slnxRelPath)
        => Regex.Matches(File.ReadAllText(Path.Combine(Root, slnxRelPath)), @"Path=""([^""]+)""")
                .Select(m => m.Groups[1].Value.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Repo project files matching <paramref name="pattern"/> (under <paramref name="underDir"/> if given),
    /// repo-relative + forward slashes. Build output and the nested git worktrees under .claude/ are never entered, so the
    /// walk stays the size of the repository however many worktrees sit inside it.</summary>
    private static IReadOnlyList<string> RepoProjects(string pattern, string underDir = "")
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(string.IsNullOrEmpty(underDir) ? Root : Path.Combine(Root, underDir));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(dir, pattern).Select(p => Path.GetRelativePath(Root, p).Replace('\\', '/')));
            foreach (var sub in new DirectoryInfo(dir).EnumerateDirectories())
                if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint) && !SkipSegments.Contains(sub.Name, StringComparer.OrdinalIgnoreCase))
                    pending.Push(sub.FullName);
        }
        return found;
    }

    [TestMethod]
    public void Every_source_project_is_listed_in_the_app_solution()
    {
        var inSolution = SolutionProjects("Nexaflow.slnx");
        var missing = RepoProjects("*.csproj", "src").Where(p => !inSolution.Contains(p)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Every src/**/*.csproj must be listed in Nexaflow.slnx. A ProjectReference alone lets `dotnet build` " +
            "compile it transitively, but an IDE (which builds only the solution's listed projects) reports it as a " +
            $"missing reference. Add each to Nexaflow.slnx. Missing: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void Every_installer_project_is_listed_in_the_setup_solution()
    {
        var inSetup = SolutionProjects("NexaflowSetup.slnx");
        var missing = RepoProjects("*.wixproj").Where(p => !inSetup.Contains(p)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Every installer .wixproj must be listed in NexaflowSetup.slnx (the packaging-only solution). " +
            $"Missing: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void The_app_solution_excludes_the_installer_projects()
    {
        // NexaflowSetup.slnx is deliberately separate so a normal app/dev build never compiles the WiX installer
        // (and needn't have the WiX SDK installed). A .wixproj leaking into Nexaflow.slnx breaks that.
        var leaked = SolutionProjects("Nexaflow.slnx")
            .Where(p => p.EndsWith(".wixproj", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.AreEqual(0, leaked.Count,
            "The installer (.wixproj) must stay out of Nexaflow.slnx — it belongs in NexaflowSetup.slnx. " +
            $"Leaked: {string.Join(", ", leaked)}");
    }
}
