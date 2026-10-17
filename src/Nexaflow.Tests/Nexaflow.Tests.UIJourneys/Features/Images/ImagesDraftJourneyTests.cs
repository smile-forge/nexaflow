using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// A picture made for the user and not saved yet — what a generation tool asked to save nowhere, or the assistant's
/// cut_out, opens — shown as a draft Images tab with its Save as. The draft is a sample picture copied into a temp folder
/// of its own and opened through the --openTab deep link with its "draft" parameter, as the shell opens one. Save as
/// opens the shell's file picker, a window of its own, so the journey stops at the button.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class ImagesDraftJourneyTests : UiJourneyTestBase
{
    private static readonly string Sample = TestSampleData.Files("images").First();

    private readonly string _draft = Drafted(Sample);

    protected override string? LaunchTabKind => "Images";

    protected override IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string>
    {
        ["paths"] = _draft,
        ["draft"] = Path.GetDirectoryName(Sample)!,
    };

    [TestMethod]
    public void ADraft_ShowsItsSaveAs()
    {
        Check("The draft opens in the image viewer", () => WaitForId("ImageView", 15) is not null);
        CheckPresent("The draft's Save as", "Draft_SaveAs");

        AssertJourney();
    }

    /// <summary>A copy of <paramref name="sample"/> in a temp folder of its own, as a draft is kept.</summary>
    private static string Drafted(string sample)
    {
        var folder = Directory.CreateTempSubdirectory("nexaflow-draft-journey-").FullName;
        var draft = Path.Combine(folder, Path.GetFileName(sample));
        File.Copy(sample, draft);
        return draft;
    }
}
