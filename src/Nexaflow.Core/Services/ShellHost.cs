using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Core.AI;
using Nexaflow.Core.Help;
using Nexaflow.Core.Localization;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services.Elevation;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;

using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Providers.AiHost;
using Nexaflow.Visuals.Common.Localization;
using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>How this launch asks the session to start.</summary>
/// <param name="Reset">"Reset Config" relaunched the app: the app-data directory is wiped before anything reads it.</param>
/// <param name="UiTest">A UI journey drives the app: every feature is activated up front, and nothing is downloaded.</param>
internal sealed record ShellHostOptions(bool Reset, bool UiTest);

/// <summary>What the UI toolkit the shell runs on gives the session: the pieces only it can make.</summary>
/// <param name="Ui">The thread the windows run on.</param>
/// <param name="Clipboard">The system clipboard.</param>
/// <param name="FolderWatchers">Makes the folder watcher, which owns a window of the toolkit's.</param>
/// <param name="ApplyAppearance">Applies the shell settings that decide how windows look — theme, animations, text size — before
/// the first window is built and before any feature folds its theme into the one applied.</param>
/// <param name="Views">Makes the view each open page is drawn with, from the toolkit's views; a page that could not be made is
/// drawn naming the session's missing components that may be why.</param>
internal sealed record ShellPlatform(
    IUiThread Ui,
    IShellClipboard Clipboard,
    Func<ShellFolderWatcher> FolderWatchers,
    Action<ShellConfig> ApplyAppearance,
    Func<ExternalDependencyRegistry, PageViews> Views);

/// <summary>
/// The shell session: everything that lives as long as the process — from login when the app starts with Windows, otherwise
/// from the first window to the last — and is shared by every workspace that runs in it. The configuration, the AI host and
/// the models it holds, the provider pool, the feature catalogue, the file system's watches and busy folders, the locations
/// found on this machine; and the factory every <see cref="WorkspaceRuntime"/> is built by. Workspaces come and go inside it,
/// several at once, each with its own runtime.
/// <para>
/// It starts in three stages, so the first window waits for nothing it does not need: what the window needs (the
/// constructor), what follows the window's first frame (<see cref="FirstWindowDrawn"/>), and what runs in the background
/// meanwhile (<see cref="StartBackground"/>). It ends in the reverse order (<see cref="DisposeAsync"/>). It names no UI
/// toolkit: what only the toolkit can make comes in a <see cref="ShellPlatform"/>.
/// </para>
/// </summary>
internal sealed class ShellHost : IAsyncDisposable
{
    /// <summary>How long ending waits for the runtimes' connections to close, the models to let go and the AI host to close.</summary>
    private static readonly TimeSpan RuntimesPatience  = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResidencyPatience = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AiHostPatience    = TimeSpan.FromSeconds(6);

    private readonly ShellHostOptions _options;
    private readonly AiHostClient _aiHost;
    private readonly ModelResidency _residency;
    private readonly CudaPackDownload? _cudaPack;
    private Task _firstDiscovery = Task.CompletedTask;
    private bool _ended;

    /// <summary>The thread the windows run on.</summary>
    public IUiThread Ui { get; }

    /// <summary>The session's background tasks: what the activity ticker shows, wherever a piece of slow work was started.</summary>
    public BackgroundActivityManager Activities { get; } = new();

    /// <summary>The location backends — WSL, SFTP and the like — every feature's registered in as its assembly activates.</summary>
    public LocationProviderRegistry LocationProviders { get; } = new();

    /// <summary>The locations found on this machine, mounted in every workspace.</summary>
    public DiscoveredLocations Discovered { get; }

    /// <summary>The session's inbox: every window shows every message.</summary>
    public MessageCenter Inbox { get; }

    /// <summary>What each shell window is given of the session.</summary>
    public WindowServices Windows { get; }

