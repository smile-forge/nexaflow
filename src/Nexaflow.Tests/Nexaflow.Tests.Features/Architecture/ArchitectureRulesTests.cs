using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Mechanical enforcement of the layering "Hard Rules" (CLAUDE.md). Nothing in MSBuild stops a
/// feature from referencing Core or a sibling feature, and nothing stops a stray
/// <c>Application.Current.Dispatcher</c> — until these tests, the rules held purely by discipline.
/// A violation is now a red build instead of a review catch.
/// </summary>
[TestClass]
public class ArchitectureRulesTests
{
    /// <summary>Every feature assembly deployed next to the test exe — this project references all
    /// of them, so a feature missing here is itself a failure (see <see cref="FeatureTouchPointTests"/>).</summary>
    private static IReadOnlyList<Assembly> FeatureAssemblies()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
                    .Select(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)))
                    .ToList();

    [TestMethod]
    public void Features_never_reference_Core()
    {
        var offenders = FeatureAssemblies()
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "Nexaflow.Core"))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "Features depend only on Features.Common + the shared Visuals.*/IO.* libs, never Core. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Features_never_reference_another_feature()
    {
        var offenders = new List<string>();
        foreach (var asm in FeatureAssemblies())
        {
            var name = asm.GetName().Name!;
            foreach (var r in asm.GetReferencedAssemblies())
                if (r.Name!.StartsWith("Nexaflow.Features.", StringComparison.Ordinal) &&
                    r.Name != "Nexaflow.Features.Common")
                    offenders.Add($"{name} → {r.Name}");
        }

        Assert.AreEqual(0, offenders.Count,
            "Features may reference only Features.Common (contracts), never each other. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Features_never_touch_the_UI_dispatcher_singletons()
    {
        // The rule: marshal via IShellServices.RunOnUiAsync / WatchFile. A view's own inherited
        // `this.Dispatcher` is allowed and does not match these tokens.
        string[] banned = ["Application.Current.Dispatcher", "Dispatcher.CurrentDispatcher"];
        var featuresRoot = Path.Combine(RepoRoot.Locate(), "src", "Nexaflow.Features");
        var sep = Path.DirectorySeparatorChar;

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}")) continue;
            var text = File.ReadAllText(file);
            foreach (var token in banned)
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(featuresRoot, file)} ({token})");
        }

        Assert.AreEqual(0, offenders.Count,
            "Features must not touch the UI dispatcher singletons — use IShellServices.RunOnUiAsync / " +
            $"WatchFile. Offenders: {string.Join("; ", offenders)}");
    }
}
