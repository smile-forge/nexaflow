using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.UIJourneys.Features.Audio;

/// <summary>
/// A recording made for the user and not saved yet opens in the player as a draft: the strip over it says so, and carries
/// the Save as that keeps it — the player has no Save of its own.
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class AudioDraftJourneyTests : UiJourneyTestBase
{
    private static readonly string Sample = TestSampleData.Files("audio").First();

    private readonly string _draft = Drafted(Sample);

    protected override string? LaunchTabKind => "Audio";

    protected override IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string>
    {
        ["paths"] = _draft,
        ["draft"] = Path.GetDirectoryName(Sample)!,
    };

    [TestMethod]
    public void ADraftRecording_SaysItIsNotSaved_AndOffersItsSaveAs()
    {
        CheckPresent("The draft says it is not saved", "Draft_Banner", 15);
        CheckPresent("Its Save as", "Draft_SaveAs");

        AssertJourney();
    }

    /// <summary>A copy of <paramref name="sample"/> in a temp folder of its own, as a draft is kept.</summary>
    private static string Drafted(string sample)
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-audio-draft-journey-").FullName;
        var draft = Path.Combine(folder, Path.GetFileName(sample));
        File.Copy(sample, draft);
        return draft;
    }
}
