using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// Pictures drawn into a canvas, the user driving. In a workspace that draws pictures with Z-Image Turbo, Artist's start screen draws
/// what is described into the canvas it makes — shown in the layers panel with its Stop while it draws, its first layer once it
/// arrives — and Generate layer draws a layer above the active one, which can be set up and let go, or stopped while it draws.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding Z-Image Turbo as the app keeps it, each file verified, which the
/// app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-start")]
public class ImagesCanvasGenerateJourneyTests : ImageEditJourneyBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";

    protected override string? LaunchTabKind => "Artist";

    /// <summary>The first picture loads Z-Image's parts before it draws.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(6);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>The workspace draws pictures with Z-Image Turbo.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir = aiDir;
        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "draw", "ProviderName": "Nexaflow", "Model": "z-image-turbo", "Kind": "ImageGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "ImageGeneration": [ "draw" ]
              }
            }
            """);
    }

    [TestMethod]
    public void APictureDrawnIntoACanvas_ArrivesAsALayer_AndGenerateLayerCanBeLetGoOrStopped()
    {
        CheckPresent("The start screen shows", "Images_Start", 15);
        Check("What to draw is described", () => TypeInto("Images_GeneratePrompt", "A lighthouse on a cliff at dusk, oil painting"));
        CheckDoes("Generate opens the canvas, the picture shown drawing in its layers", "Images_StartGenerate",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && WaitForFs(() => Exists("Image_LayersPendingStop"), 5));
        Check("The picture arrives as the canvas's first layer", () => WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 2, Affordable(240)));

        CheckPresent("Generate layer sits above the layers", "Image_LayersPictures");
        CheckDoes("Generate layer is set up", "Image_LayersGenerate", () => WaitForFs(() => Exists("Image_LayersGenerateGo"), 3));
        CheckDoes("and let go", "Image_LayersGenerateCancel", () => WaitForGone("Image_LayersGenerateGo", 3));
        CheckDoes("Generate layer again", "Image_LayersGenerate", () => WaitForFs(() => Exists("Image_LayersGenerateGo"), 3));
        Check("What to draw is described", () => TypeInto("Images_GeneratePrompt", "Gulls in flight"));
        CheckDoes("Set going, it shows drawing", "Image_LayersGenerateGo", () => WaitForFs(() => Exists("Image_LayersPendingStop"), 5));
        CheckDoes("Stopped, it goes, adding nothing", "Image_LayersPendingStop", () => WaitForGone("Image_LayersPendingStop", 10) && LayerCount() == 2);

        AssertJourney();
    }

    private int LayerCount() => FindInAppWindows("Image_LayersList")?.AsListBox().Items.Length ?? -1;
}
