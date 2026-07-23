using System.IO;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Media;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// Builds richer DICOM fixtures with fo-dicom for tests that need what the hand-rolled sample can't give:
/// a multi-frame image (cine) and a real DICOMDIR index (the CD path). Kept out of the dependency-free
/// fixtures library on purpose — this test project already references fo-dicom transitively.
/// </summary>
internal static class DicomTestFiles
{
    /// <summary>Writes a single grayscale image instance (optionally multi-frame) and returns its path.</summary>
    public static string WriteImage(string dir, string fileName, string patientId, string patientName,
                                    string studyUid, string seriesUid, string sopUid, int frames = 1,
                                    string modality = "CT")
    {
        const int rows = 4, cols = 4;
        var ds = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage.UID },
            { DicomTag.SOPInstanceUID, sopUid },
            { DicomTag.PatientID, patientId },
            { DicomTag.PatientName, patientName },
            { DicomTag.StudyInstanceUID, studyUid },
            { DicomTag.SeriesInstanceUID, seriesUid },
            { DicomTag.Modality, modality },
            { DicomTag.SeriesNumber, "1" },
            { DicomTag.InstanceNumber, "1" },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.Rows, (ushort)rows },
            { DicomTag.Columns, (ushort)cols },
            { DicomTag.BitsAllocated, (ushort)8 },
            { DicomTag.BitsStored, (ushort)8 },
            { DicomTag.HighBit, (ushort)7 },
            { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.RescaleSlope, "1" },
            { DicomTag.RescaleIntercept, "-1024" },
            { DicomTag.PixelSpacing, "1.0", "1.0" },
            { DicomTag.WindowCenter, "128" },
            { DicomTag.WindowWidth, "256" },
        };
        if (frames > 1) ds.AddOrUpdate(DicomTag.NumberOfFrames, frames);

        var pd = DicomPixelData.Create(ds, newPixelData: true);
        for (var f = 0; f < frames; f++)
        {
            // Each frame a distinct solid grey so per-frame rendering is unambiguously different.
            var frame = new byte[rows * cols];
            System.Array.Fill(frame, (byte)(30 + f * 60));
            pd.AddFrame(new MemoryByteBuffer(frame));
        }

        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        new DicomFile(ds).Save(path);
        return path;
    }

    /// <summary>
    /// Writes a small DICOM file-set (one patient, one study, two series with one image each) plus its
    /// DICOMDIR index, and returns the DICOMDIR path.
    /// </summary>
    public static string WriteDicomDir(string root)
    {
        Directory.CreateDirectory(root);
        var studyUid = "1.2.3.10";
        var f1 = WriteImage(Path.Combine(root, "IMAGES"), "IMG00001", "PAT9", "Roe^Jane",
                            studyUid, "1.2.3.10.1", "1.2.3.10.1.1");
        var f2 = WriteImage(Path.Combine(root, "IMAGES"), "IMG00002", "PAT9", "Roe^Jane",
                            studyUid, "1.2.3.10.2", "1.2.3.10.2.1");

        var dir = new DicomDirectory();
        dir.AddFile(DicomFile.Open(f1), RelativeId(root, f1));
        dir.AddFile(DicomFile.Open(f2), RelativeId(root, f2));

        var dicomdir = Path.Combine(root, "DICOMDIR");
        dir.Save(dicomdir);
        return dicomdir;
    }

    private static string RelativeId(string root, string file)
        => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '\\');
}
