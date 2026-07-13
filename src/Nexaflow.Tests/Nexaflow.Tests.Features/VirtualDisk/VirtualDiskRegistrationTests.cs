using Nexaflow.Features.VirtualDisk;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>The page-kind contract for the "As Disk" inspector registration.</summary>
[TestClass]
[CoversNode("virtualdisk")]
public class VirtualDiskRegistrationTests
{
    [TestMethod]
    public void StaticPageKind_is_stable_and_matches_the_instance_contract()
    {
        Assert.AreEqual("VirtualDisk", VirtualDiskTabRegistration.StaticPageKind);
    }
}
