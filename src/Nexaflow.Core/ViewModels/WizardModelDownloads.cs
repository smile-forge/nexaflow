using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;
using Nexaflow.Localization.Formatting;
using Nexaflow.Localization;
using System.Collections.ObjectModel;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The models on this machine the workspace is set up to use and still needs downloaded — its language model, speech in and
/// speech out, with the runtime they share — each with its size, the files already here left out; the room the download
/// takes on the asset store's drive against what is free; and the choice of downloading them now or on first use. Download
/// now hands them to the app's downloads, which go on — and resume — once the wizard is finished; Later downloads nothing,
/// each model then offering its download the first time it is used. Where the drive has too little room, only Later is
/// offered. Pictures, video and the picture tools are not asked here: they are offered when first used.
/// </summary>
/// <param name="asked">Whether the step is asked, given what earlier steps chose.</param>
/// <param name="options">The models it offers.</param>
/// <param name="providers">The session's provider pool: the files on this machine, and the app's downloads of more.</param>
internal sealed class ModelDownloadStep : ObservableObject, IWizardStep
{
    private readonly Func<bool> _asked;
    private readonly IReadOnlyList<LocalModelOption> _options;
    private readonly ProviderManager _providers;
    private readonly WizardModelDownloadContent _content;
    private LocalDownloadPlan? _plan;

    public ModelDownloadStep(Func<bool> asked, IReadOnlyList<LocalModelOption> options, ProviderManager providers)
    {
        _asked     = asked;
        _options   = options;
        _providers = providers;
        _content = new WizardModelDownloadContent(DownloadNow);
        _content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardModelDownloadContent.Choice)) OnPropertyChanged(nameof(IsComplete));
        };
    }

    public string Title      => Str.Get("Shell.Wizard.Downloads.Title");
    public object Content    => _content;
    public bool   IsComplete => _content.IsDecided;
    public bool   Applies    => _asked() && Plan() is { Rows.Count: > 0 };
    public void   Commit() { }

    /// <summary>The plan as it stood when last read: the files on disk do not change while the wizard asks.</summary>
    private LocalDownloadPlan? Plan()
        => _plan ??= _providers.Assets is { } store ? LocalDownloadPlan.For(_options, store) : null;

    /// <summary>Weighs the download afresh — the drive's room may have changed since the step was last shown — unless it has
    /// been decided.</summary>
    public void OnEnter()
    {
        if (_content.IsDecided) return;
        _plan = null;
        if (Plan() is not { } plan) return;

        _content.Rows.Clear();
        foreach (var row in plan.Rows)
            _content.Rows.Add(new WizardModelDownloadRow(row.Name, JobOf(row.Kind), SizeFormatter.FormatBytes(row.Bytes)));
        _content.Total = Str.Format("Shell.Wizard.Downloads.TotalFormat", SizeFormatter.FormatBytes(plan.Bytes));
        _content.Fits  = plan.Room.Fits;
        var room = plan.Room;
        _content.Room = room.Fits
            ? Str.Format("Shell.Wizard.Downloads.RoomFormat", SizeFormatter.FormatBytes(room.Bytes), room.Drive,
                         room.Free is { } free ? SizeFormatter.FormatBytes(free) : Str.Get("Shell.Wizard.Downloads.FreeUnknown"))
            : Str.Format("Shell.Wizard.Downloads.ShortFormat", SizeFormatter.FormatBytes(room.Bytes), room.Drive,
                         SizeFormatter.FormatBytes(room.Free ?? 0), SizeFormatter.FormatBytes(room.Shortfall));
    }

    private static string? JobOf(ModelKind? kind) => kind switch
    {
        ModelKind.Language     => Str.Get("Shell.Config.Local.Job.Language"),
        ModelKind.SpeechToText => Str.Get("Shell.Config.Local.Job.SpeechToText"),
        ModelKind.TextToSpeech => Str.Get("Shell.Config.Local.Job.TextToSpeech"),
        _                      => null,
    };

    /// <summary>Hands every model offered to the app's downloads — a model already downloading is joined — and has its row follow
    /// it; a runtime's row says it comes with them.</summary>
    private void DownloadNow()
    {
        if (_providers.Service<LocalModelDownloads>() is not { } downloads || _plan is not { } plan) return;
        var fetched = downloads.FetchAll(_options);
        foreach (var (row, line) in plan.Rows.Zip(_content.Rows))
        {
            if (row.Option is { } option) line.Follow(fetched[option]);
            else line.Progress = Str.Get("Shell.Wizard.Downloads.WithModels");
        }
    }
}

