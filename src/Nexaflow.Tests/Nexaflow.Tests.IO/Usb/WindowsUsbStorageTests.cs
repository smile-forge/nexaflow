using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// USB disks and volumes against this machine. It asks the storage stack and the volume manager only — nothing is
/// read from any media — so it is safe anywhere; inconclusive with no USB disk plugged in.
/// </summary>
[TestClass]
[CoversNode("io-usb-storage")]
public class WindowsUsbStorageTests
{
    [TestMethod]
    public async Task EveryUsbDisk_IsFound_WithItsVolumes()
    {
        using var inventory = new WindowsUsbInventory(null);
        var snapshot = await inventory.RefreshAsync(CancellationToken.None);
        var storage = new WindowsUsbStorage();

        var disks = snapshot.Devices.SelectMany(d => d.Functions).SelectMany(storage.DisksOf).ToList();
        foreach (var function in snapshot.Devices.SelectMany(d => d.Functions))
            if (!function.Children.Any(c => string.Equals(c.ClassName, "DiskDrive", StringComparison.OrdinalIgnoreCase)))
                Assert.AreEqual(0, storage.DisksOf(function).Count, $"{function.InstanceId} is no disk and presents none");
        if (disks.Count == 0) Assert.Inconclusive("no USB disk plugged in");

        foreach (var disk in disks)
        {
            Assert.IsNull(disk.Error, $"{disk.InstanceId}: {disk.Error}");
            Assert.IsNotNull(disk.DiskNumber, $"{disk.InstanceId} has no disk number");
            foreach (var volume in disk.Volumes.Where(v => v.Error is null))
            {
                StringAssert.StartsWith(volume.VolumeName, @"\\?\Volume{");
                Assert.IsTrue(volume.Capacity > 0 && volume.Free <= volume.Capacity, $"{volume.DisplayName}: {volume.Free} free of {volume.Capacity}");
                Assert.IsTrue(Directory.Exists(volume.Root), $"{volume.Root} can be browsed");
            }
        }
    }
}
