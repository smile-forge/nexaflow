using Nexaflow.Features.Common;
using Nexaflow.Providers.Local;
using Nexaflow.Session.Services.LocalAi;

namespace Nexaflow.Session.Services;

/// <summary>
/// What the app has to ask before its first window, on a first run or after an update — only where something is genuinely missing,
/// with one exception: a config that could not be read takes the startup workspace's AI setup again, since what that file held is
/// gone and what replaced it is to be confirmed.
/// </summary>
public sealed class SetupAgenda
{
    /// <summary>The jobs whose models are offered for download here; pictures, video and tools are offered on first use.</summary>
    private static readonly LocalSlot[] OfferedSlots = [LocalSlot.Language, LocalSlot.SpeechToText, LocalSlot.TextToSpeech];

    private SetupAgenda(IReadOnlyList<string> unreadable, bool welcomes, IReadOnlyList<IFeatureConfig> mandatory, bool setsUpWorkspace,
                        IReadOnlyList<LocalModelOption> awaited)
    {
        Unreadable       = unreadable;
        Welcomes         = welcomes;
        MandatoryConfigs = mandatory;
        SetsUpWorkspace  = setsUpWorkspace;
        AwaitedDownloads = awaited;
    }

    /// <summary>The configs whose files were there but could not be read: their settings are back at their defaults.</summary>
    public IReadOnlyList<string> Unreadable { get; }

    /// <summary>A first run or an update: what is new is shown.</summary>
    public bool Welcomes { get; }

    /// <summary>The app-wide configs that say <c>[MandatorySetup]</c> and are new — a first run, a new feature, or one unreadable —
    /// or were carried forward from an older version without all they now require. One carried forward whole is not asked again.</summary>
    public IReadOnlyList<IFeatureConfig> MandatoryConfigs { get; }

    /// <summary>The startup workspace's AI is set up: it has no language model ready to answer, or a config could not be read.</summary>
    public bool SetsUpWorkspace { get; }

    /// <summary>
    /// After an update, the models the saved workspaces' Local columns hold for language and speech that are not here yet, each
    /// once however many workspaces use it — a grid from before the column has it folded in, as opening the workspace would — so
    /// they are offered together rather than found missing on first use. A startup workspace being set up is offered its own there.
    /// </summary>
    public IReadOnlyList<LocalModelOption> AwaitedDownloads { get; }

    /// <param name="startup">The workspace the first window opens on.</param>
    /// <param name="config">The session's configuration: which configs are new, were carried forward or could not be read.</param>
    /// <param name="runs">This run against the one before.</param>
    /// <param name="providers">The session's provider pool: whether a workspace has a model ready, and the models on this machine.</param>
    /// <param name="saved">The saved workspaces, whose Local columns are offered their models after an update.</param>
    public static SetupAgenda Of(Workspace startup, ConfigManager config, AppRuns runs, ProviderManager providers, WorkspaceManager saved)
    {
        var unreadable = config.GetUnreadableConfigs();

        var defaulted = config.GetDefaultedConfigs().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var migrated  = config.GetMigratedConfigs().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mandatory = config.GetAll().OfType<IFeatureConfig>()
            .Where(c => c is not WorkspacesConfig && ConfigRequirements.IsMandatory(c))
            .Where(c => defaulted.Contains(c.ConfigName) || (migrated.Contains(c.ConfigName) && !ConfigRequirements.IsComplete(c)))
            .ToList();

        startup.EnsureSharedServicesLoaded(providers);
        var setsUp = unreadable.Count > 0 || !WorkspaceManager.HasAnsweringModel(startup, providers);

        var awaited = runs.IsUpdate
            ? Awaited(saved.Workspaces.Where(w => !setsUp || !ReferenceEquals(w, startup)), providers)
            : [];

        return new SetupAgenda(unreadable, runs.IsFirstRun || runs.IsUpdate, mandatory, setsUp, awaited);
    }

    /// <summary>
    /// The models the Local columns of <paramref name="workspaces"/> hold for language and speech that are not here yet, each once,
    /// after the column is folded into each grid — as opening the workspace would — with a provider set acquired just long enough
    /// from <paramref name="pool"/> to read the models on this machine.
    /// </summary>
    private static IReadOnlyList<LocalModelOption> Awaited(IEnumerable<Workspace> workspaces, ProviderManager pool)
    {
        if (pool.Assets is not { } assets) return [];
        pool.DiscoverAll();
        var awaited = new List<LocalModelOption>();
        foreach (var workspace in workspaces.ToList())
        {
            workspace.EnsureSharedServicesLoaded(pool);
            var providers = pool.AcquireProviderSet(workspace.ProviderConfigs, WorkspaceManager.AssignedColumns(workspace.AiConfig));
            try
            {
                WorkspaceManager.FoldInLocalColumns(workspace, providers, assets);
                foreach (var option in LocalDownloadPlan.Awaited(workspace.AiConfig, new LocalModelShelf(providers, assets), OfferedSlots))
                    if (!awaited.Any(a => a.IsIn(option.Column()))) awaited.Add(option);
            }
            finally { pool.ReleaseProviderSet(providers); }
        }
        return awaited;
    }
}
