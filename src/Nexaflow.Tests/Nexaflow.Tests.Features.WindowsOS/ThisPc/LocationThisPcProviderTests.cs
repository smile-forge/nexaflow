using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ThisPc;

/// <summary>
/// The runtime's locations in This PC: a row per mounted location opening at its own root, one per connection that could
/// not be mounted saying why, and nothing that would touch a location to draw them.
/// </summary>
[TestClass]
[CoversNode("locations-this-pc")]
public sealed class LocationThisPcProviderTests
{
    private sealed class Locations : ILocationDirectory
    {
        public IReadOnlyList<MountedLocation> Mounted { get; set; } = [];

        public IReadOnlyList<UnreachableLocation> Unreachable { get; set; } = [];

        public event Action? Changed;

        public void Change() => Changed?.Invoke();
    }

    private static MountedLocation Distro(string name)
        => new($"wsl-{name.ToLowerInvariant()}", name, "wsl", $"::wsl-{name.ToLowerInvariant()}",
               LocationCapabilities.Shell | LocationCapabilities.SameMachine, $"wsl://{name}/");

    [TestMethod]
    public void AMountedLocationIsARow_OpeningAtItsRoot()
    {
        var provider = new LocationThisPcProvider(new Locations { Mounted = [Distro("Ubuntu")] });

        var row = provider.GetItems().Single();

        Assert.AreEqual("::wsl-ubuntu", row.TargetPath);
        Assert.AreEqual("Ubuntu", row.Label);
        Assert.AreEqual(ThisPcItemBacking.Virtual, row.Backing, "a location is served by the file system, not mounted from a folder");
        Assert.AreEqual(ThisPcItemIcon.Disk, row.Icon, "a distro is on this PC");
    }

    [TestMethod]
    public void AServerElsewhereIsANetworkRow()
    {
        var nas = new MountedLocation("sftp-nas", "NAS", "sftp", "::sftp-nas", LocationCapabilities.Shell, "sftp://nas/");
        Assert.AreEqual(ThisPcItemIcon.Network, new LocationThisPcProvider(new Locations { Mounted = [nas] }).GetItems().Single().Icon);
    }

    [TestMethod]
    public async Task AMountedLocationIsThere_WithoutBeingTouched()
    {
        var provider = new LocationThisPcProvider(new Locations { Mounted = [Distro("Ubuntu")] });

        var detail = await provider.GetDetailAsync(provider.GetItems().Single());

        Assert.IsTrue(detail.Available);
        Assert.IsTrue(detail.HasChildren);
    }

    [TestMethod]
    public async Task AConnectionThatCouldNotBeMounted_IsListedUnavailable_SayingWhy()
    {
        var gone = new ConnectionProfile("ftp-old", "ftp", "Old FTP", "old");
        var provider = new LocationThisPcProvider(new Locations { Unreachable = [new UnreachableLocation(gone, "No backend reaches 'ftp' locations.")] });

        var row    = provider.GetItems().Single();
        var detail = await provider.GetDetailAsync(row);

        Assert.AreEqual("::ftp-old", row.TargetPath);
        Assert.IsFalse(detail.Available);
        StringAssert.Contains(detail.Label, "No backend reaches");
    }

    [TestMethod]
    public void TheRowsFollowTheRuntime()
    {
        var directory = new Locations();
        var provider  = new LocationThisPcProvider(directory);
        bool told = false;
        provider.Changed += () => told = true;

        directory.Mounted = [Distro("Debian")];
        directory.Change();

        Assert.IsTrue(told);
        Assert.AreEqual("Debian", provider.GetItems().Single().Label);
    }
}
