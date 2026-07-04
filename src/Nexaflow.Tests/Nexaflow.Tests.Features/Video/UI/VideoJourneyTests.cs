using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Video.UI;

/// <summary>
/// One-pass UI journey for the Video player: opens a sample clip via the explicit <b>"Play Video"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the transport-bar controls and
/// the overlay toggles (scene strip, info panel) — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The bundled <c>minimal.mp4</c> is a structurally-valid container that can't actually decode, so the VM
/// surfaces an error rather than crashing; the transport bar is still present (its host has no error-gated
/// visibility), and every engine-bound command no-ops safely when there's no live player. The fullscreen
/// toggle is only <i>checked for presence</i> — invoking it tears off a separate window mid-journey — and the
/// subtitle / centre-play affordances are conditional (subtitle tracks / a decoded first frame) so they are
/// not asserted here.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class VideoJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Video_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("video").First());
        var view = OpenFileVia(TestSampleData.Path("video"), file, "Play Video", "VideoView");
        Assert.IsNotNull(view, "VideoView did not open via the 'Play Video' action.");

        // Transport bar — always present (its host isn't gated on playback / error state).
        CheckPresent("Seek bar", "Video_SeekBar");
        CheckPresent("Volume slider", "Video_Volume");
        CheckInvoke("Play / Pause", "Video_PlayPause");
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

        AssertJourney();
    }
}
