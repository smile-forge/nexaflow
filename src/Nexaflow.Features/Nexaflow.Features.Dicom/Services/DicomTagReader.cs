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

    /// <summary>Reads the instance at <paramref name="path"/> into drawer rows. <paramref name="wording"/> supplies the
    /// words written in place of a value (an unnamed tag, a masked identifier, a sequence, binary data): the drawer
    /// passes <see cref="DicomTagWording.ForDisplay"/>; left out, it is the assistant's English.</summary>
    public static IReadOnlyList<DicomTagItem> Read(string path, bool hidePatient, DicomTagWording? wording = null)
    {
        var list = new List<DicomTagItem>();
        var words = wording ?? DicomTagWording.Assistant;
        DicomDataset ds;
        try { ds = DicomIo.Open(path, FileReadOption.SkipLargeTags).Dataset; }
        catch { return list; }

        foreach (var item in ds)
        {
            var tag = item.Tag;
            var name = string.IsNullOrEmpty(tag.DictionaryEntry?.Name) ? words.UnknownName : tag.DictionaryEntry.Name;
            var vr = item.ValueRepresentation?.Code ?? string.Empty;

            string value;
            if (hidePatient && Identifying.Contains(tag)) value = words.HiddenValue;
            else if (item is DicomSequence seq) value = words.SequenceValue(seq.Items.Count);
            else value = FormatValue(ds, tag, words);

            list.Add(new DicomTagItem($"({tag.Group:X4},{tag.Element:X4})", name, vr, value));
        }

        return list;
    }

    private static string FormatValue(DicomDataset ds, DicomTag tag, DicomTagWording words)
    {
        try
        {
            var joined = string.Join(" \\ ", ds.GetValues<string>(tag));
            return joined.Length > 300 ? joined[..300] + "…" : joined;
        }
        catch
        {
            return words.BinaryValue;
        }
    }
}
