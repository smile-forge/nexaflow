using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace Nexaflow.Features.Json.Models;

public abstract partial class JsonDisplayItem : ObservableObject
{
    public JsonNodeModel Node  { get; set; } = null!;
    public int           Depth { get; init; }

    /// <summary>True when a "?" page search matched this item's subtree. On the base rather than on
    /// JsonTreeDisplayItem so tree rows, table rows and virtual rows all wash the same way.</summary>
    [ObservableProperty] private bool _isSearchHit;
}

public sealed partial class JsonTreeDisplayItem : JsonDisplayItem
{
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public bool HasChildren { get; init; }

    // Pre-computed display labels set at creation time
    public string  KeyLabel   { get; init; } = string.Empty;
    public string  TypeLabel  { get; init; } = string.Empty;
    public bool    IsValue    { get; init; }
    public JsonValueKind ValueKind { get; init; }
}

// Replaces a node's subtree in the display list with an inline view. At most one at a time.
public sealed partial class JsonInlineContentDisplayItem : JsonDisplayItem
{
    public NodeViewMode ViewMode { get; init; }
    public string       RawJson  { get; init; } = string.Empty;
}

public sealed partial class JsonVirtualDisplayItem : JsonDisplayItem
{
    /// <summary>
    /// For pre-populated placeholder items: the zero-based depth-1 root child index
    /// this row represents in the file. -1 means the item has a real VirtualJsonNodeModel
    /// sentinel in Root.Children (use Node instead).
    /// </summary>
    public int RootChildIndex { get; init; } = -1;
}

// Header row shown at the top of the depth-1 list when the root array is in Table mode.
public sealed partial class JsonTableHeaderDisplayItem : JsonDisplayItem
{
    public IReadOnlyList<string> Columns { get; init; } = [];
}

// Replaces a JsonTreeDisplayItem at depth 1 when the root array is in Table mode.
// Renders the node's values as cells in a shared-size-group Grid built in code-behind.
public sealed partial class JsonTableRowDisplayItem : JsonDisplayItem
{
    public IReadOnlyList<string> Columns  { get; init; } = [];
    public IReadOnlyList<string> Values   { get; init; } = [];
}
