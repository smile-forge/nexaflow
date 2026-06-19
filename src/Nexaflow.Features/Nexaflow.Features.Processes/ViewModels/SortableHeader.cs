namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// Header descriptor for a sortable GridView column: the visible <see cref="Label"/> and the row
/// property name (<see cref="Key"/>) used as the sort key. Set as a column's <c>Header</c> so one shared
/// template drives both the sort command and the direction glyph. (Mirrors the WindowsApps / file-browser
/// helper of the same name — kept feature-local by the established pattern.)
/// </summary>
public sealed class SortableHeader
{
    public string Label { get; set; } = string.Empty;
    public string Key   { get; set; } = string.Empty;
}
