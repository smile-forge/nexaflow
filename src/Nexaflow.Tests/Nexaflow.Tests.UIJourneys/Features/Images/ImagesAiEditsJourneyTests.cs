using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// The AI edits of a layered picture, the user driving. In a workspace that draws and edits pictures with FLUX.2 [klein], a
/// picture is drawn into a canvas from the start screen; then Change makes it night as a new layer — set up for the active
/// layer, then the whole picture — Expand grows the canvas and draws its border as another, and Fill selection, set up and
/// let go, then set going over what is selected, draws a third: each shown drawing in the layers panel, each a layer once
/// it arrives, the picture beneath kept.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding FLUX.2 [klein] 4B as the app keeps it, each file verified, which
/// the app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-ai-edits")]
public class ImagesAiEditsJourneyTests : ImageEditJourneyBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";

    protected override string? LaunchTabKind => "Artist";

    /// <summary>The first picture loads klein's parts before it draws.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(6);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>The workspace draws and edits pictures with FLUX.2 [klein].</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        OwnedModelFolder.RequirePictureCheck(aiDir, AiDirVariable);
        _aiDir = aiDir;
        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "klein", "ProviderName": "Nexaflow", "Model": "flux2-klein-4b", "Kind": "ImageGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "ImageGeneration": [ "klein" ],
                "ImageEditing": [ "klein" ]
              }
            }
            """);
    }

    [TestMethod]
    public void APicture_IsChangedExpandedAndFilled_EachEditANewLayer_ThePictureKeptBeneath()
    {
        CheckPresent("The start screen shows", "Images_Start", 15);
        Check("What to draw is described", () => TypeInto("Images_GeneratePrompt", "A red-brick cafe on a street corner, people at tables outside"));
        CheckDoes("Generate opens the canvas, the picture shown drawing in its layers", "Images_StartGenerate",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && WaitForFs(() => Exists("Image_LayersPendingStop"), 5));
        Check("The picture arrives as the canvas's first layer", () => WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 2, Affordable(240)));
        CheckPresent("The AI edits sit above the layers", "Image_LayersAiEdits");

        CheckDoes("Change is set up", "Image_LayersAiChange", () => WaitForFs(() => Exists("Image_LayersAiGo"), 3));
        CheckDoes("for the active layer alone", "Image_LayersAiActiveLayer", () => true);
        CheckDoes("or the whole picture", "Image_LayersAiWholePicture", () => true);
        Check("What to change is said", () => TypeInto("Image_LayersAiPrompt", "Make it night time, the street lamps lit"));
        CheckDoes("Set going, it shows drawing, and arrives as a layer", "Image_LayersAiGo",
                  () => WaitForGone("Image_LayersAiGo", 3) && WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 3, Affordable(120)));

        CheckDoes("Expand is set up", "Image_LayersAiExpand", () => WaitForFs(() => Exists("Image_LayersAiExpandLeft"), 3));
        Check("Grown 128 pixels to the left", () => TypeInto("Image_LayersAiExpandLeft", "128"));
        Check("none at the top", () => TypeInto("Image_LayersAiExpandTop", "0"));
        Check("128 to the right", () => TypeInto("Image_LayersAiExpandRight", "128"));
        Check("none at the bottom", () => TypeInto("Image_LayersAiExpandBottom", "0"));
        CheckDoes("Set going, the border arrives as a layer", "Image_LayersAiGo",
                  () => WaitForGone("Image_LayersAiGo", 3) && WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 4, Affordable(120)));

        Check("Everything is selected", SelectAllOnCanvas);
        CheckDoes("Fill selection is set up", "Image_LayersAiFill", () => WaitForFs(() => Exists("Image_LayersAiGo"), 3));
        CheckDoes("and let go", "Image_LayersAiCancel", () => WaitForGone("Image_LayersAiGo", 3));
        CheckDoes("Fill selection again", "Image_LayersAiFill", () => WaitForFs(() => Exists("Image_LayersAiGo"), 3));
        Check("What to draw in it is described", () => TypeInto("Image_LayersAiPrompt", "The same street in deep snow"));
        CheckDoes("Set going, the fill arrives as a layer", "Image_LayersAiGo",
                  () => WaitForGone("Image_LayersAiGo", 3) && WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 5, Affordable(120)));

        AssertJourney();
    }

    private int LayerCount() => FindInAppWindows("Image_LayersList")?.AsListBox().Items.Length ?? -1;

    /// <summary>Opens a section of the layers panel.</summary>
    private bool Expand(string automationId)
    {
        if (FindInAppWindows(automationId)?.Patterns.ExpandCollapse.PatternOrDefault is not { } section) return false;
        section.Expand();
        return WaitForFs(() => section.ExpandCollapseState.Value == ExpandCollapseState.Expanded, 2);
    }

    /// <summary>Ctrl+A over the canvas: everything selected — seen in Fill selection being offered.</summary>
    private bool SelectAllOnCanvas()
    {
        if (!Expand("Image_LayersPixelsSection") || !ClickCanvas()) return false;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => Enabled("Image_LayersAiFill"), 2);
    }
}
