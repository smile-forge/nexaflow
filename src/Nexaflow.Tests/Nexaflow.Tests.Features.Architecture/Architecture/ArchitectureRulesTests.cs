using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Mechanical enforcement of the layering "Hard Rules" (CLAUDE.md). Nothing in MSBuild stops a
/// feature from referencing Core or a sibling feature, and nothing stops a stray
/// <c>Application.Current.Dispatcher</c> — until these tests, the rules held purely by discipline.
/// A violation is now a red build instead of a review catch.
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
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

    /// <summary>
    /// A location backend (<c>Nexaflow.IO.Location.&lt;Kind&gt;</c>) is a leaf over <c>IO.Common</c>, as the codec backends
    /// are: the registry loads it, and nothing of the app's — Core, a feature, the visuals, another backend — is anything it
    /// may know. Its packages (an SSH client, an SMB one) are its own business. A backend joins the check by being referenced
    /// from this project; one in the tree that is not is itself a failure, so the rule cannot narrow by being forgotten, and
    /// with none in the tree it says that nothing was checked rather than passing.
    /// </summary>
    [TestMethod]
    public void Location_backends_reference_only_IO_Common()
    {
        var inTree = Directory.GetDirectories(Path.Combine(RepoRoot.Locate(), "src"), "Nexaflow.IO.Location.*")
                              .Select(Path.GetFileName)
                              .ToList();
        var backends = Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.IO.Location.*.dll")
                                .Select(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)))
                                .ToList();

        var unseen = inTree.Except(backends.Select(a => a.GetName().Name)).ToList();
        Assert.AreEqual(0, unseen.Count,
            "Every location backend is referenced from Tests.Features.Architecture, so this rule sees it. " +
            $"Not referenced: {string.Join(", ", unseen)}");

        if (backends.Count == 0)
            Assert.Inconclusive("No Nexaflow.IO.Location.* backend exists yet, so nothing was checked.");

        var offenders = backends
            .SelectMany(a => a.GetReferencedAssemblies()
                              .Where(r => r.Name!.StartsWith("Nexaflow.", StringComparison.Ordinal) && r.Name != "Nexaflow.IO.Common")
                              .Select(r => $"{a.GetName().Name} → {r.Name}"))
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "A location backend references IO.Common and its own packages, nothing else of Nexaflow's. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }
}
