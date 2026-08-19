using System;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Rows contributed to "This PC" by an <c>IThisPcItemProvider</c>. They are shaped like drive rows on
/// purpose — same entry type, same Loading→Ready settle — so the list, the columns and the context menu
/// need no special case.
/// </summary>
/// <remarks>
/// Serialized: a mount is process-wide (every window browses the same namespace), so two fixtures alive
/// at once would each see the other's location in This PC. That is correct product behaviour and a
/// hopeless basis for parallel assertions.
/// </remarks>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-thispc-providers")]
public class ThisPcProvidedRowsTests
{
    /// <summary>The settle runs off the UI thread (as CheckDriveAsync does), so poll rather than assume.</summary>
    private static void WaitFor(Func<bool> condition, string because)
    {
        for (int i = 0; i < 200 && !condition(); i++) Thread.Sleep(10);
        Assert.IsTrue(condition(), because);
    }

    /// <summary>This fixture's row, by identity rather than position.</summary>
    private static FileSystemEntry Row(ProvidedRootFixture fx, FileSystemViewModel vm)
        => vm.Entries.Single(e => e.ProviderId == fx.MountId);

    [TestMethod]
    public void AProvidedLocationAppearsAfterTheRealDrives()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        var driveCount = DriveInfo.GetDrives().Length;
        Assert.AreEqual(driveCount + 1, vm.Entries.Count);
        Assert.AreEqual(fx.Label, vm.Entries[^1].Name, "provided rows follow the drives");
    }

    [TestMethod]
    public void AProvidedRowCarriesItsProviderIdTypeAndCloudIcon()
    {
        using var fx = new ProvidedRootFixture();
        var row = Row(fx, fx.ThisPc());

        Assert.AreEqual(fx.MountId, row.ProviderId);
        Assert.AreEqual("Test Cloud", row.DriveKindLabel);
        Assert.AreEqual("Test Cloud", row.TypeLabel);
        Assert.AreEqual(DriveIconType.Cloud, row.DriveIconType);
        Assert.IsTrue(row.IsThisPcItem, "it is a top-level This PC row, like a drive");
        Assert.IsTrue(row.IsDirectory);
    }

    [TestMethod]
    public void AProvidedRowPointsAtItsVirtualRootNotTheRealFolder()
    {
        using var fx = new ProvidedRootFixture();
        var row = Row(fx, fx.ThisPc());

        Assert.AreEqual(fx.VirtualRoot, row.FullPath);
        Assert.IsFalse(row.FullPath.Contains(fx.RealRoot, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AProvidedRowSettlesReadyWhenItsLocationIsThere()
    {
        using var fx = new ProvidedRootFixture();
        var row = Row(fx, fx.ThisPc());

        WaitFor(() => row.DriveStatus == DriveStatus.Ready, "an existing location settles Ready");
    }

    [TestMethod]
    public void AProvidedRowSettlesUnavailableWhenItsLocationHasGone()
    {
        using var fx = new ProvidedRootFixture();
        Directory.Delete(fx.RealRoot, recursive: true);

        var row = Row(fx, fx.ThisPc());

        WaitFor(() => row.DriveStatus == DriveStatus.Unavailable,
                "a sync folder that has gone shows the unavailable badge rather than pretending to work");
    }

    [TestMethod]
    public void AProvidedRowShowsNoSizeBecauseWeDoNotWalkTheTreeToInventOne()
    {
        using var fx = new ProvidedRootFixture();
        var row = Row(fx, fx.ThisPc());

        WaitFor(() => row.DriveStatus == DriveStatus.Ready, "settled");
        Assert.AreEqual(string.Empty, row.SizeLabel);
    }

    [TestMethod]
    public void ReturningToThisPcRefreshesTheProvidedRowWithoutDuplicatingIt()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();
        var before = vm.Entries.Count;

        vm.NavigateTo(fx.VirtualRoot);
        vm.GoToThisPc();

        Assert.AreEqual(before, vm.Entries.Count);
        Assert.AreEqual(1, vm.Entries.Count(e => e.ProviderId == fx.MountId));
    }

    [TestMethod]
    public void WithNoProvidersThisPcIsExactlyTheDriveListItAlwaysWas()
    {
        using var fx = new ProvidedRootFixture(withProvider: false);
        var vm = fx.ThisPc();

        Assert.AreEqual(DriveInfo.GetDrives().Length, vm.Entries.Count);
        Assert.IsTrue(vm.Entries.All(e => e.ProviderId is null));
    }

    [TestMethod]
    public void TheFolderTreeGetsANodeForTheProvidedLocation()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        var thisPc = vm.TreeRoots.Single(n => n.Kind == TreeNodeKind.ThisPc);
        var node   = thisPc.Children.SingleOrDefault(c => c.FullPath == fx.VirtualRoot);

        Assert.IsNotNull(node, "the provided location sits beside the drives in the tree");
        Assert.AreEqual(fx.Label, node!.Name);
        Assert.AreEqual(DriveIconType.Cloud, node.DriveIconType);
    }

    [TestMethod]
    public void AProvidedRowGetsTheThisPcContextMenuJustLikeADrive()
    {
        using var fx = new ProvidedRootFixture();
        var vm  = fx.ThisPc();
        var row = Row(fx, vm);

        // The guard in BuildContextActions short-circuits This PC mode unless the selection is a
        // top-level row; a provided row must clear it, or right-clicking one gives nothing at all.
        var actions = vm.BuildContextActions([row]);

        Assert.IsNotNull(actions);
    }
}
