using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals;

/// <summary>
/// Keeps the two kinds of WPF test apart, mechanically.
///
/// <para>
/// A test that only <em>constructs</em> WPF objects needs an STA thread and nothing else: it is fast,
/// harmless, and safe to run alongside a thousand others. A test that <em>shows a window</em> needs an
/// interactive desktop and competes for a single machine-wide resource — focus. Run several of those at
/// once and they take each other's focus mid-assertion, which surfaces as a different test failing on
/// each run rather than as anything that looks like a real bug.
/// </para>
/// <para>
/// The two used to share one <c>TestCategory("UI")</c> label, which hid the handful that contend among
/// the several hundred that do not, and cost the fast filter every WPF test in the suite. They are now
/// <c>UI</c> and <c>Desktop</c>, and this guard holds the line: anything that shows a window must say so
/// and must not run in parallel.
/// </para>
/// <para>
/// The check reads source rather than reflecting, because "shows a window" is a property of what the
/// code does, not of anything visible in its metadata.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("a test-suite hygiene rule, not a product behaviour")]
public class DesktopTestCategoryGuardTests
{
    /// <summary>What, in source, gives away a test that puts a real window on the desktop.</summary>
    private static readonly string[] NeedsADesktop = ["new Window", "MarkdownEditorHarness"];

    [TestMethod]
    public void ATestThatShowsAWindowDeclaresItAndDoesNotRunInParallel()
    {
        var offenders = new List<string>();

        foreach (var file in SuiteSources())
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("[TestClass]", StringComparison.Ordinal)) continue;
            if (!NeedsADesktop.Any(marker => source.Contains(marker, StringComparison.Ordinal))) continue;

            var name = Path.GetFileName(file);
            if (!source.Contains("""TestCategory("Desktop")""", StringComparison.Ordinal))
                offenders.Add($"{name} shows a window but is not [TestCategory(\"Desktop\")]");
            if (!source.Contains("[DoNotParallelize]", StringComparison.Ordinal))
                offenders.Add($"{name} shows a window but is not [DoNotParallelize]");
        }

        Assert.AreEqual(0, offenders.Count, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void TheTwoCategoriesStaySeparate()
    {
        // A test declares the strongest thing it needs, not a list. Carrying both would put it back in
        // the fast filter's way for no reason, which is the confusion the split exists to remove.
        var both = SuiteSources()
            .Select(file => (Name: Path.GetFileName(file), Source: File.ReadAllText(file)))
            .Where(f => f.Source.Contains("""TestCategory("UI")""", StringComparison.Ordinal)
                        && f.Source.Contains("""TestCategory("Desktop")""", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.AreEqual(0, both.Count,
            $"these claim both categories: {string.Join(", ", both)}");
    }

    /// <summary>
    /// The in-process WPF suites — both, not just the one this guard sits in. Focus is machine-wide, so a
    /// window-showing test contends with every other suite's regardless of which assembly holds it. When
    /// the Visuals tests moved out of <c>Tests.Core</c> this guard came with them; a check still pointed
    /// at one suite would have passed over the other, which reads green while testing nothing.
    /// </summary>
    private static readonly string[] Suites = ["Nexaflow.Tests.Visuals", "Nexaflow.Tests.Core"];

    private static IEnumerable<string> SuiteSources()
    {
        var repo = SuiteRoot();

        return Suites
            .SelectMany(suite =>
            {
                var root = Path.Combine(repo, "src", "Nexaflow.Tests", suite);
                Assert.IsTrue(Directory.Exists(root), $"could not find {suite}'s source at {root}");
                return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);
            })
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        // This file names the markers it looks for, so it would otherwise report itself.
                        && !f.EndsWith(nameof(DesktopTestCategoryGuardTests) + ".cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// The repo root, by the same walk-up every source-level test here uses. Done locally rather than
    /// through the shared helper, which lives in a project this suite deliberately does not reference.
    /// From a worktree this lands on that branch's own sources, which is what should be checked.
    /// </summary>
    private static string SuiteRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }
}
