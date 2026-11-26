using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Nexaflow.Core.Services;

using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;
using Nexaflow.Localization.Formatting;
using Nexaflow.Localization;
using System.Collections.ObjectModel;
using System.Windows.Threading;

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
    private readonly Func<AiAbility, LocalCellState>? _local;
    private readonly Func<AiAbility, AiColumnViewModel, bool>? _serves;

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

    /// <summary>A Local cell whose model is chosen but not downloaded yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresDownload))]
    private bool _needsDownload;

    /// <summary>The row uses this Local cell, whose model is not downloaded yet: marked with a warning that it needs downloading.</summary>
    public bool RequiresDownload => NeedsDownload && IsInChain;

    /// <summary>Where this column stands in the row's chain, from 1; 0 when the row does not use it.</summary>
    public int Position => _row.Chain.IndexOf(ChainId) + 1;

    public bool IsInChain => Position > 0;

    /// <summary>Whether the cell is shown greyed: a Local cell this machine cannot use.</summary>
    public bool IsGreyed => Column.IsLocal && !IsUsable;

    public IRelayCommand ToggleCommand { get; }

    /// <param name="local">For the Local column: whether a row's job can use it, and the note to show.</param>
    /// <param name="serves">For another column: whether its model serves a row beyond being of the row's kind
    /// (<see cref="AiAbilityNames.Serves"/>); none for a row that asks only its kind.</param>
    public AiAbilityCellViewModel(AiAbilityRowViewModel row, AiColumnViewModel column, Func<AiAbility, LocalCellState>? local = null,
                                  Func<AiAbility, AiColumnViewModel, bool>? serves = null)
    {
        _row    = row;
        _local  = local;
        _serves = serves;
        Column  = column;
        ChainId = column.IsLocal ? LocalColumns.IdOf(LocalColumns.SlotOf(row.Ability)) : column.Id;

        ToggleCommand = new RelayCommand(() => _row.Toggle(ChainId), () => IsUsable);
        Refresh();

        _row.Chain.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(IsInChain));
            OnPropertyChanged(nameof(RequiresDownload));
        };
    }

    /// <summary>Takes again whether the cell can be used — a Local model chosen or fetched since.</summary>
    public void Refresh()
    {
        if (_local is null) { IsUsable = Column.Kind == _row.Kind && (_serves?.Invoke(_row.Ability, Column) ?? true); return; }
        var state = _local(_row.Ability);
        IsUsable      = state.Usable;
        Note          = state.Note;
        NeedsDownload = state.NeedsDownload;
        OnPropertyChanged(nameof(IsGreyed));
    }
}

/// <summary>A Local cell as its row's job has it: whether it can be used, what its note says, and whether its model has yet to
/// be downloaded.</summary>
public readonly record struct LocalCellState(bool Usable, string? Note, bool NeedsDownload = false);

// ── Row (one per ability) ────────────────────────────────────────────────────

public partial class AiAbilityRowViewModel : ObservableObject
{
    private readonly AiConfigDraft _draft;

    public AiAbility Ability     { get; }
    public string    AbilityName { get; }
    public ModelKind Kind        => AiAbilityNames.Kind(Ability);

    /// <summary>The columns that serve this ability, in order of preference: the draft's chain, as the cells show it.</summary>
    public ObservableCollection<string> Chain { get; } = [];

    /// <summary>No model serves this ability: what needs it is disabled, and the row is marked so.</summary>
    public bool IsUnassigned => Chain.Count == 0;

    public ObservableCollection<AiAbilityCellViewModel> Cells { get; } = [];

    public AiAbilityRowViewModel(AiAbility ability, AiConfigDraft draft)
    {
        _draft      = draft;
        Ability     = ability;
        AbilityName = AiAbilityNames.Friendly(ability);
        foreach (var id in draft.ChainOf(ability)) Chain.Add(id);
        Chain.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsUnassigned));
    }

    /// <summary>A column not in the chain joins it at the end; one already in it leaves, and those after it
    /// move up.</summary>
    public void Toggle(string columnId) => _draft.Toggle(Ability, columnId);

    /// <summary>Shows the draft's chain, where it has changed.</summary>
    internal void Sync()
    {
        var chain = _draft.ChainOf(Ability);
        if (Chain.SequenceEqual(chain)) return;
        Chain.Clear();
        foreach (var id in chain) Chain.Add(id);
    }
}

// ── Root grid view model ──────────────────────────────────────────────────────

/// <summary>
/// The ability grid: a row per ability, a column per model, and in each row the chain of columns that serve it — as an
/// <see cref="AiConfigDraft"/> holds them. The Local column comes first and is always there: the models on this machine, one chosen
/// for each job on the Local page and saved as a column of its own (<see cref="LocalColumns"/>); its cell in a row is the row's
/// job's model, greyed where this machine runs none or none is chosen. The other columns are the providers' models the user adds.
/// </summary>
public partial class AiAbilityGridViewModel : ObservableObject
{
    private readonly AiConfigDraft _draft;
    private readonly LocalModelDownloads? _downloads;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly HashSet<LocalDownload> _following = [];

