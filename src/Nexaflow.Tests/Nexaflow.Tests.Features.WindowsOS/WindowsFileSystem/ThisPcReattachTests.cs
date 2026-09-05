using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// What happens when a This PC tab is shown again. The view attaches its provider subscriptions on every
/// load, which is every switch back to the tab, and that attach used to rebuild the row list
/// unconditionally: the rows were replaced, each one flashed back to Loading, and every drive was
/// re-probed — readiness, capacity, a device query — for an answer it already had. It now compares the
/// set of places first, so the rebuild happens when something actually moved and not otherwise.
/// </summary>
/// <remarks>
/// Serialized for the same reason <see cref="ThisPcProvidedRowsTests"/> is: a mount is process-wide, so
/// two fixtures alive at once each see the other's location.
/// </remarks>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-thispc")]
public class ThisPcReattachTests
{
    [TestMethod]
    public void ReattachingWithNothingChanged_KeepsTheRowsItAlreadyHad()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();
        vm.AttachThisPcProviderTracking();

        var before = vm.Entries.ToArray();

        vm.DetachThisPcProviderTracking();
        vm.AttachThisPcProviderTracking();

        // Reference equality, deliberately: a rebuild produces new entry objects even when it produces
        // the same list, and it is the rebuild — not the contents — this is about.
        CollectionAssert.AreEqual(before, vm.Entries.ToArray(),
            "the rows were rebuilt for a This PC nothing had changed about");
    }

    [TestMethod]
    public void ReattachingAfterALocationAppeared_RebuildsTheRows()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();
        vm.AttachThisPcProviderTracking();
        var before = vm.Entries.Count;

        vm.DetachThisPcProviderTracking();
        using var arrived = new ProvidedRootFixture();   // contributed while nobody was listening
        vm.AttachThisPcProviderTracking();

        Assert.AreEqual(before + 1, vm.Entries.Count);
        Assert.IsTrue(vm.Entries.Any(e => e.ProviderId == arrived.MountId),
            "a location that appeared while the tab was away never showed up");
    }

    [TestMethod]
    public void ReattachingAfterALocationWentAway_RebuildsTheRows()
    {
        using var fx = new ProvidedRootFixture();
        var going = new ProvidedRootFixture();

        var vm = fx.ThisPc();
        vm.AttachThisPcProviderTracking();
        Assert.IsTrue(vm.Entries.Any(e => e.ProviderId == going.MountId), "fixture never listed");

        vm.DetachThisPcProviderTracking();
        going.Dispose();
        vm.AttachThisPcProviderTracking();

        Assert.IsFalse(vm.Entries.Any(e => e.ProviderId == going.MountId),
            "a location that went while the tab was away is still listed");
    }
}
