using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// A save to a real distro's file named by the Windows folder that shows it (<c>\\wsl.localhost\…</c>) — as an editor that
/// opened it by that path saves it — keeps what Linux keeps: a script stays runnable, and its owner and group stay. The
/// files are the test's own, under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("io-locations-wsl")]
public sealed class WslNativeSaveTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ASaveByTheWindowsFolder_KeepsAScriptRunnable()
    {
        var distro   = WslUnderTest.Distro();
        var provider = new WslLocationProvider();
        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([WslLocationProvider.ConnectionFor(distro)]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var name   = $"nexaflow-native-save-{Guid.NewGuid():N}";
        var folder = $@"{set.Mounts[0].Root}\tmp\{name}";
        await files.CreateDirectoryAsync(folder);
        try
        {
            await WslUnderTest.RunInDistroAsync(distro.Name, "sh", "-c", $"printf 'echo one\\n' > /tmp/{name}/run.sh && chmod 750 /tmp/{name}/run.sh");
            var before = (await files.GetEntryAsync($@"{folder}\run.sh"))!;
            var native = files.TryResolveReal($@"{folder}\run.sh")!;

            // As an editor saves: written whole beside the file, then put in its place.
            await using (var write = await files.BeginWriteAsync(native, WriteMode.Replace))
            {
                await write.Content.WriteAsync("echo two\n"u8.ToArray());
                await write.CommitAsync(CancellationToken.None);
            }

            var after = (await files.GetEntryAsync($@"{folder}\run.sh"))!;
            Assert.AreEqual("echo two\n", await files.ReadAllTextAsync($@"{folder}\run.sh"));
            Assert.AreEqual(before.Mode, after.Mode, "rwxr-x--- stays rwxr-x---");
            Assert.AreEqual(before.Owner, after.Owner);
            Assert.AreEqual(before.Group, after.Group);
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AFileFromWindowsCopiedOverAScript_ByTheWindowsFolder_KeepsItRunnable()
    {
        var distro   = WslUnderTest.Distro();
        var registry = new LocationProviderRegistry();
        registry.Register(new WslLocationProvider());
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([WslLocationProvider.ConnectionFor(distro)]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var name   = $"nexaflow-native-copy-{Guid.NewGuid():N}";
        var folder = $@"{set.Mounts[0].Root}\tmp\{name}";
        await files.CreateDirectoryAsync(folder);
        try
        {
            await WslUnderTest.RunInDistroAsync(distro.Name, "sh", "-c", $"printf 'echo one\\n' > /tmp/{name}/run.sh && chmod 750 /tmp/{name}/run.sh");
            var before = (await files.GetEntryAsync($@"{folder}\run.sh"))!;
            var source = Path.Combine(TestTemp.CreateSubdirectory("nexa-native-copy-").FullName, "run.sh");
            await File.WriteAllTextAsync(source, "echo two\n");

            await FileTransferEngine.RunAsync(new FileTransferRequest(TransferKind.Copy,
                [new TransferItem(source, files.TryResolveReal($@"{folder}\run.sh")!)], ConflictPolicy.Overwrite, false) { FileSystem = files });

            var after = (await files.GetEntryAsync($@"{folder}\run.sh"))!;
            Assert.AreEqual("echo two\n", await files.ReadAllTextAsync($@"{folder}\run.sh"));
            Assert.AreEqual(before.Mode, after.Mode, "the replaced script's permissions carry over");
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
