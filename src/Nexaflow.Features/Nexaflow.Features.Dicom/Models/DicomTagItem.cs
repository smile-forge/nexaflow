namespace Nexaflow.Features.Dicom.Models;

/// <summary>One row in the DICOM tag drawer: the tag id, its dictionary name, VR and formatted value.</summary>
public sealed record DicomTagItem(string Tag, string Name, string Vr, string Value);
