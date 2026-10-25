using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Browsing a provided location. The promise is that it behaves like a place of its own: you go into it
/// from This PC, Up walks back out to This PC, and the folder it actually maps to is never shown. Reaching
/// the same folder by its real path is a separate journey that keeps the real trail — the two coexist and
/// neither is canonicalised.
/// </summary>
/// <remarks>Serialized for the same reason as <see cref="ThisPcProvidedRowsTests"/>: mounts are process-wide.</remarks>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-thispc-providers")]
public class VirtualNavigationTests
{
    private static async Task<List<(string Label, string Path)>> Crumbs(FileSystemViewModel vm)
    {
        List<(string, string)> captured = [];
        vm.NavigationChanged += segs => { captured = [.. segs]; };
        await vm.BrowseToAsync(vm.CurrentPath);   // re-fire for the current location
        return captured;
    }

    private static void WaitForEntries(FileSystemViewModel vm, int expected)
    {
        for (int i = 0; i < 200 && vm.Entries.Count != expected; i++) Thread.Sleep(10);
    }

    // ── The mounted journey ──────────────────────────────────────────────────

    [TestMethod]
    public async Task EnteringAProvidedLocationListsItsContents()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        await vm.BrowseToAsync(fx.VirtualRoot);
        WaitForEntries(vm, 2);

        CollectionAssert.AreEquivalent(
            new[] { "Documents", "top.txt" },
            vm.Entries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public async Task NothingInsideAProvidedLocationEverRevealsTheRealFolder()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        await vm.BrowseToAsync(fx.VirtualRoot);
        WaitForEntries(vm, 2);

        Assert.IsFalse(vm.CurrentPath.Contains(fx.RealRoot, StringComparison.OrdinalIgnoreCase));
        foreach (var entry in vm.Entries)
            Assert.IsFalse(entry.FullPath.Contains(fx.RealRoot, StringComparison.OrdinalIgnoreCase),
                $"'{entry.Name}' leaked the real location: {entry.FullPath}");
    }

    [TestMethod]
    public async Task TheRealFolderStaysHiddenTwoLevelsDownToo()
    {
        // The regression this guards: the streaming loader reports FileSystemInfo.FullName, which IS the
        // real path. Child paths must be composed from the virtual parent instead.
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        await vm.BrowseToAsync(Path.Combine(fx.VirtualRoot, "Documents"));
        WaitForEntries(vm, 2);

        Assert.AreEqual($@"{fx.VirtualRoot}\Documents", vm.CurrentPath);
        var notes = vm.Entries.Single(e => e.Name == "notes.txt");
        Assert.AreEqual($@"{fx.VirtualRoot}\Documents\notes.txt", notes.FullPath);
    }

    [TestMethod]
    public async Task GoingUpWalksBackToTheProvidedRootAndThenToThisPc()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        await vm.BrowseToAsync(Path.Combine(fx.VirtualRoot, "Documents", "Nested"));
        Assert.AreEqual($@"{fx.VirtualRoot}\Documents\Nested", vm.CurrentPath);

        vm.NavigateUp();
        await vm.Browsing;
        Assert.AreEqual($@"{fx.VirtualRoot}\Documents", vm.CurrentPath);

        vm.NavigateUp();
        await vm.Browsing;
        Assert.AreEqual(fx.VirtualRoot, vm.CurrentPath);

        vm.NavigateUp();
        await vm.Browsing;
        Assert.IsTrue(vm.IsThisPcMode, "above a provided root is This PC, not the folder it maps to");
    }

    [TestMethod]
    public async Task TheBreadcrumbNamesTheLocationNotItsId()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        await vm.BrowseToAsync(Path.Combine(fx.VirtualRoot, "Documents"));

        var crumbs = await Crumbs(vm);
        CollectionAssert.AreEqual(
            new[] { Str.Get("WindowsFileSystem.ThisPc.Title"), fx.Label, "Documents" },
            crumbs.Select(c => c.Label).ToArray());
        Assert.IsFalse(crumbs.Any(c => c.Path.Contains(fx.RealRoot, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task AVirtualPathSurvivesTheRoundTripThroughSavedTabState()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();
        await vm.BrowseToAsync(Path.Combine(fx.VirtualRoot, "Documents"));

        // What the page registration persists is the last breadcrumb's path.
        var persisted = (await Crumbs(vm))[^1].Path;

        var restored = fx.ThisPc();
        await restored.BrowseToAsync(persisted);

        Assert.AreEqual($@"{fx.VirtualRoot}\Documents", restored.CurrentPath);
    }

    [TestMethod]
    public async Task TheTreeExpandsAProvidedLocationIntoItsRealSubfolders()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();

        var node = vm.TreeRoots.Single(n => n.Kind == TreeNodeKind.ThisPc)
                     .Children.Single(c => c.FullPath == fx.VirtualRoot);

        for (int i = 0; i < 200 && node.Children.Count == 0; i++) Thread.Sleep(10);
        node.IsExpanded = true;
        await node.LoadChildrenAsync();

        var child = node.Children.SingleOrDefault(c => c.Name == "Documents");
        Assert.IsNotNull(child, "a mounted node expands like any other folder");
        Assert.AreEqual($@"{fx.VirtualRoot}\Documents", child!.FullPath,
                        "and its children stay in the virtual path space");
    }

    // ── The real journey, unchanged ──────────────────────────────────────────

    [TestMethod]
    public async Task ReachingTheSameFolderByItsRealPathKeepsTheRealTrail()
    {
        using var fx = new ProvidedRootFixture();
        var real = Path.Combine(fx.RealRoot, "Documents");
        var vm   = await fx.AtAsync(real);

        Assert.AreEqual(real, vm.CurrentPath, "the real route is not rewritten to the mount");

        vm.NavigateUp();
        await vm.Browsing;
        Assert.AreEqual(fx.RealRoot, vm.CurrentPath,
                        "Up from a real path walks the real chain, not out to This PC");
    }

    [TestMethod]
    public async Task AnOrdinaryFolderIsUnaffectedByAnyOfThis()
    {
        using var fx = new ProvidedRootFixture(withProvider: false);
        var dir = Path.Combine(TestTemp.Root, "nexa-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        try
        {
            var vm = await fx.AtAsync(dir);
            WaitForEntries(vm, 2);

            Assert.AreEqual(dir, vm.CurrentPath);
            Assert.AreEqual(Path.Combine(dir, "a.txt"), vm.Entries.Single(e => e.Name == "a.txt").FullPath);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    // ── Withdrawal ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ATabInsideAProvidedLocationCannotFollowItAfterItIsWithdrawn()
    {
        using var fx = new ProvidedRootFixture();
        var vm = fx.ThisPc();
        await vm.BrowseToAsync(fx.VirtualRoot);
        Assert.AreEqual(fx.VirtualRoot, vm.CurrentPath);

        Nexaflow.IO.Common.VirtualFileSystem.Instance.UnregisterMount(fx.MountId);

        // Navigation is gated on the VFS, so a withdrawn location simply stops resolving rather than
        // showing the user a folder that is no longer theirs to await browse.
        vm.BrowseToAsync(Path.Combine(fx.VirtualRoot, "Documents"));
        Assert.AreEqual(fx.VirtualRoot, vm.CurrentPath);
    }
}
