using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// A workspace page for a provider whose models are files on this machine (<see cref="ILocalModelCatalog"/>): every
/// model it offers, its size, whether it is here, and the means to fetch or remove it. When the provider's kind of
/// model serves exactly one ability — Whisper and Speech In — each model can be chosen for it here; the choice is an
/// edit of the ability grid it shares with the AI page, applied with it.
/// </summary>
public sealed partial class LocalModelsViewModel : ObservableObject
{
    private readonly AiAbilityGridViewModel _grid;
    private readonly Dispatcher             _ui = Dispatcher.CurrentDispatcher;

    public string ProviderName { get; }

    /// <summary>The ability this provider's models serve, when there is exactly one; null otherwise.</summary>
    public AiAbility? Ability { get; }

    public bool CanChoose => Ability is not null;

    public string Heading { get; }
    public string Intro   { get; }

    public ObservableCollection<LocalModelRowViewModel> Models { get; } = [];

    public LocalModelsViewModel(IAiProvider provider, ILocalModelCatalog catalog, AiAbilityGridViewModel grid,
                                ModelAssetStore store, IBackgroundActivityManager? activities)
    {
        _grid        = grid;
        ProviderName = provider.Name;

        var abilities = Enum.GetValues<AiAbility>()
            .Where(a => Enum.GetValues<ModelKind>().Any(k => provider.Serves(k) && AiAbilityNames.Kind(a) == k))
            .ToList();
        Ability = abilities.Count == 1 ? abilities[0] : null;

        Heading = Str.Format("Shell.Config.LocalModels.Heading", ProviderName);
        Intro   = Ability is { } ability
            ? Str.Format("Shell.Config.LocalModels.IntroChoose", ProviderName, AiAbilityNames.Friendly(ability))
            : Str.Format("Shell.Config.LocalModels.Intro", ProviderName);

        foreach (var model in catalog.Catalog)
            Models.Add(new LocalModelRowViewModel(this, model, store, activities));

        store.Changed += (_, asset) => _ui.InvokeAsync(() =>
        {
            foreach (var row in Models.Where(r => r.Holds(asset))) row.Refresh();
        });
        if (Ability is { } chosen) _grid.Rows.Single(r => r.Ability == chosen).Chain.CollectionChanged += (_, _) => RefreshInUse();
        _grid.Columns.CollectionChanged += (_, _) => RefreshInUse();
        RefreshInUse();
    }

    /// <summary>The model the grid has the ability use — the first of this provider's columns in its chain.</summary>
    public string? InUse => Ability is { } ability ? _grid.ModelFor(ability, ProviderName) : null;

    internal void Use(LocalModelRowViewModel row)
    {
        if (Ability is not { } ability) return;
        _grid.UseModel(ability, ProviderName, row.Id);
    }

    private void RefreshInUse()
    {
        var inUse = InUse;
        foreach (var row in Models) row.IsInUse = row.Id == inUse;
    }
}

/// <summary>One model on a <see cref="LocalModelsViewModel"/> page.</summary>
public sealed partial class LocalModelRowViewModel : ObservableObject
{
    private readonly LocalModelsViewModel        _page;
    private readonly CatalogModel                _model;
    private readonly ModelAssetStore             _store;
    private readonly IBackgroundActivityManager? _activities;

    public string Id          => _model.Id;
    public string DisplayName => _model.DisplayName;
    public string Size        => SizeFormatter.FormatBytes(_model.Files.Sum(f => f.Size));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand))]
    private bool _isOnDisk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private int _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private string? _failure;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseCommand), nameof(RemoveCommand))]
    private bool _isInUse;

    public LocalModelRowViewModel(LocalModelsViewModel page, CatalogModel model, ModelAssetStore store, IBackgroundActivityManager? activities)
    {
        _page       = page;
        _model      = model;
        _store      = store;
        _activities = activities;
        Refresh();
    }

    public string Status =>
          IsDownloading    ? Str.Format("Shell.Config.LocalModels.Downloading", Percent)
        : Failure is { } f ? f
        : IsOnDisk         ? Str.Get("Shell.Config.LocalModels.OnDisk")
        :                    Str.Get("Shell.Config.LocalModels.NotDownloaded");

    internal bool Holds(ModelAsset asset) => _model.Files.Contains(asset);

    internal void Refresh() => IsOnDisk = _model.Files.All(f => _store.Locate(f) is not null);

    private bool CanDownload => !IsOnDisk && !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task Download()
    {
        IsDownloading = true;
        Failure       = null;
        var activity  = _activities?.StartActivity($"Downloading {DisplayName}…");
        try
        {
            foreach (var file in _model.Files)
                await _store.EnsureAsync(file, new Progress<AssetProgress>(p =>
                {
                    Percent = (int)(p.Fraction * 100);
                    activity?.Update($"Downloading {DisplayName}… {Percent}%");
                }));
            activity?.Complete();
        }
        catch (Exception ex)
        {
            Failure = ex.Message;
            activity?.Fail(ex.Message);
        }
        finally
        {
            IsDownloading = false;
            Refresh();
        }
    }

    /// <summary>A model in use stays: removing it would leave its ability with nothing to run.</summary>
    private bool CanRemove => IsOnDisk && !IsDownloading && !IsInUse;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task Remove()
    {
        foreach (var file in _model.Files) await _store.RemoveAsync(file);
        Refresh();
    }

    private bool CanUse => _page.CanChoose && !IsInUse;

    /// <summary>Chooses this model for the page's ability, and fetches it when it is not here yet.</summary>
    [RelayCommand(CanExecute = nameof(CanUse))]
    private async Task Use()
    {
        _page.Use(this);
        if (CanDownload) await Download();
    }
}
