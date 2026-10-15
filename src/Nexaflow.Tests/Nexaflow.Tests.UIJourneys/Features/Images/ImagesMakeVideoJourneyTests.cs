using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// A video made from the picture on screen, the user driving. In a workspace with a model that carries a picture on into
/// a clip, the image viewer's toolbar offers Video: a box for what happens, how long it runs and which way up; Start makes
/// the clip at once as one of the workspace's background tasks, and the AI bar's pill lists it, with Stop.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding Wan2.2-TI2V-5B as the app keeps it, each file verified, which the
/// app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-make-video")]
public class ImagesMakeVideoJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";

    /// <summary>Starting the clip loads the video model's prompt encoder before the job is listed.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(4);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace that makes clips, at high quality only, with Wan2.2-TI2V-5B — which starts from a picture.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir = aiDir;

        var version = FileVersionInfo.GetVersionInfo(FindAppExe()).FileVersion ?? "1.0.0.0";
        var workspaces = Path.Combine(configDir, "workcontexts");
        Directory.CreateDirectory(workspaces);
        File.WriteAllText(Path.Combine(workspaces, $"config_{version}.json"), $$"""
            {
              "ConfigName": "workcontexts",
              "FriendlyName": "Workspaces",
              "Contexts": [
                {
                  "DefaultTabs": [
                    { "PageKind": "FileSystem", "PageParams": { "mode": "path", "path": {{JsonSerializer.Serialize(TestSampleData.Path("images"))}} }, "Pane": 0, "Title": "images", "IsActive": true }
                  ],
                  "LastSessionTabs": [],
                  "Name": "Default",
                  "Color": "#5B8CFF",
                  "Icon": "⬡"
                }
              ]
            }
            """);
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "film", "ProviderName": "Nexaflow", "Model": "wan2.2-ti2v-5b", "Kind": "VideoGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "HighQualityVideoGeneration": [ "film" ]
              }
            }
            """);
    }

    [TestMethod]
    public void AVideoMadeFromThePictureOnScreen_RunsAsABackgroundTask()
    {
        var file = Path.GetFileName(TestSampleData.Files("images").First());
        var view = OpenFileVia(TestSampleData.Path("images"), file, "ShowImageAction", "ImageView");
        Assert.IsNotNull(view, "ImageView did not open via the 'As Image' action.");

        // The box opens on the toolbar; nothing starts until it is said what happens.
        CheckDoes("Video opens the box for the clip", "Image_MakeVideo", BoxIsOpen);
        Check("Start waits for what happens in it", () => FindInAppWindows("Image_MakeVideoStart")?.IsEnabled == false);
        Check("Cancel puts the box away", () => PressInDialog("Image_MakeVideoCancel"));
        CheckDoes("Video opens it again", "Image_MakeVideo", BoxIsOpen);

        Check("What happens is typed", () => TypeInPopup("Image_MakeVideoDescription", "The light moves slowly across the scene."));
        Check("A length is picked", PickFirstLength);
        Check("Portrait is chosen", () => SelectRadio("Image_MakeVideoPortrait"));
        Check("Landscape is chosen back", () => SelectRadio("Image_MakeVideoLandscape"));
        Check("Start is offered now", () => WaitForFs(() => FindInAppWindows("Image_MakeVideoStart")?.IsEnabled == true, 2));

        // Started, the box goes and the clip is made in the background, listed from the AI bar's pill — and stopped there,
        // since a whole clip takes minutes.
        Check("Start closes the box and sets the clip making", () =>
            PressInDialog("Image_MakeVideoStart") && WaitForId("AiBar_BackgroundTasks", Affordable(60)) is not null);
        CheckDoes("The pill lists it, with Stop", "AiBar_BackgroundTasks", () => WaitForId("AiBar_BackgroundTaskStop", 10) is not null);
        CheckDoes("Stop ends it, and the pill goes with it", "AiBar_BackgroundTaskStop",
                  () => WaitForGone("AiBar_BackgroundTaskStop", 20) && WaitForGone("AiBar_BackgroundTasks", 20));

        AssertJourney();
    }

    /// <summary>The box is open: its description waits in its own popup window.</summary>
    private bool BoxIsOpen() => WaitFor(() => FindInAppWindows("Image_MakeVideoDescription"), 5) is not null;

    /// <summary>Picks the first length the model makes.</summary>
    private bool PickFirstLength()
    {
        if (FindInAppWindows("Image_MakeVideoLength")?.AsComboBox() is not { } lengths) return false;
        var picked = lengths.Select(0);
        return WaitForFs(() => lengths.SelectedItem?.Name == picked.Name, 2);
    }
}
