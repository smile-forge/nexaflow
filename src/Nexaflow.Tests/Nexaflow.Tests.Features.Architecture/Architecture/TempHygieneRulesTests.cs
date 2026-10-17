using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Test code makes its files through <see cref="TestTemp"/>, never straight in the machine's <c>%TEMP%</c>.
/// A test that writes there and forgets to clean up leaks a folder per run, and the leaks compound: tens of
/// thousands of them made every test that touches Temp slow — a file tree probing each one for children, an
/// app's size summed across all of them. <see cref="TestTemp"/> gives each test host one folder that goes with
/// the host, so the clean-up no longer depends on each test; this keeps a new test from stepping around it.
/// <para>
/// No baseline: every test already uses it, so the rule is absolute. The exemptions are the two files that need
/// the machine's own Temp — <see cref="TestTemp"/> itself, and <see cref="UiTestGate"/>, whose consent record
/// is shared on purpose by every test host on the desktop and is one file, not a folder.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo test-hygiene guard; maps to no single product node")]
public class TempHygieneRulesTests
{
    private static readonly string Root = RepoRoot.Locate();

    private static readonly string TestsRoot = Path.Combine(Root, "src", "Nexaflow.Tests");

    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/TestTemp.cs"] = "the one place that claims a folder in %TEMP%",
        ["src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/UiTestGate.cs"] = "a consent record every test host reads",
    };

    /// <summary>The ways to reach the machine's Temp: the folder itself, a folder in it, a file in it, its variable.</summary>
    private static readonly Regex RawTemp = new(
        @"\bGetTempPath\s*\(|\bCreateTempSubdirectory\s*\(|\bGetTempFileName\s*\(|GetEnvironmentVariable\s*\(\s*""(?:TEMP|TMP)""",
        RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void Test_code_makes_its_files_through_TestTemp()
    {
        var offences = new List<string>();
        foreach (var file in Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (Exempt.ContainsKey(relative)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (RawTemp.IsMatch(lines[i])) offences.Add($"  {relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.AreEqual(0, offences.Count,
            $"{offences.Count} line(s) of test code reach the machine's %TEMP% directly:\n"
          + string.Join("\n", offences)
          + "\nUse TestTemp (Nexaflow.Tests.Fixtures): TestTemp.Root for a folder that exists, "
          + "TestTemp.CreateSubdirectory(prefix) for a fresh one, TestTemp.CreateFile(extension) for a fresh file. "
          + "It is deleted with the test host, so nothing a test leaves behind outlives the run.");
    }

    /// <summary>An exemption whose file is gone exempts nothing and would hide a new file that took its path.</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Every_exemption_names_a_file_that_exists()
    {
        var gone = Exempt.Keys.Where(relative => !File.Exists(Path.Combine(Root, relative))).ToList();
        Assert.AreEqual(0, gone.Count, "exempt from the temp rule but no longer there:\n  " + string.Join("\n  ", gone));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
