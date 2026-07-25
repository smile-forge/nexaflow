using Nexaflow.Features.Common;            // IShellServices
using Nexaflow.Features.Images.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Headless command/state logic behind the image viewer's toolbar buttons — rotation stepping, fit/actual
/// toggle, sub-view selection, navigation and the dot-window paging. None of these need a decoded image
/// surface or GPU: the view-model's <c>LoadImage</c> tolerates missing files (try/catch → null bitmap), so
/// every observable state transition runs without touching a real image. The full pixel/full-screen path is
/// covered by the UI journey instead.
/// </summary>
[TestClass]
public class ImagesViewModelTests
{
    private static ImageViewModel Make(params string[] paths)
        => new(paths, Substitute.For<IShellServices>());

    // ── Rotation (90° steps, wrapping both ways) ──────────────────────────

    [TestMethod]
    [CoversNode("images-rotate")]
    public void RotateRight_Steps90_AndWrapsAt360()
    {
        var vm = Make("a.png");
        Assert.AreEqual(0d, vm.RotationAngle);

        vm.RotateRightCommand.Execute(null);
        Assert.AreEqual(90d, vm.RotationAngle);

        vm.RotateRightCommand.Execute(null);
        vm.RotateRightCommand.Execute(null);
        vm.RotateRightCommand.Execute(null);   // 90 → 180 → 270 → 360 ≡ 0
        Assert.AreEqual(0d, vm.RotationAngle);
    }

    [TestMethod]
    [CoversNode("images-rotate")]
    public void RotateLeft_Steps90_AndWrapsBelowZero()
    {
        var vm = Make("a.png");
        vm.RotateLeftCommand.Execute(null);     // 0 → 270 (no negative angles)
        Assert.AreEqual(270d, vm.RotationAngle);

        vm.RotateLeftCommand.Execute(null);
        Assert.AreEqual(180d, vm.RotationAngle);
    }

    // ── Fit ↔ actual size ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-fit")]
    public void ToggleFit_FlipsFitToWindow()
    {
        var vm = Make("a.png");
        Assert.IsTrue(vm.FitToWindow);          // default: fit to window
        vm.ToggleFitCommand.Execute(null);
        Assert.IsFalse(vm.FitToWindow);
        vm.ToggleFitCommand.Execute(null);
        Assert.IsTrue(vm.FitToWindow);
    }

    // ── Sub-view selection + derived flags ────────────────────────────────

    [TestMethod]
    [CoversNode("images-mode-selector")]
    public void ViewMode_DrivesMutuallyExclusiveFlags()
    {
        var vm = Make("a.png", "b.png", "c.png");
        Assert.IsTrue(vm.IsCarousel);           // default sub-view

        vm.ViewMode = ImageViewMode.Album;
        Assert.IsTrue(vm.IsAlbum);
        Assert.IsFalse(vm.IsCarousel);
        Assert.IsFalse(vm.IsExplore);
        Assert.IsFalse(vm.IsCollage);

        vm.ViewMode = ImageViewMode.Collage;
        Assert.IsTrue(vm.IsCollage);
        Assert.IsFalse(vm.IsAlbum);
    }

    // ── Full-screen flag (window creation is a view concern) ──────────────

    [TestMethod]
    [CoversNode("images-fullscreen-btn")]
    [CoversNode("images-fullscreen-nav")]
    public void EnterThenExitFullScreen_TogglesFlag_AndStopsAuto()
    {
        var vm = Make("a.png", "b.png");
        vm.IsAuto = true;

        vm.EnterFullScreenCommand.Execute(null);
        Assert.IsTrue(vm.IsFullScreen);

        vm.ExitFullScreen();                    // Esc handler
        Assert.IsFalse(vm.IsFullScreen);
        Assert.IsFalse(vm.IsAuto);              // exiting also stops the slideshow
    }

    // ── Navigation (manual stepping is clamped, can-execute reflects ends) ─

