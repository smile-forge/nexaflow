using System.IO;
using System.Linq;
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
/// is present throughout (its host has no error-gated visibility). The fullscreen toggle is only
/// <i>checked for presence</i> — invoking it tears off a separate window mid-journey — and the subtitle /
/// centre-play affordances are conditional (subtitle tracks / a decoded first frame) so they are not
/// asserted here.
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

        // Overlay toggles (in-tab state — safe to invoke). Fullscreen opens a separate window, so only check
        // it is present without invoking it.
        CheckInvoke("Scene-strip toggle", "Video_SceneStripToggle");
        CheckInvoke("Info-panel toggle", "Video_InfoToggle");
        CheckPresent("Fullscreen toggle", "Video_Fullscreen");

        // Last, for the reason in the class summary: this is the press that can legitimately take the tab
        // away. Both outcomes are correct behaviour for a file that cannot decode, so the only thing worth
        // asserting afterwards is that neither of them took the application with it.
        CheckInvoke("Play / Pause", "Video_PlayPause");
        Check("The app survives pressing play on a file that cannot decode", () => !App.HasExited);

        AssertJourney();
    }
}
