using System.Reflection;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Every custom Options page that saves says when it has something to save. Options cannot tell otherwise, so it
/// counts such a page as always changed, and closing the panel asks to save every time — even when nothing was
/// touched.
/// </summary>
[TestClass]
[NoCoverage("repo guard over the custom controls Options hosts, not a product behaviour")]
public class CustomOptionsControlGuardTests
{
    [TestMethod]
    public void EveryCustomPageThatSaves_TracksItsChanges()
    {
        var problems = ConfigLabelGuardTests.ConfigTypes()
            .Select(CustomControlType)
            .OfType<Type>()
            .Distinct()
            .Where(Saves)
            .Where(c => !typeof(Nexaflow.Features.Common.IConfigChangeTracker).IsAssignableFrom(c))
            .Select(c => $"{c.FullName}: implements ICustomConfigApply without IConfigChangeTracker")
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>Nothing found is not nothing wrong: the scan reaches custom pages in Core and in the features.</summary>
    [TestMethod]
    public void TheScan_ReachesCoreAndFeatureCustomPages()
    {
        var controls = ConfigLabelGuardTests.ConfigTypes().Select(CustomControlType).OfType<Type>().ToList();

        Assert.IsTrue(controls.Any(c => c.Assembly == typeof(Nexaflow.Core.SecurityConfig).Assembly), "no Core custom page found");
        Assert.IsTrue(controls.Any(c => c.Assembly.GetName().Name!.StartsWith("Nexaflow.Features.", StringComparison.Ordinal)),
                      "no feature custom page found");
    }

    private static Type? CustomControlType(Type config)
        => config.GetCustomAttribute<Nexaflow.Features.Common.CustomControlAttribute>()?.ControlType
        ?? config.GetCustomAttribute<Nexaflow.Providers.Common.CustomControlAttribute>()?.ControlType;

    private static bool Saves(Type control)
        => typeof(Nexaflow.Features.Common.ICustomConfigApply).IsAssignableFrom(control)
        || typeof(Nexaflow.Providers.Common.ICustomConfigApply).IsAssignableFrom(control);
}
