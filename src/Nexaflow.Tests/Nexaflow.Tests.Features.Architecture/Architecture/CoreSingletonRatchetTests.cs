using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The shell's session is built in one place, <c>ShellHost</c>, which makes each of its services once and hands them to what
/// uses them. A static <c>Instance</c> is a service that makes itself the first time anything touches it, in whatever order
/// that happens, and that a test can only reach by resetting it — the opposite. This is the count of those left to hand over
/// to the host, and what stops it growing.
/// <para>
/// <b>A ratchet, like <see cref="ViewModelWpfRatchetTests"/>.</b> The files under the shell's projects — <c>src/Nexaflow.Core</c>,
/// <c>.Session</c>, <c>.Assistant</c>, <c>.Host</c> — and the <c>src/Nexaflow.IO.*</c> libraries that declare a static
/// <c>Instance</c> are listed in <see cref="BaselineFile"/>, one
/// repo-relative path per line: a new one fails, and a listed one that no longer declares it fails until its line is
/// deleted, so the list can only shrink.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class CoreSingletonRatchetTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>The ratchet. One repo-relative path per line; <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features.Architecture", "Architecture", "core-singletons.txt");

    /// <summary>A static member named <c>Instance</c>: a property or a field.</summary>
    private static readonly Regex StaticInstance = new(
        @"\bstatic\s+(?:readonly\s+)?[\w.<>?,\s]+?\s+Instance\s*(?:\{|=|;)", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void No_new_static_Instance_in_Core_or_IO()
    {
        var found = Singletons();
        var baseline = Baseline();

        var fresh = found.Keys
            .Where(path => !baseline.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, fresh.Count,
            $"{fresh.Count} file(s) declare a static Instance:\n"
          + string.Join("\n", fresh.Select(path => $"  {path}    (line {found[path]})"))
          + "\nA service of the session is made once by ShellHost and handed to what uses it through its constructor\n"
          + "(docs/Architecture.md, Ownership & Lifetime).");
    }

    /// <summary>
    /// The other half of the ratchet. A listed file that no longer declares a static <c>Instance</c> has to leave the list —
    /// otherwise the count stops being the work left, and a singleton brought back would pass unseen.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_baseline_has_no_stale_entries()
    {
        var found = Singletons();

        var gone = Baseline()
            .Where(path => !found.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, gone.Count,
            $"{Path.GetFileName(BaselineFile)} is out of date — these no longer declare a static Instance; delete the lines:\n"
          + string.Join("\n", gone.Select(path => $"  {path}")));
    }

    /// <summary>Every file that declares a static <c>Instance</c>, by repo-relative path, with the line it does so on.</summary>
    private static Dictionary<string, int> Singletons()
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        var src = Path.Combine(Root, "src");
        var roots = Directory.EnumerateDirectories(src, "Nexaflow.IO.*")
            .Concat(new[] { "Nexaflow.Core", "Nexaflow.Session", "Nexaflow.Assistant", "Nexaflow.Host" }.Select(project => Path.Combine(src, project)));

        foreach (var file in roots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)))
        {
            if (CodeWithoutComments.IsBuildOutput(file)) continue;

            var code = CodeWithoutComments.Read(file);
            var singleton = StaticInstance.Match(code);
            if (!singleton.Success) continue;

            found.Add(Path.GetRelativePath(Root, file).Replace('\\', '/'), 1 + code.AsSpan(0, singleton.Index).Count('\n'));
        }
        return found;
    }

    private static HashSet<string> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new HashSet<string>(StringComparer.Ordinal);
        return File.ReadAllLines(BaselineFile)
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                   .ToHashSet(StringComparer.Ordinal);
    }
}
