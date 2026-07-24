using System.Collections.Generic;
using System.Linq;
using FellowOakDicom;
using Nexaflow.Features.Dicom.Models;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Flattens a DICOM instance's dataset into a readable list of (tag, name, VR, value) rows for the tag
/// drawer. Opens with <c>SkipLargeTags</c> so pixel data and other bulk elements aren't loaded. When
/// <c>hidePatient</c> is set, the values of identifying (PHI) tags are masked — the same policy the rest of
/// the viewer applies.
/// </summary>
internal static class DicomTagReader
{
    private static readonly HashSet<DicomTag> Identifying =
    [
        DicomTag.PatientName, DicomTag.PatientID, DicomTag.PatientBirthDate, DicomTag.PatientSex,
        DicomTag.PatientAge, DicomTag.PatientAddress, DicomTag.OtherPatientNames,
        DicomTag.PatientTelephoneNumbers, DicomTag.AccessionNumber, DicomTag.ReferringPhysicianName,
        DicomTag.InstitutionName, DicomTag.InstitutionAddress, DicomTag.OperatorsName,
        DicomTag.PerformingPhysicianName, DicomTag.PatientComments,
        DicomTag.IssuerOfPatientID,
    ];

    public static IReadOnlyList<DicomTagItem> Read(string path, bool hidePatient)
    {
        var list = new List<DicomTagItem>();
        DicomDataset ds;
        try { ds = DicomIo.Open(path, FileReadOption.SkipLargeTags).Dataset; }
        catch { return list; }

        foreach (var item in ds)
        {
            var tag = item.Tag;
            var name = string.IsNullOrEmpty(tag.DictionaryEntry?.Name) ? "Private / Unknown" : tag.DictionaryEntry.Name;
            var vr = item.ValueRepresentation?.Code ?? string.Empty;

            string value;
            if (hidePatient && Identifying.Contains(tag)) value = "••• (hidden)";
            else if (item is DicomSequence seq) value = $"‹sequence: {seq.Items.Count} item(s)›";
            else value = FormatValue(ds, tag);

            list.Add(new DicomTagItem($"({tag.Group:X4},{tag.Element:X4})", name, vr, value));
        }

        return list;
    }

    private static string FormatValue(DicomDataset ds, DicomTag tag)
    {
        try
        {
            var joined = string.Join(" \\ ", ds.GetValues<string>(tag));
            return joined.Length > 300 ? joined[..300] + "…" : joined;
        }
        catch
        {
            return "‹binary›";
        }
    }
}
