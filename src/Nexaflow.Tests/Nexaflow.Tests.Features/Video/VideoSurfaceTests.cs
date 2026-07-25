using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.FileActions;
using Nexaflow.Features.Video.Models;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Video;

/// <summary>
/// The video tab before and around playback: the overlays that stand in for a picture that isn't running
/// yet, the controls that must survive being pressed before the engine exists, and the entry point that
/// opens a file.
/// <para>
/// The tab is constructed and painted before <c>LoadAsync</c> builds any libVLC engine, so every one of
/// these can be reached with <c>_mp</c> still null. That window is exactly where a video player throws, and
/// it needs no native device to test.
/// </para>
/// </summary>
[TestClass]
public class VideoSurfaceTests
{
    private static VideoViewModel Make() => new("clip.mp4", Substitute.For<IShellServices>());

    /// <summary>A 1x1 frozen bitmap — enough to stand in for a decoded poster without a decoder.</summary>
    private static ImageSource Frame() =>
        BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[4], 4);

    private static KeyframeViewModel At(double seconds) =>
        new() { Position = TimeSpan.FromSeconds(seconds) };

    // ── Overlays ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("video-states")]
    public void ItOpensInTheLoadingState_AndAnErrorReplacesEveryAffordance()
    {
        var vm = Make();
        Assert.IsTrue(vm.IsLoading, "the placeholder shows while the engine builds and parses");
        Assert.IsFalse(vm.HasError);

        vm.IsLoaded = true;
        vm.IsLoading = false;
        Assert.IsTrue(vm.ShowBigPlayButton);

        vm.HasError = true;
        vm.ErrorMessage = "Could not open this file.";
        Assert.IsFalse(vm.ShowBigPlayButton, "an error card must not sit behind a play button that does nothing");
        Assert.IsFalse(vm.ShowPoster);
    }

    [TestMethod]
    [CoversNode("video-poster")]
    public void ThePosterCoversTheSurfaceOnlyUntilPlaybackHasStarted()
    {
        var vm = Make();
        vm.IsLoaded = true;
        Assert.IsFalse(vm.ShowPoster, "there is nothing to show until the first keyframe is decoded");

        vm.PosterImage = Frame();
        Assert.IsTrue(vm.ShowPoster);

        vm.HasPlayed = true;
        Assert.IsFalse(vm.ShowPoster, "the still must get out of the way of the moving picture");
    }

    // ── Controls pressed before the engine exists ─────────────────────────────

    [TestMethod]
    [CoversNode("video-click-pause")]
    [CoversNode("video-playpause")]
    public void ClickingThePictureBeforeTheEngineIsUp_IsANoOp_NotACrash()
    {
        var vm = Make();   // the tab paints before LoadAsync builds anything

        vm.TogglePlayPauseCommand.Execute(null);

        Assert.IsFalse(vm.IsPlaying);
    }

    [TestMethod]
    [CoversNode("video-step")]
    public void SteppingBeforeTheEngineIsUp_IsANoOp_NotACrash()
    {
        var vm = Make();

        vm.StepForwardCommand.Execute(null);
        vm.StepBackCommand.Execute(null);

        Assert.AreEqual(0, vm.PositionSeconds);
    }

    [TestMethod]
    [CoversNode("video-scene-seek")]
    public void SeekingToASceneBeforeTheEngineIsUp_IsANoOp_NotACrash()
    {
        var vm = Make();
        vm.SeekToKeyframeCommand.Execute(At(30));
        vm.SeekToKeyframeCommand.Execute(null);

        Assert.AreEqual(0, vm.PositionSeconds);
    }

    [TestMethod]
    [CoversNode("video-scenestrip-toggle")]
    public void TheStripToggleCanBeOnWithNothingToShow()
    {
        var vm = Make();
        Assert.IsTrue(vm.IsSceneStripVisible, "the strip is on by default");
        Assert.IsFalse(vm.ShowSceneStrip, "but an empty strip would just be a grey bar");

        vm.Keyframes.Add(At(0));

        Assert.IsTrue(vm.ShowSceneStrip);
    }

    // ── Info panel ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("video-info-sections")]
    public void TheInfoPanelIsEmptyUntilTheMediaHasBeenParsed()
    {
        var vm = Make();

        Assert.IsNull(vm.MediaInfo, "there is nothing honest to list before libVLC has read the tracks");

        vm.MediaInfo = new MediaInfo
        {
            FileName = "clip.mp4",
            Summary = "1920×1080 H264, 00:00:10, AAC stereo",
            Sections = [new MediaInfoSection { Header = "Video", Rows = { new MediaInfoRow("Codec", "H264") } }],
        };

        Assert.AreEqual("Video", vm.MediaInfo.Sections.Single().Header);
        Assert.AreEqual("Codec", vm.MediaInfo.Sections.Single().Rows.Single().Label);
    }

    // ── Open action ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("video-open")]
    public void PlayVideo_OpensTheTabOnTheFileItWasInvokedOn()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Video", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));

        Assert.IsTrue(new ShowVideoAction(shell).PerformAction(@"C:\clips\holiday.mp4"));

        Assert.AreEqual(@"C:\clips\holiday.mp4", opened.Single()["path"]);
    }

    [TestMethod]
    [CoversNode("video-open")]
    public void PlayVideo_OnASelection_OpensTheFirstRatherThanATabPerFile()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Video", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        var action = new ShowVideoAction(shell);

        Assert.IsTrue(action.PerformAction([@"C:\clips\a.mp4", @"C:\clips\b.mkv"]));

        Assert.AreEqual(1, opened.Count, "two players competing for the audio device helps nobody");
        Assert.IsFalse(action.SupportsMultipleFiles);
        Assert.IsFalse(action.PerformAction([]));
    }
}
