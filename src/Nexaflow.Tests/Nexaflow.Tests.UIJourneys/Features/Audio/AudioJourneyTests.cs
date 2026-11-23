using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio.UI;

/// <summary>
/// One-pass UI journey for the Audio player: opens a sample track via the explicit <b>"ShowAudioAction"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the drawer toggles, tag-editor
/// buttons, and always-present transport controls — soft-asserting each so a single gap doesn't hide the rest.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("audio")]
public class AudioJourneyTests : UiJourneyTestBase
{
    private static readonly string Sample = TestSampleData.Files("audio").First();

    private readonly string _draft = Drafted(Sample);

    // The app opens into a recording made for the user and not saved yet, so its draft strip is the first thing there is to
    // check; the player's own controls follow, on a track opened from the file browser.
    protected override string? LaunchTabKind => "Audio";

    protected override IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string>
    {
        ["paths"] = _draft,
        ["draft"] = Path.GetDirectoryName(Sample)!,
    };

    /// <summary>A recording not yet saved, then the player on a saved track.</summary>
    [TestMethod]
    [CoversNode("audio-ui")]
    [CoversNode("made-file-drafts")]
    public void Audio_Journey()
    {
        RunStep("a draft recording says it is not saved, and offers its Save as", DraftStrip);
        RunStep("the player's drawers, tag editor and transport", PlayerControls);
        AssertJourney();
    }

    /// <summary>The strip over a draft says so, and carries the Save as that keeps it — the player has no Save of its own.</summary>
    private void DraftStrip()
    {
        CheckPresent("The draft says it is not saved", "Draft_Banner", 15);
        CheckPresent("Its Save as", "Draft_SaveAs");
    }

    /// <summary>
    /// A copy of <paramref name="sample"/> in a temp folder of its own, as a draft is kept — under a name of its own, because the
    /// draft's tab is titled with it and the journey finds the sample's row in the file browser by name.
    /// </summary>
    private static string Drafted(string sample)
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-audio-draft-journey-").FullName;
        var draft = Path.Combine(folder, "unsaved-" + Path.GetFileName(sample));
        File.Copy(sample, draft);
        return draft;
    }

    private void PlayerControls()
    {
        var file = Path.GetFileName(TestSampleData.Files("audio").First());
        var view = OpenFileVia(TestSampleData.Path("audio"), file, "ShowAudioAction", "AudioView");
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
    }
}
