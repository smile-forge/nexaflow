using Nexaflow.Features.VirtualDisk;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>
/// Smoke test for the VirtualDisk stub registration. The feature is not yet implemented
/// (DiscUtils-backed disk mounting is pending specification), so there is no behaviour to cover —
/// this just holds the page-kind contract and satisfies the per-feature coverage guard.
/// </summary>
[TestClass]
[NoCoverage("VirtualDisk is an unimplemented stub — registration smoke only, no product node yet")]
public class VirtualDiskRegistrationTests
{
    [TestMethod]
    public void StaticPageKind_is_stable_and_matches_the_instance_contract()
    {
        Assert.AreEqual("VirtualDisk", VirtualDiskTabRegistration.StaticPageKind);
    }
}
