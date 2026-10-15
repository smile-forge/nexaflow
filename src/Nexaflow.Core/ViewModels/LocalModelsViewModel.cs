using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The workspace's Local page: for each job the models on this machine do — conversation and reasoning, speech to text, text
/// to speech, pictures, high-quality pictures, video — the model chosen for it, from a list that shows which are here, which
/// would be downloaded and how much, and which this machine cannot run. Choosing a model that is here takes effect at once;
/// one that is not is downloaded first by the app's downloads (<see cref="LocalModelDownloads"/>) — once, however many jobs
/// chose it, and on after the page closes — the job keeping its model until the new one is here. Opened again, the page shows
/// a download still under way.
/// </summary>
public sealed class LocalModelsViewModel
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    /// <param name="committed">Saves the grid's config once a choice of a model that is here has been written into it.</param>
    public LocalModelsViewModel(LocalModelShelf shelf, AiAbilityGridViewModel grid, Workspace workspace, LocalModelDownloads downloads,
                                Action committed)
    {
        foreach (var slot in Enum.GetValues<LocalSlot>())
            Jobs.Add(new LocalJobViewModel(slot, shelf, grid, workspace, downloads, committed));

        shelf.Store.Changed += (_, _) => _ui.InvokeAsync(() => { foreach (var job in Jobs) job.Refresh(); });

        // A choice that waited on a download is saved by the downloads; the grid this page shares is told of it too, so what
        // it holds stays what is saved.
        downloads.Chose += (chosenIn, slot, column) =>
        {
            if (!ReferenceEquals(chosenIn, workspace)) return;
            _ui.InvokeAsync(() =>
            {
                grid.ChooseLocal(slot, column);
                foreach (var job in Jobs.Where(j => j.Slot == slot)) job.Refresh();
            });
        };
    }

    public ObservableCollection<LocalJobViewModel> Jobs { get; } = [];
}

/// <summary>One job on the Local page: its model, the models it could have, and the download a choice is waiting on.</summary>
public sealed partial class LocalJobViewModel : ObservableObject
{
    private readonly LocalModelShelf        _shelf;
    private readonly AiAbilityGridViewModel _grid;
    private readonly Workspace              _workspace;
    private readonly LocalModelDownloads    _downloads;
    private readonly Action                 _committed;
    private readonly Dispatcher             _ui = Dispatcher.CurrentDispatcher;
    private LocalDownload?                  _waiting;
    private bool                            _showing;

    /// <summary>1 while telling the download's progress waits on the UI thread.</summary>
    private int _tellQueued;

    public LocalJobViewModel(LocalSlot slot, LocalModelShelf shelf, AiAbilityGridViewModel grid, Workspace workspace,
                             LocalModelDownloads downloads, Action committed)
    {
        Slot       = slot;
        _shelf     = shelf;
        _grid      = grid;
        _workspace = workspace;
        _downloads = downloads;
        _committed = committed;
        Label      = Str.Get($"Shell.Config.Local.Job.{slot}");
        Remark     = Str.Source?.Find($"Shell.Config.Local.Job.{slot}.Remark");

        foreach (var option in shelf.OptionsFor(slot)) Choices.Add(new LocalChoiceViewModel(option, shelf));

        // A choice made before the page was last closed may still be downloading.
        if (downloads.WaitingFor(workspace, slot) is { } waiting && downloads.Of(waiting) is { } download)
            Wait(download);
        Refresh();
    }

    public LocalSlot Slot { get; }

    public string Label { get; }

    /// <summary>What the job is worth knowing of, beside the models it could have — the fast video job makes clips from a
    /// description alone — or null; each job's own, in the string table.</summary>
    public string? Remark { get; }

    public ObservableCollection<LocalChoiceViewModel> Choices { get; } = [];

    /// <summary>Whether there is a model to choose: one this machine runs, where the job is more than dictation.</summary>
    public bool CanChoose => Choices.Any(c => c.Runs) && !IsDictationOnly;

