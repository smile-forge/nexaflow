using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// One row of a <see cref="VirtualizedRowsControl"/>: its absolute position in the source, its cell
/// text, and the selection and search-hit state the control paints.
/// </summary>
public partial class VirtualizedRow : ObservableObject
{
    /// <summary>0-based absolute row index in the source (excluding any header).</summary>
    public int AbsoluteIndex { get; }
    public IReadOnlyList<string> Cells { get; }
    public bool IsAlternate => (AbsoluteIndex & 1) == 1;

    [ObservableProperty] private bool _isSelected;

    /// <summary>True when a "?" page search matched a cell in this row. Row-level rather than
    /// per-cell: the grid stack (row filter, windowed reads, focal row, row selection) is
    /// row-addressed, and the cell backgrounds already have an owner in the column-selection tint.</summary>
    [ObservableProperty] private bool _isSearchHit;

    public VirtualizedRow(int absoluteIndex, IReadOnlyList<string> cells)
    {
        AbsoluteIndex = absoluteIndex;
        Cells         = cells;
    }
}