    [TestMethod]
    [CoversNode("images-navigation")]
    [CoversNode("images-prevnext")]
    [CoversNode("images-wheel-keys")]
    public void Navigation_ClampsAtEnds_AndCanExecuteTracksPosition()
    {
        var vm = Make("a.png", "b.png", "c.png");
        Assert.AreEqual(0, vm.CurrentIndex);
        Assert.IsFalse(vm.PreviousCommand.CanExecute(null));
        Assert.IsTrue(vm.NextCommand.CanExecute(null));

        vm.NextCommand.Execute(null);
        Assert.AreEqual(1, vm.CurrentIndex);
        Assert.IsTrue(vm.PreviousCommand.CanExecute(null));

        vm.NextCommand.Execute(null);           // → last
        Assert.AreEqual(2, vm.CurrentIndex);
        Assert.IsFalse(vm.NextCommand.CanExecute(null));

        vm.StepNext();                          // clamped — stays on the last
        Assert.AreEqual(2, vm.CurrentIndex);
    }

    [TestMethod]
    [CoversNode("images-navigation")]
    [CoversNode("images-slideshow")]
    public void Advance_WrapsToFirst_UnlikeManualStep()
    {
        var vm = Make("a.png", "b.png");
        vm.StepNext();                          // → last
        Assert.AreEqual(1, vm.CurrentIndex);
        vm.Advance();                           // slideshow wraps
        Assert.AreEqual(0, vm.CurrentIndex);
    }

    [TestMethod]
    [CoversNode("images-dot-indicator")]
    public void GoToIndex_JumpsWithinRange_IgnoresOutOfRange()
    {
        var vm = Make("a.png", "b.png", "c.png");
        vm.GoToIndexCommand.Execute(2);
        Assert.AreEqual(2, vm.CurrentIndex);

        vm.GoToIndexCommand.Execute(99);        // ignored
        Assert.AreEqual(2, vm.CurrentIndex);
    }

    // ── Dot-window paging (windows at 20) ─────────────────────────────────

    [TestMethod]
    [CoversNode("images-dot-indicator")]
    [CoversNode("images-dot-paging")]
    public void DotWindow_PagesInBlocksOfTwenty()
    {
        var paths = new string[45];
        for (int i = 0; i < paths.Length; i++) paths[i] = $"img_{i}.png";
        var vm = Make(paths);

        Assert.AreEqual(20, vm.VisibleDots.Count);   // first block
        Assert.IsFalse(vm.CanPageDotsLeft);
        Assert.IsTrue(vm.CanPageDotsRight);

        vm.PageDotsRightCommand.Execute(null);       // → second block (index 20)
        Assert.AreEqual(20, vm.CurrentIndex);
        Assert.IsTrue(vm.CanPageDotsLeft);

        vm.PageDotsLeftCommand.Execute(null);        // → first block (index 0)
        Assert.AreEqual(0, vm.CurrentIndex);
        Assert.IsFalse(vm.CanPageDotsLeft);
    }

    // ── Multiplicity ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-mode-selector")]
    public void SingleImage_HasNoMultipleAndStaysCarousel()
    {
        var vm = Make("only.png");
        Assert.IsFalse(vm.HasMultiple);
        Assert.IsTrue(vm.IsCarousel);
        Assert.AreEqual(1, vm.TotalImages);
    }

    [TestMethod]
    [CoversNode("images-speed-slider")]
    public void AutoSpeed_RoundTrips_AndLabelsCorrectly()
    {
        var vm = Make("a.png", "b.png");
        Assert.AreEqual("Medium", vm.AutoSpeedLabel);   // default speed = 1
        vm.AutoSpeed = 0;
        Assert.AreEqual("Slow", vm.AutoSpeedLabel);
        vm.AutoSpeed = 2;
        Assert.AreEqual("Fast", vm.AutoSpeedLabel);
    }
}
