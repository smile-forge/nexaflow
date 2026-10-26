using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ThisPc;

/// <summary>
/// The volumes of this computer as This PC's rows: listed at once by their letters, browsed at their own roots, and
/// described — label, capacity, file system, solid-state — only once each has answered, because a drive that is not ready
/// blocks whoever asks it.
/// </summary>
[TestClass]
[CoversNode("winfs-thispc-drives")]
public class DriveThisPcProviderTests
{
    private static readonly DriveThisPcProvider Provider = new(VirtualFileSystem.Instance);

    [TestMethod]
    public void EveryVolumeIsARowBrowsedAtItsOwnRoot()
    {
        var items = Provider.GetItems();

        CollectionAssert.AreEqual(DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName).ToArray(),
                                  items.Select(i => i.TargetPath).ToArray());
        Assert.IsTrue(items.All(i => i.Backing == ThisPcItemBacking.OwnPath), "a volume is never mounted under ::{id}");
        Assert.AreEqual(items.Count, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(items.All(i => i.Id.StartsWith("drive.", StringComparison.Ordinal) && i.Id.IndexOfAny(['\\', '/', ':']) < 0),
                      "an id is one path segment");
    }

    [TestMethod]
    public void AVolumeIsNamedByItsLetterUntilItHasAnswered()
    {
        // Reading the volume label can block on the hardware, so listing never does.
        CollectionAssert.AreEqual(DriveInfo.GetDrives().Select(d => d.Name).ToArray(),
                                  Provider.GetItems().Select(i => i.Label).ToArray());
    }

    [TestMethod]
    public void AVolumeIsNeverACloudAndIsListedAsADiskUntilItHasAnswered()
    {
        foreach (var item in Provider.GetItems())
            Assert.IsFalse(item.Icon is ThisPcItemIcon.Cloud or ThisPcItemIcon.SolidState, item.TargetPath);
    }

    [TestMethod]
    public async Task AVolumeThatAnsweredIsDescribedByItsLabelCapacityAndFileSystem()
    {
        var drive = DriveInfo.GetDrives().First(d => d.IsReady);
        var item = Provider.GetItems().Single(i => i.TargetPath == drive.RootDirectory.FullName);

        var detail = await Provider.GetDetailAsync(item);

        Assert.IsTrue(detail.Available);
        StringAssert.Contains(detail.Label, drive.Name.TrimEnd('\\'));
        Assert.IsTrue(detail.TotalBytes > 0 && detail.UsedBytes <= detail.TotalBytes);
        Assert.AreEqual(drive.DriveFormat, detail.FileSystem);
    }

    [TestMethod]
    public async Task AVolumeThatIsNotThereIsUnavailable()
    {
        var taken = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free  = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !taken.Contains(c));
        var gone  = new ThisPcItem { Id = $"drive.{free}", Label = $@"{free}:\", TargetPath = $@"{free}:\", TypeLabel = "Local Disk",
                                     Backing = ThisPcItemBacking.OwnPath, Icon = ThisPcItemIcon.Disk };

        Assert.IsFalse((await Provider.GetDetailAsync(gone)).Available);
    }
}
