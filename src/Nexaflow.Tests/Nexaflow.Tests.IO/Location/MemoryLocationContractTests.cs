using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// The provider contract against the memory host, at the round trips a remote location really has. Latency is a
/// test input: the same tests at 80 ms and 250 ms show a change that multiplies round trips as time, in CI rather
/// than on someone's VPN.
/// </summary>
internal static class MemoryLocationUnderTest
{
    public static LocationUnderTest At(TimeSpan latency)
    {
        var host = new MemoryLocationHost("nas", latency, password: "s3cret");
        var profile = new ConnectionProfile("memory-nas", "memory", "NAS", "nas") { User = "adam" };
        return new LocationUnderTest(new MemoryLocationProvider(host), profile, new StoredCredentials("s3cret"), host.Drop);
    }
}

[TestClass]
[CoversNode("io-locations-contract")]
public sealed class MemoryLocationContractTests : LocationSessionContract
{
    protected override LocationUnderTest Connect() => MemoryLocationUnderTest.At(TimeSpan.Zero);
}

[TestClass]
[CoversNode("io-locations-contract")]
public sealed class MemoryLocationAt80MsContractTests : LocationSessionContract
{
    protected override LocationUnderTest Connect() => MemoryLocationUnderTest.At(TimeSpan.FromMilliseconds(80));
}

[TestClass]
[CoversNode("io-locations-contract")]
public sealed class MemoryLocationAt250MsContractTests : LocationSessionContract
{
    protected override LocationUnderTest Connect() => MemoryLocationUnderTest.At(TimeSpan.FromMilliseconds(250));
}

/// <summary>What a provider does with what it is given to sign in.</summary>
[TestClass]
[CoversNode("io-locations-contract")]
public sealed class LocationSignInTests
{
    [TestMethod]
    public async Task AWrongPasswordIsAuthFailed()
    {
        var host    = new MemoryLocationHost("nas", TimeSpan.Zero, password: "s3cret");
        var profile = new ConnectionProfile("memory-nas", "memory", "NAS", "nas") { User = "adam" };

        try
        {
            await new MemoryLocationProvider(host).ConnectAsync(profile, new StoredCredentials("guess"), CancellationToken.None);
            Assert.Fail("Expected the sign-in to fail.");
        }
        catch (LocationException e)
        {
            Assert.AreEqual(LocationError.AuthFailed, e.Error);
        }
    }

    [TestMethod]
    public void AProfileHoldsNoSecretOnlyWhereToFindOne()
    {
        var profile = new ConnectionProfile("sftp-nas", "sftp", "NAS", "nas") { User = "adam" };

        Assert.AreEqual("nexaflow:sftp-nas", profile.CredentialTargetFor(SecretKind.Password));
        Assert.AreEqual("nexaflow:sftp-nas:keypassphrase", profile.CredentialTargetFor(SecretKind.KeyPassphrase),
                        "each kind of secret has a target of its own");
    }

    [TestMethod]
    public void TwoProfilesThatDifferOnlyInAnOptionAreDifferentEndpoints()
    {
        var a = new ConnectionProfile("sftp-nas", "sftp", "NAS", "nas") { Options = new Dictionary<string, string> { ["key"] = "a" } };
        var b = new ConnectionProfile("sftp-nas", "sftp", "NAS", "nas") { Options = new Dictionary<string, string> { ["key"] = "b" } };
        var c = new ConnectionProfile("sftp-nas", "sftp", "NAS", "nas") { Options = new Dictionary<string, string> { ["key"] = "a" } };

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(a, c);
        Assert.AreEqual(a.GetHashCode(), c.GetHashCode());
    }
}
