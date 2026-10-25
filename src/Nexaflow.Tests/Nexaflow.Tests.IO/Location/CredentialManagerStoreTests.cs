using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>A connection's secret in the signed-in user's Windows Credential Manager: stored, read back, replaced and
/// forgotten, under a target of the test's own so nothing of the person's is touched.</summary>
[TestClass]
[CoversNode("io-locations-connections")]
public sealed class CredentialManagerStoreTests
{
    private readonly string _target = $"nexaflow-tests:{Guid.NewGuid():N}";

    [TestCleanup]
    public void Cleanup() => CredentialManagerStore.Default.Delete(_target);

    [TestMethod]
    public void ASecretIsStoredReadBackReplacedAndForgotten()
    {
        var store = CredentialManagerStore.Default;
        Assert.IsNull(store.Read(_target), "nothing is stored under a new target");

        store.Write(_target, "adam", "first");
        Assert.AreEqual("first", store.Read(_target));

        store.Write(_target, "adam", "second");
        Assert.AreEqual("second", store.Read(_target), "writing again replaces it");

        Assert.IsTrue(store.Delete(_target));
        Assert.IsNull(store.Read(_target));
        Assert.IsFalse(store.Delete(_target), "there is nothing left to forget");
    }

    [TestMethod]
    public void ASecretLongerThanCredentialManagerHoldsIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CredentialManagerStore.Default.Write(_target, null, new string('x', 1281)));
        Assert.IsNull(CredentialManagerStore.Default.Read(_target));
    }
}
