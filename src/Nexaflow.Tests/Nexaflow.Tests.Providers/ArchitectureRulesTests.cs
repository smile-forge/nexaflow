using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Mechanical enforcement of the provider layering rule (CLAUDE.md "Hard Rules"): providers depend only on the shared
/// provider libraries — never on Core, never on each other. Until these tests the rule held purely by discipline.
/// </summary>
[TestClass]
public class ArchitectureRulesTests
{
    /// <summary>The libraries providers share: the contracts, the OpenAI-compatible chat-completions wire format more than one
    /// backend speaks, and the contracts of providers whose models run on this machine. A provider may reference these and no
    /// other provider assembly.</summary>
    private static readonly HashSet<string> SharedLibraries =
        ["Nexaflow.Providers.Common", "Nexaflow.Providers.OpenAICompatible", LocalContracts];

    /// <summary>What a provider whose models run on this machine tells the app — its models, their files, what this machine
    /// cannot run. A cloud provider has none of it.</summary>
    private const string LocalContracts = "Nexaflow.Providers.Local";

    /// <summary>Whether an assembly reference is to local-model support: the local contracts, or the AI host's SDK.</summary>
    private static bool IsLocal(AssemblyName reference)
        => reference.Name == LocalContracts || reference.Name!.StartsWith("Nexaflow.AiHost.", StringComparison.Ordinal);

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

    [TestMethod]
    public void The_shared_contracts_hold_nothing_local()
    {
        // Every provider references Providers.Common, cloud ones included: a local type there would make each of them carry
        // local-model support a cloud-only user never runs.
        var offenders = ProviderAssemblies()
            .Where(a => a.GetName().Name is "Nexaflow.Providers.Common" or "Nexaflow.Providers.OpenAICompatible")
            .SelectMany(a => a.GetReferencedAssemblies().Where(IsLocal).Select(r => $"{a.GetName().Name} → {r.Name}"))
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"The contracts every provider shares reference nothing of local models. Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Only_a_provider_whose_models_run_here_references_local_model_support()
    {
        var offenders = ProviderAssemblies()
            .Where(a => !SharedLibraries.Contains(a.GetName().Name!))
            .Where(a => a.GetReferencedAssemblies().Any(IsLocal))
            .Where(a => !a.GetTypes().Any(t => !t.IsAbstract && typeof(ILocalModelCatalog).IsAssignableFrom(t)))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"A provider that references {LocalContracts} or the AI host's SDK offers models kept on this machine (ILocalModelCatalog); " +
            $"a cloud provider references neither. Offenders: {string.Join(", ", offenders)}");
    }
}
