using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Icons;

/// <summary>One of the set chips above the grid. <see cref="Set"/> null is "All".</summary>
public sealed record IconFilterOption(string Label, IconSet? Set, string AutomationId);

/// <summary>An icon in the grid, and whether it is the one picked.</summary>
public sealed partial class IconCell(IconEntry entry, string automationId) : ObservableObject
{
    public IconEntry Entry { get; } = entry;

    public string AutomationId { get; } = automationId;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A row of the grid. The grid is rows so it can virtualise on a plain stack panel.</summary>
public sealed record IconRow(IReadOnlyList<IconCell> Cells);

/// <summary>
/// The searchable icon grid behind <see cref="IconPicker"/>: a query, a set to search in, and the matches cut into
/// rows as wide as the picker has room for.
/// </summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private IReadOnlyList<IconCell> _cells = [];

    public IconPickerViewModel(string automationPrefix = "IconPicker")
    {
        _automationPrefix = automationPrefix;
        Filters =
        [
            new(Str.Get("Icons.Picker.All"),          null,                  $"{automationPrefix}_SetAll"),
            new(Str.Get("Icons.Picker.Emoji"),        IconSet.Emoji,         $"{automationPrefix}_SetEmoji"),
            new(Str.Get("Icons.Picker.Fluent"),       IconSet.FluentRegular, $"{automationPrefix}_SetFluent"),
            new(Str.Get("Icons.Picker.FluentFilled"), IconSet.FluentFilled,  $"{automationPrefix}_SetFluentFilled"),
        ];
        _selectedFilter = Filters[0];
        Refresh();
    }

    public IReadOnlyList<IconFilterOption> Filters { get; }

    /// <summary>Raised when the user picks an icon, as distinct from <see cref="Selected"/> being set from outside.</summary>
    public event Action<IconRef>? Picked;

    [ObservableProperty]
    private string _automationPrefix;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private IconFilterOption _selectedFilter;

    [ObservableProperty]
    private int _columns = 8;

    [ObservableProperty]
    private IReadOnlyList<IconRow> _rows = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private IconRef _selected;

    /// <summary>Every match, in order — what <see cref="Rows"/> is cut from.</summary>
    public IReadOnlyList<IconCell> Cells => _cells;

    partial void OnAutomationPrefixChanged(string value) => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedFilterChanged(IconFilterOption value) => Refresh();

    partial void OnColumnsChanged(int value) => Cut();

    partial void OnSelectedChanged(IconRef value)
    {
        foreach (var cell in _cells) cell.IsSelected = cell.Entry.Icon == value;
    }

    [RelayCommand]
    private void Pick(IconCell? cell)
    {
        if (cell is null) return;
        Selected = cell.Entry.Icon;
        Picked?.Invoke(cell.Entry.Icon);
    }

    private void Refresh()
    {
        _cells = IconCatalog.Search(SearchText, SelectedFilter?.Set)
            .Select(e => new IconCell(e, CellId(e)) { IsSelected = e.Icon == Selected })
            .ToList();
        Summary = _cells.Count == 0
            ? Str.Get("Icons.Picker.NoMatches")
            : Str.Format("Icons.Picker.Count", _cells.Count);
        OnPropertyChanged(nameof(Cells));
        Cut();
    }

    private void Cut()
    {
        var width = Math.Max(1, Columns);
        var rows  = new List<IconRow>((_cells.Count + width - 1) / width);
        for (var i = 0; i < _cells.Count; i += width)
            rows.Add(new IconRow(_cells.Skip(i).Take(width).ToList()));
        Rows = rows;
    }

    /// <summary>An id a journey can name without typing an emoji: the set, then the name with its spaces taken out.</summary>
    private string CellId(IconEntry entry)
        => $"{AutomationPrefix}_Icon_{entry.Icon.Set}_{entry.Name.Replace(" ", string.Empty).Replace("-", string.Empty)}";
}
