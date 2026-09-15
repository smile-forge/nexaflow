using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Video.UI;

/// <summary>
/// One-pass UI journey for the Video player: opens a sample clip via the explicit <b>"Play Video"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the transport-bar controls and
/// the overlay toggles (scene strip, info panel) — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The bundled <c>minimal.mp4</c> is a 24-byte container that exists to prove routing, not playback: it
/// can't decode. Every engine-bound command still no-ops safely with no live player, and the transport bar
/// is present throughout (its host has no error-gated visibility). Each speed-menu entry is picked and read
/// back off the speed button. Fullscreen is round-tripped — opened, its window found, and left with Escape
/// before anything else runs, since it is a topmost window over the whole screen. The subtitle menu, the
/// centre-play button and the scene-strip thumbnails all need something decoded (text tracks / a first frame
/// / keyframes), so they are not asserted here.
/// </para>
/// <para>
/// <b>Play/Pause goes last, and its outcome is deliberately permissive.</b> Pressing it is what makes the
/// engine actually try to decode, and the feature has two legitimate answers: a soft error painted inside
/// the tab, or a fatal one — which by design shows a notice and <i>closes the tab</i>
/// (<c>VideoTabRegistration</c>: "a fatal engine error closes the tab with a notice rather than leaving a
/// broken player open"). Which one libVLC raises for this stub is a race, so with the press in the middle
/// about one run in four lost the tab and reported eight missing buttons — eight symptoms of one designed
/// behaviour. Pressed last, the only thing asserted after it is what holds either way: the app is still up.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("video")]
public class VideoJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("video-ui")]
    public void Video_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("video").First());
        var view = OpenFileVia(TestSampleData.Path("video"), file, "Play Video", "VideoView");
        Assert.IsNotNull(view, "VideoView did not open via the 'Play Video' action.");

        // Transport bar — always present (its host isn't gated on playback / error state).
        CheckPresent("Seek bar", "Video_SeekBar");
        CheckPresent("Volume slider", "Video_Volume");
        CheckInvoke("Step back", "Video_StepBack");
        CheckInvoke("Step forward", "Video_StepForward");
        CheckInvoke("Mute", "Video_Mute");

        // Speed pop-up: open it, then close it again so a left-over menu doesn't mask later controls.
        CheckInvoke("Speed menu open", "Video_Speed");
        CheckInvoke("Speed menu close", "Video_Speed");

        // Every entry in the speed menu, each proven by the rate it leaves on the speed button. 1× goes last so
        // the player is back at normal speed for everything after. Picking an entry closes the menu itself.
        foreach (var (id, label) in new[]
                 { ("Video_Speed_2x", "2×"), ("Video_Speed_1_5x", "1.5×"), ("Video_Speed_1_25x", "1.25×"), ("Video_Speed_1x", "1×") })
        {
            Check($"Speed {label} is picked from the menu", () => PickSpeed(id));
            Check($"The speed button reads {label}", () =>
                WaitForFs(() => FindInAppWindows("Video_Speed")?.Name == label, 3));
        }

        // Overlay toggles (in-tab state — safe to invoke).
        CheckInvoke("Scene-strip toggle", "Video_SceneStripToggle");
        CheckInvoke("Info-panel toggle", "Video_InfoToggle");

        // The picture's click-catcher. Present only: pressing it is Play, which has to go last (class summary).
        CheckPresent("Picture click-catcher", "Video_ClickCatcher");

        // Fullscreen, round-tripped. It opens a separate topmost window over the whole screen, so it has to be
        // closed again before anything else runs — Escape is the window's own way out.
        CheckInvoke("Fullscreen toggle", "Video_Fullscreen");
        Check("Fullscreen opens its window, with the picture's click-catcher", () =>
            WaitForFs(() => FindInAppWindows("Video_FullscreenClickCatcher") is not null, 5));
        Check("Escape leaves fullscreen", LeaveFullScreen);

        // Last, for the reason in the class summary: this is the press that can legitimately take the tab
        // away. Both outcomes are correct behaviour for a file that cannot decode, so the only thing worth
        // asserting afterwards is that neither of them took the application with it.
        CheckInvoke("Play / Pause", "Video_PlayPause");
        Check("The app survives pressing play on a file that cannot decode", () => !App.HasExited);

        AssertJourney();
    }

    /// <summary>Opens the speed menu and presses one of its entries.</summary>
    private bool PickSpeed(string entryId)
    {
        var speed = WaitForId("Video_Speed", 5);
        if (speed is null) return false;
        speed.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        var entry = WaitFor(() => FindInAppWindows(entryId), 4);
        if (entry is null) return false;
        entry.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// Presses Escape in the fullscreen window and waits for it to go. If Escape did not land, the window's
    /// own fullscreen toggle is pressed instead, so a missed key never leaves a topmost window over the rest.
    /// </summary>
    private bool LeaveFullScreen()
    {
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed();
        if (WaitForFs(() => FindInAppWindows("Video_FullscreenClickCatcher") is null, 3)) return true;

        // The tab's own toggle is bound to the same view-model, so pressing it takes fullscreen down too.
        WaitForId("Video_Fullscreen", 2)?.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return false;
    }
}
