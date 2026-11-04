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
/// The generative edits, the user driving, each where its kind of edit lives. In a workspace that draws and edits pictures with
/// FLUX.2 [klein], a photo open in the Artist is expanded from Reshape — Generate the new area switched on, the crop frame
/// dragged past the photo's edge, and what it reaches generated, the photo made a layered picture; then Adjust's Change picture
/// makes it night, set up for the active layer and then the whole picture; then everything selected, Select's Generate fill,
/// set up and let go, then set going, draws over it; and last the Layers panel's Canvas size grows the canvas with its new area
/// generated. Each is shown drawing in the layers panel and arrives as a layer, the picture beneath kept.
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

    /// <summary>The photo edited: a copy in a folder of the journey's own, since an edit writes beside it.</summary>
    private readonly string _photo = CopyOf(TestSampleData.Path("images", "photo_17.png"));

    private string _aiDir = "";

    protected override string? LaunchTabKind => "Artist";

    protected override IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string> { ["path"] = _photo };

    /// <summary>The first edit loads klein's parts before it draws.</summary>
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

    [TestCleanup]
    public void RemovePhoto()
    {
        try { Directory.Delete(Path.GetDirectoryName(_photo)!, recursive: true); } catch { }
    }

    [TestMethod]
    public void APhoto_IsExpandedChangedFilledAndGrown_EachWhereItsEditLives_EachANewLayer()
    {
        Check("The photo opens in the Artist, on Reshape", () => WaitForId("ImageEditView", 15) is not null && WaitForId("Image_GroupCropExpand", 10) is not null);
        Check("Generate the new area lets the frame past the photo's edge", () => SetToggle("Image_EditCropGenerate", true) && WaitForFs(() => Exists("Image_EditCropGeneratePrompt"), 3));
        Check("Generate waits for the frame to reach past it", () => WaitForFs(() => !Enabled("Image_EditCropGenerateGo"), 2));
        Check("The frame is dragged past the photo's right edge", () => FitCanvas() && DragOnCanvas(0, 0, 60, 0) && WaitForFs(() => Enabled("Image_EditCropGenerateGo"), 3));
        Check("What the new area shows is said", () => TypeInto("Image_EditCropGeneratePrompt", "More of the same street"));
        CheckDoes("Generated, the photo is made a layered picture and the new area arrives as a layer", "Image_EditCropGenerateGo",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 2, Affordable(240)));
        Check("On a layered picture, Reshape offers no expansion: its canvas grows in Layers", () => WaitForGone("Image_GroupCropExpand", 3));

        Check("Adjust is taken up", () => SelectRadio("Image_EditToolAdjust"));
        CheckPresent("Change picture is a group of Adjust", "Image_GroupAdjustChange");
        Check("for the active layer alone", () => SelectRadio("Image_EditChangeLayer"));
        Check("or the whole picture", () => SelectRadio("Image_EditChangeWhole"));
        Check("What to change is said", () => TypeInto("Image_EditChangePrompt", "Make it night time, the street lamps lit"));
        CheckDoes("Generated, it shows drawing, and arrives as a layer", "Image_EditChangeGo",
                  () => WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 3, Affordable(120)));

        Check("Everything is selected", SelectAllOnCanvas);
        Check("Select is taken up, Generate fill offered for what is selected", () => SelectRadio("Image_EditToolSelect") && WaitForFs(() => Enabled("Image_SelectGenerateFill"), 3));
        CheckDoes("Generate fill is set up beside the other ways with a selection", "Image_SelectGenerateFill", () => WaitForFs(() => Exists("Image_SelectFillGo"), 3));
        CheckDoes("and let go", "Image_SelectFillCancel", () => WaitForGone("Image_SelectFillGo", 3));
        CheckDoes("Generate fill again", "Image_SelectGenerateFill", () => WaitForFs(() => Exists("Image_SelectFillGo"), 3));
        Check("What to generate in it is described", () => TypeInto("Image_SelectFillPrompt", "The same street in deep snow"));
        CheckDoes("Generated, the fill arrives as a layer", "Image_SelectFillGo",
                  () => WaitForGone("Image_SelectFillGo", 3) && WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 4, Affordable(120)));

        Check("The canvas section opens", () => Expand("Image_LayersCanvasSection"));
        Check("A wider canvas", () => TypeInto("Image_LayersCanvasWidth", "320"));
        Check("Generate the new area, in place of Resize canvas", () => SetToggle("Image_LayersCanvasGenerate", true)
                                                                         && WaitForFs(() => Exists("Image_LayersCanvasGenerateGo") && !Exists("Image_LayersCanvasApply"), 3));
        Check("What it shows is said", () => TypeInto("Image_LayersCanvasGeneratePrompt", "More of the same street"));
        CheckDoes("Generated, what the canvas gains arrives as a layer", "Image_LayersCanvasGenerateGo",
                  () => WaitForFs(() => !Exists("Image_LayersPendingStop") && LayerCount() == 5, Affordable(120)));

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

    /// <summary>Ctrl+A over the canvas, Adjust in hand so a click picks nothing: everything selected.</summary>
    private bool SelectAllOnCanvas()
    {
        if (!ClickCanvas()) return false;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>A copy of <paramref name="sample"/> in a temp folder of its own.</summary>
    private static string CopyOf(string sample)
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-ai-edits-journey-").FullName;
        var copy = Path.Combine(folder, Path.GetFileName(sample));
        File.Copy(sample, copy);
        return copy;
    }
}
