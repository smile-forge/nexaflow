using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;
using System.IO;
using System.Reflection;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Rules specific to network discovery plugins (<c>Nexaflow.Features.Network.*</c>).
///
/// <para>
/// The general architecture tests already forbid a feature referencing another feature, which is what
/// forces the shared contracts into the <c>Nexaflow.IO.Network</c> leaf. These add the constraint that
/// makes plugins <i>stay</i> cheap: no WPF, ever. A probe that pulls in PresentationFramework can no longer
/// be exercised headlessly, and a discovery layer that cannot be tested without a desktop session will not
/// be tested.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class NetworkPluginRulesTests
{
    private static readonly string[] WpfAssemblies =
        ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml"];

    private static IEnumerable<Assembly> PluginAssemblies()
    {
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.Network.*.dll"))
        {
            Assembly asm;
            try { asm = Assembly.LoadFrom(dll); } catch { continue; }
            yield return asm;
        }
    }

    [TestMethod]
    public void Network_plugins_never_reference_WPF()
    {
        List<string> offenders = [];

        foreach (var asm in PluginAssemblies())
            foreach (var reference in asm.GetReferencedAssemblies())
                if (WpfAssemblies.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))
                    offenders.Add($"{asm.GetName().Name} -> {reference.Name}");

        Assert.AreEqual(0, offenders.Count,
            "A discovery plugin must stay headless so it can be tested without a desktop session. "
          + $"Offending references: {string.Join("; ", offenders)}");
    }

    [TestMethod]
    public void Every_network_plugin_declares_itself_with_a_Subfeature_attribute()
    {
        // Without the attribute the assembly ships, loads and contributes nothing — the silent-disappearance
        // failure the subfeature framework exists to prevent. Fail loudly at build time instead.
        List<string> undeclared = [];

        foreach (var asm in PluginAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            if (!types.Any(t => t.GetCustomAttribute<SubfeatureAttribute>() is not null))
                undeclared.Add(asm.GetName().Name ?? "<unknown>");
        }

        Assert.AreEqual(0, undeclared.Count,
            $"these plugin assemblies carry no [Subfeature] type and would never be discovered: "
          + string.Join(", ", undeclared));
    }

    [TestMethod]
    public void Subfeature_ids_are_unique_within_their_owner()
    {
        // The id keys the user's enable/disable choice and any persisted per-plugin settings. A duplicate
        // would silently share them between two plugins.
        List<(string Owner, string Id, string Type)> declared = [];

        foreach (var asm in PluginAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types)
                if (t.GetCustomAttribute<SubfeatureAttribute>() is { } sf)
                    declared.Add((sf.Owner, sf.Id, t.FullName ?? t.Name));
        }

        var dupes = declared
            .GroupBy(d => (d.Owner, d.Id))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Owner}/{g.Key.Id}: {string.Join(" and ", g.Select(x => x.Type))}")
            .ToList();

        Assert.AreEqual(0, dupes.Count, string.Join("; ", dupes));
    }

    [TestMethod]
    public void Every_declared_subfeature_describes_itself_for_the_user_and_the_model()
    {
        // The description is surfaced in the plugin list AND handed to the AI. An empty one means neither
        // can tell what enabling it would do.
        List<string> bare = [];

        foreach (var asm in PluginAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types)
                if (t.GetCustomAttribute<SubfeatureAttribute>() is { } sf
                    && string.IsNullOrWhiteSpace(sf.Description))
                    bare.Add($"{sf.Owner}/{sf.Id} ({t.Name})");
        }

        Assert.AreEqual(0, bare.Count, $"[Subfeature] needs a Description: {string.Join(", ", bare)}");
    }
}
