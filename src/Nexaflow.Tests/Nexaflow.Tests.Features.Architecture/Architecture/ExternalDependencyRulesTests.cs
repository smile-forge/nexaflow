using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Mechanical rules for <see cref="IExternalDependency"/> declarations.
/// <para>
/// The registry is deliberately forgiving at runtime — a declaration it cannot build, or whose probe throws,
/// is skipped so one bad feature cannot blank the whole About page. That tolerance is right for users and
/// wrong for us: a declaration silently dropped in production is a component the user is never told about.
/// These turn each of those silent skips into a build failure instead.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class ExternalDependencyRulesTests
{
    /// <summary>Every declaration shipped by a feature assembly deployed next to the test exe.</summary>
    private static IReadOnlyList<Type> Declarations()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
                    .Select(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)))
                    .SelectMany(a => a.GetTypes())
                    .Where(t => typeof(IExternalDependency).IsAssignableFrom(t)
                             && t is { IsAbstract: false, IsInterface: false })
                    .ToList();

    [TestMethod]
    public void Every_declaration_has_a_public_parameterless_constructor()
    {
        // How the registry builds them. Without one it is skipped at runtime with no diagnostic at all.
        var offenders = Declarations()
            .Where(t => t.GetConstructor(Type.EmptyTypes) is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"IExternalDependency implementations need a public parameterless constructor: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Every_declaration_has_a_usable_id_and_display_name()
    {
        var offenders = new List<string>();

        foreach (var type in Declarations())
        {
            var decl = (IExternalDependency)Activator.CreateInstance(type)!;

            // A blank id is dropped by the registry; a blank name renders an unlabelled row on About.
            if (string.IsNullOrWhiteSpace(decl.Id))          offenders.Add($"{type.FullName}: blank Id");
            if (string.IsNullOrWhiteSpace(decl.DisplayName)) offenders.Add($"{type.FullName}: blank DisplayName");
            if (string.IsNullOrWhiteSpace(decl.Description)) offenders.Add($"{type.FullName}: blank Description");
        }

        Assert.AreEqual(0, offenders.Count, string.Join("; ", offenders));
    }

    [TestMethod]
    public void Declarations_sharing_an_id_agree_on_what_it_is()
    {
        // Two features may declare the same component — WebView2 is declared by both the PDF reader and the
        // Web tab. The registry keeps whichever it meets first, so if their wording differs the About page
        // reads differently depending on assembly load order.
        var offenders = Declarations()
            .Select(t => (IExternalDependency)Activator.CreateInstance(t)!)
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(d => d.DisplayName).Distinct().Count() > 1
                     || g.Select(d => d.Description).Distinct().Count() > 1
                     || g.Select(d => d.InstallUrl).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            $"declarations sharing an id must present it identically: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Every_probe_answers_rather_than_throwing()
    {
        // The registry catches a throwing probe and reports Unknown, which is the right thing to show a user
        // and the wrong thing to ship: Unknown means "we could not tell", so a component that is genuinely
        // missing would never be flagged.
        var offenders = new List<string>();

        foreach (var type in Declarations())
        {
            var decl = (IExternalDependency)Activator.CreateInstance(type)!;
            try
            {
                Assert.IsNotNull(decl.Probe(), $"{type.FullName}: Probe returned null");
            }
            catch (Exception ex) when (ex is not AssertFailedException)
            {
                offenders.Add($"{type.FullName}: {ex.GetType().Name} {ex.Message}");
            }
        }

        Assert.AreEqual(0, offenders.Count, string.Join("; ", offenders));
    }
}
