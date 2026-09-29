using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Visuals.Common.Controls;

public enum SortDirection { None, Asc, Desc }

/// <summary>
/// One column of a <see cref="VirtualizedRowsControl"/>: what its header shows, its width, and its
/// selection and sort state. A feature derives its own column view-model to carry what only it knows
/// (a value type, a filter) and sets <see cref="Glyph"/> from that.
/// </summary>
public partial class VirtualizedColumn : ObservableObject
{
    [ObservableProperty] private string        _header        = string.Empty;
    /// <summary>Short symbol drawn before the header label, typically the column's value type.</summary>
    [ObservableProperty] private string        _glyph         = string.Empty;
    [ObservableProperty] private bool          _isSelected;
    [ObservableProperty] private SortDirection _sortDirection = SortDirection.None;
    [ObservableProperty] private double        _width         = 140;
}
