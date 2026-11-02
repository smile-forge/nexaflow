using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The models AI picture editing runs on — the picture tools, picture selection and picture erasing — offered, fetched and
/// spoken of as one. Which of them a control needs is the editor's business, not the user's: each control still waits only
/// for its own, so what is already here works, but what is offered, its size, how far it has come and why it stopped are
/// the set's, and Download fetches every one not here yet.
/// </summary>
public sealed partial class PictureModels : ObservableObject
{
    private readonly IToolModels[] _sets;

    /// <param name="sets">Each set of models, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the set is read.</param>
    public PictureModels(IReadOnlyList<IToolModels?> sets, Func<Action, Task> runOnUi)
    {
        _sets = [.. sets.OfType<IToolModels>()];
        foreach (var set in _sets) set.Changed += (_, _) => _ = runOnUi(Refresh);
    }

    /// <summary>Raised on the UI thread when what the set says may have changed.</summary>
    public event EventHandler? Changed;

    // The sets this machine can run at all; one it never can is not offered, and does not count.
    private IEnumerable<IToolModels> Runnable => _sets.Where(s => s.State != ToolModelsState.Unavailable);

    private IEnumerable<IToolModels> Missing => Runnable.Where(s => s.State != ToolModelsState.Ready);

    /// <summary>Some of them can be fetched now — not here, and not coming already.</summary>
    public bool CanDownload => Runnable.Any(s => s.State == ToolModelsState.NotDownloaded);

    /// <summary>Some of them are coming.</summary>
    public bool IsDownloading => Runnable.Any(s => s.State == ToolModelsState.Downloading);

    /// <summary>What fetching the rest still costs, in bytes.</summary>
    public long MissingBytes => Missing.Sum(s => s.DownloadBytes);

    /// <summary>How far the rest has come, 0…100: each set by its share of what was left to fetch.</summary>
    public int DownloadPercent
    {
        get
        {
            var left = Missing.ToList();
            long bytes = left.Sum(s => s.DownloadBytes);
            if (bytes <= 0) return left.Count == 0 ? 100 : (int)left.Average(s => s.State == ToolModelsState.Downloading ? s.DownloadPercent : 0);
            return (int)Math.Round(left.Sum(s => (double)s.DownloadBytes * (s.State == ToolModelsState.Downloading ? s.DownloadPercent : 0)) / bytes);
        }
    }

    /// <summary>Why the last download stopped short, while some are still to fetch; null when none has.</summary>
    public string? DownloadFailure => Runnable.Where(s => s.State == ToolModelsState.NotDownloaded).Select(s => s.DownloadFailure).FirstOrDefault(f => !string.IsNullOrEmpty(f));

    /// <summary>What a banner offering them says — what they are for and what they cost, how far they have come, or why the last
    /// download stopped; null once there is nothing to fetch.</summary>
    public string? Notice => Status(Str.Format("Images.Models.OfferFormat", SizeFormatter.FormatBytes(MissingBytes)));

    /// <summary>
    /// What something waiting on them says: <paramref name="needs"/> — what it is for, and what the set costs — until a download
    /// starts; then how far it has come, or why it stopped. Null when nothing is left to fetch.
    /// </summary>
    public string? Status(string needs) =>
        IsDownloading ? Str.Format("Images.Models.DownloadingFormat", DownloadPercent)
        : DownloadFailure is { } failure ? Str.Format("Images.Models.StoppedFormat", failure)
        : CanDownload ? needs
        : null;

    /// <summary>What a control waiting on <paramref name="needed"/> says while it is disabled; null once those are here, or where
    /// this machine never runs them.</summary>
    public string? DisabledReason(IToolModels? needed) => needed switch
    {
        null or { State: ToolModelsState.Ready or ToolModelsState.Unavailable } => null,
        _ when IsDownloading => Str.Format("Images.Models.DownloadingTipFormat", DownloadPercent),
        _ => Str.Get("Images.Models.NeedsTip"),
    };

    /// <summary>Fetches every set not here yet, each shown with the app's other work in the background.</summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    public void Download()
    {
        foreach (var set in Runnable.Where(s => s.State == ToolModelsState.NotDownloaded).ToList()) set.Download();
    }

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        DownloadCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
