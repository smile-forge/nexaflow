using System.Windows;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.Features.Dicom.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>The measurement geometry: length converts pixels to mm via PixelSpacing (falls back to px when
/// uncalibrated), angle is the interior angle at the vertex, and ROI reports area.</summary>
[TestClass]
public class MeasurementMathTests
{
    private static DicomInstanceInfo Calibrated(double spacing) => new(
        FilePath: "x", SopClassUid: "1", Modality: "CT", Rows: 16, Columns: 16, Frames: 1,
        IsImage: true, IsEncapsulatedDocument: false, EncapsulatedMimeType: null,
        PixelSpacingX: spacing, PixelSpacingY: spacing, RescaleSlope: 1, RescaleIntercept: -1024,
        DefaultWindowWidth: 256, DefaultWindowCenter: 128, TransferSyntaxUid: "1.2.840.10008.1.2.1",
        TransferSyntaxName: "Explicit VR LE", StudyDescription: "", SeriesDescription: "", BodyPart: "");

    private static DicomInstanceInfo Uncalibrated() => Calibrated(0) with { PixelSpacingX = null, PixelSpacingY = null };

    [TestMethod]
    [CoversNode("dicom-measurements")]
    public void Length_WithPixelSpacing_ReportsMillimetres()
    {
        // 3-4-5 triangle at 2 mm/px → 5 px × 2 = 10 mm.
        var label = MeasurementMath.LengthLabel(new Point(0, 0), new Point(3, 4), Calibrated(2.0));
        StringAssert.Contains(label, "mm");
        StringAssert.Contains(label, "10");
    }

    [TestMethod]
    [CoversNode("dicom-measurements")]
    public void Length_WithoutSpacing_ReportsPixels()
    {
        var label = MeasurementMath.LengthLabel(new Point(0, 0), new Point(3, 4), Uncalibrated());
        StringAssert.Contains(label, "px");
        StringAssert.Contains(label, "5");
    }

    [TestMethod]
    [CoversNode("dicom-measurements")]
    public void Angle_RightAngle_Is90Degrees()
    {
        var label = MeasurementMath.AngleLabel(new Point(1, 0), new Point(0, 0), new Point(0, 1));
        StringAssert.Contains(label, "90");
        StringAssert.Contains(label, "°");
    }

    [TestMethod]
    [CoversNode("dicom-measurements")]
    public void RoiRectangle_ReportsArea_InMillimetresSquared()
    {
        // 4×4 px at 1 mm/px, rectangle → 16 mm². No sampler → area only.
        var label = MeasurementMath.RoiLabel(new Point(0, 0), new Point(4, 4), ellipse: false, Calibrated(1.0), sampler: null);
        StringAssert.Contains(label, "mm²");
        StringAssert.Contains(label, "16");
    }

    [TestMethod]
    [CoversNode("dicom-measurements")]
    [CoversNode("dicom-probe")]
    public void Probe_OnRealImage_ReadsRawPixelValue()
    {
        // Exercises PixelSampler end-to-end against the hand-rolled 8-bit sample (raw pixel access).
        var info = DicomContainerLoader.Load([TestSampleData.Path("dicom", "ct.dcm")]).Images[0].Instance!;
        var vm = new MeasurementViewModel();
        vm.SetFrame(info, 0);

        var probe = vm.ProbeAt(new Point(1, 1));
        Assert.IsNotNull(probe, "the pixel probe should read a value from the sampled frame");
        StringAssert.Contains(probe, "(1, 1)");
    }
}
