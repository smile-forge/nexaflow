using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Each feature assembly is taken into the session as one unit holding everything it put in, so a feature can be told apart from
/// the rest of the session and withdrawn as a whole. Every way a feature puts something into the session is recorded on its unit.
/// </summary>
[TestClass]
[CoversNode("feature-startup")]
public class FeatureActivationTests
{
    [TestMethod]
    public void EverythingAFeaturePutsIn_IsRecordedOnItsOwnUnit()
    {
        var features = TestRuntimes.Features;
        features.Catalog.EnsureAllActivated();
        var units = features.Activation.Activated;

        Assert.IsTrue(units.Any(u => u.Configs.Count > 0), "a global config is recorded");
        Assert.IsTrue(units.Any(u => u.ArchiveHandlers.Count > 0), "an archive handler is recorded");
        Assert.IsTrue(units.Any(u => u.StreamCodecs.Count > 0), "a stream codec is recorded");
        Assert.IsTrue(units.Any(u => u.ThemeResources.Count > 0), "a theme resource is recorded");
        Assert.IsTrue(units.Any(u => u.Startups.Any(s => s is WindowsFileSystemStartup)), "a feature's own setup is recorded");

        foreach (var unit in units)
        {
            Assert.IsTrue(unit.Configs.All(c => c.Assembly == unit.Assembly), $"{unit.File} holds another feature's config");
            Assert.IsTrue(unit.ArchiveHandlers.All(h => h.GetType().Assembly == unit.Assembly), $"{unit.File} holds another feature's handler");
            Assert.IsTrue(unit.StreamCodecs.All(c => c.GetType().Assembly == unit.Assembly), $"{unit.File} holds another feature's codec");
            Assert.IsTrue(unit.Startups.All(s => s.GetType().Assembly == unit.Assembly), $"{unit.File} holds another feature's setup");
        }
    }

    [TestMethod]
    public void AFeatureIsTakenInOnce()
    {
        var features = TestRuntimes.Features;
        features.Catalog.ActivateAssemblyOwning(typeof(WindowsFileSystemStartup).FullName!);
        features.Catalog.ActivateAssemblyOwning(typeof(WindowsFileSystemStartup).FullName!);

        Assert.AreEqual(1, features.Activation.Activated.Count(u => u.Assembly == typeof(WindowsFileSystemStartup).Assembly));
    }
}
