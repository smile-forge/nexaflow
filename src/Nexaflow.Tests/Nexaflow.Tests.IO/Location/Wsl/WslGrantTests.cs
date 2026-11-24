using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// The assistant's reach in a real distro, granted by the Windows folder that shows it: <c>proj</c> and <c>PROJ</c> are two
/// folders in Linux, both readable through <c>\\wsl.localhost</c>, and a grant on one holds nothing of the other. The
/// folders are the test's own, under the distro's <c>/tmp</c>, and are removed.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("ai-context-paths")]
public sealed class WslGrantTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AGrantOnAFolder_HoldsNothingOfAFolderThatDiffersOnlyByCase()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var folder = $@"{set.Mounts[0].Root}\tmp\nexaflow-grant-{Guid.NewGuid():N}";
        try
        {
            await files.WriteAllTextAsync($@"{folder}\proj\notes.txt", "granted");
            await files.WriteAllTextAsync($@"{folder}\PROJ\notes.txt", "not granted");
            var granted = files.TryResolveReal($@"{folder}\proj")!;
            var other   = files.TryResolveReal($@"{folder}\PROJ")!;
            Assert.AreEqual("not granted", await File.ReadAllTextAsync($@"{other}\notes.txt"), "Windows reaches both folders");

            var scope = new PathScope([], [granted], files.GetNameComparison);

            Assert.AreEqual("granted", await File.ReadAllTextAsync(scope.Resolve($@"{granted}\notes.txt")!));
            Assert.IsNull(scope.Resolve($@"{other}\notes.txt"));
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
