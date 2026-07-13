using System;
using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>
/// One row in the disk's lazily-expanded contents tree (doubling as a flattened-list row). A folder's
/// children are read from the image only on first expand, so a huge volume is never enumerated up front —
/// the same "load one directory at a time" rule the whole feature follows.
/// </summary>
public sealed partial class DiskNode : ObservableObject
{
    /// <summary>Display name — the last path segment.</summary>
    public required string Name { get; init; }

    /// <summary>Path inside the image, forward-slash separated, relative to the image root
    /// (e.g. <c>"Volume 1/Windows"</c>). Empty for a root node.</summary>
    public required string InnerPath { get; init; }

    public required bool IsFolder { get; init; }
    public int Depth { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }

    public List<DiskNode> Children { get; } = [];

    /// <summary>True once this folder's children have been read from the image.</summary>
    public bool Loaded { get; set; }

    [ObservableProperty] private bool _isExpanded;

    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    // Folders are assumed expandable until opened — we don't pre-read a directory just to draw a chevron.
    public bool HasChildren => IsFolder;
    public string Chevron => IsFolder ? (IsExpanded ? "▾" : "▸") : string.Empty;

    public string SizeText => IsFolder || Size < 0 ? string.Empty : SizeFormatter.FormatBytes(Size);
    public string ModifiedText => Modified == default ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Chevron));
}