    /// <summary>1 while a refresh of the notice waits on the UI thread.</summary>
    private int _noticeQueued;

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

    /// <summary>What the grid says of the Local column: its models not downloaded yet, their download under way, or that this
    /// machine runs none; null when there is nothing to say.</summary>
    [ObservableProperty] private string? _localNotice;

    /// <summary>Whether the Local column's models that are not here can be downloaded from the grid now.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadLocalCommand))]
    private bool _canDownloadLocal;

    // ── Commands ──────────────────────────────────────────────────────────

    public IRelayCommand OpenAddPanelCommand  { get; }
    public IRelayCommand CancelAddCommand     { get; }
    public IRelayCommand ConfirmAddCommand    { get; }
    public IRelayCommand RemoveColumnCommand  { get; }

    // ── Construction ──────────────────────────────────────────────────────

    /// <summary>A grid over a draft of its own of <paramref name="config"/>.</summary>
    /// <param name="shelf">The models on this machine; without it, the Local column has nothing to use.</param>
    /// <param name="downloads">The app's downloads of them; without it, the grid offers none.</param>
    public AiAbilityGridViewModel(AiConfig config, ProviderSet? providers = null, LocalModelShelf? shelf = null, LocalModelDownloads? downloads = null)
        : this(new AiConfigDraft(config, providers, shelf), downloads)
    {
    }

