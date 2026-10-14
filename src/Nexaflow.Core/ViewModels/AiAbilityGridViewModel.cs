using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
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

    /// <summary>The Local column: the models on this machine, one for each job, chosen on the Local page. It is always there.</summary>
    public bool IsLocal { get; }

    public bool CanRemove => !IsLocal;

    public AiColumnViewModel(string id, string providerName, string model, ModelKind kind, string assemblyFileName, bool staysOnMachine,
                             bool isLocal = false)
    {
        Id               = id;
        ProviderName     = providerName;
        Model            = model;
        Kind             = kind;
        AssemblyFileName = assemblyFileName;
        StaysOnMachine   = staysOnMachine;
        IsLocal          = isLocal;
        KindLabel        = isLocal ? string.Empty : ProviderOffer.KindLabel(kind);
    }

    /// <summary>The Local column's header.</summary>
    public static AiColumnViewModel Local() =>
        new("local", Str.Get("Shell.Config.AiGrid.Local"), Str.Get("Shell.Config.AiGrid.LocalModels"), ModelKind.Language, string.Empty,
            staysOnMachine: true, isLocal: true);
}

/// <summary>A provider's models of one kind, as the add-column picker lists them.</summary>
public sealed record ProviderOffer(string ProviderName, ModelKind Kind)
{
    public string Display => Kind == ModelKind.Language ? ProviderName : $"{ProviderName} — {KindLabel(Kind)}";

    public static string KindLabel(ModelKind kind) => kind switch
    {
        ModelKind.SpeechToText    => Str.Get("Shell.Config.AiGrid.Kind.SpeechToText"),
        ModelKind.TextToSpeech    => Str.Get("Shell.Config.AiGrid.Kind.TextToSpeech"),
        ModelKind.ImageGeneration => Str.Get("Shell.Config.AiGrid.Kind.ImageGeneration"),
        ModelKind.VideoGeneration => Str.Get("Shell.Config.AiGrid.Kind.VideoGeneration"),
        _                         => string.Empty,
    };
}

// ── Cell (one per row×column intersection) ────────────────────────────────────

public partial class AiAbilityCellViewModel : ObservableObject
{
    private readonly AiAbilityRowViewModel _row;
    private readonly Func<AiAbility, (bool Usable, string? Note)>? _local;

    public AiColumnViewModel Column { get; }

    /// <summary>The id the row's chain names this cell by: the column's, or in the Local column, the row's job's model's.</summary>
    public string ChainId { get; }

    /// <summary>The column holds the kind of model the row's ability takes — in the Local column, this machine runs a model
    /// for the row's job and one is chosen; other cells are blank.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isUsable;

    /// <summary>Why a Local cell cannot be used, or what it holds; null elsewhere.</summary>
    [ObservableProperty] private string? _note;

    /// <summary>Where this column stands in the row's chain, from 1; 0 when the row does not use it.</summary>
    public int Position => _row.Chain.IndexOf(ChainId) + 1;

    public bool IsInChain => Position > 0;

    /// <summary>Whether the cell is shown greyed: a Local cell this machine cannot use.</summary>
    public bool IsGreyed => Column.IsLocal && !IsUsable;

    public IRelayCommand ToggleCommand { get; }

