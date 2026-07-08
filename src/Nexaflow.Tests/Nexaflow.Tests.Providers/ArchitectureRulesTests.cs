using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Mechanical enforcement of the provider layering rule (CLAUDE.md "Hard Rules"): providers depend
/// only on Providers.Common — never on Core, never on each other. Until these tests the rule held
/// purely by discipline.
/// </summary>
[TestClass]
public class ArchitectureRulesTests
{
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
            $"Providers depend only on Providers.Common, never Core. Offenders: {string.Join(", ", offenders)}");
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
                    r.Name != "Nexaflow.Providers.Common")
                    offenders.Add($"{name} → {r.Name}");
        }

        Assert.AreEqual(0, offenders.Count,
            $"Providers may reference only Providers.Common, never each other. Offenders: {string.Join(", ", offenders)}");
    }
}
