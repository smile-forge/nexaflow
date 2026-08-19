using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom.UI;

/// <summary>
/// One-pass UI journey for the DICOM viewer: opens the sample instance via the explicit <b>"As DICOM"</b>
/// ActionStrip button, then walks the toolbar — framing, invert, a measurement tool, a CT window preset —
/// and opens the tag drawer, soft-asserting each so one gap doesn't hide the rest.
/// <para>
/// The cine transport is deliberately not exercised: it only exists for a multi-frame image, and the shared
/// sample is a single frame. Frame stepping and the per-frame annotation rule are asserted against the
/// view-model in <see cref="DicomSurfaceTests"/>.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("dicom")]
public class DicomJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("dicom-ui")]
    public void Dicom_Controls_RespondInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("dicom"), "ct.dcm", "As DICOM", "DicomView");
        Assert.IsNotNull(view, "DicomView did not open via the 'As DICOM' action.");

        // Contents panel.
        CheckPresent("Content tree", "Dicom_ContentTree");
        CheckInvoke("Hide patient toggle", "Dicom_HidePatient");
        CheckInvoke("Hide patient toggle (back on)", "Dicom_HidePatient");

        // Toolbar — framing and rendering.
        CheckInvoke("Actual size", "Dicom_ActualSize");
        CheckInvoke("Fit to window", "Dicom_Fit");
        CheckInvoke("Invert", "Dicom_Invert");
        CheckInvoke("Invert (back)", "Dicom_Invert");

        // Toolbar — measurement and window presets.
        CheckInvoke("Length tool", "Dicom_Tool_Length");
        CheckInvoke("Pan tool", "Dicom_Tool_Pan");
        CheckInvoke("Clear measurements", "Dicom_ClearMeasurements");
        CheckInvoke("Bone preset", "Dicom_Preset_Bone");
        CheckInvoke("Default preset", "Dicom_Preset_Default");

        // Tag drawer — closed by default, so it has to be opened before its filter exists.
        CheckInvoke("Tags drawer toggle", "Dicom_TagsToggle");
        CheckPresent("Tag filter", "Dicom_TagFilter");
        CheckInvoke("Tags drawer toggle (closed)", "Dicom_TagsToggle");

        AssertJourney();
    }
}
