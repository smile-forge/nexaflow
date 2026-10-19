using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>Something on screen already offering a download of models — a picture tool's banner, for an edit the recipe asks for.</summary>
public interface IDownloadOffer : INotifyPropertyChanged
{
    bool CanDownload { get; }
}

/// <summary>
/// One set of models the editor's controls need — picture selection, picture erasing, the picture tools — and whether they
/// are here. Until they are, the controls that need them are disabled and say why; while a panel that needs them is open,
/// the banner over the picture offers the download, unless a picture tool's own banner already does.
/// </summary>
public sealed partial class ModelsGate : ObservableObject
{
    private readonly IToolModels? _models;
    private readonly string _texts;
    private IDownloadOffer[] _offeredElsewhere = [];

    /// <param name="models">The models, where the app has them.</param>
    /// <param name="texts">The prefix of the gate's own words: <c>Name</c> and <c>Offer</c> after it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the gate is read.</param>
    public ModelsGate(IToolModels? models, string texts, Func<Action, Task> runOnUi)
    {
        _models = models;
        _texts = texts;
        if (models is not null) models.Changed += (_, _) => _ = runOnUi(Refresh);
    }

    /// <summary>This machine runs them at all: the controls that need them show.</summary>
    public bool IsOffered => _models is { State: not ToolModelsState.Unavailable };

    /// <summary>They are here: the controls that need them work.</summary>
    public bool IsReady => _models is { State: ToolModelsState.Ready };

    /// <summary>A panel that needs them is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOffer))]
    private bool _isWanted;

    /// <summary>The banner offers them: wanted, runnable here, not here yet, and offered by nothing else on screen.</summary>
    public bool ShowsOffer => IsWanted && IsOffered && !IsReady && !_offeredElsewhere.Any(o => o.CanDownload);

    /// <summary>They can be fetched now — not here, and not coming already.</summary>
    public bool CanDownload => _models is { State: ToolModelsState.NotDownloaded };

    /// <summary>What a control that needs them says while it is disabled; null once they are here.</summary>
    public string? DisabledReason => _models switch
    {
        null or { State: ToolModelsState.Ready or ToolModelsState.Unavailable } => null,
        { State: ToolModelsState.Downloading } models => Str.Format("Images.Models.DownloadingTipFormat", Name, models.DownloadPercent),
        { } models => Str.Format("Images.Models.NeedsTipFormat", Name, SizeFormatter.FormatBytes(models.DownloadBytes)),
    };

    /// <summary>What the banner says: what needs them and what they cost, how far they have come, or why the last download stopped.</summary>
    public string? Notice => _models switch
    {
        null or { State: ToolModelsState.Ready or ToolModelsState.Unavailable } => null,
        { State: ToolModelsState.Downloading } models => Str.Format("Images.Models.DownloadingFormat", Name, models.DownloadPercent),
        { DownloadFailure: { } failure } => Str.Format("Images.Models.StoppedFormat", Name, failure),
        { } models => Str.Format(_texts + "Offer", SizeFormatter.FormatBytes(models.DownloadBytes)),
    };

    private string Name => Str.Get(_texts + "Name");

    /// <summary>Banners that offer these models too: while one does, this gate's offer stands back, so one download is offered once.</summary>
    public void Defer(params IDownloadOffer[] offers)
    {
        _offeredElsewhere = offers;
        foreach (var offer in offers)
            offer.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IDownloadOffer.CanDownload)) OnPropertyChanged(nameof(ShowsOffer));
            };
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => _models?.Download();

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsOffered));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(ShowsOffer));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(DisabledReason));
        OnPropertyChanged(nameof(Notice));
        DownloadCommand.NotifyCanExecuteChanged();
    }
}
