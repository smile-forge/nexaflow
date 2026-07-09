using System.Text.Json.Serialization;

namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// One configurable backlog status (per-workspace). A backlog item stores this status's <see cref="Key"/>
/// — a stable name — never a list index, so reordering the statuses never moves an item and deleting a
/// status leaves items on it flagged <b>invalid</b> (see <c>ProjectsConfig.Resolve</c>) rather than being
/// silently remapped. The list ORDER defines the forward "Progress" step order; the single
/// <see cref="IsTerminalCancelled"/> entry is skipped by that stepping and is reachable only by picking it
/// in the status dropdown.
/// </summary>
public sealed class BacklogStatusDef
{
    /// <summary>Stable identity written into <c>.project</c>. Relabelling never changes it.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label shown throughout the UI.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>A <c>Swatch.*</c> theme-token key (resolved to a themeable brush via
    /// <c>Nexaflow.Visuals.Common.Theming.SwatchPalette</c> at paint time).</summary>
    public string SwatchKey { get; set; } = "Swatch.Slate";

    /// <summary>The single terminal "cancelled" state: excluded from the forward Progress stepping,
    /// reachable only by choosing it in the status dropdown.</summary>
    public bool IsTerminalCancelled { get; set; }

    /// <summary>False only for the synthesized sentinel returned when a stored key is no longer configured
    /// (a deleted status). Transient — never serialized.</summary>
    [JsonIgnore] public bool IsValid { get; init; } = true;
}
