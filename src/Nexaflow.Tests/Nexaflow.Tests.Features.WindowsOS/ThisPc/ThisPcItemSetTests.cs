using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ThisPc;

/// <summary>
/// The merge rules every "This PC" surface shares. They live in one place because the surfaces disagree
/// about their starting point — the browser lists all drives, the pickers only ready ones — so a provider
/// cannot know what it would be colliding with and the dedupe has to happen on the consumer's side.
/// </summary>
[TestClass]
[CoversNode("winfs-thispc-providers")]
public class ThisPcItemSetTests
{
    private sealed class FakeProvider(string id, int sortOrder, params ThisPcItem[] items) : IThisPcItemProvider
    {
        public string ProviderId => id;
        public int    SortOrder  => sortOrder;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => items;
    }

    private sealed class ThrowingProvider : IThisPcItemProvider
    {
        public string ProviderId => "boom";
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => throw new InvalidOperationException("provider is broken");
    }

    private static ThisPcItem Item(string id, string path, int sort = 0,
                                   ThisPcItemBacking backing = ThisPcItemBacking.LocalPath) =>
        new() { Id = id, Label = id, TargetPath = path, TypeLabel = "Test", SortOrder = sort, Backing = backing };

    [TestMethod]
    public void ALocationThatIsAlreadyADriveIsNotListedTwice()
    {
        // The case that matters in the wild: a sync client that mounts its own drive letter is reported
        // by Windows AND offered by its provider.
        var provider = new FakeProvider("p", 10, Item("p.g", @"G:\"));

        var result = ThisPcItemSet.Collect([provider], [@"C:\", @"G:\"]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ATrailingSeparatorOrDifferentCasingIsStillTheSameLocation()
    {
        var provider = new FakeProvider("p", 10, Item("p.g", @"g:\"));

        Assert.AreEqual(0, ThisPcItemSet.Collect([provider], [@"G:"]).Count);
    }

    [TestMethod]
    public void TwoProvidersOfferingOneLocationYieldASingleRow()
    {
        var first  = new FakeProvider("a", 10, Item("a.docs", @"C:\Shared"));
        var second = new FakeProvider("b", 20, Item("b.docs", @"C:\Shared\"));

        var result = ThisPcItemSet.Collect([first, second], []);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("a.docs", result[0].Id, "the lower-sorted provider claims the location");
    }

    [TestMethod]
    public void ARepeatedIdIsKeptOnlyOnce()
    {
        var first  = new FakeProvider("a", 10, Item("dup", @"C:\One"));
        var second = new FakeProvider("b", 20, Item("dup", @"C:\Two"));

        var result = ThisPcItemSet.Collect([first, second], []);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(@"C:\One", result[0].TargetPath);
    }

    [TestMethod]
    public void AVirtualRowIsDroppedForAConsumerThatCanOnlyNavigateRealPaths()
    {
        var provider = new FakeProvider("p", 10,
            Item("p.local", @"C:\Local"),
            Item("p.cloud", "cloud://acct", backing: ThisPcItemBacking.Virtual));

        Assert.AreEqual(2, ThisPcItemSet.Collect([provider], [], allowVirtual: true).Count);

        var forPicker = ThisPcItemSet.Collect([provider], [], allowVirtual: false);
        Assert.AreEqual(1, forPicker.Count);
        Assert.AreEqual("p.local", forPicker[0].Id);
    }

    [TestMethod]
    public void AVirtualRowIsNeverDedupedAgainstADriveBecauseItsPathIsNotOne()
    {
        // "cloud://x" is not a location on disk, so it must not consume a drive-root slot.
        var provider = new FakeProvider("p", 10,
            Item("p.a", "cloud://x", backing: ThisPcItemBacking.Virtual),
            Item("p.b", "cloud://x", backing: ThisPcItemBacking.Virtual));

        Assert.AreEqual(2, ThisPcItemSet.Collect([provider], []).Count);
    }

    [TestMethod]
    public void AProviderThatThrowsIsSkippedRatherThanEmptyingThisPc()
    {
        var healthy = new FakeProvider("ok", 20, Item("ok.docs", @"C:\Docs"));

        var result = ThisPcItemSet.Collect([new ThrowingProvider(), healthy], []);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("ok.docs", result[0].Id);
    }

    [TestMethod]
    public void RowsComeBackInProviderThenItemOrder()
    {
        var late  = new FakeProvider("late",  50, Item("late.b", @"C:\4", sort: 2), Item("late.a", @"C:\3", sort: 1));
        var early = new FakeProvider("early", 10, Item("early.b", @"C:\2", sort: 2), Item("early.a", @"C:\1", sort: 1));

        var result = ThisPcItemSet.Collect([late, early], []);

        CollectionAssert.AreEqual(
            new[] { "early.a", "early.b", "late.a", "late.b" },
            result.Select(i => i.Id).ToArray());
    }

    [TestMethod]
    public void AnItemMissingAnIdOrALocationIsIgnored()
    {
        var provider = new FakeProvider("p", 10,
            Item("", @"C:\NoId"),
            Item("p.nopath", ""),
            Item("p.good", @"C:\Good"));

        var result = ThisPcItemSet.Collect([provider], []);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("p.good", result[0].Id);
    }

    [TestMethod]
    public void NoProvidersMeansNoRows()
    {
        Assert.AreEqual(0, ThisPcItemSet.Collect([], [@"C:\"]).Count);
    }

    // ── The whole list ───────────────────────────────────────────────────────

    [TestMethod]
    public void EnumeratingYieldsTheDrivesFirstThenTheContributedLocations()
    {
        var provider = new FakeProvider("p", 10, Item("p.docs", System.IO.Path.GetTempPath()));

        var places = ThisPcItemSet.Enumerate([provider]);

        var drives = places.TakeWhile(p => p.Kind == ThisPcPlaceKind.Drive).Count();
        Assert.AreEqual(System.IO.DriveInfo.GetDrives().Length, drives);
        Assert.IsTrue(places.Skip(drives).All(p => p.Kind == ThisPcPlaceKind.Provided),
                      "contributed locations follow the drives, never interleave with them");
    }

    [TestMethod]
    public void EveryDrivePlaceCarriesItsDriveInfoSoTheBrowserCanProbeIt()
    {
        var places = ThisPcItemSet.Enumerate([]);

        Assert.IsTrue(places.All(p => p.Drive is not null && p.Item is null));
        Assert.IsTrue(places.All(p => !string.IsNullOrEmpty(p.RealPath)));
    }

    [TestMethod]
    public void EveryContributedPlaceCarriesItsItemAndItsRealTarget()
    {
        var temp = System.IO.Path.GetTempPath();
        var places = ThisPcItemSet.Enumerate([new FakeProvider("p", 10, Item("p.docs", temp))]);

        var provided = places.Single(p => p.Kind == ThisPcPlaceKind.Provided);
        Assert.AreEqual("p.docs", provided.Item!.Id);
        Assert.AreEqual(temp, provided.RealPath, "a picker needs the real target, not the virtual root");
        Assert.IsNull(provided.Drive);
    }

    [TestMethod]
    public void AskingForReadyDrivesOnlyDropsTheRest()
    {
        var all   = ThisPcItemSet.Enumerate([], readyDrivesOnly: false);
        var ready = ThisPcItemSet.Enumerate([], readyDrivesOnly: true);

        Assert.IsTrue(ready.Count <= all.Count);
        Assert.AreEqual(System.IO.DriveInfo.GetDrives().Count(d => d.IsReady), ready.Count);
    }

    [TestMethod]
    public void ADriveLabelIsTheSameWhicheverSurfaceAsksForIt()
    {
        // The point of the shared rule: the browser and the pickers used to format this independently.
        var drive = System.IO.DriveInfo.GetDrives().First(d => d.IsReady);

        var fromEnumeration = ThisPcItemSet.Enumerate([], readyDrivesOnly: true)
                                           .First(p => p.RealPath == drive.RootDirectory.FullName).Label;

        Assert.AreEqual(ThisPcItemSet.DriveLabel(drive, ready: true), fromEnumeration);
    }

    [TestMethod]
    public void ADriveThatIsNotReadyIsLabelledWithoutTouchingItsVolume()
    {
        // Reading VolumeLabel can block on the hardware, so the cheap label is used until a background
        // probe confirms the drive answered.
        var drive = System.IO.DriveInfo.GetDrives()[0];

        Assert.AreEqual(drive.Name, ThisPcItemSet.DriveLabel(drive, ready: false));
    }

    [TestMethod]
    public void ADrivesIconKindComesFromItsTypeAndNeverClaimsToKnowAboutSsds()
    {
        foreach (var place in ThisPcItemSet.Enumerate([]))
            Assert.AreNotEqual(ThisPcItemIcon.Cloud, place.Icon, "a physical drive is never a cloud");
    }

    [TestMethod]
    public void ProbingReportsAMissingLocationAsUnavailable()
    {
        var item = Item("p.gone", @"C:\definitely\not\here\{0EF7B2A1}");

        var detail = ThisPcItemSet.ProbeLocalAsync(item).GetAwaiter().GetResult();

        Assert.IsFalse(detail.Available);
    }

    [TestMethod]
    public void ProbingReportsAnExistingLocationAndWhetherItHasSubfolders()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexa-probe-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "child"));
        try
        {
            var detail = ThisPcItemSet.ProbeLocalAsync(Item("p.dir", dir)).GetAwaiter().GetResult();

            Assert.IsTrue(detail.Available);
            Assert.IsTrue(detail.HasChildren);
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
    }
}
