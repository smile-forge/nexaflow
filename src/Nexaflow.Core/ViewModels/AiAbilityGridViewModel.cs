using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.ObjectModel;

namespace Nexaflow.Core.ViewModels;

// ── Column header ─────────────────────────────────────────────────────────────

public sealed class AiColumnViewModel
{
    public string    Id               { get; }
    public string    ProviderName     { get; }
    public string    Model            { get; }
    public ModelKind Kind             { get; }
    public string    AssemblyFileName { get; }

    /// <summary>What this column's model is sent stays on this machine.</summary>
    public bool StaysOnMachine { get; }

    /// <summary>The kind, named under the model when it is not a language model ("speech to text").</summary>
    public string KindLabel { get; }

    public AiColumnViewModel(string id, string providerName, string model, ModelKind kind, string assemblyFileName, bool staysOnMachine)
    {
        Id               = id;
        ProviderName     = providerName;
        Model            = model;
        Kind             = kind;
        AssemblyFileName = assemblyFileName;
        StaysOnMachine   = staysOnMachine;
        KindLabel        = ProviderOffer.KindLabel(kind);
    }
}

/// <summary>A provider's models of one kind, as the add-column picker lists them.</summary>
public sealed record ProviderOffer(string ProviderName, ModelKind Kind)
{
    public string Display => Kind == ModelKind.Language ? ProviderName : $"{ProviderName} — {KindLabel(Kind)}";

    public static string KindLabel(ModelKind kind) => kind switch
    {
        ModelKind.SpeechToText => Str.Get("Shell.Config.AiGrid.Kind.SpeechToText"),
        _                      => string.Empty,
    };
}

// ── Cell (one per row×column intersection) ────────────────────────────────────

public partial class AiAbilityCellViewModel : ObservableObject
{
    private readonly AiAbilityRowViewModel _row;

    public AiColumnViewModel Column { get; }

    /// <summary>The column holds the kind of model the row's ability takes; other cells are blank.</summary>
    public bool IsUsable { get; }

    /// <summary>Where this column stands in the row's chain, from 1; 0 when the row does not use it.</summary>
    public int Position => _row.Chain.IndexOf(Column.Id) + 1;

    public bool IsInChain => Position > 0;

    public IRelayCommand ToggleCommand { get; }

    public AiAbilityCellViewModel(AiAbilityRowViewModel row, AiColumnViewModel column)
    {
        _row     = row;
        Column   = column;
        IsUsable = column.Kind == row.Kind;

        ToggleCommand = new RelayCommand(() => _row.Toggle(Column.Id), () => IsUsable);

        _row.Chain.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(IsInChain));
        };
    }
}

// ── Row (one per ability) ────────────────────────────────────────────────────

public partial class AiAbilityRowViewModel : ObservableObject
{
    public AiAbility Ability     { get; }
    public string    AbilityName { get; }
    public ModelKind Kind        => AiAbilityNames.Kind(Ability);

    /// <summary>The columns that serve this ability, in order of preference.</summary>
    public ObservableCollection<string> Chain { get; } = [];

    public ObservableCollection<AiAbilityCellViewModel> Cells { get; } = [];

    public AiAbilityRowViewModel(AiAbility ability, AbilityChain saved)
    {
        Ability     = ability;
        AbilityName = AiAbilityNames.Friendly(ability);
        foreach (var id in saved.ColumnIds) Chain.Add(id);
    }

    /// <summary>A column not in the chain joins it at the end; one already in it leaves, and those after it
    /// move up.</summary>
    public void Toggle(string columnId)
    {
        if (!Chain.Remove(columnId)) Chain.Add(columnId);
    }

    public AbilityChain ToChain() => new(Chain);
}

// ── Root grid view model ──────────────────────────────────────────────────────

public partial class AiAbilityGridViewModel : ObservableObject
{
    private readonly AiConfig _config;

    public ObservableCollection<AiAbilityRowViewModel> Rows    { get; } = [];
    public ObservableCollection<AiColumnViewModel>     Columns { get; } = [];

    // ── Add-column panel state ────────────────────────────────────────────

    [ObservableProperty] private bool           _showAddPanel;
    [ObservableProperty] private ProviderOffer? _newOffer;
    [ObservableProperty] private string         _newModelName = string.Empty;
    [ObservableProperty] private bool           _isLoadingModels;

    public ObservableCollection<ProviderOffer> AvailableOffers { get; } = [];
    public ObservableCollection<string>        SuggestedModels { get; } = [];

    public bool HasModelSuggestions => SuggestedModels.Count > 0;

    [ObservableProperty] private bool _hasChanges;

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

        // Provider plugins are loaded and this context's provider set is (re)built by
        // WorkspaceConfigViewModel before the grid is shown; we read it via _config.Providers.
        RebuildAvailableOffers();

        foreach (var pair in config.Columns)
            Columns.Add(NewColumn(pair.Id, pair.ProviderName, pair.Model, pair.Kind, pair.AssemblyFileName));

        // Build rows for every defined ability, keeping only the saved chain's columns that still exist and
        // hold the ability's kind of model.
        foreach (AiAbility ability in Enum.GetValues<AiAbility>())
        {
            var kind   = AiAbilityNames.Kind(ability);
            var usable = Columns.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet();
            var saved  = config.Assignments.GetValueOrDefault(ability.ToString()) ?? AbilityChain.Empty;
            var row    = new AiAbilityRowViewModel(ability, new AbilityChain(saved.ColumnIds.Where(usable.Contains)));

            foreach (var col in Columns)
                row.Cells.Add(new AiAbilityCellViewModel(row, col));

            Rows.Add(row);
        }

