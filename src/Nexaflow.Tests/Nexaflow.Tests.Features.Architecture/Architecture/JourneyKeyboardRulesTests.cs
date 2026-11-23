using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// FlaUI's <c>Keyboard.Press(key)</c> sends the key going down and nothing else. A journey that taps a key with it — Escape to close
/// a menu, Enter to submit — leaves the key held in the operating system after the run, where it cancels every drag begun afterwards
/// (a drag ends when Escape is down) and is read as held by whatever the machine does next. A key is tapped with
/// <c>Keyboard.Type(key)</c>; <c>Press</c> is for a key deliberately held, and the file that holds it releases it.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class JourneyKeyboardRulesTests
{
    private static readonly string Root = RepoRoot.Locate();

    [TestMethod]
    [TestCategory("Unit")]
    public void A_key_pressed_in_a_journey_is_released_in_the_same_file()
    {
        var suite = Path.Combine(Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.UIJourneys");
        Assert.IsTrue(Directory.Exists(suite), $"the journeys suite is not where this test expects it: {suite}");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(suite, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(suite, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            if (text.Contains("Keyboard.Press(", StringComparison.Ordinal) && !text.Contains("Keyboard.Release(", StringComparison.Ordinal))
                offenders.Add(relative);
        }

        Assert.AreEqual(0, offenders.Count,
            "Keyboard.Press sends only the key going down, so the key stays held after the run and cancels the next drag. Tap it with "
          + "Keyboard.Type(key); where a key is held on purpose, release it in the same file with Keyboard.Release(key):\n  "
          + string.Join("\n  ", offenders));
    }
}
