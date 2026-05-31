namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>
/// Header descriptor for a sortable GridView column: the visible <see cref="Label"/> and the
/// <see cref="FileSystemEntry"/> property name (<see cref="Key"/>) used as the sort key.
/// Set as a column's <c>Header</c> so a single shared header template can drive both the
/// sort command and the direction glyph.
/// </summary>
public sealed class SortableHeader
{
    public string Label { get; set; } = string.Empty;
    public string Key   { get; set; } = string.Empty;
}
