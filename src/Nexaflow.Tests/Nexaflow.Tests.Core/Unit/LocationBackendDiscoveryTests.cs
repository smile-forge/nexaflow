using System;
using System.Linq;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A location backend is found by its assembly being there: the catalog sweeps <c>Nexaflow.IO.Location.*.dll</c> beside the
/// features, and its stamp covers them, so a backend dropped in is noticed as a feature is.
/// </summary>
[TestClass]
[CoversNode("io-locations-registry")]
public sealed class LocationBackendDiscoveryTests
{
    [TestMethod]
    public void TheCatalogFindsALocationBackendBesideTheFeatures()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });

        CollectionAssert.Contains(catalog.TypesImplementing<ILocationProvider>().ToList(), typeof(MemoryLocationProvider));
    }

    [TestMethod]
    public void TheCatalogsStampCoversTheLocationBackends()
    {
        var stamp = FeatureCatalogStamp.Capture(AppContext.BaseDirectory, "Nexaflow.Core.dll");

        Assert.IsTrue(stamp.Any(s => string.Equals(s.Name, "Nexaflow.IO.Location.Memory.dll", StringComparison.OrdinalIgnoreCase)),
                      "a backend rebuilt or dropped in must invalidate the cached index");
    }
}
