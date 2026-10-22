using System.Collections.Generic;
using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// The editor's AI tools where their models are here: AI enhance with its strength and faces, AI upscale, picking things
/// out to cut out or retouch, and AI fill — each made with nothing offered to download or try again. The edit journey
/// covers the same controls waiting, disabled, for models that are not here.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding the picture tools, picture selection and picture erasing as the
/// app keeps them, which the app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it, or with one of them missing —
/// the editor then offers it for download — the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-edit-models")]
public class ImagesEditAiJourneyTests : ImageEditJourneyBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";

    /// <summary>Each tool loads its models the first time it is used.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(4);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir = aiDir;
    }

    [TestMethod]
    [CoversNode("images-edit-models")]
    public void Image_EditAiTools_WorkWhereTheirModelsAreHere()
    {
        var folder = Path.Combine(TestTemp.Root, "nexaflow_edit_ai_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var photo = Path.Combine(folder, OrientedPhotoSample.FileName);
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), photo);
            Assert.IsNotNull(OpenFileVia(folder, OrientedPhotoSample.FileName, "ShowImageAction", "ImageView"), "ImageView did not open via the 'As Image' action.");
            CheckDoes("Edit opens the editor", "Image_EditMode", () => WaitForFs(() => Exists("ImageEditView"), 5));

            // Adjust: AI enhance, made once its strength and faces are set; the photo can be saved once it is.
            Check("Adjust panel", () => SelectRadio("Image_EditToolAdjust"));
            NeedsHere("Image_RestoringModelsDownload", "the picture tools");
            Check("AI enhance turns on, its strength and faces with it", () => SetToggle("Image_EditAiEnhance", true) && WaitForFs(() => Exists("Image_EditAiStrength"), 3));
            Check("AI strength moves", () => MoveSlider("Image_EditAiStrength"));
            Check("Repair faces toggles", () => SetToggle("Image_EditAiFaces", false) && SetToggle("Image_EditAiFaces", true));
            Check("The photo is restored: Save is ready, and nothing is offered to download or try again",
                  () => WaitForFs(() => Enabled("Image_EditSave"), Affordable(60)) && !Exists("Image_RestoreAction"));

            // Resize: AI upscale, on the restoration.
            Check("AI upscale twice", () => SelectRadio("Image_EditToolResize") && SelectRadio("Image_EditUpscaleTwice"));
            Check("AI upscale four times", () => SelectRadio("Image_EditUpscaleFourTimes"));
            Check("AI upscale off, and AI enhance off from Adjust", () => SelectRadio("Image_EditUpscaleOff")
                  && SelectRadio("Image_EditToolAdjust") && SetToggle("Image_EditAiEnhance", false));

            // Cut out: a click picks what is under it; its background removed and put back.
            Check("Cut out panel", () => SelectRadio("Image_EditToolCutOut") && FitCanvas());
            NeedsHere("Image_PickingModelsDownload", "picture selection");
            Check("A phrase is typed, and Find looks for it", () => TypeInto("Image_CutOutPhrase", "square") && Enabled("Image_CutOutFind"));
            CheckInvoke("Find", "Image_CutOutFind");
            Check("A click on the photo picks what is under it, with nothing to try again",
                  () => ClickCanvas() && WaitForFs(() => Enabled("Image_CutOutCopy"), Affordable(30)) && !Exists("Image_CutOutAction"));
            CheckPresent("Save the cut-out", "Image_CutOutSave");
            CheckDoes("Remove background keeps what is picked", "Image_CutOutRemoveBackground", () => WaitForFs(() => Exists("Image_CutOutKeepBackground"), 5));
            CheckDoes("Keep background puts it back", "Image_CutOutKeepBackground", () => WaitForGone("Image_CutOutKeepBackground", 5));

            // Retouch: something picked, erased and filled by AI as a step of the edits.
            Check("Retouch, picking", () => SelectRadio("Image_EditToolRetouch") && SelectRadio("Image_EraseMarkPick"));
            NeedsHere("Image_ErasingModelsDownload", "picture erasing");
            Check("A phrase is typed for Find", () => TypeInto("Image_ErasePhrase", "square") && Enabled("Image_EraseFind"));
            CheckInvoke("Find", "Image_EraseFind");
            Check("A click picks what to erase, with nothing to try again",
                  () => ClickCanvas() && WaitForFs(() => Enabled("Image_EraseApply"), Affordable(30)) && !Exists("Image_ErasePickAction"));
            Check("AI fill erases it as a step: Save is ready, with nothing to download or try again",
                  () => SelectRadio("Image_EraseFillAi") && PressNth("Image_EraseApply", 0)
                        && WaitForFs(() => Enabled("Image_EditSave"), Affordable(60)) && !Exists("Image_EraseAction"));
            CheckInvoke("Undo takes the step back", "Image_EditUndo");

            AssertJourney();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }

    /// <summary>The panel offers <paramref name="what"/> for download: the AI folder lacks it, and the journey cannot say more.</summary>
    private void NeedsHere(string offer, string what)
    {
        if (Exists(offer)) Assert.Inconclusive($"{AiDirVariable} holds no {what}: the editor offers it for download.");
    }
}
