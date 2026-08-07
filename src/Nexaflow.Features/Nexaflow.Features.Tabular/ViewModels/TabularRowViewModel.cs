using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Tabular.ViewModels;

public sealed partial class TabularRowViewModel : ObservableObject
{
    /// <summary>0-based absolute row index in the file (excluding header).</summary>
    public int AbsoluteIndex { get; }
    public IReadOnlyList<string> Cells { get; }
    public bool IsAlternate => (AbsoluteIndex & 1) == 1;

    [ObservableProperty] private bool _isSelected;

    /// <summary>True when a "?" page search matched a cell in this row. Row-level rather than
    /// per-cell: the whole grid stack (RowFilter, GetVisibleAsync, FocalRow, SelectedRowIndices) is
    /// row-addressed, and the cell backgrounds already have an owner in the column-selection tint.</summary>
    [ObservableProperty] private bool _isSearchHit;

    public TabularRowViewModel(int absoluteIndex, IReadOnlyList<string> cells)
    {
        AbsoluteIndex = absoluteIndex;
        Cells         = cells;
    }
}
