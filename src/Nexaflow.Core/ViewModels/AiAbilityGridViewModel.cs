using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using System.Collections.ObjectModel;

namespace Nexaflow.Core.ViewModels;

// ── Column header ─────────────────────────────────────────────────────────────

public sealed class AiColumnViewModel
{
    public string Id           { get; }
    public string ProviderName { get; }
    public string Model        { get; }
    public bool   IsNone       => Id == string.Empty;

    public AiColumnViewModel(string id, string providerName, string model)
    {
        Id           = id;
        ProviderName = providerName;
        Model        = model;
    }

    public static AiColumnViewModel None { get; } = new(string.Empty, "None", string.Empty);
}

// ── Cell (one per row×column intersection) ────────────────────────────────────

public partial class AiAbilityCellViewModel : ObservableObject
{
    private readonly AiAbilityRowViewModel _row;

    public AiColumnViewModel Column { get; }

    public string GroupName => _row.Ability.ToString();

    public bool IsSelected => _row.SelectedColumnId == Column.Id;

    public IRelayCommand SelectCommand { get; }

    public AiAbilityCellViewModel(AiAbilityRowViewModel row, AiColumnViewModel column)
    {
        _row   = row;
        Column = column;

        SelectCommand = new RelayCommand(() => _row.SelectedColumnId = Column.Id);

        _row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiAbilityRowViewModel.SelectedColumnId))
                OnPropertyChanged(nameof(IsSelected));
        };
    }
}

// ── Row (one per ability) ────────────────────────────────────────────────────

public partial class AiAbilityRowViewModel : ObservableObject
{
    public AiAbility Ability     { get; }
    public string    AbilityName { get; }

    [ObservableProperty] private string _selectedColumnId = string.Empty;

    public ObservableCollection<AiAbilityCellViewModel> Cells { get; } = [];

    public AiAbilityRowViewModel(AiAbility ability)
    {
        Ability     = ability;
        AbilityName = AiAbilityNames.Friendly(ability);
    }
}

// ── Root grid view model ──────────────────────────────────────────────────────

public partial class AiAbilityGridViewModel : ObservableObject
{
    private readonly AiConfig _config;

    public ObservableCollection<AiAbilityRowViewModel> Rows    { get; } = [];
    public ObservableCollection<AiColumnViewModel>     Columns { get; } = [];

    // ── Add-column panel state ────────────────────────────────────────────

    [ObservableProperty] private bool    _showAddPanel;
    [ObservableProperty] private string? _newProviderName;
    [ObservableProperty] private string  _newModelName = string.Empty;
    [ObservableProperty] private bool    _isLoadingModels;

    public ObservableCollection<string> AvailableProviders { get; } = [];
    public ObservableCollection<string> SuggestedModels    { get; } = [];

    public bool HasModelSuggestions => SuggestedModels.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────

    public IRelayCommand OpenAddPanelCommand  { get; }
    public IRelayCommand CancelAddCommand     { get; }
    public IRelayCommand ConfirmAddCommand    { get; }
    public IRelayCommand RemoveColumnCommand  { get; }

    // ── Construction ──────────────────────────────────────────────────────

    public AiAbilityGridViewModel(AiConfig config)
    {
        _config = config;

        SuggestedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModelSuggestions));

        foreach (var name in AIService.Instance.AllProviders.Keys)
            AvailableProviders.Add(name);

        // Build column list: None first, then saved pairs
        Columns.Add(AiColumnViewModel.None);
        foreach (var pair in config.Columns)
            Columns.Add(new AiColumnViewModel(pair.Id, pair.ProviderName, pair.Model));

        // Build rows for every defined ability
        foreach (AiAbility ability in Enum.GetValues<AiAbility>())
        {
            var row = new AiAbilityRowViewModel(ability);

            foreach (var col in Columns)
                row.Cells.Add(new AiAbilityCellViewModel(row, col));

            // Restore saved assignment; default to None if missing or provider gone
            var key = ability.ToString();
            if (config.Assignments.TryGetValue(key, out var colId)
                && !string.IsNullOrEmpty(colId)
                && Columns.Any(c => c.Id == colId))
            {
                row.SelectedColumnId = colId;
            }

            Rows.Add(row);
        }

        OpenAddPanelCommand = new RelayCommand(() =>
        {
            ShowAddPanel     = true;
            NewProviderName  = AvailableProviders.FirstOrDefault();
        });

        CancelAddCommand = new RelayCommand(() =>
        {
            ShowAddPanel    = false;
            NewModelName    = string.Empty;
            NewProviderName = null;
            SuggestedModels.Clear();
        });

        ConfirmAddCommand = new RelayCommand(
            AddColumn,
            () => !string.IsNullOrWhiteSpace(NewProviderName) && !string.IsNullOrWhiteSpace(NewModelName));

        RemoveColumnCommand = new RelayCommand<object>(
            obj => RemoveColumn(obj as AiColumnViewModel),
            obj => obj is AiColumnViewModel col && !col.IsNone);
    }

    // ── Provider changed — reload model suggestions ───────────────────────

    partial void OnNewProviderNameChanged(string? value)
    {
        SuggestedModels.Clear();
        NewModelName = string.Empty;
        ConfirmAddCommand.NotifyCanExecuteChanged();
        if (value is null) return;
        _ = LoadModelsAsync(value);
    }

    partial void OnNewModelNameChanged(string? value)
        => ConfirmAddCommand.NotifyCanExecuteChanged();

    private async Task LoadModelsAsync(string providerName)
    {
        if (!AIService.Instance.AllProviders.TryGetValue(providerName, out var provider))
            return;

        IsLoadingModels = true;
        try
        {
            var models = await provider.GetAvailableModelsAsync();
            foreach (var m in models)
                SuggestedModels.Add(m);
            if (SuggestedModels.Count > 0)
                NewModelName = SuggestedModels[0];
        }
        catch { }
        finally
        {
            IsLoadingModels = false;
        }
    }

    // ── Add / remove columns ──────────────────────────────────────────────

    private void AddColumn()
    {
        if (string.IsNullOrWhiteSpace(NewProviderName) || string.IsNullOrWhiteSpace(NewModelName))
            return;

        var colVm = new AiColumnViewModel(
            Guid.NewGuid().ToString("N"), NewProviderName, NewModelName);

        Columns.Add(colVm);

        foreach (var row in Rows)
            row.Cells.Add(new AiAbilityCellViewModel(row, colVm));

        ShowAddPanel    = false;
        NewModelName    = string.Empty;
        NewProviderName = null;
        SuggestedModels.Clear();
    }

    private void RemoveColumn(AiColumnViewModel? col)
    {
        if (col is null || col.IsNone) return;

        Columns.Remove(col);

        foreach (var row in Rows)
        {
            var cell = row.Cells.FirstOrDefault(c => c.Column.Id == col.Id);
            if (cell is not null) row.Cells.Remove(cell);
            if (row.SelectedColumnId == col.Id)
                row.SelectedColumnId = string.Empty;
        }
    }

    // ── Apply (write back to config at Save time) ─────────────────────────

    public void Apply()
    {
        _config.Columns = Columns
            .Where(c => !c.IsNone)
            .Select(c => new ProviderModelPair { Id = c.Id, ProviderName = c.ProviderName, Model = c.Model })
            .ToList();

        _config.Assignments.Clear();
        foreach (var row in Rows)
            _config.Assignments[row.Ability.ToString()] = row.SelectedColumnId;

        AIService.Instance.LoadAbilityConfig(_config);
    }
}
