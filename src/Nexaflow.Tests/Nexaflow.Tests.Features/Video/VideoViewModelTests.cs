using System;
using Nexaflow.Features.Common;            // IShellServices
using Nexaflow.Features.Video.Models;       // SubtitleTrackOption
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Video;

/// <summary>
/// Headless command/state logic behind the Video transport &amp; overlay buttons. The constructor builds no
/// libvlc engine (that happens in <c>LoadAsync</c>), and every pure-state command guards a null player — so
/// the toggles, the speed/rate state, the subtitle-menu state and the derived visibility flags all run here
/// without a native device. The engine-bound commands (TogglePlayPause / Step / Mute / SeekToKeyframe /
/// LoadAsync / keyframe generation / frame capture) need a real MediaPlayer + decodable clip and are covered
/// by the UI journey instead.
/// </summary>
[TestClass]
[CoversNode("video-subtitles")]
public class VideoViewModelTests
{
    private static VideoViewModel Make()
        => new("clip.mp4", Substitute.For<IShellServices>());

    [TestMethod]
    public void ToggleInfoPanel_FlipsVisibility()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsInfoPanelVisible);
        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.IsTrue(vm.IsInfoPanelVisible);
        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.IsFalse(vm.IsInfoPanelVisible);
    }

    [TestMethod]
    public void ToggleSceneStrip_FlipsVisibility_AndShowSceneStripFollowsKeyframes()
    {
        var vm = Make();
        Assert.IsTrue(vm.IsSceneStripVisible);   // default: strip on
        Assert.IsFalse(vm.HasKeyframes);
        Assert.IsFalse(vm.ShowSceneStrip);       // visible flag on, but no frames yet → hidden

        vm.Keyframes.Add(new KeyframeViewModel { Position = TimeSpan.FromSeconds(1) });
        Assert.IsTrue(vm.HasKeyframes);
        Assert.IsTrue(vm.ShowSceneStrip);        // now both true

        vm.ToggleSceneStripCommand.Execute(null);
        Assert.IsFalse(vm.IsSceneStripVisible);
        Assert.IsFalse(vm.ShowSceneStrip);       // toggled off even though frames exist
    }

    [TestMethod]
    public void ToggleFullScreen_FlipsState_AndExitForcesItOff()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsFullScreen);
        vm.ToggleFullScreenCommand.Execute(null);
        Assert.IsTrue(vm.IsFullScreen);
        vm.ExitFullScreen();
        Assert.IsFalse(vm.IsFullScreen);
    }

    [TestMethod]
    public void ToggleSpeedMenu_FlipsOpenState()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsSpeedMenuOpen);
        vm.ToggleSpeedMenuCommand.Execute(null);
        Assert.IsTrue(vm.IsSpeedMenuOpen);
        vm.ToggleSpeedMenuCommand.Execute(null);
        Assert.IsFalse(vm.IsSpeedMenuOpen);
    }

    [TestMethod]
    public void SetSpeed_UpdatesRateAndLabel_AndClosesMenu_EngineSafe()
    {
        var vm = Make();
        vm.ToggleSpeedMenuCommand.Execute(null);
        Assert.IsTrue(vm.IsSpeedMenuOpen);

        vm.SetSpeedCommand.Execute("1.5");        // OnPlaybackRateChanged must not throw with no engine
        Assert.AreEqual(1.5, vm.PlaybackRate, 1e-9);
        Assert.AreEqual("1.5×", vm.RateLabel);
        Assert.IsFalse(vm.IsSpeedMenuOpen);       // picking a speed closes the menu

        vm.SetSpeedCommand.Execute("1");
        Assert.AreEqual(1.0, vm.PlaybackRate, 1e-9);
        Assert.AreEqual("1×", vm.RateLabel);      // exactly 1.0 renders as the bare "1×"
    }

    [TestMethod]
    public void SetSpeed_IgnoresUnparsableFactor_ButStillClosesMenu()
    {
        var vm = Make();
        vm.IsSpeedMenuOpen = true;
        vm.SetSpeedCommand.Execute("not-a-number");
        Assert.AreEqual(1.0, vm.PlaybackRate, 1e-9);   // unchanged
        Assert.IsFalse(vm.IsSpeedMenuOpen);
    }

    [TestMethod]
    public void ToggleSubtitleMenu_OpensWithOffEntry_EngineSafe()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsSubtitleMenuOpen);

        vm.ToggleSubtitleMenuCommand.Execute(null);    // refreshes tracks: with no player, only "Off"
        Assert.IsTrue(vm.IsSubtitleMenuOpen);
        Assert.AreEqual(1, vm.SubtitleTracks.Count);
        Assert.IsNull(vm.SubtitleTracks[0].Id);
        Assert.AreEqual("Off", vm.SubtitleTracks[0].Label);

        vm.ToggleSubtitleMenuCommand.Execute(null);
        Assert.IsFalse(vm.IsSubtitleMenuOpen);
    }

    [TestMethod]
    public void SetSubtitle_ClosesMenu_WithoutEngine()
    {
        var vm = Make();
        vm.IsSubtitleMenuOpen = true;
        vm.SetSubtitleCommand.Execute(new SubtitleTrackOption(null, "Off"));   // no player → no-op + close
        Assert.IsFalse(vm.IsSubtitleMenuOpen);
    }

    [TestMethod]
    public void Volume_RoundTrips_AndSetterIsEngineSafe()
    {
        var vm = Make();
        vm.Volume = 40;                            // OnVolumeChanged must not throw with no engine
        Assert.AreEqual(40, vm.Volume);
    }

    [TestMethod]
    public void PositionSeconds_RoundTrips_AndSeekSetterIsEngineSafe()
    {
        var vm = Make();
        vm.PositionSeconds = 12.5;                  // OnPositionSecondsChanged must not throw with no engine
        Assert.AreEqual(12.5, vm.PositionSeconds, 1e-9);
    }

    [TestMethod]
    public void DerivedVisibility_ReflectsLoadedPlayingErrorState()
    {
        var vm = Make();
        Assert.IsFalse(vm.ShowBigPlayButton);      // not loaded yet
        Assert.IsFalse(vm.ShowPoster);

        vm.IsLoaded = true;
        Assert.IsTrue(vm.ShowBigPlayButton);       // loaded, not playing, no error
        Assert.IsFalse(vm.ShowPoster);             // still no poster image

        vm.IsPlaying = true;
        Assert.IsFalse(vm.ShowBigPlayButton);      // playing hides the centre affordance

        vm.IsPlaying = false;
        vm.HasError = true;
        Assert.IsFalse(vm.ShowBigPlayButton);      // an error hides it too
    }

    [TestMethod]
    public void Context_DescribesFileAndState()
    {
        var vm = Make();
        StringAssert.Contains(vm.GetContext(), "clip.mp4");
    }
}
