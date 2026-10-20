using Nexaflow.Core.AI;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Features.Common;

using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// Where each of a workspace's abilities stands: the models its row in the ability grid names, whether one of them serves
/// now, and — for a model Nexaflow runs itself — whether it waits on its download; and, on a use the user asked for, the
/// offer of that download.
/// </summary>
/// <param name="config">The workspace's ability grid, as the service holds it.</param>
/// <param name="registered">The provider registered for a column, available or not.</param>
/// <param name="providers">The runtime's provider set, which the models on this machine are read from.</param>
/// <param name="shell">Where the offer of a download is asked and its start said.</param>
internal sealed class AbilityReadiness(Func<AiConfig?> config, Func<string, IAiProvider?> registered,
                                       Func<ProviderSet?> providers, Func<IShellServices?> shell)
{
    public AiAbilityStatus StatusOf(AiAbility ability)
    {
        var columns = Columns(ability);
        if (columns.Count == 0) return AiAbilityStatus.Unassigned;
        if (columns.Any(c => registered(c.Id) is { IsAvailable: true })) return AiAbilityStatus.Ready;
        if (Shelf() is not { } shelf || Awaited(ability, columns, shelf) is not { } option) return new(AiAbilityState.Unavailable);
        return Downloads?.Of(option) is { IsRunning: true } download
            ? new(AiAbilityState.Downloading, download.Total, download.Percent)
            : new(AiAbilityState.NeedsDownload, shelf.MissingBytes(option));
    }

    /// <summary>
    /// True when <paramref name="ability"/> is served now. Where it waits on a download, the user is asked whether to download
    /// it — false either way, the download going on in the background if they agree; while it downloads, they are told how far
    /// it has got; where the drive has no room for it, they are told what it needs instead. Otherwise false.
    /// </summary>
    public async Task<bool> MakeReadyAsync(AiAbility ability, CancellationToken ct)
    {
        var status = StatusOf(ability);
        if (status.State == AiAbilityState.Ready) return true;
        if (shell() is not { } ui) return false;

        var name = AiAbilityNames.Friendly(ability);
        if (status.State == AiAbilityState.Downloading)
        {
            ui.ShowNotification(Str.Format("Shell.Ai.StillDownloading", name, status.DownloadPercent));
            return false;
        }
        if (status.State != AiAbilityState.NeedsDownload || Shelf() is not { } shelf || Downloads is not { } downloads
            || Awaited(ability, Columns(ability), shelf) is not { } option)
            return false;

        if (shelf.Store.RoomFor(option.Needs) is { Fits: false } room)
        {
            ui.ShowNotification(room.Refusal);   // offered only where it would fit: the download would be refused
            return false;
        }

        var model = option.Model.DisplayName;
        if (!await ui.ConfirmAsync(Str.Get("Shell.Ai.DownloadTitle"),
                                   Str.Format("Shell.Ai.DownloadAsk", name, model, SizeFormatter.FormatBytes(status.DownloadBytes)),
                                   Str.Get("Shell.Ai.DownloadConfirm"), Str.Get("Shell.Ai.DownloadLater"), ct))
            return false;
        downloads.Fetch(option);
        ui.ShowNotification(Str.Format("Shell.Ai.DownloadStarted", model, name));
        return false;
    }

    /// <summary>The columns <paramref name="ability"/>'s chain names that hold its kind of model, in its order.</summary>
    private List<ProviderModelPair> Columns(AiAbility ability)
    {
        if (config() is not { } grid || grid.Assignments.GetValueOrDefault(ability.ToString()) is not { } chain) return [];
        var kind = AiAbilityNames.Kind(ability);
        return [.. chain.ColumnIds.Select(id => grid.Columns.FirstOrDefault(c => c.Id == id)).OfType<ProviderModelPair>().Where(c => c.Kind == kind)];
    }

    /// <summary>The first model of Nexaflow's own in <paramref name="columns"/> that runs here and waits on its download.</summary>
    private static LocalModelOption? Awaited(AiAbility ability, IReadOnlyList<ProviderModelPair> columns, LocalModelShelf shelf)
    {
        var options = shelf.OptionsFor(LocalColumns.SlotOf(ability));
        return columns.Where(c => LocalColumns.IsLocal(c.Id))
                      .Select(c => options.FirstOrDefault(o => o.IsIn(c)))
                      .FirstOrDefault(o => o is { Runs: true } && !shelf.IsReady(o));
    }

    private LocalModelShelf? Shelf() => providers() is { } set && ProviderManager.Instance.Assets is { } store ? new(set, store) : null;

    private static LocalModelDownloads? Downloads => ProviderManager.Instance.Service<LocalModelDownloads>();
}