    /// <summary>The session's file and folder watches, and its busy folders.</summary>
    public FolderActivity Folders { get; }

    /// <summary>Builds every workspace runtime, and ends those still running when the session ends.</summary>
    public WorkspaceRuntimeFactory RuntimeFactory { get; }

    /// <summary>The session's live workspaces: one runtime each, active or dormant.</summary>
    public WorkspaceRuntimes Workspaces { get; }

    /// <summary>The shell's own settings.</summary>
    public ShellConfig Shell { get; }

    /// <summary>The UI language: the installed packs, the active one, and the strings and help pages they hold.</summary>
    public LanguageManager Language { get; }

    /// <summary>The third-party components the features declare, and what the last probe found of each.</summary>
    public ExternalDependencyRegistry Dependencies { get; }

    /// <summary>The view each open page is drawn with, kept while its tab is open, in whichever window.</summary>
    public PageViews Views { get; }

    /// <summary>
    /// Opens the session up to what the first window needs: the configuration and the shell's settings, the AI host, the
    /// providers every workspace names, the workspace list, the feature catalogue and the runtime factory. Runs on the UI
    /// thread; a throw ends the launch.
    /// </summary>
    public ShellHost(ShellHostOptions options, ShellPlatform platform)
    {
        _options = options;
        Ui       = platform.Ui;
        Folders  = new FolderActivity(platform.Ui, platform.FolderWatchers);
        Inbox    = new MessageCenter(platform.Ui);
        Discovered = new DiscoveredLocations(LocationProviders);
        Dependencies = new ExternalDependencyRegistry(platform.Ui);
        Views = platform.Views(Dependencies);

        // ── 0. Base path — single source of truth for all app-data paths ─────
        // NEXAFLOW_CONFIG_DIR overrides the default (used by UI tests for an isolated, fresh config
        // root, and handy for portable installs); otherwise %AppData%\Smile\nexaflow.
        var isolated = Environment.GetEnvironmentVariable("NEXAFLOW_CONFIG_DIR");
        var baseDir  = string.IsNullOrWhiteSpace(isolated)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smile", "nexaflow")
            : isolated;

        // "Reset Config" relaunch: wipe the directory now, in the fresh process, so no handle from the
        // old (exiting) instance is held. The empty dir then drives a clean first-run.
        if (options.Reset) DeleteDirectoryWithRetry(baseDir);

        // What is kept on this machine alone — caches too large to roam with the settings — under %LocalAppData%, unless the base
        // was set apart, when it is kept beside it so a UI test's root holds everything the run wrote.
        var localDir = string.IsNullOrWhiteSpace(isolated)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow")
            : Path.Combine(baseDir, "local");

        ConfigManager.Instance.Initialize(baseDir, localDir);
        StartupTimings.Mark("Init.ConfigManager");

        // ── 1. Shell config ──────────────────────────────────────────────────
        Shell = new ShellConfig();
        ConfigManager.Instance.Register(Shell, Shell.ConfigName);

        // From here this session knows whether it keeps a safety log: what startup held so far is written out or dropped.
        ShellSafetyLog.Instance.Apply(Shell.SafetyLogging);
        platform.ApplyAppearance(Shell);
        StartupTimings.Mark("Init.Theme");

        // The UI language: records the choice and points Str at it. Loads nothing - the first string or help page
        // asked for reads the pack (one resource-only assembly, once).
        Language = new LanguageManager(Path.Combine(AppContext.BaseDirectory, "Languages"), AssemblyLanguagePack.Load);
        Language.Select(Shell.Language);
        Str.Source = Language;
        // The help pages, read through the language: the Help page's registration is given them, and each runtime's assistant
        // the tools that read them.
        var help = new HelpLibrary(Language);
        FeatureManager.Instance.ProvideTool(help);
        StartupTimings.Mark("Init.Language");

        var securityConfig = new SecurityConfig();
        ConfigManager.Instance.Register(securityConfig, securityConfig.ConfigName);

        // ── 2. Workspaces config — must come before providers so we know which provider assemblies each workspace needs ──
        var wcConfig = new WorkspacesConfig();
        ConfigManager.Instance.Register(wcConfig, wcConfig.ConfigName);

        // A workcontexts.json that could not be read leaves the list defaulted to a single workspace,
        // which would strand every existing workspace's data — AI and provider config, ribbon, scratchpad
        // notes, conversations — in a folder nothing references any more. Rebuild the list from those
        // folders before the provider scan below, so the recovered workspaces' providers load too.
        var listUnreadable = ConfigManager.Instance.GetUnreadableConfigs()
            .Contains(wcConfig.ConfigName, StringComparer.OrdinalIgnoreCase);
        var recoveredWorkspaces = listUnreadable
            ? WorkspaceManager.RecoverWorkspacesFromDataFolders(wcConfig)
            : [];

        // ── 3. The AI host, the local tools and the providers ────────────────
        // Model files live outside the roaming profile; the folder voice models were once kept in is adopted from.
        // A UI journey fetches nothing: a download is minutes of network the run cannot wait on or count on.
        // The AI host — the process models Nexaflow runs itself are loaded in — starts on the first request for one.
        // Where the host's models run is planned once for the machine, across every workspace's models.
        // Models on this machine are downloaded once each, app-wide, whichever workspace asks.
        // Which models are loaded ahead, and what they may hold, is this machine's to weigh: what it runs, what was used on it this week.
        var preloadConfig = (ModelPreloadConfig)ConfigManager.Instance.Register(new ModelPreloadConfig(), new ModelPreloadConfig().ConfigName);
        _aiHost = StartAiHost(preloadConfig.MemoryForModelsPercent, options.UiTest);
        // Joined to the machine's AI host service, the share of memory its models may hold is the service's, and Settings shows it so.
        ConfigManager.Instance.AddRulings(new AiHostSettingRulings(() => _aiHost.ServiceMemory));
        // The model store is the AI host's: it alone reads and fetches the files, and the app sees the store as the host describes it,
        // read here in the background — each file found there is told of as it is read, as each that arrives later is.
        var assets = new AiHostModelStore(_aiHost);
        _ = ReadModelStoreAsync(assets);
        // The picture tools run in the same host, on the same plan of its GPUs, from the same store, as the owned models.
        var holds = new HostHolds(_aiHost);
        _residency = new ModelResidency(preloadConfig, new ModelUseHistory(Path.Combine(LocalAiFolder.Root(), "model-use.json")));
        // A host with nothing held in it closes after the same idle time a model is let go after.
        if (preloadConfig.IdleRelease is { } idle) _aiHost.RetireWhenIdle(idle, () => holds.HoldsModels);
        StartupTimings.Mark("Init.ModelStore");
        var hardware = MachineCards.Probe();
        StartupTimings.Mark("Init.HardwareProbe");
        // A machine that had the CUDA pack gets this version's in place of it, in the background, without being asked again.
        _cudaPack = new CudaPackDownload(assets, () => assets.HadEarlierRuntimePack, hardware, Activities);
        var pictureTools     = new PictureTools(new OwnedPictureTools(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        var pictureSelection = new PictureSelection(new OwnedPictureSelection(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        var pageReading      = new PageReading(new OwnedPageReading(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        var pictureErasing   = new PictureErasing(new OwnedPictureErasing(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        var sceneDepth       = new SceneDepthEstimation(new OwnedSceneDepth(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        // Not a feature's tool: the check Enhanced AI Protection Mode holds every picture made to, which Core runs itself.
        var pictureChecking  = new PictureChecking(new OwnedPictureChecking(Activities, assets, _aiHost, holds, hardware), assets, Activities);
        ProviderManager.Instance.Initialize(Activities, assets, _residency,
            _aiHost, holds, hardware,
            new LocalModelDownloads(assets, Activities, WorkspaceManager.Instance.UseLocalModel), _cudaPack, pictureTools, pictureSelection,
            pageReading, pictureErasing, sceneDepth, pictureChecking);
        FeatureManager.Instance.ProvideTool<IPictureTools>(pictureTools);
        FeatureManager.Instance.ProvideTool<IPictureSelection>(pictureSelection);
        FeatureManager.Instance.ProvideTool<IPageReading>(pageReading);
        FeatureManager.Instance.ProvideTool<IPictureErasing>(pictureErasing);
        FeatureManager.Instance.ProvideTool<ISceneDepth>(sceneDepth);
        StartupTimings.Mark("Init.LocalTools");

        // Load each workspace's AI config (stored per-workspace on disk) to discover which provider
        // assemblies are needed before loading them. Workspace has no runtime fields yet, so use a
        // temporary AiConfig per entry.
        var allProviderFiles = wcConfig.Contexts
            .SelectMany(workspace =>
            {
                var ai = new AiConfig();
                ConfigManager.Instance.LoadFrom(WorkspaceManager.WorkspaceDir(workspace.Name), ai, ai.ConfigName);
                ai.BringUpToDate();   // the workspace saves it
                return ai.Columns.Select(p => p.AssemblyFileName);
            })
            .Concat(LocalColumns.Assemblies)   // every workspace has the Local column
            .Distinct();
        ProviderManager.Instance.LoadConfigured(allProviderFiles);
        StartupTimings.Mark("Init.Providers");

        // ── 4. Workspaces — the list, and the factory their runtimes are built by ──
        WorkspaceManager.Instance.Initialize(wcConfig);

        // Quarantine workspace data folders orphaned by an older build's version-bump config reset (before
        // forward-migration existed): the list is authoritative only when workcontexts.json actually
        // loaded — a defaulted (missing/unreadable) list would make every folder look orphaned, so gate on it.
        var listIsAuthoritative = !ConfigManager.Instance.GetDefaultedConfigs()
            .Contains(wcConfig.ConfigName, StringComparer.OrdinalIgnoreCase);
        WorkspaceManager.Instance.QuarantineOrphanedDataFolders(listIsAuthoritative);

        // Persist the rebuilt list at once, so the recovery holds even if this session ends badly.
        if (listUnreadable) WorkspaceManager.Instance.SaveWorkspaces();

        RuntimeFactory = new WorkspaceRuntimeFactory(new RuntimeSessionServices(
            ProviderManager.Instance, Activities, Inbox.Post, VirtualFileSystem.Instance,
            LocationProviders, Discovered,
            new StoredCredentialSource(CredentialManagerStore.Default), () => _firstDiscovery,
            platform.Ui, Folders, new ElevatedBridgeLauncher(), platform.Clipboard,
            Dependencies, Language, Views, [.. new HelpClientTools(help).Tools, .. new MarkdownSyntaxTools(help).Tools]));
        // A workspace left stays dormant for as long as the person has said, then ends; a deleted one ends with its last window.
        Workspaces = new WorkspaceRuntimes(RuntimeFactory, platform.Ui, () => TimeSpan.FromMinutes(Shell.DormantWorkspaceMinutes),
                                           workspace => WorkspaceManager.Instance.Workspaces.Contains(workspace), TimeProvider.System);
        WorkspaceManager.Instance.Runtimes = Workspaces;
        Windows = new WindowServices(Activities, Workspaces, Inbox, Dependencies, Language, Views);
        StartupTimings.Mark("Init.Workspaces");

        // ── 5. Feature system ────────────────────────────────────────────────
        // Builds the (cached) discovery index — on a normal launch this loads NO feature assemblies.
        // Each assembly is loaded + activated lazily (first page, handler query, or the post-paint warm-up
        // of StartBackground); activation registers that assembly's global configs, archive handlers /
        // codecs (into the VFS) and theme contributions (folded below the theme applied in step 1), and runs the
        // feature's own setup for the session (IFeatureStartup) — the file system's file map and registries among them.
        FeatureManager.Instance.RegisterFeatures(platform.Ui, LocationProviders);

        // UI-test automation (--uiTest) drives the app far faster than a human and would outrun the
        // post-paint background warm-up, racing features that haven't loaded yet. Force every feature
        // assembly to load + activate synchronously now so the whole app is ready before automation starts.
        if (options.UiTest) FeatureCatalog.Instance.EnsureAllActivated();
        StartupTimings.Mark("Init.Features");



        // ── 6b. Damaged config: say so, say what came back ───────────────────
        // Settings that could not be read are already on defaults and on record in the crash log; this is
        // what tells the user, rather than leaving them to notice. The setup wizard opens for the same
        // reason (SetupWizardViewModel.Build re-asks on an unreadable config), so this is the account of
        // what happened and the wizard is the re-verification.
        ReportUnreadableConfigs(recoveredWorkspaces);

        // ── 7. About — read-only Options section (registered last so it sorts to the bottom) ──
        var aboutConfig = new AboutConfig();
        ConfigManager.Instance.Register(aboutConfig, aboutConfig.ConfigName);

        // ── 8. The machine's locations — looked for once, for every workspace, off the UI thread ──
        // The backends are loaded and the first look started now, beside the first window's build rather than ahead of it; a
        // runtime lets it finish, up to a point, before it mounts (WorkspaceRuntimeFactory.AwaitFirstDiscovery).
        _firstDiscovery = Task.Run(() =>
        {
            FeatureManager.Instance.EnsureLocationProviders();
            return Discovered.StartAsync();
        });
    }

    /// <summary>The first window has drawn: the models assigned to the open workspaces load ahead from now, so none of their
    /// loading competes with the frame.</summary>
    public void FirstWindowDrawn() => _residency.Open();

    /// <summary>
    /// Loads and activates the remaining feature assemblies off the UI thread — archive backends first, so browsing into an
    /// archive works as early as possible — so deferred features and their handlers, themes and configs are ready within a
    /// second or two without blocking first paint. The task ends when every feature is active.
    /// </summary>
    public Task StartBackground()
        => Task.Run(() => FeatureCatalog.Instance.WarmUpAll(["Nexaflow.Features.Compressed", "Nexaflow.Features.Email"]));

    /// <summary>
    /// Ends the session, in the reverse of the order it started: every workspace runtime still running — with windows, on
    /// standby or none — then the watches, the models held ahead, and the AI host. The runtimes let go of what they hold on
    /// the calling thread, the UI thread; what is waited on after that never needs it, so a synchronous wait on the UI
    /// thread cannot deadlock. Each wait is bounded. Ending again does nothing.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_ended) return ValueTask.CompletedTask;
        _ended = true;

        ShellSafetyLog.Instance.Step("session ending");
        Workspaces.Dispose();
        var runtimesEnding = RuntimeFactory.EndAllAsync();
        Folders.Dispose();
        return new ValueTask(FinishAsync(runtimesEnding));
    }

    private async Task FinishAsync(Task runtimesEnding)
    {
        await Bounded(runtimesEnding, RuntimesPatience, "the workspaces' connections").ConfigureAwait(false);
        await Bounded(_residency.DisposeAsync().AsTask(), ResidencyPatience, "model residency").ConfigureAwait(false);
        // Closing the pipe ends the host; it is killed if it lingers. The job object is the backstop for a crash.
        await Bounded(_aiHost.DisposeAsync().AsTask(), AiHostPatience, "the AI host").ConfigureAwait(false);
    }

    /// <summary>Waits for <paramref name="ending"/> no longer than <paramref name="patience"/>, recording what did not finish.</summary>
    private static async Task Bounded(Task ending, TimeSpan patience, string what)
    {
        try { await ending.WaitAsync(patience).ConfigureAwait(false); }
        catch (TimeoutException) { ShellSafetyLog.Instance.Step($"shutdown: {what} did not finish within {patience.TotalSeconds:0} s"); }
        catch (Exception ex) { ShellSafetyLog.Instance.Failed($"ending {what}", ex); }
    }

    /// <summary>
    /// The app's AI host client. Its store is the machine's own (<c>%ProgramData%\Smile-Forge\Models</c>), into which the host moves the
    /// files Nexaflow kept in its own folder before the host kept them — or, where <c>NEXAFLOW_AI_DIR</c> names a folder, that folder:
    /// a test's. A UI test's host downloads nothing.
    /// </summary>
    private static AiHostClient StartAiHost(int memoryShare, bool uiTest)
    {
        string? testStore = LocalAiFolder.Configured();
        var launcher = new ProcessAiHostLauncher(AiHostInstall.ExecutablePath,
                                                 store: testStore,
                                                 adoptFrom: testStore is null ? [LocalAiFolder.Root()] : [],
                                                 memoryShare: memoryShare,
                                                 logDirectory: () => ShellSafetyLog.Instance.Folder,
                                                 downloads: !uiTest);
        // A run on a store of its own, or under a UI test, keeps to a host of its own: the service's store is the machine's. A journey of
        // the service itself joins it as the app does.
        bool joins = testStore is null && !uiTest || Environment.GetEnvironmentVariable("NEXAFLOW_TEST_JOIN_SERVICE") == "1";
        var host = new AiHostClient(launcher, app: "Nexaflow", service: joins ? AiHostService.Machine : null);
        // A host's end is recorded only while the user keeps logs; the request it took down fails on its own.
        host.HostExited += (_, e) => ShellSafetyLog.Instance.AiHostEnded(e);
        return host;
    }

    /// <summary>Reads the host's model store, starting the host; then a machine that had the CUDA pack gets this version's in place of
    /// it, without being asked again. A host that cannot start leaves the store unread: every model shows as not here, and a download
    /// says why.</summary>
    private async Task ReadModelStoreAsync(AiHostModelStore store)
    {
        _ = Task.Run(LocalAiFolder.RetireWhisperCpp);
        try { await store.RefreshAsync(); }
        catch (AiHostException) { return; }
        if (!_options.UiTest && _cudaPack is { } pack) await pack.ReplaceEarlierAsync();
    }

    /// <summary>
    /// Posts one message naming the settings that could not be read this launch and the workspaces
    /// recovered from their data folders. Persistent (not a passing toast): it is the record of settings
    /// the user is expected to check, and the wizard that opens beside it covers the re-verification.
    /// </summary>
    private void ReportUnreadableConfigs(IReadOnlyList<string> recoveredWorkspaces)
    {
        var unreadable = ConfigManager.Instance.GetUnreadableConfigs();
        if (unreadable.Count == 0) return;

        var body = Str.Format("Shell.Notifications.UnreadableFormat", string.Join(", ", unreadable), CrashLog.Instance.CurrentPath);

        if (recoveredWorkspaces.Count > 0)
            body += " " + Str.Format("Shell.Notifications.RecoveredFormat", recoveredWorkspaces.Count, string.Join(", ", recoveredWorkspaces));

        Inbox.Post(new NotificationItem
        {
            Title     = Str.Get("Shell.Notifications.UnreadableTitle"),
            Body      = body,
            Severity  = MessageSeverity.Warning,
            ShowToast = true,
        });
    }

    /// <summary>
    /// Recursively deletes <paramref name="dir"/>, retrying briefly to wait out any handle the exiting
    /// previous instance still holds. Best-effort: gives up quietly after the retry budget.
    /// </summary>
    private static void DeleteDirectoryWithRetry(string dir)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch { Thread.Sleep(100); }
        }
    }
}
