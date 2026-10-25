using Nexaflow.IO.Common;
using System.IO;
using System.Linq;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The container loader: the hand-rolled single-image sample and a real fo-dicom DICOMDIR both build the
/// Patient→Study→Series→Instance tree, and the DICOM file sniffer recognises instances by extension and by
/// the <c>DICM</c> magic (extensionless CD files).
/// </summary>
[TestClass]
public class DicomLoaderTests
{
    private static string SampleDcm => TestSampleData.Path("dicom", "ct.dcm");

    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(TestTemp.Root, "nexa-dicom-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    [CoversNode("dicom-sniffer")]
    public async Task Sniffer_Recognises_Dcm_And_Magic_ButNotText()
    {
        Assert.IsTrue(DicomFileSniffer.HasDicomExtension("scan.dcm"));
        Assert.IsTrue(DicomFileSniffer.HasDicmMagic(File.OpenRead(SampleDcm)), "the DICM marker at offset 128 must be found");

        var txt = Path.Combine(_tmp, "notdicom.txt");
        File.WriteAllText(txt, "hello");
        Assert.IsFalse(await new DicomIo(VirtualFileSystem.Instance).IsDicomAsync(txt));

        // An extensionless CD instance is recognised by magic alone.
        var noExt = Path.Combine(_tmp, "IM_0001");
        File.Copy(SampleDcm, noExt);
        Assert.IsTrue(await new DicomIo(VirtualFileSystem.Instance).IsDicomAsync(noExt));
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    public async Task Load_SingleImage_BuildsTree_WithOneImage()
    {
        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [SampleDcm]);

        Assert.IsFalse(container.IsEmpty);
        Assert.AreEqual(1, container.Patients.Count);
        Assert.AreEqual(1, container.Images.Count);
        Assert.AreEqual(0, container.Reports.Count);

        var image = container.Images[0];
        Assert.AreEqual(DicomNodeKind.Image, image.Kind);
        Assert.IsNotNull(image.Instance);
        Assert.AreEqual(4, image.Instance!.Rows);
        Assert.AreEqual(4, image.Instance.Columns);
        Assert.IsTrue(image.Instance.HasSpatialCalibration, "PixelSpacing 1.0 should calibrate the image");
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    [CoversNode("dicom-hide-patient")]
    public async Task Load_SingleImage_PatientNodeCarriesPhi_MaskedWhenHidden()
    {
        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [SampleDcm]);
        var patient = container.Patients[0];

        Assert.IsTrue(patient.IsPhi);
        StringAssert.Contains(patient.Label, "John");         // real (PHI) label
        Assert.AreNotEqual(patient.Label, patient.SafeLabel);

        container.ApplyPhiMask(hide: true);
        StringAssert.Contains(patient.Display, "Patient");    // de-identified stand-in
        Assert.IsFalse(patient.Display.Contains("John"));

        container.ApplyPhiMask(hide: false);
        StringAssert.Contains(patient.Display, "John");
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    public async Task Load_DicomDir_ResolvesReferencedFiles_IntoSeries()
    {
        var dicomdir = DicomTestFiles.WriteDicomDir(_tmp);

        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [dicomdir]);

        Assert.AreEqual(1, container.Patients.Count, "one patient in the file-set");
        Assert.AreEqual(2, container.Images.Count, "two instances across two series");
        // Patient → one study → two series.
        var studies = container.Patients[0].Children;
        Assert.AreEqual(1, studies.Count);
        Assert.AreEqual(2, studies[0].Children.Count, "two series under the study");
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    public async Task Load_Folder_WithoutDicomDir_ScansInstances()
    {
        DicomTestFiles.WriteImage(_tmp, "a.dcm", "PX", "A^B", "1.9.1", "1.9.1.1", "1.9.1.1.1");
        DicomTestFiles.WriteImage(_tmp, "b.dcm", "PX", "A^B", "1.9.1", "1.9.1.2", "1.9.1.2.1");

        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [_tmp]);

        Assert.AreEqual(1, container.Patients.Count);
        Assert.AreEqual(2, container.Images.Count);
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    public async Task Load_Series_OrdersByInstanceNumber_AndLinksSeriesImages()
    {
        // Written out of order (instance 3, 1, 2); the loaded series must be ordered 1, 2, 3.
        DicomTestFiles.WriteImage(_tmp, "c.dcm", "PX", "A^B", "1.9.3", "1.9.3.1", "1.9.3.1.3", instanceNumber: 3);
        DicomTestFiles.WriteImage(_tmp, "a.dcm", "PX", "A^B", "1.9.3", "1.9.3.1", "1.9.3.1.1", instanceNumber: 1);
        DicomTestFiles.WriteImage(_tmp, "b.dcm", "PX", "A^B", "1.9.3", "1.9.3.1", "1.9.3.1.2", instanceNumber: 2);

        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [_tmp]);

        Assert.AreEqual(3, container.Images.Count);
        CollectionAssert.AreEqual(
            new[] { "Image 1", "Image 2", "Image 3" },
            container.Images.Select(i => i.Label).ToArray(),
            "slices must be ordered by InstanceNumber");
        // Every image shares the same ordered series list, so the wheel can step through it.
        Assert.IsNotNull(container.Images[0].SeriesImages);
        Assert.AreEqual(3, container.Images[0].SeriesImages!.Count);
        Assert.AreSame(container.Images[0].SeriesImages, container.Images[2].SeriesImages);
    }

    [TestMethod]
    [CoversNode("dicom-load")]
    public async Task Load_LargePixelData_ClassifiedAsImage_NotReport()
    {
        // The loader opens headers with SkipLargeTags, which DROPS PixelData for real (large) images. Image
        // detection must therefore key on geometry (Rows/Columns), not PixelData presence — else every real
        // slice is misfiled as a report. A 512×512 instance pushes PixelData well past the large-tag threshold.
        DicomTestFiles.WriteImage(_tmp, "big.dcm", "PX", "Big^One", "1.9.2", "1.9.2.1", "1.9.2.1.1", dim: 512);

        var container = await DicomContainerLoader.LoadAsync(new DicomIo(VirtualFileSystem.Instance), [_tmp]);

        Assert.AreEqual(1, container.Images.Count, "a large image must still be recognised as an image");
        Assert.AreEqual(0, container.Reports.Count, "it must NOT be misclassified as a report");
    }
}