        // Wire change tracking after all rows are populated
        Columns.CollectionChanged += (_, _) => RecheckChanges();
        foreach (var row in Rows)
            row.Chain.CollectionChanged += (_, _) => RecheckChanges();

        OpenAddPanelCommand = new RelayCommand(() =>
        {
            RebuildAvailableOffers();
            ShowAddPanel = true;
            NewOffer     = null;
        });

        CancelAddCommand = new RelayCommand(ResetAddPanel);

        ConfirmAddCommand = new RelayCommand(
            AddColumn,
            () => NewOffer is not null && !string.IsNullOrWhiteSpace(NewModelName));

        RemoveColumnCommand = new RelayCommand<object>(
            obj => RemoveColumn(obj as AiColumnViewModel),
            obj => obj is AiColumnViewModel);
    }

    // ── Offer changed — reload model suggestions ──────────────────────────

    partial void OnNewOfferChanged(ProviderOffer? value)
    {
        SuggestedModels.Clear();
        NewModelName = string.Empty;
        ConfirmAddCommand.NotifyCanExecuteChanged();
        if (value is null) return;
        _ = LoadModelsAsync(value);
    }

    partial void OnNewModelNameChanged(string value)
        => ConfirmAddCommand.NotifyCanExecuteChanged();

    private async Task LoadModelsAsync(ProviderOffer offer)
    {
        if (_config.Providers?.Find(offer.ProviderName, offer.Kind) is not { } provider)
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

    // ── Provider list ─────────────────────────────────────────────────────

    /// <summary>Every (provider, kind) the workspace can add a column for — a provider whose required config
    /// (an API key) is missing offers nothing yet.</summary>
    private void RebuildAvailableOffers()
    {
        AvailableOffers.Clear();
        if (_config.Providers is not { } set) return;

        var providerConfigs = set.Configs.ToDictionary(c => c.FriendlyName);
        foreach (var provider in set.Providers)
        {
            if (providerConfigs.TryGetValue(provider.Name, out var cfg)
                && !ConfigEditViewModel.AreRequiredPropertiesSatisfied(cfg))
                continue;

            foreach (var kind in Enum.GetValues<ModelKind>())
                if (provider.Serves(kind))
                    AvailableOffers.Add(new ProviderOffer(provider.Name, kind));
        }
    }

    public void ResetAddPanel()
    {
        ShowAddPanel = false;
        NewModelName = string.Empty;
        NewOffer     = null;
        SuggestedModels.Clear();
    }

    // ── Add / remove columns ──────────────────────────────────────────────

    /// <summary>A column, with where its provider sends what it is asked — read from the provider as this workspace
    /// configures it (an Ollama server's address, an OpenAI-compatible base URL).</summary>
    private AiColumnViewModel NewColumn(string id, string providerName, string model, ModelKind kind, string assemblyFileName)
    {
        var staysOnMachine = _config.Providers?.Find(providerName, kind) is { } provider
                             && provider.Residency == DataResidency.StaysOnMachine;
        return new AiColumnViewModel(id, providerName, model, kind, assemblyFileName, staysOnMachine);
    }

    private void AddColumn()
    {
        if (NewOffer is not { } offer || string.IsNullOrWhiteSpace(NewModelName))
            return;

        var colVm = NewColumn(
            Guid.NewGuid().ToString("N"), offer.ProviderName, NewModelName, offer.Kind,
            _config.Providers?.GetAssemblyFileName(offer.ProviderName) ?? string.Empty);

        Columns.Add(colVm);

        foreach (var row in Rows)
            row.Cells.Add(new AiAbilityCellViewModel(row, colVm));

        ResetAddPanel();
    }

    private void RemoveColumn(AiColumnViewModel? col)
    {
        if (col is null) return;

        Columns.Remove(col);

        foreach (var row in Rows)
        {
            var cell = row.Cells.FirstOrDefault(c => c.Column.Id == col.Id);
            if (cell is not null) row.Cells.Remove(cell);
            row.Chain.Remove(col.Id);
        }
    }

    // ── Change tracking ───────────────────────────────────────────────────

    private void RecheckChanges()
    {
        HasChanges = !Columns.Select(c => c.Id).SequenceEqual(_config.Columns.Select(c => c.Id))
                     || Rows.Any(row => !row.ToChain().SameAs(
                            _config.Assignments.GetValueOrDefault(row.Ability.ToString()) ?? AbilityChain.Empty));
    }

    // ── Apply (write back to config at Save time) ─────────────────────────

    public void Apply()
    {
        _config.Columns = Columns
            .Select(c => new ProviderModelPair
            {
                Id = c.Id, ProviderName = c.ProviderName, Model = c.Model, Kind = c.Kind, AssemblyFileName = c.AssemblyFileName,
            })
            .ToList();

        _config.Assignments.Clear();
        foreach (var row in Rows)
            _config.Assignments[row.Ability.ToString()] = row.ToChain();

        // Note: the AIService reload is handled by WorkspaceConfigViewModel.Apply() after this control's
        // ICustomConfigApply.Apply() has written back to the config object.
        RecheckChanges(); // resets HasChanges since _config now matches current state
    }
}
