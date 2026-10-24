using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Mechanical enforcement of the provider layering rule (CLAUDE.md "Hard Rules"): providers depend only on the shared
/// provider libraries — never on Core, never on each other. Until these tests the rule held purely by discipline.
/// </summary>
[TestClass]
public class ArchitectureRulesTests
{
    /// <summary>The libraries providers share: the contracts, and the OpenAI-compatible chat-completions wire format more
    /// than one backend speaks. A provider may reference these and no other provider assembly.</summary>
    private static readonly HashSet<string> SharedLibraries =
        ["Nexaflow.Providers.Common", "Nexaflow.Providers.OpenAICompatible"];

    private static IReadOnlyList<Assembly> ProviderAssemblies()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Providers.*.dll")
                    .Select(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)))
                    .ToList();

    [TestMethod]
    public void Providers_never_reference_Core()
    {
        var offenders = ProviderAssemblies()
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "Nexaflow.Core"))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"Providers depend only on the shared provider libraries, never Core. Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Providers_never_reference_another_provider()
    {
        var offenders = new List<string>();
        foreach (var asm in ProviderAssemblies())
        {
            var name = asm.GetName().Name!;
            foreach (var r in asm.GetReferencedAssemblies())
                if (r.Name!.StartsWith("Nexaflow.Providers.", StringComparison.Ordinal) &&
                    !SharedLibraries.Contains(r.Name))
                    offenders.Add($"{name} → {r.Name}");
        }

        Assert.AreEqual(0, offenders.Count,
            $"Providers may reference only the shared provider libraries ({string.Join(", ", SharedLibraries)}), never each other. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Shared_libraries_declare_no_provider()
    {
        // The app loads every Nexaflow.Providers.*.dll and offers each concrete provider and config it finds there: a
        // shared library's must stay abstract, or it would be offered as a backend of its own.
        var offenders = ProviderAssemblies()
            .Where(a => SharedLibraries.Contains(a.GetName().Name!))
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface &&
                        (ModelKinds.Of(t).Count > 0 || typeof(IProviderConfig).IsAssignableFrom(t)))
            .Select(t => t.FullName)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"A shared provider library declares no concrete provider or config. Offenders: {string.Join(", ", offenders)}");
    }
}
