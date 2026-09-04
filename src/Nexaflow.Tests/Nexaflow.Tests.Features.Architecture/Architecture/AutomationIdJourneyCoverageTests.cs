using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// An <c>AutomationProperties.AutomationId</c> exists for exactly one reason: so a UI journey can find that
/// element. An id no journey names is therefore not a small omission — it is the whole cost of the id (a
/// contract nobody may rename freely) with none of the benefit, and it reads as covered to anyone auditing
/// the view. This closes the loop that <c>NXUI001</c> opens: the analyzer makes sure the buttons have ids,
/// and this makes sure the ids are actually clicked by something.
///
/// <para>
/// <b>It is a ratchet, not a pass/fail on the whole repo.</b> The ids that predate the rule are listed in
/// <see cref="BaselineFile"/>, and the two tests here pull in opposite directions: the first refuses a
/// <i>new</i> unreferenced id, the second refuses a baseline entry that has since been covered or deleted.
/// So the list can only shrink, and it cannot rot — the second test is what stops it becoming a permanent
/// allowlist that quietly outlives the ids it names.
/// </para>
/// <para>
/// Matching is deliberately loose: an id counts as reached if its text appears anywhere in the journey
/// sources. A journey may hold it in a constant, build a scoped search around it, or pass it to a helper,
/// and a stricter test would fail those honest uses. The failure this catches is an id no journey mentions
/// at all, which no amount of indirection explains.
/// </para>
/// <para>
/// Ids whose value is a markup extension (<c>{Binding AutomationId}</c>) are skipped — the real id is
/// computed at run time from data, so there is no literal here for a journey to name.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class AutomationIdJourneyCoverageTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>The ratchet. One id per line; <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features.Architecture", "Architecture",
        "automation-ids-without-a-journey.txt");

    private static readonly Regex IdRe =
        new(@"AutomationProperties\.AutomationId\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void Every_automation_id_is_named_by_a_journey()
    {
        var declared = DeclaredIds();
        var journeys = JourneySource();
        var baseline = Baseline();

        var unreferenced = declared.Keys
            .Where(id => !journeys.Contains(id, StringComparison.Ordinal))
            .Where(id => !baseline.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, unreferenced.Count,
            $"{unreferenced.Count} AutomationId(s) are declared in a view but named by no journey test.\n"
          + "Write the journey that clicks them — or, if this id genuinely cannot be reached yet, add it to\n"
          + $"{Path.GetFileName(BaselineFile)} with a line saying why:\n"
          + string.Join("\n", unreferenced.Select(id => $"  {id}    ({declared[id]})")));
    }

    /// <summary>
    /// The other half of the ratchet. An entry that is now covered, or names an id no view declares any more,
    /// has to leave the file — otherwise the baseline slowly stops describing anything and a genuinely new
    /// gap can hide behind a stale line that happens to match.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_baseline_has_no_stale_entries()
    {
        var declared = DeclaredIds();
        var journeys = JourneySource();

        var covered = new List<string>();
        var gone = new List<string>();
        foreach (var id in Baseline())
        {
            if (!declared.ContainsKey(id)) gone.Add(id);
            else if (journeys.Contains(id, StringComparison.Ordinal)) covered.Add(id);
        }

        Assert.IsTrue(covered.Count == 0 && gone.Count == 0,
            $"{Path.GetFileName(BaselineFile)} is out of date — delete these lines:\n"
          + string.Join("\n", covered.Select(id => $"  {id}    (a journey names it now)")
                              .Concat(gone.Select(id => $"  {id}    (no view declares it any more)"))));
    }

    /// <summary>Every literal AutomationId in a hand-authored view, mapped to the view that declares it.</summary>
    private static Dictionary<string, string> DeclaredIds()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            foreach (Match m in IdRe.Matches(File.ReadAllText(file)))
            {
                var id = m.Groups[1].Value.Trim();
                // A markup extension is a run-time value, not an id a journey could be asked to name.
                if (id.Length == 0 || id.StartsWith("{", StringComparison.Ordinal)) continue;
                if (!ids.ContainsKey(id)) ids[id] = relative;
            }
        }
        return ids;
    }

    /// <summary>Every journey source file, concatenated. One read, then N substring checks.</summary>
    private static string JourneySource()
    {
        var dir = Path.Combine(Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.UIJourneys");
        Assert.IsTrue(Directory.Exists(dir), $"the journeys suite is not where this test expects it: {dir}");
        return string.Join("\n", Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                                          .Where(f => !IsBuildOutput(f))
                                          .Select(File.ReadAllText));
    }

    private static HashSet<string> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new HashSet<string>(StringComparer.Ordinal);
        return File.ReadAllLines(BaselineFile)
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                   .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
