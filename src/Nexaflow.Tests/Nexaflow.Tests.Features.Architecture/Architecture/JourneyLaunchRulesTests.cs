using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A feature has one UI journey, made of steps, and one launch of the app. The app takes about twenty seconds to start and be
/// taken down, which is most of a small journey, so a journey per scenario made the suite slow and — because a scenario that
/// failed took the rest of its journey with it — brittle. This holds the line the consolidation drew: a feature's second launch
/// has to carry <see cref="SecondLaunchAttribute"/> and say why it is not a step of the first.
///
/// <para>
/// The suite is read as source, as <see cref="AutomationIdJourneyCoverageTests"/> reads it: <c>Nexaflow.Tests.UIJourneys</c> knows the
/// app as a process and the architecture suite does not reference it. A <c>[TestMethod]</c> is a launch, and so is each
/// <c>[DataRow]</c> of one. A feature is the folder under <c>Features\</c> its journeys sit in.
/// </para>
/// <para>
/// Features that predate the rule and have not been folded yet are listed in <see cref="BaselineFile"/> with the launches they still
/// owe, and the two tests pull in opposite directions: the first refuses a launch beyond what the file allows, the second refuses
/// an entry that no longer matches — so the file can only shrink, and a folded feature leaves it.
/// </para>
/// <para>
/// The shell (<c>Core\</c>) is not a feature: its journeys are the handful that cover general use of the app, and are held to a count
/// of their own, <see cref="ShellJourneyLaunches"/>.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class JourneyLaunchRulesTests
{
    /// <summary>
    /// The launches the shell's journeys make, which is the shell's frame, Options, drops, the first-run wizard (three, each needing
    /// a first run of its own), the two that need the app's own models, and a window moving between workspaces — the lifetimes of
    /// the workspaces it leaves and comes back to. Adding a general journey is a decision made here, in view.
    /// </summary>
    private const int ShellJourneyLaunches = 9;

    private static readonly string Root = RepoRoot.Locate();

    private static string SuiteDirectory => Path.Combine(Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.UIJourneys");

    /// <summary>The features still to fold, and how many launches beyond their first each owes. <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features.Architecture", "Architecture",
        "journey-launches-still-to-fold.txt");

    /// <summary>What a launch is, as far as this reads: where it sits, and whether it has said why it is a second.</summary>
    private sealed record Launch(string Group, string File, int Line, bool IsSecond, string? Reason);

    [TestMethod]
    [TestCategory("Unit")]
    public void A_feature_launches_the_app_once_unless_it_says_why()
    {
        var baseline = Baseline();
        var failures = new List<string>();

        foreach (var (group, launches) in FeatureGroups())
        {
            var firsts = launches.Where(l => !l.IsSecond).ToList();
            if (launches.Any(l => l.IsSecond) && firsts.Count == 0)
            {
                failures.Add($"  {group}: every launch is marked a second one, so none is the first. Take the mark off the feature's main journey.");
                continue;
            }

            var owed = Math.Max(0, firsts.Count - 1);
            var allowed = baseline.GetValueOrDefault(group);
            if (owed > allowed)
                failures.Add($"  {group}: {firsts.Count} launches without a reason, one allowed{(allowed > 0 ? $" and {allowed} still to fold" : "")}:\n"
                           + string.Join("\n", firsts.Select(l => $"      {l.File}:{l.Line}")));
        }

        Assert.AreEqual(0, failures.Count,
            "A feature has one journey, made of steps, and one launch of the app. Fold these into its steps — or, where the app\n"
          + "really needs a second launch, put [SecondLaunch(SecondLaunchReason.OverTwoMinutes | SecondLaunchReason.SecondState, \"why\")]\n"
          + "on the test method:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The other half of the ratchet. An entry whose feature has been folded, or owes fewer launches than the file says, has to be
    /// lowered or deleted — otherwise the file slowly stops describing anything, and a feature that grows again hides behind a
    /// stale line.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_baseline_has_no_stale_entries()
    {
        var owedBy = FeatureGroups().ToDictionary(g => g.Key, g => Math.Max(0, g.Value.Count(l => !l.IsSecond) - 1));
        var stale = Baseline()
            .Where(entry => owedBy.GetValueOrDefault(entry.Key) != entry.Value)
            .Select(entry => $"  {entry.Key} {entry.Value}    (it owes {owedBy.GetValueOrDefault(entry.Key)} now)")
            .ToList();

        Assert.AreEqual(0, stale.Count,
            $"{Path.GetFileName(BaselineFile)} is out of date — lower these entries, or delete the ones that now owe nothing:\n"
          + string.Join("\n", stale));
    }

    /// <summary>A second launch's reason is a sentence, not a flag: the attribute names one of the two reasons and says what makes it so.</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Every_second_launch_says_what_makes_it_so()
    {
        var bare = AllLaunches().Where(l => l.IsSecond && l.Reason is null)
                                .Select(l => $"  {l.File}:{l.Line}")
                                .ToList();

        Assert.AreEqual(0, bare.Count,
            "[SecondLaunch] takes a SecondLaunchReason and a sentence between quotes, on one line:\n" + string.Join("\n", bare));
    }

    /// <summary>The shell's journeys are the handful that cover general use of the app; a new one is added to the count here on purpose.</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_shell_keeps_to_its_handful_of_journeys()
    {
        var shell = AllLaunches().Count(l => l.Group == "Core");

        Assert.AreEqual(ShellJourneyLaunches, shell,
            $"The shell's journeys launch the app {shell} times, not {ShellJourneyLaunches}. A new general journey is a decision —\n"
          + "make it by changing ShellJourneyLaunches, with the reason; a folded one lowers it.");
    }

    private static Dictionary<string, List<Launch>> FeatureGroups() =>
        AllLaunches().Where(l => l.Group != "Core")
                     .GroupBy(l => l.Group)
                     .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>Every launch the journeys make, grouped by the folder their file sits in.</summary>
    private static List<Launch> AllLaunches()
    {
        Assert.IsTrue(Directory.Exists(SuiteDirectory), $"the journeys suite is not where this test expects it: {SuiteDirectory}");

        var launches = new List<Launch>();
        foreach (var file in Directory.EnumerateFiles(SuiteDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(SuiteDirectory, file).Replace('\\', '/');
            var group = GroupOf(relative);
            if (group is null) continue;

            var text = File.ReadAllText(file);
            // Run by hand to measure the app, never by a run of the suite.
            if (text.Contains("TestCategory(\"Interactive\")", StringComparison.Ordinal)) continue;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("[TestMethod", StringComparison.Ordinal)) continue;

                var attributes = AttributesAround(lines, i);
                var second = attributes.FirstOrDefault(a => a.StartsWith("[SecondLaunch(", StringComparison.Ordinal));
                var reason = second is not null && SecondLaunchRe.IsMatch(second) ? second : null;
                var rows = Math.Max(1, attributes.Count(a => a.StartsWith("[DataRow(", StringComparison.Ordinal)));

                for (var row = 0; row < rows; row++)
                    launches.Add(new Launch(group, relative, i + 1, second is not null, reason));
            }
        }
        return launches;
    }

    private static readonly Regex SecondLaunchRe =
        new(@"^\[SecondLaunch\(SecondLaunchReason\.(OverTwoMinutes|SecondState),\s*""[^""]+""\)\]", RegexOptions.Compiled);

    /// <summary>
    /// Where a file's tests belong: <c>Core</c> for the shell, <c>Features/&lt;Name&gt;</c> for a feature. The scaffolding around the
    /// journeys, and the captures that photograph the app for the documentation, are not journeys.
    /// </summary>
    private static string? GroupOf(string relative)
    {
        var parts = relative.Split('/');
        return parts[0] switch
        {
            "Core" => "Core",
            "Features" when parts.Length > 2 => $"Features/{parts[1]}",
            _ => null,
        };
    }

    /// <summary>The attribute lines stacked directly above and below a line, so a <c>[SecondLaunch]</c> is found wherever it is put.</summary>
    private static List<string> AttributesAround(string[] lines, int at)
    {
        var found = new List<string>();
        for (var i = at - 1; i >= 0 && lines[i].TrimStart().StartsWith('['); i--) found.Add(lines[i].Trim());
        for (var i = at; i < lines.Length && lines[i].TrimStart().StartsWith('['); i++) found.Add(lines[i].Trim());
        return found;
    }

    private static Dictionary<string, int> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new Dictionary<string, int>(StringComparer.Ordinal);

        var entries = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(BaselineFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var cut = line.LastIndexOf(' ');
            Assert.IsTrue(cut > 0 && int.TryParse(line[(cut + 1)..], out _), $"{Path.GetFileName(BaselineFile)}: '{line}' is not '<Features/Name> <launches still owed>'.");
            entries[line[..cut].Trim()] = int.Parse(line[(cut + 1)..]);
        }
        return entries;
    }
}