    /// <param name="local">For the Local column: whether a row's job can use it, and the note to show.</param>
    public AiAbilityCellViewModel(AiAbilityRowViewModel row, AiColumnViewModel column, Func<AiAbility, (bool Usable, string? Note)>? local = null)
    {
        _row    = row;
        _local  = local;
        Column  = column;
        ChainId = column.IsLocal ? LocalColumns.IdOf(LocalColumns.SlotOf(row.Ability)) : column.Id;

        ToggleCommand = new RelayCommand(() => _row.Toggle(ChainId), () => IsUsable);
        Refresh();

        _row.Chain.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(IsInChain));
        };
    }

    /// <summary>Takes again whether the cell can be used — a Local model chosen or fetched since.</summary>
    public void Refresh()
    {
        if (_local is null) { IsUsable = Column.Kind == _row.Kind; return; }
        (IsUsable, Note) = _local(_row.Ability);
        OnPropertyChanged(nameof(IsGreyed));
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

/// <summary>
/// The ability grid: a row per ability, a column per model, and in each row the chain of columns that serve it. The Local
/// column comes first and is always there: the models on this machine, one chosen for each job on the Local page and saved
/// as a column of its own (<see cref="LocalColumns"/>); its cell in a row is the row's job's model, greyed where this machine
/// runs none or none is chosen. The other columns are the providers' models the user adds.
/// </summary>
public partial class AiAbilityGridViewModel : ObservableObject
{
    private readonly AiConfig _config;
    private readonly LocalModelShelf? _shelf;

    /// <summary>The model chosen for each job of the Local column, as the grid holds it.</summary>
    private readonly Dictionary<LocalSlot, ProviderModelPair> _local = [];

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

    /// <param name="shelf">The models on this machine; without it, the Local column has nothing to use.</param>
    public AiAbilityGridViewModel(AiConfig config, LocalModelShelf? shelf = null)
    {
        _config = config;
        _shelf  = shelf;

        SuggestedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModelSuggestions));

        // Provider plugins are loaded and this context's provider set is (re)built by
        // WorkspaceConfigViewModel before the grid is shown; we read it via _config.Providers.
        if (config.Providers is { } set) LocalColumns.Gather(config, set.IsLocal);
        RebuildAvailableOffers();

        foreach (var slot in Enum.GetValues<LocalSlot>())
            if (config.Columns.FirstOrDefault(c => c.Id == LocalColumns.IdOf(slot)) is { } chosen) _local[slot] = Copy(chosen);

        Columns.Add(AiColumnViewModel.Local());
        foreach (var pair in config.Columns.Where(c => !LocalColumns.IsLocal(c.Id)))
            Columns.Add(NewColumn(pair.Id, pair.ProviderName, pair.Model, pair.Kind, pair.AssemblyFileName));

        // Build rows for every defined ability, keeping only the saved chain's columns that still exist and hold the
        // ability's kind of model, and its job's Local model where one is chosen.
        foreach (AiAbility ability in Enum.GetValues<AiAbility>())
        {
            var kind   = AiAbilityNames.Kind(ability);
            var usable = Columns.Where(c => !c.IsLocal && c.Kind == kind).Select(c => c.Id).ToHashSet();
            if (_local.ContainsKey(LocalColumns.SlotOf(ability))) usable.Add(LocalColumns.IdOf(LocalColumns.SlotOf(ability)));
            var saved  = config.Assignments.GetValueOrDefault(ability.ToString()) ?? AbilityChain.Empty;
            var row    = new AiAbilityRowViewModel(ability, new AbilityChain(saved.ColumnIds.Where(usable.Contains)));

            foreach (var col in Columns)
                row.Cells.Add(Cell(row, col));

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
            obj => obj is AiColumnViewModel { CanRemove: true });
    }

    private AiAbilityCellViewModel Cell(AiAbilityRowViewModel row, AiColumnViewModel column)
        => new(row, column, column.IsLocal ? LocalCell : null);

    /// <summary>Whether a row's Local cell can be used, and what its note says: the job's model, or why there is none.</summary>
    private (bool Usable, string? Note) LocalCell(AiAbility ability)
    {
        var slot = LocalColumns.SlotOf(ability);
        if (_shelf is null || !_shelf.Runs(slot))
            return (false, Str.Get(_shelf?.OptionsFor(slot).Count > 0 ? "Shell.Config.AiGrid.Local.TooLarge" : "Shell.Config.AiGrid.Local.None"));
        if (!_local.TryGetValue(slot, out var chosen)) return (false, Str.Get("Shell.Config.AiGrid.Local.Choose"));

        var option = _shelf.OptionsFor(slot).FirstOrDefault(o => o.IsIn(chosen));
        var name   = option?.Model.DisplayName ?? chosen.Model;
        return (true, option is not null && !_shelf.IsReady(option) ? Str.Format("Shell.Config.AiGrid.Local.NotHereFormat", name) : name);
    }

    /// <summary>Takes the Local cells again — a model fetched or removed since.</summary>
    public void RefreshLocal()
    {
        foreach (var cell in Rows.SelectMany(r => r.Cells).Where(c => c.Column.IsLocal)) cell.Refresh();
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

    /// <summary>Every (provider, kind) the workspace can add a column for — a provider whose required config (an API key) is
    /// missing offers nothing yet, and the models on this machine are the Local column's, never a column of their own.</summary>
    private void RebuildAvailableOffers()
    {
        AvailableOffers.Clear();
        if (_config.Providers is not { } set) return;

        var providerConfigs = set.Configs.ToDictionary(c => c.FriendlyName);
        foreach (var provider in set.Providers)
        {
            if (provider.Harness == ModelHarness.Owned) continue;
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
            row.Cells.Add(Cell(row, colVm));

        ResetAddPanel();
    }

    private void RemoveColumn(AiColumnViewModel? col)
    {
        if (col is null || col.IsLocal) return;

        Columns.Remove(col);

        foreach (var row in Rows)
        {
            var cell = row.Cells.FirstOrDefault(c => c.Column.Id == col.Id);
            if (cell is not null) row.Cells.Remove(cell);
            row.Chain.Remove(col.Id);
        }
    }

    // ── The Local column's models ─────────────────────────────────────────

    /// <summary>The model chosen for <paramref name="slot"/>, or null when none is.</summary>
    public ProviderModelPair? LocalModelFor(LocalSlot slot) => _local.GetValueOrDefault(slot);

    /// <summary>
    /// Makes <paramref name="model"/> <paramref name="slot"/>'s model and writes that into the config at once — a choice that
    /// takes effect straight away, while every other edit on the grid waits for Apply. A job none of whose rows uses the Local
    /// column yet starts using it in every one, at the end of its chain: the Local column is on wherever it can be.
    /// </summary>
    public void ChooseLocal(LocalSlot slot, ProviderModelPair model)
    {
        var id = LocalColumns.IdOf(slot);
        _local[slot] = Copy(model, id);
        LocalColumns.Choose(_config, slot, model);

        var rows = Rows.Where(r => LocalColumns.SlotOf(r.Ability) == slot).ToList();
        var used = rows.Any(r => r.Chain.Contains(id))
                   || rows.Any(r => _config.Assignments.GetValueOrDefault(r.Ability.ToString())?.ColumnIds.Contains(id) == true);
        if (!used)
            foreach (var row in rows)
            {
                row.Chain.Add(id);
                var saved = _config.Assignments.GetValueOrDefault(row.Ability.ToString()) ?? AbilityChain.Empty;
                _config.Assignments[row.Ability.ToString()] = new AbilityChain([.. saved.ColumnIds, id]);
            }

        RefreshLocal();
        RecheckChanges();
    }

    private static ProviderModelPair Copy(ProviderModelPair column, string? id = null) => new()
    {
        Id = id ?? column.Id, ProviderName = column.ProviderName, Model = column.Model, Kind = column.Kind,
        AssemblyFileName = column.AssemblyFileName,
    };

    // ── Change tracking ───────────────────────────────────────────────────

    /// <summary>Every column the grid holds, as the config saves them: the Local column's models, then the providers'.</summary>
    private IEnumerable<ProviderModelPair> AllColumns()
        => _local.Values.Select(c => Copy(c)).Concat(Columns.Where(c => !c.IsLocal).Select(c => new ProviderModelPair
        {
            Id = c.Id, ProviderName = c.ProviderName, Model = c.Model, Kind = c.Kind, AssemblyFileName = c.AssemblyFileName,
        }));

    private void RecheckChanges()
    {
        HasChanges = !AllColumns().Select(c => (c.Id, c.Model)).OrderBy(c => c.Id)
                         .SequenceEqual(_config.Columns.Select(c => (c.Id, c.Model)).OrderBy(c => c.Id))
                     || Rows.Any(row => !row.ToChain().SameAs(
                            _config.Assignments.GetValueOrDefault(row.Ability.ToString()) ?? AbilityChain.Empty));
    }

    // ── Apply (write back to config at Save time) ─────────────────────────

    public void Apply()
    {
        _config.Columns = [.. AllColumns()];

        _config.Assignments.Clear();
        foreach (var row in Rows)
            _config.Assignments[row.Ability.ToString()] = row.ToChain();

        // Note: the AIService reload is handled by WorkspaceConfigViewModel.Apply() after this control's
        // ICustomConfigApply.Apply() has written back to the config object.
        RecheckChanges(); // resets HasChanges since _config now matches current state
    }
}