    /// <param name="draft">The edit the grid shows, which the pages that share it edit too.</param>
    /// <param name="downloads">The app's downloads of the models on this machine; without it, the grid offers none.</param>
    public AiAbilityGridViewModel(AiConfigDraft draft, LocalModelDownloads? downloads = null)
    {
        _draft     = draft;
        _downloads = downloads;
        if (draft.Shelf is { } shelf) shelf.Store.Changed += (_, _) => _ui.InvokeAsync(RefreshLocal);

        SuggestedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModelSuggestions));
        RebuildAvailableOffers();

        Columns.Add(AiColumnViewModel.Local());
        foreach (var column in draft.Columns)
            Columns.Add(NewColumn(column));

        foreach (AiAbility ability in Enum.GetValues<AiAbility>())
        {
            var row = new AiAbilityRowViewModel(ability, draft);
            foreach (var col in Columns)
                row.Cells.Add(Cell(row, col));
            Rows.Add(row);
        }

        HasChanges = draft.HasChanges;
        draft.Changed += (_, _) =>
        {
            foreach (var row in Rows) row.Sync();
            HasChanges = draft.HasChanges;
            RefreshNotice();
        };

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

        RefreshNotice();
    }

    private AiAbilityCellViewModel Cell(AiAbilityRowViewModel row, AiColumnViewModel column)
        => new(row, column, column.IsLocal ? LocalCell : null,
               (ability, col) => _draft.Serves(ability, col.ProviderName, col.Kind, col.Model));

    /// <summary>A row's Local cell: whether it can be used, its note — the job's model, or why there is none — and whether that
    /// model has yet to be downloaded.</summary>
    private LocalCellState LocalCell(AiAbility ability)
    {
        var slot  = LocalColumns.SlotOf(ability);
        var shelf = _draft.Shelf;
        if (shelf is null || !shelf.Runs(slot))
            return new(false, Str.Get(WhyNoneRunsKey(shelf?.WhyNoneRuns(slot))));
        if (_draft.LocalModelFor(slot) is not { } chosen) return new(false, Str.Get("Shell.Config.AiGrid.Local.Choose"));

        var option = shelf.OptionsFor(slot).FirstOrDefault(o => o.IsIn(chosen));
        var name   = option?.Model.DisplayName ?? chosen.Model;
        var absent = option is not null && !shelf.IsReady(option);
        return new(true, absent ? Str.Format("Shell.Config.AiGrid.Local.NotHereFormat", name) : name, absent);
    }

    /// <summary>The string saying why no model for a job runs here: none offered, none with a build for this machine — whose
    /// System components say why — or none small enough.</summary>
    internal static string WhyNoneRunsKey(UnrunnableReason? reason) => reason switch
    {
        UnrunnableReason.NoBuildForThisMachine  => "Shell.Config.AiGrid.Local.NoBuild",
        UnrunnableReason.TooLargeForThisMachine => "Shell.Config.AiGrid.Local.TooLarge",
        _                                       => "Shell.Config.AiGrid.Local.None",
    };

    /// <summary>Takes the Local cells again — a model fetched or removed since — and what the grid says of them.</summary>
    public void RefreshLocal()
    {
        foreach (var cell in Rows.SelectMany(r => r.Cells).Where(c => c.Column.IsLocal)) cell.Refresh();
        RefreshNotice();
    }

    /// <summary>Says what the Local column needs: that this machine runs no model, that its models are downloading and how far
    /// they have got, or that they are not here and can be downloaded at once.</summary>
    private void RefreshNotice()
    {
        if (_draft.Shelf is not { } shelf) { (LocalNotice, CanDownloadLocal) = (null, false); return; }
        if (!Enum.GetValues<LocalSlot>().Any(shelf.Runs)) { (LocalNotice, CanDownloadLocal) = (Str.Get("Shell.Config.AiGrid.Local.NoHardware"), false); return; }

        var missing = _draft.MissingLocal();
        if (missing.Count == 0) { (LocalNotice, CanDownloadLocal) = (null, false); return; }

        var running = missing.Select(o => _downloads?.Of(o)).OfType<LocalDownload>().ToList();
        if (running.Count > 0)
        {
            foreach (var download in running) Follow(download);
            var total   = running.Sum(d => d.Total);
            var percent = total > 0 ? (int)(running.Sum(d => d.Received) * 100 / total) : 0;
            var left    = LocalDownload.Say(running.Any(d => d.Left is null) ? null : running.Max(d => d.Left));
            LocalNotice      = Str.Format("Shell.Config.AiGrid.Local.DownloadingFormat", percent) + (left is null ? "" : " — " + left);
            CanDownloadLocal = false;
            return;
        }

        var plan = LocalDownloadPlan.For(missing, shelf.Store);
        if (!plan.Room.Fits) { (LocalNotice, CanDownloadLocal) = (plan.Room.Refusal, false); return; }
        LocalNotice      = Str.Format("Shell.Config.AiGrid.Local.MissingFormat", SizeFormatter.FormatBytes(plan.Bytes));
        CanDownloadLocal = _downloads is not null;
    }

    /// <summary>Downloads every model the Local column is used with that is not here — each once, however many jobs use it.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadLocal))]
    private void DownloadLocal()
    {
        if (_downloads is null) return;
        foreach (var download in _downloads.FetchAll(_draft.MissingLocal()).Values) Follow(download);
        RefreshNotice();
    }

    /// <summary>Says again what the Local column needs as a download moves on.</summary>
    private void Follow(LocalDownload download)
    {
        if (_following.Add(download)) download.PropertyChanged += (_, _) => QueueNotice();
    }

    /// <summary>Refreshes the notice on the UI thread once for however many changes arrive before it gets there.</summary>
    private void QueueNotice()
    {
        if (Interlocked.Exchange(ref _noticeQueued, 1) == 1) return;
        _ui.InvokeAsync(() =>
        {
            Volatile.Write(ref _noticeQueued, 0);
            RefreshNotice();
        });
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
        if (_draft.Providers?.Find(offer.ProviderName, offer.Kind) is not { } provider)
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

    private void RebuildAvailableOffers()
    {
        AvailableOffers.Clear();
        foreach (var (providerName, kind) in _draft.Offers())
            AvailableOffers.Add(new ProviderOffer(providerName, kind));
    }

    public void ResetAddPanel()
    {
        ShowAddPanel = false;
        NewModelName = string.Empty;
        NewOffer     = null;
        SuggestedModels.Clear();
    }

    // ── Add / remove columns ──────────────────────────────────────────────

    private AiColumnViewModel NewColumn(ProviderModelPair column)
        => new(column.Id, column.ProviderName, column.Model, column.Kind, column.AssemblyFileName, _draft.StaysOnMachine(column));

    private void AddColumn()
    {
        if (NewOffer is not { } offer || string.IsNullOrWhiteSpace(NewModelName))
            return;

        var colVm = NewColumn(_draft.AddColumn(offer.ProviderName, offer.Kind, NewModelName));
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
            if (row.Cells.FirstOrDefault(c => c.Column.Id == col.Id) is { } cell) row.Cells.Remove(cell);
        _draft.RemoveColumn(col.Id);
    }

    // ── The Local column's models ─────────────────────────────────────────

    /// <summary>The model chosen for <paramref name="slot"/>, or null when none is.</summary>
    public ProviderModelPair? LocalModelFor(LocalSlot slot) => _draft.LocalModelFor(slot);

    /// <summary>Makes <paramref name="model"/> <paramref name="slot"/>'s model (<see cref="AiConfigDraft.ChooseLocal"/>).</summary>
    public void ChooseLocal(LocalSlot slot, ProviderModelPair model)
    {
        _draft.ChooseLocal(slot, model);
        RefreshLocal();
    }

    /// <summary>Writes the grid into its config.</summary>
    public void Apply() => _draft.Apply();
}
