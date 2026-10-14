using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The workspace's Local page: for each job the models on this machine do — conversation and reasoning, speech to text, text
/// to speech, pictures, high-quality pictures, video — the model chosen for it, from a list that shows which are here, which
/// would be downloaded and how much, and which this machine cannot run. Choosing a model that is here takes effect at once;
/// one that is not is downloaded first, the job keeping the model it has until the new one is here.
/// </summary>
public sealed class LocalModelsViewModel
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    /// <param name="committed">Saves the grid's config once a choice has been written into it.</param>
    public LocalModelsViewModel(LocalModelShelf shelf, AiAbilityGridViewModel grid, Action committed, IBackgroundActivityManager? activities)
    {
        foreach (var slot in Enum.GetValues<LocalSlot>())
            Jobs.Add(new LocalJobViewModel(slot, shelf, grid, committed, activities));

        shelf.Store.Changed += (_, _) => _ui.InvokeAsync(() =>
        {
            foreach (var job in Jobs) job.Refresh();
            grid.RefreshLocal();
        });
    }

    public ObservableCollection<LocalJobViewModel> Jobs { get; } = [];
}

/// <summary>One job on the Local page: its model, the models it could have, and a download under way.</summary>
public sealed partial class LocalJobViewModel : ObservableObject
{
    private readonly LocalModelShelf             _shelf;
    private readonly AiAbilityGridViewModel      _grid;
    private readonly Action                      _committed;
    private readonly IBackgroundActivityManager? _activities;
    private CancellationTokenSource?             _fetching;
    private bool                                 _showing;

    public LocalJobViewModel(LocalSlot slot, LocalModelShelf shelf, AiAbilityGridViewModel grid, Action committed, IBackgroundActivityManager? activities)
    {
        Slot        = slot;
        _shelf      = shelf;
        _grid       = grid;
        _committed  = committed;
        _activities = activities;
        Label       = Str.Get($"Shell.Config.Local.Job.{slot}");

        foreach (var option in shelf.OptionsFor(slot)) Choices.Add(new LocalChoiceViewModel(option, shelf));
        Refresh();
    }

    public LocalSlot Slot { get; }

    public string Label { get; }

    public ObservableCollection<LocalChoiceViewModel> Choices { get; } = [];

    /// <summary>Whether this machine runs any model for the job.</summary>
    public bool CanChoose => Choices.Any(c => c.Runs);

    /// <summary>Why the job has nothing to choose from; null when it has.</summary>
    public string? Note => CanChoose ? null
        : Str.Get(Choices.Count == 0 ? "Shell.Config.AiGrid.Local.None" : "Shell.Config.AiGrid.Local.TooLarge");

    /// <summary>The model chosen — or being fetched to be chosen.</summary>
    [ObservableProperty] private LocalChoiceViewModel? _selected;

    /// <summary>A download under way, or what stopped one; null otherwise.</summary>
    [ObservableProperty] private string? _status;

    [ObservableProperty] private bool _isDownloading;

    /// <summary>The model the job uses now.</summary>
    private LocalChoiceViewModel? Current => Choices.FirstOrDefault(c => c.Option.IsIn(_grid.LocalModelFor(Slot)));

    /// <summary>Takes again what is here, and shows the model in use unless another is being fetched.</summary>
    public void Refresh()
    {
        foreach (var choice in Choices) choice.Refresh();
        if (!IsDownloading) Show(Current);
    }

    partial void OnSelectedChanged(LocalChoiceViewModel? value)
    {
        if (_showing || value is null) return;
        _ = ChooseAsync(value);
    }

    private async Task ChooseAsync(LocalChoiceViewModel choice)
    {
        _fetching?.Cancel();
        if (!choice.Runs || choice == Current) { Show(IsDownloading ? Selected : Current); return; }
        if (_shelf.IsReady(choice.Option)) { Commit(choice); return; }

        var fetching = _fetching = new CancellationTokenSource();
        var keeping  = Current?.DisplayName;
        IsDownloading = true;
        Report(choice, 0, keeping);
        var activity = _activities?.StartActivity(Str.Format("Shell.Config.Local.DownloadingFormat", choice.DisplayName, 0));
        try
        {
            await _shelf.FetchAsync(choice.Option, new Progress<AssetProgress>(p =>
            {
                var percent = (int)(p.Fraction * 100);
                Report(choice, percent, keeping);
                activity?.Update(Str.Format("Shell.Config.Local.DownloadingFormat", choice.DisplayName, percent));
            }), fetching.Token);
            activity?.Complete();
            IsDownloading = false;
            Status = null;
            Commit(choice);
        }
        catch (OperationCanceledException)
        {
            activity?.Complete();
            if (_fetching == fetching) { IsDownloading = false; Status = null; Show(Current); }
        }
        catch (Exception ex)
        {
            activity?.Fail(ex.Message);
            IsDownloading = false;
            Status = Str.Format("Shell.Config.Local.FailedFormat", choice.DisplayName, ex.Message);
            Show(Current);
        }
    }

    private void Report(LocalChoiceViewModel choice, int percent, string? keeping)
        => Status = keeping is null
            ? Str.Format("Shell.Config.Local.DownloadingFormat", choice.DisplayName, percent)
            : Str.Format("Shell.Config.Local.DownloadingKeepingFormat", choice.DisplayName, percent, keeping);

    private void Commit(LocalChoiceViewModel choice)
    {
        _grid.ChooseLocal(Slot, choice.Option.Column());
        _committed();
        Refresh();
    }

    /// <summary>Shows <paramref name="choice"/> as selected without choosing it.</summary>
    private void Show(LocalChoiceViewModel? choice)
    {
        _showing = true;
        try { Selected = choice; }
        finally { _showing = false; }
    }
}

/// <summary>A model a job's list offers: its name, and whether it is here, how much it would download, or why it cannot run.</summary>
public sealed partial class LocalChoiceViewModel(LocalModelOption option, LocalModelShelf shelf) : ObservableObject
{
    public LocalModelOption Option => option;

    public string DisplayName => option.Model.DisplayName;

    public bool Runs => option.Runs;

    [ObservableProperty] private bool _isHere;

    /// <summary>What the list says beside it: here, the download it would take, or why this machine cannot run it.</summary>
    [ObservableProperty] private string _detail = string.Empty;

    public void Refresh()
    {
        IsHere = Runs && shelf.IsReady(option);
        Detail = option.Unrunnable switch
        {
            UnrunnableReason.TooLargeForThisMachine => Str.Get("Shell.Config.Local.TooLarge"),
            UnrunnableReason.NoBuildForThisMachine  => Str.Get("Shell.Config.Local.NoBuild"),
            _ when IsHere                           => Str.Get("Shell.Config.Local.Here"),
            _                                       => Str.Format("Shell.Config.Local.DownloadFormat", SizeFormatter.FormatBytes(shelf.MissingBytes(option))),
        };
    }
}
