using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Rules an <see cref="IThisPcItemProvider"/> has to keep. They are cheap to break silently: a provider
/// whose constructor the DI cannot satisfy simply never appears, and one that throws or returns null from
/// <c>GetItems</c> would take This PC down with it — both look like "the feature isn't finished" rather
/// than a bug.
/// </summary>
[TestClass]
[NoCoverage("architecture guard — a rule about providers, not a feature behaviour")]
public class ThisPcProviderRulesTests
{
    private static IReadOnlyList<Type> ProviderTypes() =>
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Nexaflow.Features.", StringComparison.Ordinal) == true)
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IThisPcItemProvider).IsAssignableFrom(t))
    ];

    /// <summary>The parameter kinds the feature DI knows how to supply.</summary>
    private static bool IsResolvable(ParameterInfo p)
        => p.IsOptional
           || typeof(IFeatureConfig).IsAssignableFrom(p.ParameterType)
           || typeof(IShellServices).IsAssignableFrom(p.ParameterType)
           || typeof(IAIService).IsAssignableFrom(p.ParameterType)
           || p.ParameterType == typeof(IReadOnlyDictionary<Type, IFeatureConfig>)
           || p.ParameterType.Name == "WorkspaceRuntime";

    [TestMethod]
    public void EveryProviderHasAConstructorTheFeatureDiCanSatisfy()
    {
        var offenders = ProviderTypes()
            .Where(t => !t.GetConstructors().Any(c => c.GetParameters().All(IsResolvable)))
            .Select(t => t.FullName)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "these providers can never be built, so they would vanish from This PC without a word: "
            + string.Join(", ", offenders));
    }

    [TestMethod]
    public void EveryProviderCanListItsItemsWithoutThrowingOrReturningNull()
    {
        foreach (var type in ProviderTypes())
        {
            var ctor = type.GetConstructors()
                           .Where(c => c.GetParameters().All(p => p.IsOptional || typeof(IFeatureConfig).IsAssignableFrom(p.ParameterType)))
                           .OrderBy(c => c.GetParameters().Length)
                           .FirstOrDefault();
            if (ctor is null) continue;   // needs a shell; covered by the constructor rule above

            object?[] args = [.. ctor.GetParameters().Select(p =>
                p.IsOptional ? Type.Missing : Activator.CreateInstance(p.ParameterType))];

            object instance;
            try { instance = ctor.Invoke(args); }
            catch (Exception ex) { Assert.Fail($"{type.FullName} threw while being constructed: {ex.Message}"); return; }

            IReadOnlyList<ThisPcItem>? items = null;
            try { items = ((IThisPcItemProvider)instance).GetItems(); }
            catch (Exception ex)
            {
                Assert.Fail($"{type.FullName}.GetItems() threw ({ex.Message}). " +
                            "Nothing configured is the normal case and must return an empty list.");
            }

            Assert.IsNotNull(items, $"{type.FullName}.GetItems() returned null; return an empty list instead.");
        }
    }
}
