using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>Hive-root identity and token resolution. Touches only the read-only base-key singletons.</summary>
[TestClass]
[CoversNode("registry-hive-dropdown")]
public class RegistryRootTests
{
    [TestMethod]
    public void FromToken_ResolvesEachHive_CaseInsensitively()
    {
        Assert.AreSame(RegistryRoot.CurrentUser,  RegistryRoot.FromToken("HKCU"));
        Assert.AreSame(RegistryRoot.LocalMachine, RegistryRoot.FromToken("hklm"));
        Assert.AreSame(RegistryRoot.ClassesRoot,  RegistryRoot.FromToken("HkCr"));
    }

    [TestMethod]
    public void FromToken_UnknownOrNull_ReturnsNull()
    {
        Assert.IsNull(RegistryRoot.FromToken("HKEY_BOGUS"));
        Assert.IsNull(RegistryRoot.FromToken(null));
    }

    [TestMethod]
    public void CurrentUser_HasTokenAndDisplayName()
    {
        Assert.AreEqual("HKCU", RegistryRoot.CurrentUser.Token);
        Assert.AreEqual("HKEY_CURRENT_USER", RegistryRoot.CurrentUser.DisplayName);
        Assert.AreEqual("HKEY_CURRENT_USER", RegistryRoot.CurrentUser.ToString());
    }

    [TestMethod]
    public void All_ListsThreeHives_InDropdownOrder()
    {
        CollectionAssert.AreEqual(
            new[] { RegistryRoot.CurrentUser, RegistryRoot.LocalMachine, RegistryRoot.ClassesRoot },
            RegistryRoot.All.ToArray());
    }
}
