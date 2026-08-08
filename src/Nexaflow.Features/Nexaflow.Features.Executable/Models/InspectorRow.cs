using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Executable.Models;

/// <summary>
/// One label/value line in an inspector card. <see cref="Target"/> is set when the value names a
/// place in the file, which is what turns the row into a "View in hex" jump.
/// </summary>
public sealed partial class InspectorRow : ObservableObject
{
    public InspectorRow(string label, string? value, string? detail = null, FileByteRange? target = null)
    {
        Label  = label;
        Value  = value ?? string.Empty;
        Detail = detail;
        Target = target;
    }

    public string  Label  { get; }
    public string  Value  { get; }
    public string? Detail { get; }

    /// <summary>The bytes this row describes, when it describes any. Null rows have no hex jump.</summary>
    public FileByteRange? Target { get; }

    public bool CanJump => Target is not null;

    /// <summary>Set while the row is a search hit, so the view can wash it.</summary>
    [ObservableProperty] private bool _isSearchHit;

    /// <summary>Severity tint key resolved by the view — null for an ordinary row.</summary>
    public string? StatusBrushKey { get; init; }

    /// <summary>
    /// The untruncated text, when <see cref="Label"/> has been shortened for display. Copy uses
    /// this, so shortening a row for layout never costs the user the actual value.
    /// </summary>
    public string? FullText { get; init; }

    /// <summary>Everything a search should look at for this row.</summary>
    public string SearchText => Detail is { Length: > 0 } ? $"{Label} {Value} {Detail}" : $"{Label} {Value}";
}

/// <summary>A titled group of <see cref="InspectorRow"/>s — one card in a section.</summary>
public sealed class InspectorCard(string title, IEnumerable<InspectorRow> rows)
{
    public string Title { get; } = title;
    public ObservableCollection<InspectorRow> Rows { get; } = new(rows);

    /// <summary>Extra note shown under the title, e.g. why a card is empty.</summary>
    public string? Note { get; init; }

    public bool HasRows => Rows.Count > 0;
}

/// <summary>
/// A node in one of the inspector's trees (sections, resources, dependencies). Kept generic so the
/// same template and the same right-click plumbing serve every tree in the page.
/// </summary>
public sealed partial class InspectorNode : ObservableObject
{
    public InspectorNode(string label, string? detail = null, FileByteRange? target = null)
    {
        Label  = label;
        Detail = detail;
        Target = target;
    }

    public string  Label  { get; }
    public string? Detail { get; }

    /// <summary>The bytes this node covers, when it covers any.</summary>
    public FileByteRange? Target { get; }

    /// <summary>Payload for node-specific actions — the PE resource behind a resources-tree row, say.</summary>
    public object? Payload { get; init; }

    public ObservableCollection<InspectorNode> Children { get; } = [];

    public bool CanJump    => Target is not null;
    public bool CanExtract { get; init; }

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSearchHit;

    public string SearchText => Detail is { Length: > 0 } ? $"{Label} {Detail}" : Label;

    /// <summary>This node and every descendant, depth first.</summary>
    public IEnumerable<InspectorNode> Descend()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.Descend())
                yield return node;
    }
}
