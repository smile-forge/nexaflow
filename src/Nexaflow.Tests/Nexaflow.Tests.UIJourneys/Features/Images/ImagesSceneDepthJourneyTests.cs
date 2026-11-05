using System.Collections.Generic;
using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// What the picture's scene depth draws in Artist, where its model is here: a lens blur focused on a point or a thing clicked,
/// a rectangle or an outline drawn, its band of distance dragged, its aperture and highlights' shape; haze, its thickness,
/// start and colour; selecting by a span of distance in Select and in Retouch; and the depth as a layer and as a mask — each
/// made with nothing offered to download or try again.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding scene depth and picture selection as the app keeps them, which the app
/// is started on as its <c>NEXAFLOW_AI_DIR</c>; without it, or with one of them missing — the editor then offers it for download
/// — the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-scene-depth")]
public class ImagesSceneDepthJourneyTests : ImageEditJourneyBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";

    /// <summary>The depth is estimated the first time it is asked for, and picture selection loads for the subject.</summary>
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
    [CoversNode("images-scene-depth-lens-blur")]
    [CoversNode("images-scene-depth-haze")]
    [CoversNode("images-selection-distance")]
    [CoversNode("images-scene-depth-layers")]
    public void Image_SceneDepthTools_WorkWhereTheirModelIsHere()
    {
        var folder = Path.Combine(TestTemp.Root, "nexaflow_scene_depth_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var photo = Path.Combine(folder, OrientedPhotoSample.FileName);
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), photo);
            Assert.IsNotNull(OpenFileVia(folder, OrientedPhotoSample.FileName, "ShowImageAction", "ImageView"), "ImageView did not open via the 'As Image' action.");
            CheckDoes("Edit opens the photo in an Artist tab", "Image_Edit", () => WaitForFs(() => Exists("ArtistView") && Exists("ImageEditView"), 8));

            // Adjust: a lens blur, focused on a point clicked, a thing clicked, a rectangle and an outline drawn, and its band dragged.
            Check("Adjust panel, its Depth opened", () => SelectRadio("Image_EditToolAdjust") && FitCanvas() && OpenGroup("Image_GroupAdjustDepth"));
            NeedsHere("Image_PictureModelsDownload", "AI picture models");
            Check("Lens blur turns on, its controls with it", () => SetToggle("Image_DepthLensBlur", true) && WaitForFs(() => Exists("Image_DepthAperture"), 3));
            Check("The depth is estimated: Save is ready, with nothing to download or try again",
                  () => WaitForFs(() => Enabled("Image_EditSave"), Affordable(60)) && !Exists("Image_SceneDepthAction"));
            // Each focus is shown to land: the band parked far out first, then moved by the focus. A focus works its part out
            // in the background — a thing's a while on a slow machine — so each is waited for before the next is made. The
            // sample is small on the canvas, so what is drawn is drawn on it, not on the canvas round it.
            (double, double) Band() => (Value("Image_DepthFocusBandLower"), Value("Image_DepthFocusBandUpper"));
            double Value(string id) => FindInAppWindows(id)?.Patterns.RangeValue.PatternOrDefault?.Value.Value ?? double.NaN;
            bool Parked() => MoveSliderTo("Image_DepthFocusBandUpper", 90) && MoveSliderTo("Image_DepthFocusBandLower", 80);
            bool Refocused() => WaitForFs(() => Band() != (80, 90), Affordable(60));
            Check("Click for depth: a click on the picture focuses on that distance", () => SelectRadio("Image_DepthFocusRange") && Parked()
                  && SelectRadio("Image_DepthFocusPoint") && ClickCanvas() && SelectRadio("Image_DepthFocusRange") && Refocused());
            Check("From selection, with nothing selected, hides the band and waits", () => SelectRadio("Image_DepthFocusSelection")
                  && WaitForFs(() => !Exists("Image_DepthFocusBand"), 3) && !Exists("Image_FocusedSubjectAction"));
            Check("Specify depth shows the band", () => SelectRadio("Image_DepthFocusRange") && WaitForFs(() => Exists("Image_DepthFocusBand"), 3));
            Check("The band in focus is there, and its ends move", () => Exists("Image_DepthFocusBand")
                  && MoveSliderTo("Image_DepthFocusBandUpper", 60) && MoveSliderTo("Image_DepthFocusBandLower", 20));
            Check("Narrowed, both ends of the band still move", () => MoveSliderTo("Image_DepthFocusBandUpper", 22)
                  && MoveSliderTo("Image_DepthFocusBandLower", 21) && MoveSliderTo("Image_DepthFocusBandUpper", 40));
            Check("Aperture moves", () => MoveSlider("Image_DepthAperture"));
            Check("Highlights hexagonal, then round", () => SelectRadio("Image_DepthBokehHexagon") && SelectRadio("Image_DepthBokehCircle"));

            // Haze, thickening with distance.
            Check("Haze turns on, its controls with it", () => SetToggle("Image_DepthHaze", true) && WaitForFs(() => Exists("Image_DepthHazeDensity"), 3));
            Check("Its thickness moves", () => MoveSlider("Image_DepthHazeDensity"));
            Check("Its start moves", () => MoveSlider("Image_DepthHazeStart"));
            Check("It takes the ink, then the scene's own colour again", () => SelectRadio("Image_DepthHazeInk") && SelectRadio("Image_DepthHazeScene"));
            Check("Lens blur and haze off", () => SetToggle("Image_DepthHaze", false) && SetToggle("Image_DepthLensBlur", false));

            // Select: what lies within a span of distance, the span moved after it is selected.
            Check("Select by distance selects the span at once, with no press: Copy is ready", () => SelectRadio("Image_EditToolSelect")
                  && SelectRadio("Image_SelectDistance") && WaitForFs(() => Exists("Image_RegionDistanceSpan") && Enabled("Image_CutOutCopy"), Affordable(30)));
            Check("The span's ends move, selecting as they go", () => MoveSliderTo("Image_RegionDistanceSpanUpper", 40)
                  && MoveSliderTo("Image_RegionDistanceSpanLower", 5) && WaitForFs(() => Enabled("Image_CutOutCopy"), Affordable(30)));
            Check("Its soft edge moves", () => MoveSlider("Image_RegionDistanceSoftness"));



            // The depth itself: a layer above the photo, then the photo's mask.
            Check("Adjust panel again", () => SelectRadio("Image_EditToolAdjust"));
            CheckDoes("Depth as layer makes the photo a layered picture with its depth above", "Image_DepthAsLayer",
                      () => WaitForFs(() => Exists("Image_LayersPanel") && Exists("Image_DepthAsMask"), Affordable(30)));
            CheckPresent("Depth as mask, for the active picture layer", "Image_DepthAsMask");

            AssertJourney();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }
}
