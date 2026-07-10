using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio.UI;

/// <summary>
/// One-pass UI journey for the Audio player: opens a sample track via the explicit <b>"As Audio"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the drawer toggles, tag-editor
/// buttons, and always-present transport controls — soft-asserting each so a single gap doesn't hide the rest.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("audio")]
public class AudioJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Audio_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("audio").First());
        var view = OpenFileVia(TestSampleData.Path("audio"), file, "As Audio", "AudioView");
        Assert.IsNotNull(view, "AudioView did not open via the 'As Audio' action.");

        // Side drawer toggles (Lyrics then back to Tags so the tag-editor buttons are visible).
        CheckInvoke("Lyrics toggle", "Audio_LyricsToggle");
        Check("Lyrics panel shows", () =>
            WaitForId("LyricsList", 4) is not null || WaitForName("No lyrics found.", 4) is not null);
        CheckInvoke("Tags toggle", "Audio_TagsToggle");

        // Tag editor (Tags panel).
        CheckPresent("Tag: change art", "Audio_TagChangeArt");
        CheckInvoke("Tag: remove art", "Audio_TagRemoveArt");
        CheckPresent("Tag: save", "Audio_TagSave");

        // Transport — always present for a single track.
        CheckPresent("Volume slider", "Audio_Volume");
        CheckInvoke("Play / Pause", "Audio_PlayPause");
        CheckInvoke("Stop", "Audio_Stop");

        AssertJourney();
    }
}
