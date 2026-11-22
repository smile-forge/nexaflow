using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// A distro's file handed to Windows — the clipboard, a drag-out, a shell verb, an external program — over a real distro:
/// it leaves by the <c>\\wsl.localhost</c> path Windows opens, a name Windows cannot hold included, and reads back as
/// written. The files are the test's own, under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("winfs-action-backing")]
public sealed class WslHandoverTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ADistrosFile_LeavesForWindowsByAPathWindowsOpens()
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

        var folder = $@"{set.Mounts[0].Root}\tmp\nexaflow-handover-{Guid.NewGuid():N}";
        await files.CreateDirectoryAsync(folder);
        try
        {
            var plain = $@"{folder}\notes.txt";
            var odd   = $@"{folder}\{LocationPath.EncodeSegment("a:b?.txt")}";
            await files.WriteAllTextAsync(plain, "plain");
            await files.WriteAllTextAsync(odd, "odd");

            var handed = await new ShellPath(files).RealizeAsync([plain, odd]);
            var verb   = await new ShellVerbAction(files, "open", "Open", "notepad.exe \"%1\"", "/").StartInfoAsync(plain);

            foreach (var path in handed) StringAssert.StartsWith(path, @"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual("plain", await File.ReadAllTextAsync(handed[0]), "Windows opens it by that path");
            Assert.AreEqual("odd", await File.ReadAllTextAsync(handed[1]), "a name Windows cannot hold too");
            Assert.AreEqual(handed[0], verb.FileName);
            Assert.AreEqual(plain, new ShellPath(files).RealForMutation(plain), "a change still goes through the distro");
            Assert.AreEqual(plain, new ShellPath(files).RealForMutation(handed[0]), "however the file came back, a cut pasted here included");
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