/// <summary>
/// The models on this machine a workspace is set up to use and still needs: each with what it adds to the download, the
/// total, and the room it takes on the drive against what is free — with Download now where it fits, and Later always.
/// Once downloading, each model's row follows its download; the wizard can be finished while they go on.
/// </summary>
public sealed partial class WizardModelDownloadContent : ObservableObject
{
    private readonly Action _downloadNow;

    internal WizardModelDownloadContent(Action downloadNow) => _downloadNow = downloadNow;

    public ObservableCollection<WizardModelDownloadRow> Rows { get; } = [];

    [ObservableProperty] private string _total = string.Empty;

    /// <summary>The room the download takes, and what is free — or, where it does not fit, how much is short.</summary>
    [ObservableProperty] private string _room = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadNowCommand))]
    private bool _fits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDecided), nameof(Outcome))]
    [NotifyCanExecuteChangedFor(nameof(DownloadNowCommand), nameof(LaterCommand))]
    private WizardDownloadChoice? _choice;

    public bool IsDecided => Choice is not null;

    /// <summary>What the choice made comes to; null until one is made.</summary>
    public string? Outcome => Choice switch
    {
        WizardDownloadChoice.Now   => Str.Get("Shell.Wizard.Downloads.Started"),
        WizardDownloadChoice.Later => Str.Get("Shell.Wizard.Downloads.Deferred"),
        _                          => null,
    };

    private bool CanDownloadNow() => Fits && Choice is null;

    [RelayCommand(CanExecute = nameof(CanDownloadNow))]
    private void DownloadNow()
    {
        _downloadNow();
        Choice = WizardDownloadChoice.Now;
    }

    private bool CanChooseLater() => Choice is null;

    [RelayCommand(CanExecute = nameof(CanChooseLater))]
    private void Later() => Choice = WizardDownloadChoice.Later;
}

/// <summary>One line of the Download models step: a model, or the runtime the models share, with its size — and, once
/// downloading, how far it has got.</summary>
public sealed partial class WizardModelDownloadRow : ObservableObject
{
    public WizardModelDownloadRow(string name, string? job, string size)
    {
        Name = name;
        Job  = job;
        Size = size;
    }

    public string  Name { get; }

    /// <summary>The job the model does for the workspace; null for a runtime.</summary>
    public string? Job  { get; }

    public string  Size { get; }

    /// <summary>The download fetching it, for its percentage; null until there is one.</summary>
    [ObservableProperty] private LocalDownload? _download;

    /// <summary>How its download goes, as the row says it; null until it starts.</summary>
    [ObservableProperty] private string? _progress;

    /// <summary>Follows <paramref name="download"/>, saying how far it has got, about how long is left, and how it ended.
    /// Its figures change on the thread it runs on; the row's text follows them there.</summary>
    internal void Follow(LocalDownload download)
    {
        Download = download;
        download.PropertyChanged += (_, _) => Tell(download);
        Tell(download);
    }

    private void Tell(LocalDownload download)
        => Progress = download.Failure is { } why ? Str.Format("Shell.Wizard.Downloads.FailedFormat", why)
                    : !download.IsRunning         ? Str.Get("Shell.Wizard.Downloads.Done")
                    : LocalDownload.Say(download.Left) is { } left
                        ? Str.Format("Shell.Wizard.Downloads.PercentLeftFormat", download.Percent, left)
                        : Str.Format("Shell.Wizard.Downloads.PercentFormat", download.Percent);
}

/// <summary>Whether the models a step offers are downloaded now or on first use.</summary>
public enum WizardDownloadChoice { Now, Later }
