using System.Collections.ObjectModel;

namespace Nexaflow.Features.Dicom.Models;

/// <summary>
/// The parsed content of a DICOM container (a CD/DICOMDIR, a folder of loose instances, or a selection).
/// A Patient → Study → Series → Instance tree plus quick-access flat lists, and a short non-identifying
/// summary line for the tab. Built off the UI thread by <see cref="Services.DicomContainerLoader"/>.
/// </summary>
public sealed class DicomContainer
{
    /// <summary>Top-level patient nodes (the tree the left pane lists).</summary>
    public ObservableCollection<DicomNode> Patients { get; } = [];

    /// <summary>Every image instance, in tree order — the cine/first-open targets.</summary>
    public IReadOnlyList<DicomNode> Images { get; init; } = [];

    /// <summary>Every non-image (report / encapsulated document) instance.</summary>
    public IReadOnlyList<DicomNode> Reports { get; init; } = [];

    /// <summary>Human title for the tab (folder or DICOMDIR name). Non-identifying.</summary>
    public string Title { get; init; } = "DICOM";

    /// <summary>Non-identifying one-liner: patient/study/series/image counts + modalities.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Any instances that failed to parse, with the reason — surfaced but non-fatal.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsEmpty => Images.Count == 0 && Reports.Count == 0;

    /// <summary>Applies the PHI mask across the whole tree.</summary>
    public void ApplyPhiMask(bool hide)
    {
        foreach (var p in Patients) p.ApplyPhiMask(hide);
    }
}
