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

    public TabularRowViewModel(int absoluteIndex, IReadOnlyList<string> cells)
    {
        AbsoluteIndex = absoluteIndex;
        Cells         = cells;
    }
}
