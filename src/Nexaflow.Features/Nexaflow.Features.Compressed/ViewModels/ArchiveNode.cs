using Nexaflow.Visuals.Common.Formatting;
using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Compressed.ViewModels;

/// <summary>
/// One node in the archive's directory tree, doubling as a flattened-list row. The view binds a single
/// <see cref="Indent"/> margin on the name cell so columns stay aligned while the hierarchy indents.
/// </summary>
public sealed partial class ArchiveNode : ObservableObject
{
    /// <summary>Display name (the last path segment).</summary>
    public required string Name { get; init; }

    /// <summary>Full forward-slash path inside the archive.</summary>
    public required string ArchivePath { get; init; }

    public required bool IsFolder { get; init; }
    public int Depth { get; init; }

    public long Size { get; set; }
    public long CompressedSize { get; set; }
    public System.DateTime Modified { get; set; }

    public List<ArchiveNode> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    public bool HasChildren => Children.Count > 0;
    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>Expander triangle for folders (empty for files / childless folders).</summary>
    public string Chevron => IsFolder && HasChildren ? (IsExpanded ? "▾" : "▸") : string.Empty;

    // Folders show their rolled-up totals (summed from descendants), same columns as files.
    public string SizeText => FormatBytes(Size);
    public string CompressedText => FormatBytes(CompressedSize);

    public string RatioText
    {
        get
        {
            if (Size <= 0 || CompressedSize < 0) return string.Empty;
            var pct = 100.0 * (1.0 - (double)CompressedSize / Size);
            return pct <= 0 ? "—" : $"{pct:0}%";
        }
    }

    public string ModifiedText => Modified == default ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Chevron));

    // Negative = "no size known" for archive entries → empty, not "0 B"; the scaling is the shared formatter's.
    internal static string FormatBytes(long bytes)
        => bytes < 0 ? string.Empty : SizeFormatter.FormatBytes(bytes);
}
