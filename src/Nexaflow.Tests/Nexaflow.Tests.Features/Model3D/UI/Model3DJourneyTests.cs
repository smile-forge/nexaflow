using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Model3D.UI;

/// <summary>
/// One-pass UI journey for the 3D model viewer: opens a sample model via the explicit <b>"As 3D Model"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — wireframe toggle,
/// reset-view button and the inspector toggle — soft-asserting each so a single gap doesn't hide the rest.
/// The <c>triangle.gltf</c> fixture carries a named material, so the inspector toggle (visible only when
/// there's inspector content) is reliably present.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("model3d-toolbar")]
public class Model3DJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("model3d-reset")]
    public void Model3D_Controls_RespondInOnePass()
    {
        // A model with a material so the inspector toggle is present (the STL/OBJ tetras have none).
        var file = TestSampleData.Files("model3d")
                                 .Select(Path.GetFileName)
                                 .First(f => f!.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase));

        var view = OpenFileVia(TestSampleData.Path("model3d"), file!, "As 3D Model", "Model3DView");
        Assert.IsNotNull(view, "Model3DView did not open via the 'As 3D Model' action.");

        // Toolbar — render mode + framing.
        CheckInvoke("Wireframe toggle", "Model3D_Wireframe");
        CheckInvoke("Wireframe toggle (back to solid)", "Model3D_Wireframe");
        CheckInvoke("Reset view", "Model3D_ResetView");

        // Inspector drawer (present because the glTF fixture declares a material).
        CheckInvoke("Inspector toggle", "Model3D_InspectorToggle");

        AssertJourney();
    }
}
