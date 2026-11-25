using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Localization;
using NSubstitute;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Deleting a picture in a real WSL distro, which has no Recycle Bin: the question says it is for good, and the picture
/// goes through the distro — whether the tab holds it by the distro's path or by the Windows folder showing it. The
/// pictures are the test's own, under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("images-delete")]
public sealed class ImageDeleteOnADistroTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [DataRow(false, DisplayName = "held by the distro's path")]
    [DataRow(true, DisplayName = "held by the Windows folder showing it")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ADistrosPicture_IsAskedForAsForGood_ThenDeletedThroughTheDistro(bool byWindowsFolder)
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

        var folder  = $@"{set.Mounts[0].Root}\tmp\nexaflow-image-delete-{Guid.NewGuid():N}";
        var picture = $@"{folder}\photo.png";
        await files.CreateDirectoryAsync(folder);
        try
        {
            await files.WriteAllBytesAsync(picture, await File.ReadAllBytesAsync(TestSampleData.Files("images")[0]));
            await files.WriteAllTextAsync($@"{folder}\keep.png", "a neighbour");
            var shell = Substitute.For<IShellServices>();
            string? asked = null;
            shell.ConfirmAsync(Arg.Any<string>(), Arg.Do<string>(message => asked = message)).Returns(true);
            var held = byWindowsFolder ? files.TryResolveReal(picture)! : picture;
            using var vm = new ImageViewModel([held], shell, ImageViewMode.Carousel, new ImageEditingServices(files));

            await vm.DeleteCommand.ExecuteAsync(0);

            Assert.AreEqual(Str.Format("Images.Delete.ConfirmForGoodFormat", "photo.png"), asked, "the question says it is for good");
            Assert.IsFalse(await files.ExistsAsync(picture), "the picture is gone from the distro");
            Assert.IsTrue(await files.ExistsAsync($@"{folder}\keep.png"));
            Assert.AreEqual(0, vm.TotalImages);
            shell.DidNotReceiveWithAnyArgs().ShowError(default!);
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
