using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The parts of the image tab that are about which image you are looking at rather than how it is drawn:
/// the heading, thumbnail selection, the collage's stable scatter, background thumbnail loading, and the
/// delete confirmation.
/// <para>
/// Delete is the only destructive control in the viewer, so it is tested at the gate: the declined path
/// asserts that nothing was recycled, and the accepted path runs against a temp file of the test's own
/// making. Decoding pixels is not needed — <c>LoadImage</c> tolerates a missing file, so every selection
/// transition is observable without a real image.
/// </para>
/// </summary>
[TestClass]
public class ImagesSurfaceTests
{
    /// <summary>The editing engine every viewer here shares, as a registration's tabs do.</summary>
    private static readonly ImageEditingServices Editing = new(VirtualFileSystem.Instance);

    private static ImageViewModel Make(IShellServices shell, params string[] paths) => new(paths, shell, ImageViewMode.Carousel, Editing);

    private static ImageViewModel Make(params string[] paths) => Make(Substitute.For<IShellServices>(), paths);

    // ── Heading ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-ai-preview")]
    public void TheCount_ReadsNaturally_AtOneAndAtMany()
    {
        Assert.AreEqual(Str.Format("Images.Header.ImageCountManyFormat", 3), Make("a.png", "b.png", "c.png").ImageCountText);
        Assert.AreEqual(Str.Get("Images.Header.ImageCountOne"), Make("only.png").ImageCountText);
    }

    // ── Thumbnail selection ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-thumb-select")]
    public void ClickingAThumbnail_SelectsIt_WithoutLeavingTheCurrentView()
    {
        var vm = Make("a.png", "b.png", "c.png");
        vm.ViewMode = ImageViewMode.Album;

        vm.Select(2);

        Assert.AreEqual(2, vm.CurrentIndex);
        Assert.IsTrue(vm.IsAlbum, "a single click browses the grid; it must not throw you into the carousel");
        Assert.IsTrue(vm.Thumbnails[2].IsSelected);
        Assert.IsFalse(vm.Thumbnails[0].IsSelected, "only one thumbnail is selected at a time");
    }

    [TestMethod]
    [CoversNode("images-thumb-select")]
    public void DoubleClickingAThumbnail_OpensItInTheCarousel()
    {
        var vm = Make("a.png", "b.png", "c.png");
        vm.ViewMode = ImageViewMode.Album;

        vm.OpenInCarousel(1);

        Assert.AreEqual(1, vm.CurrentIndex);
        Assert.IsTrue(vm.IsCarousel);

        vm.OpenInCarousel(99);   // out of range — ignored rather than blanking the view
        Assert.AreEqual(1, vm.CurrentIndex);
    }

    [TestMethod]
    [CoversNode("images-explore-step")]
    public void SteppingInExplore_KeepsTheThumbnailStripInSync()
    {
        var vm = Make("a.png", "b.png", "c.png");
        vm.ViewMode = ImageViewMode.Explore;

        vm.StepNext();

        Assert.IsTrue(vm.IsExplore, "wheeling the image pane must not switch view");
        Assert.AreEqual("b.png", vm.CurrentFileName);
        Assert.IsTrue(vm.Thumbnails[1].IsSelected, "the strip highlights whatever the pane is showing");
    }

    // ── Collage layout ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-collage-layout")]
    public void CollageScatter_IsStableAcrossReEntry_AndSpreadsTheCards()
    {
        var vm = Make("a.png", "b.png", "c.png", "d.png");

        vm.ViewMode = ImageViewMode.Collage;
        var first = vm.Thumbnails.Select(t => (t.CollageX, t.CollageY, t.CollageRotation)).ToArray();

        vm.ViewMode = ImageViewMode.Album;
        vm.ViewMode = ImageViewMode.Collage;   // leaving and coming back must not reshuffle

        CollectionAssert.AreEqual(
            first, vm.Thumbnails.Select(t => (t.CollageX, t.CollageY, t.CollageRotation)).ToArray());
        Assert.AreEqual(4, first.Select(p => (p.CollageX, p.CollageY)).Distinct().Count(),
                        "cards are scattered, not stacked");
        Assert.IsTrue(first.All(p => Math.Abs(p.CollageRotation) <= 13.0001), "tilt stays within ±13°");
    }

    [TestMethod]
    [CoversNode("images-collage-layout")]
    public void ASeparateViewer_ScattersTheSameSetTheSameWay()
    {
        string[] paths = ["a.png", "b.png", "c.png"];

        var first = Make(paths);
        first.ViewMode = ImageViewMode.Collage;
        var second = Make(paths);
        second.ViewMode = ImageViewMode.Collage;

        CollectionAssert.AreEqual(
            first.Thumbnails.Select(t => t.CollageX).ToArray(),
            second.Thumbnails.Select(t => t.CollageX).ToArray(),
            "the scatter is seeded, so reopening a folder shows the layout the user remembers");
    }