    /// <summary>The speech job where this machine runs none of Nexaflow's own speech models: the mic dictates with
    /// <see cref="LocalColumns.Dictation"/>, and there is nothing to choose.</summary>
    public bool IsDictationOnly => _shelf.Dictates(Slot);

    /// <summary>Why the job has nothing to choose from; null when it has.</summary>
    public string? Note => CanChoose ? null
        : IsDictationOnly ? Str.Get("Shell.Config.Local.DictationNote")
        : Str.Get(Choices.Count == 0 ? "Shell.Config.AiGrid.Local.None" : "Shell.Config.AiGrid.Local.TooLarge");

    /// <summary>The model chosen — or being downloaded to be chosen.</summary>
    [ObservableProperty] private LocalChoiceViewModel? _selected;

    /// <summary>The download a choice waits on — how far it has got, about how long is left, the model in use meanwhile — or
    /// what stopped one; null otherwise.</summary>
    [ObservableProperty] private string? _status;

    [ObservableProperty] private bool _isDownloading;

    /// <summary>The model the job uses now.</summary>
    private LocalChoiceViewModel? Current => Choices.FirstOrDefault(c => c.Option.IsIn(_grid.LocalModelFor(Slot)));

    /// <summary>Takes again what is here, and shows the model in use — or the one a download is fetching.</summary>
    public void Refresh()
    {
        foreach (var choice in Choices) choice.Refresh();
        if (_waiting is { IsRunning: true } waiting) Show(Choices.FirstOrDefault(c => c.Option.IsIn(waiting.Option.Column())));
        else Show(Current);
    }

    partial void OnSelectedChanged(LocalChoiceViewModel? value)
    {
        if (_showing || value is null) return;
        Choose(value);
    }

    private void Choose(LocalChoiceViewModel choice)
    {
        if (!choice.Runs) { Refresh(); return; }
        if (choice == Current || _shelf.IsReady(choice.Option))
        {
            _downloads.Forget(_workspace, Slot);
            StopWaiting();
            if (choice != Current)
            {
                _grid.ChooseLocal(Slot, choice.Option.Column());
                _committed();
            }
            Refresh();
            return;
        }
        Wait(_downloads.Choose(_workspace, Slot, choice.Option));
    }

    /// <summary>Follows the download a choice waits on, saying how it goes.</summary>
    private void Wait(LocalDownload download)
    {
        StopWaiting();
        _waiting = download;
        download.PropertyChanged += OnDownloadChanged;
        IsDownloading = download.IsRunning;
        Tell();
    }

    private void StopWaiting()
    {
        if (_waiting is not null) _waiting.PropertyChanged -= OnDownloadChanged;
        _waiting = null;
        IsDownloading = false;
        Status = null;
    }

    /// <summary>Tells the download's progress on the UI thread once for however many changes arrive before it gets there.</summary>
    private void OnDownloadChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Interlocked.Exchange(ref _tellQueued, 1) == 1) return;
        _ui.InvokeAsync(() =>
        {
            Volatile.Write(ref _tellQueued, 0);
            Tell();
        });
    }

    private void Tell()
    {
        if (_waiting is not { } download) return;
        var name = download.Option.Model.DisplayName;
        if (download.Failure is { } failure)
        {
            StopWaiting();
            Status = Str.Format("Shell.Config.Local.FailedFormat", name, failure);
            Refresh();
            return;
        }
        if (!download.IsRunning)
        {
            StopWaiting();   // the downloads make the choice good, and the page hears of it
            Refresh();
            return;
        }

        IsDownloading = true;
        var said = Str.Format("Shell.Config.Local.DownloadingFormat", name, download.Percent);
        if (LocalDownload.Say(download.Left) is { } left) said += " — " + left;
        if (Current is { } keeping && !keeping.Option.IsIn(download.Option.Column())) said += ". " + Str.Format("Shell.Config.Local.KeepingFormat", keeping.DisplayName);
        Status = said;
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
