using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
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
    /// <param name="tools">The tools this machine runs with models of their own — app-wide, offered beside the jobs.</param>
    /// <param name="cudaPack">The CUDA pack's download, app-wide; null where there is none to show.</param>
    public LocalModelsViewModel(LocalModelShelf shelf, AiAbilityGridViewModel grid, Workspace workspace, LocalModelDownloads downloads,
                                Action committed, IReadOnlyList<ILocalTool> tools, CudaPackDownload? cudaPack = null)
    {
        CudaPack = cudaPack is null ? null : new LocalCudaPackViewModel(cudaPack);
        foreach (var tool in tools) Tools.Add(new LocalToolViewModel(tool));
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

    /// <summary>The tools, each with what it does and Download while it is not here.</summary>
    public ObservableCollection<LocalToolViewModel> Tools { get; } = [];

    /// <summary>The CUDA pack's line above the tools, shown while this machine runs on its GPU without it; null where there is no
    /// pack download to show.</summary>
    public LocalCudaPackViewModel? CudaPack { get; }
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
        : Str.Get(AiAbilityGridViewModel.WhyNoneRunsKey(_shelf.WhyNoneRuns(Slot)));

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

/// <summary>
/// A tool on the Local page: what it does, whether it is here, and Download while it is not — fetched app-wide, for
/// every workspace, since a tool is no job of one.
/// </summary>
public sealed partial class LocalToolViewModel : ObservableObject
{
    private readonly ILocalTool _tool;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public LocalToolViewModel(ILocalTool tool)
    {
        _tool = tool;
        tool.Changed += (_, _) => _ui.InvokeAsync(Refresh);
        Refresh();
    }

    public string Name => _tool.Name;

    public string About => _tool.About;

    /// <summary>Here, coming and how far, or why this machine cannot run it; empty while it waits to be fetched.</summary>
    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _canDownload;

    /// <summary>What Download fetches.</summary>
    [ObservableProperty] private string _downloadLabel = string.Empty;

    public void Refresh()
    {
        // Its own files here, it lacks only the CUDA pack: that is fetched once, from the pack's own line, not as this tool.
        bool waitsForPack = _tool.State == ToolModelsState.NotDownloaded && _tool.OwnFilesHere;
        CanDownload   = _tool.State == ToolModelsState.NotDownloaded && !waitsForPack;
        DownloadLabel = Str.Format("Shell.Config.Local.DownloadFormat", SizeFormatter.FormatBytes(_tool.DownloadBytes));
        Status = _tool.State switch
        {
            ToolModelsState.Unavailable => Str.Get("Shell.Config.Local.NoBuild"),
            ToolModelsState.Downloading => Str.Format("Shell.Config.Local.DownloadingFormat", _tool.Name, _tool.DownloadPercent),
            ToolModelsState.Ready       => Str.Get("Shell.Config.Local.Here"),
            _ when waitsForPack         => Str.Get("Shell.Config.Local.WaitsForCudaPack"),
            _ when _tool.DownloadFailure is { } why => Str.Format("Shell.Config.Local.FailedFormat", _tool.Name, why),
            _                           => string.Empty,
        };
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => _tool.Download();
}

/// <summary>
/// The CUDA pack on the Local page, above the tools: shown while a machine it suits is without it, with a Download of its
/// own — fetched once, app-wide, for every tool and model that runs on the GPU. A tool whose own files are here waits on it
/// alone, and offers no download of its own.
/// </summary>
public sealed partial class LocalCudaPackViewModel : ObservableObject
{
    private readonly CudaPackDownload _pack;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public LocalCudaPackViewModel(CudaPackDownload pack)
    {
        _pack = pack;
        pack.Changed += (_, _) => _ui.InvokeAsync(Refresh);
        Refresh();
    }

    public string Name => Str.Get("Shell.Components.CudaPack.Name");

    public string About => Str.Get("Shell.Config.Local.CudaPackAbout");

    /// <summary>The machine runs on its GPU and the pack is not here: the line shows.</summary>
    [ObservableProperty] private bool _isMissing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _canDownload;

    /// <summary>What Download fetches: only what an earlier pack does not already hold.</summary>
    [ObservableProperty] private string _downloadLabel = string.Empty;

    /// <summary>Coming and how far, or why it stopped; empty while it waits to be fetched.</summary>
    [ObservableProperty] private string _status = string.Empty;

    public void Refresh()
    {
        IsMissing     = _pack.Applies && !_pack.IsHere;
        CanDownload   = IsMissing && !_pack.IsDownloading;
        DownloadLabel = Str.Format("Shell.Config.Local.DownloadFormat", SizeFormatter.FormatBytes(_pack.DownloadBytes));
        Status = _pack.IsDownloading                ? Str.Format("Shell.Config.Local.DownloadingFormat", Name, _pack.Percent)
               : IsMissing && _pack.Failure is { } why ? Str.Format("Shell.Config.Local.FailedFormat", Name, why)
               : string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => _pack.Download();
}
