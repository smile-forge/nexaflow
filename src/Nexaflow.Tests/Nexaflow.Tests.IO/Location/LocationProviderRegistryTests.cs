using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>The process's location backends: one per kind, the first to arrive keeping it, found ignoring case.</summary>
[TestClass]
[CoversNode("io-locations-registry")]
public sealed class LocationProviderRegistryTests
{
    [TestMethod]
    public void TheFirstBackendForAKindKeepsIt_AndIsFoundIgnoringCase()
    {
        var registry = new LocationProviderRegistry();
        var first    = new MemoryLocationProvider();
        int changes  = 0;
        registry.Changed += () => changes++;

        Assert.IsTrue(registry.Register(first));
        Assert.IsFalse(registry.Register(new MemoryLocationProvider()), "a second backend for the kind is refused");

        Assert.IsTrue(registry.TryGet("MEMORY", out var found));
        Assert.AreSame(first, found);
        Assert.AreEqual(1, changes, "only an arrival is news");
        Assert.AreEqual(1, registry.Providers.Count);
    }

    [TestMethod]
    public void AKindNoBackendReachesIsNotFound()
    {
        Assert.IsFalse(new LocationProviderRegistry().TryGet("sftp", out var provider));
        Assert.IsNull(provider);
    }
}