    // ── Background thumbnail loading ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-thumbnails")]
    public void ThumbnailsAreQueuedOnce_TheFirstTimeAGridViewIsShown()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell, "a.png", "b.png");

        shell.DidNotReceiveWithAnyArgs().QueueBackgroundTask(default!, ct: default);

        vm.ViewMode = ImageViewMode.Album;
        shell.Received(1).QueueBackgroundTask(Arg.Any<ThumbnailLoadTask>(), ct: Arg.Any<CancellationToken>());

        vm.ViewMode = ImageViewMode.Collage;
        vm.ViewMode = ImageViewMode.Album;
        shell.Received(1).QueueBackgroundTask(Arg.Any<ThumbnailLoadTask>(), ct: Arg.Any<CancellationToken>());
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="count"/> files to delete, in a folder of their own. The folder is the test host's to remove when
    /// it exits: a picture the view model is still reading holds its file, so a test deleting its folder as it ends
    /// races that read.
    /// </summary>
    private static string[] WriteImages(int count)
    {
        var dir = TestTemp.CreateSubdirectory("neximg_").FullName;
        var paths = new string[count];
        for (var i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(dir, $"img{i}.png");
            File.WriteAllBytes(paths[i], [0x89, (byte)'P', (byte)'N', (byte)'G']);   // not a decodable image; not needed
        }
        return paths;
    }

    [TestMethod]
    [CoversNode("images-delete")]
    public async Task DecliningTheConfirmation_LeavesTheFileAndTheSetAlone()
    {
        var paths = WriteImages(2);
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        using var vm = Make(shell, paths);

        await vm.DeleteCommand.ExecuteAsync(0);

        Assert.IsTrue(File.Exists(paths[0]), "a declined delete must not touch the file");
        Assert.AreEqual(2, vm.TotalImages);
        Assert.AreEqual(2, vm.Thumbnails.Count);
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    [TestMethod]
    [CoversNode("images-delete")]
    public async Task ConfirmingRecyclesTheFile_AndRePointsEveryView()
    {
        var paths = WriteImages(3);
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        using var vm = Make(shell, paths);
        vm.GoToIndexCommand.Execute(2);   // viewing the last image

        await vm.DeleteCommand.ExecuteAsync(0);   // delete one *before* it

        Assert.IsFalse(File.Exists(paths[0]), "the file goes to the Recycle Bin");
        Assert.AreEqual(2, vm.TotalImages);
        Assert.AreEqual(2, vm.Thumbnails.Count);
        Assert.AreEqual(2, vm.Dots.Count);
        Assert.AreEqual(1, vm.CurrentIndex, "the view follows the image it was on, now one slot earlier");
        Assert.AreEqual("img2.png", vm.CurrentFileName);
        CollectionAssert.AreEqual(new[] { 0, 1 }, vm.Thumbnails.Select(t => t.Index).ToArray(),
                                  "indices are re-numbered, or the next delete hits the wrong file");
    }

    [TestMethod]
    [CoversNode("images-delete")]
    public async Task AFileThatCannotBeRecycled_IsReportedAndKept()
    {
        var shell = Substitute.For<IShellServices>();
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var missing = Path.Combine(TestTemp.Root, $"nexaflow_missing_{Guid.NewGuid():N}.png");
        var vm = Make(shell, missing);

        await vm.DeleteCommand.ExecuteAsync(0);

        shell.Received().ShowError(Arg.Is<string>(m => m == Str.Format("Images.Delete.FailedFormat", Path.GetFileName(missing))));
        Assert.AreEqual(1, vm.TotalImages, "a failed delete must not drop the image from the view anyway");
    }

    [TestMethod]
    [CoversNode("images-copy")]
    public async Task APictureThatCannotBeRead_IsReported_AndNothingIsCopied()
    {
        var shell = Substitute.For<IShellServices>();
        var missing = Path.Combine(TestTemp.Root, $"nexaflow_missing_{Guid.NewGuid():N}.png");
        var vm = Make(shell, missing);

        await vm.CopyPictureCommand.ExecuteAsync(0);

        shell.Received().ShowError(Arg.Is<string>(m => m == Str.Format("Images.Copy.FailedFormat", Path.GetFileName(missing))));
        shell.DidNotReceive().ShowNotification(Arg.Any<string>());
    }
}
