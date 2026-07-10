using System;
using System.Linq;
using Nexaflow.Features.Audio;             // AudioConfig
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;            // IShellServices
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>
/// Headless command/state logic behind the Audio transport &amp; drawer buttons. The engine-bound commands
/// (PlayPause / Next-Previous execution / Seek) need a real NAudio device + file and are covered by the
/// UI journey instead; everything here runs without touching an audio device.
/// </summary>
[TestClass]
[CoversNode("audio-playlist-reorder")]
public class AudioViewModelTests
{
    private static AudioViewModel Make(params string[] paths)
        => new(paths, 0, Substitute.For<IShellServices>(), new AudioConfig());

    [TestMethod]
    public void TogglePlaylist_FlipsOpenState()
    {
        var vm = Make("a.mp3", "b.mp3");
        Assert.IsFalse(vm.IsPlaylistOpen);
        vm.TogglePlaylistCommand.Execute(null);
        Assert.IsTrue(vm.IsPlaylistOpen);
        vm.TogglePlaylistCommand.Execute(null);
        Assert.IsFalse(vm.IsPlaylistOpen);
    }

    [TestMethod]
    public void TogglePanel_SwitchesPanel_ThenClosesOnActive()
    {
        var vm = Make("a.mp3");
        Assert.IsTrue(vm.ShowTagsPanel);          // default: Tags open
        Assert.IsTrue(vm.IsTagsButtonActive);

        vm.TogglePanelCommand.Execute("Lyrics");  // switch to Lyrics
        Assert.IsTrue(vm.ShowLyricsPanel);
        Assert.IsTrue(vm.IsLyricsButtonActive);
        Assert.IsFalse(vm.IsTagsButtonActive);

        vm.TogglePanelCommand.Execute("Lyrics");  // clicking the active panel closes the drawer
        Assert.IsFalse(vm.IsPanelOpen);
        Assert.IsFalse(vm.IsLyricsButtonActive);
    }

    [TestMethod]
    public void Stop_ResetsTransportState_WithoutEngine()
    {
        var vm = Make("a.mp3");
        vm.StopCommand.Execute(null);
        Assert.IsFalse(vm.IsPlaying);
        Assert.AreEqual(TimeSpan.Zero, vm.Position);
    }

    [TestMethod]
    public void Volume_RoundTrips_AndSetterIsEngineSafe()
    {
        var vm = Make("a.mp3");
        vm.Volume = 0.42;                          // OnVolumeChanged must not throw with no engine
        Assert.AreEqual(0.42, vm.Volume, 1e-9);
    }

    [TestMethod]
    public void Queue_ReflectsPosition_AndNextPreviousCanExecute()
    {
        var vm = Make("a.mp3", "b.mp3", "c.mp3");
        Assert.IsTrue(vm.HasNext);
        Assert.IsFalse(vm.HasPrevious);
        Assert.IsTrue(vm.NextCommand.CanExecute(null));
        Assert.IsFalse(vm.PreviousCommand.CanExecute(null));
        Assert.IsTrue(vm.HasPlaylist);
        Assert.AreEqual("1 / 3", vm.QueueText);
    }

    [TestMethod]
    public void MovePlaylistItem_ReordersQueue()
    {
        var vm = Make("a.mp3", "b.mp3", "c.mp3");
        vm.MovePlaylistItem(0, 2);                 // a,b,c -> b,c,a
        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, vm.Playlist.Select(p => p.Display).ToList());
    }

    [TestMethod]
    public void SingleTrack_HasNoPlaylistOrQueue()
    {
        var vm = Make("only.mp3");
        Assert.IsFalse(vm.HasPlaylist);
        Assert.IsFalse(vm.HasQueue);
        Assert.AreEqual("", vm.QueueText);
    }
}
