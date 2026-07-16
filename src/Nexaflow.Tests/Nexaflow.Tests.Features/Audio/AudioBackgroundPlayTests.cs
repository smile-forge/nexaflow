using System;
using System.Threading.Tasks;
using Nexaflow.Features.Audio;             // AudioConfig
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;            // IShellServices, MediatedTaskRegistration
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>
/// Background-play toggle + tab-switch handoff logic on <see cref="AudioViewModel"/>, driven headlessly. The
/// register/unregister path is gated on a loaded track (not a playing engine), so it runs without an audio
/// device — the chrome control's factory is never invoked here (the shell realizes it). The real
/// keeps-playing-with-position behaviour is covered by the UI journey / manual run.
/// </summary>
[TestClass]
[CoversNode("audio-background-play")]
public class AudioBackgroundPlayTests
{
    private static AudioViewModel Make(IShellServices shell, AudioConfig config, params string[] paths)
        => new(paths, 0, shell, config);

    private static IShellServices ShellReturningHandle(out IDisposable handle)
    {
        var shell = Substitute.For<IShellServices>();
        handle = Substitute.For<IDisposable>();
        shell.RegisterMediatedTask(Arg.Any<MediatedTaskRegistration>()).Returns(handle);
        return shell;
    }

    [TestMethod]
    public void BackgroundPlay_Initializes_FromConfig()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell, new AudioConfig { BackgroundPlay = true }, "a.mp3");
        Assert.IsTrue(vm.BackgroundPlay);
    }

    [TestMethod]
    public void TogglingBackgroundPlay_PersistsToConfig()
    {
        var shell = Substitute.For<IShellServices>();
        var config = new AudioConfig();                 // default false
        var vm = Make(shell, config, "a.mp3");

        vm.BackgroundPlay = true;

        Assert.IsTrue(config.BackgroundPlay);
        shell.Received().SaveFeatureConfig(config);
    }

    [TestMethod]
    public void OnDeactivated_WithBackgroundPlayOff_DoesNotRegisterChromeControl()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell, new AudioConfig { BackgroundPlay = false }, "a.mp3");

        vm.OnDeactivated();

        shell.DidNotReceive().RegisterMediatedTask(Arg.Any<MediatedTaskRegistration>());
    }

    [TestMethod]
    public void OnDeactivated_WithBackgroundPlayOn_RegistersChromeControl_Once()
    {
        var shell = ShellReturningHandle(out _);
        var vm = Make(shell, new AudioConfig { BackgroundPlay = true }, "a.mp3");

        vm.OnDeactivated();

        shell.Received(1).RegisterMediatedTask(Arg.Any<MediatedTaskRegistration>());
    }

    [TestMethod]
    public void OnDeactivated_WithBackgroundPlayOn_NoTrackLoaded_DoesNotRegister()
    {
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell, new AudioConfig { BackgroundPlay = true } /* no paths → CurrentPath null */);

        vm.OnDeactivated();

        shell.DidNotReceive().RegisterMediatedTask(Arg.Any<MediatedTaskRegistration>());
    }

    [TestMethod]
    public void OnActivated_AfterBackgroundHandoff_DisposesChromeControl()
    {
        var shell = ShellReturningHandle(out var handle);
        var vm = Make(shell, new AudioConfig { BackgroundPlay = true }, "a.mp3");

        vm.OnDeactivated();     // hands off → registers, stores the handle
        vm.OnActivated();       // retakes the tab → must remove the chrome control

        handle.Received().Dispose();
    }

    [TestMethod]
    public void BackgroundHandoff_ThenReturn_ThenLeaveAgain_RegistersEachTime()
    {
        var shell = ShellReturningHandle(out _);
        var vm = Make(shell, new AudioConfig { BackgroundPlay = true }, "a.mp3");

        vm.OnDeactivated();
        vm.OnActivated();
        vm.OnDeactivated();

        shell.Received(2).RegisterMediatedTask(Arg.Any<MediatedTaskRegistration>());
    }

    [TestMethod]
    public async Task Reinitialize_SameQueue_IgnoresStaleIndex_DoesNotRewind()
    {
        // Re-selecting the tab re-pushes its ORIGINAL params (a frozen index) through Reinitialize. After the
        // user skips ahead in the background, the queue is still the same, so this must be a no-op — otherwise
        // reactivating the tab rewinds to the start track and stops playback (the reported bug).
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell, new AudioConfig(), "a.mp3", "b.mp3", "c.mp3");
        Assert.AreEqual("1 / 3", vm.QueueText);

        await vm.ReinitializeAsync(new[] { "a.mp3", "b.mp3", "c.mp3" }, startIndex: 2);

        Assert.AreEqual("1 / 3", vm.QueueText);   // did not jump to track 3
        Assert.IsFalse(vm.HasPrevious);
    }
}
