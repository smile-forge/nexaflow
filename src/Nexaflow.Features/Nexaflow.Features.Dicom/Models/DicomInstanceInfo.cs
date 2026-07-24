namespace Nexaflow.Features.Dicom.Models;

/// <summary>
/// Non-identifying metadata for one SOP instance, read once at load. Everything here is safe to show
/// the AI: modality, geometry and frame count carry no PHI.
/// </summary>
public sealed record DicomInstanceInfo(
    string FilePath,
    string SopClassUid,
    string Modality,
    int Rows,
    int Columns,
    int Frames,
    bool IsImage,
    bool IsEncapsulatedDocument,
    string? EncapsulatedMimeType,
    double? PixelSpacingX,   // mm per column, if present
    double? PixelSpacingY,   // mm per row, if present
    double RescaleSlope,
    double RescaleIntercept,
    double? DefaultWindowWidth,
    double? DefaultWindowCenter,
    string TransferSyntaxUid,
    string TransferSyntaxName,
    string StudyDescription,
    string SeriesDescription,
    string BodyPart)
{
    /// <summary>True when pixels can be measured in millimetres (PixelSpacing/ImagerPixelSpacing present).</summary>
    public bool HasSpatialCalibration => PixelSpacingX is > 0 && PixelSpacingY is > 0;

    /// <summary>True when stored pixel values map to Hounsfield units (CT rescale to HU).</summary>
    public bool IsHounsfield => string.Equals(Modality, "CT", StringComparison.OrdinalIgnoreCase);
}
