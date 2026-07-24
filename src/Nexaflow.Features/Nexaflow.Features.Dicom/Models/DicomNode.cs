using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Dicom.Models;

/// <summary>The level of a node in the DICOM content tree.</summary>
public enum DicomNodeKind
{
    Patient,
    Study,
    Series,
    /// <summary>A single image SOP instance (one or more frames) — rendered in-page.</summary>
    Image,
    /// <summary>A non-image SOP instance (encapsulated PDF, structured report, embedded model) —
    /// opened in its own viewer tab with an origin breadcrumb back to this page.</summary>
    Report,
}

/// <summary>
/// One node in the Patient → Study → Series → Instance content tree the viewer lists. Patient and Study
/// nodes may carry identifying (PHI) labels; <see cref="ApplyPhiMask"/> flips every node's live display
/// between its real label and a de-identified one when the user toggles <c>Hide patient info</c>.
/// </summary>
public sealed partial class DicomNode : ObservableObject
{
    public DicomNodeKind Kind { get; init; }

    /// <summary>The full (possibly identifying) label — patient name, study description, etc.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>A never-identifying stand-in shown when PHI is hidden (e.g. "Patient 1", modality+date).</summary>
    public string SafeLabel { get; init; } = string.Empty;

    /// <summary>Secondary line (modality, frame count, "PDF report", …). Never identifying.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Label"/> contains PHI (so it is masked when hiding patient info).</summary>
    public bool IsPhi { get; init; }

    /// <summary>Live display label bound by the tree; switched by <see cref="ApplyPhiMask"/>.</summary>
    [ObservableProperty] private string _display = string.Empty;

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;

    /// <summary>A per-kind glyph for the tree (a symbol string, not a colour).</summary>
    public string Glyph => Kind switch
    {
        DicomNodeKind.Patient => "🧑",
        DicomNodeKind.Study => "📁",
        DicomNodeKind.Series => "🎞",
        DicomNodeKind.Image => "🖼",
        DicomNodeKind.Report => "📄",
        _ => "•",
    };

    public ObservableCollection<DicomNode> Children { get; } = [];

    /// <summary>For <see cref="DicomNodeKind.Image"/>/<see cref="DicomNodeKind.Report"/> leaves: the
    /// on-disk instance file this node opens. Null for grouping nodes.</summary>
    public string? FilePath { get; init; }

    /// <summary>Instance metadata (frame count, geometry, modality) — populated for leaves. Non-identifying.</summary>
    public DicomInstanceInfo? Instance { get; init; }

    /// <summary>For an image leaf: the ordered image instances of its series (shared by every image in the
    /// series), so the scroll wheel can step through the stack. Null for grouping/report nodes.</summary>
    public IReadOnlyList<DicomNode>? SeriesImages { get; set; }

    public DicomNode() => Display = Label;

    /// <summary>Switches this node and its subtree between real and de-identified labels.</summary>
    public void ApplyPhiMask(bool hide)
    {
        Display = hide && IsPhi ? SafeLabel : Label;
        foreach (var c in Children) c.ApplyPhiMask(hide);
    }

    /// <summary>Depth-first enumeration of this node and all descendants.</summary>
    public IEnumerable<DicomNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }
}
