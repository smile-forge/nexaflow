using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.Console;
using Nexaflow.Features.Images;
using Nexaflow.Features.Logs;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Json;
using Nexaflow.Features.Text;
using Nexaflow.Features.Web;
using Nexaflow.Features.WindowsSearch;
using System.IO;
using System.Windows;
using Updatum;
using WorkContext = Nexaflow.Core.Models.WorkContext;
using StageKit.Runtime;

namespace Nexaflow.Core;

public partial class App : Application
{
    private const string RepositoryOwner = "smile-forge";
    private const string RepositoryName = "nexaflow";

    internal static readonly UpdatumManager AppUpdater = new(RepositoryOwner, RepositoryName)
    {
        AssetRegexPattern = $"^{RepositoryName}_{EntryApplication.GenericRuntimeIdentifier}_v",
        InstallUpdateWindowsExeType = UpdatumWindowsExeType.Installer,
        InstallUpdateWindowsInstallerArguments = "/qb",
        InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.EntryApplicationName,
        InstallUpdateSingleFileExecutableName = RepositoryName,
        InstallUpdateCodesignMacOSApp = true,
    };

    private static readonly SingleInstanceService _singleInstance = new();

    /// <summary>
    /// True when this process was launched with <c>--prestart</c> as a windowless login daemon.
    /// The app then stays alive after its last window closes instead of shutting down, so the next
    /// click can show a window instantly. See <see cref="ShellServices.UnregisterWindow"/>.
    /// </summary>
    public static bool IsResident { get; private set; }

    /// <summary>
    /// True once an update install has been kicked off and this process is committed to exiting so the
    /// installer can replace its (otherwise locked) files. Overrides the resident keep-alive in
    /// <see cref="ShellServices.UnregisterWindow"/> — a <c>--prestart</c> daemon must actually die for
    /// the installer's wait-for-exit script to proceed, instead of lingering windowless.
    /// </summary>
    public static bool IsUpdating { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool prestart = e.Args.Any(a => string.Equals(a, "--prestart", StringComparison.OrdinalIgnoreCase));

#if !DEBUG
        // ── Single-instance guard ────────────────────────────────────────────
        if (!_singleInstance.TryAcquire())
        {
            // A daemon (or an existing window) already owns the instance. A normal launch forwards a
            // new-window request (honouring --context "Name" from a taskbar JumpList); a --prestart
            // launch must never spawn a window, so it just exits.
            if (!prestart)
                SingleInstanceService.SignalNewWindow(ParseContextArg(e.Args));
            Shutdown();
            return;
        }
#endif

        IsResident = prestart;

        var activityManager = new BackgroundActivityManager();
        var voiceConfig = InitializeApp(activityManager);

        // ── Main window — skipped in --prestart mode (windowless login daemon) ──
        if (!prestart)
        {
            // Honour --context "Name" from a taskbar JumpList.
            var startupConfig = ResolveConfig(ParseContextArg(e.Args))
                                ?? WorkContextManager.Instance.Configs[0];
            var startupCtx = WorkContextManager.Instance.CreateFromConfig(startupConfig);
            startupCtx.ShellServices!.CreateWindowFactory = MakeWindowFactory(activityManager);
            var win = new MainWindow(activityManager, startupCtx);
            win.Show();

            if (ConfigManager.Instance.IsFirstRun)
                win.ViewModel.OptionsOpen = true;
        }

        // Voice model download — background, kicked off only after the window is up (or after init in
        // prestart mode) so it never competes with window construction / first render.
        Task.Run(() => WhisperModelManager.Instance.EnsureModelDownloaded(voiceConfig));

        // Update check — runs in both paths (the daemon finds updates windowless and posts a message;
        // the first window replays it as a toast). Skipped on first run.
        if (!ConfigManager.Instance.IsFirstRun)
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CheckForUpdates();
            });
    }

    /// <summary>
    /// Runs all app-level initialisation shared by both the normal and <c>--prestart</c> launch paths:
    /// config registration, providers, work contexts, file map, features, the voice capability probe,
    /// the torn-off window factory, the taskbar JumpList and the single-instance IPC listener.
    /// Returns the <see cref="VoiceConfig"/> so the caller can start the model download afterwards.
    /// </summary>
    private VoiceConfig InitializeApp(BackgroundActivityManager activityManager)
    {
        // ── 0. Base path — single source of truth for all app-data paths ─────
        ConfigManager.Instance.Initialize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smile", "nexaflow"));

        // ── 1. Shell config ──────────────────────────────────────────────────
        var shellConfig = new ShellConfig();
        ConfigManager.Instance.Register(shellConfig, shellConfig.ConfigName);
        ThemeManager.Apply(shellConfig.Theme);

        // ── 1a. AI persona (global): name + system prompt for the assistant ──
        var personaConfig = new AiPersonaConfig();
        ConfigManager.Instance.Register(personaConfig, personaConfig.ConfigName);

        // ── 2. WorkContexts config — must come before providers so we know
        //       which provider assemblies each context needs ──────────────────
        var wcConfig = new WorkContextsConfig();
        ConfigManager.Instance.Register(wcConfig, wcConfig.ConfigName);

        // ── 3. Providers — union of all assembly file names across contexts ──
        ProviderManager.Instance.Initialize(activityManager);

        // Load each context's AI config (stored per-context on disk) to discover which provider
        // assemblies are needed before loading them. WorkContextConfig has no runtime fields, so
        // we use a temporary AiConfig per entry.
        var allProviderFiles = wcConfig.Contexts
            .SelectMany(cfg =>
            {
                var ai = new AiConfig();
                ConfigManager.Instance.LoadFrom(WorkContextManager.ContextDir(cfg.Name), ai, ai.ConfigName);
                return ai.Columns.Select(p => p.AssemblyFileName);
            })
            .Distinct();
        ProviderManager.Instance.LoadConfigured(allProviderFiles);

        // ── 4. WorkContextManager — creates per-context AIService instances
        //       and registers all loaded providers into each ─────────────────
        WorkContextManager.Instance.Initialize(wcConfig);

        // ── 5. File map + external apps ──────────────────────────────────────
        var fileMapConfig = new FileMapConfig();
        ConfigManager.Instance.Register(fileMapConfig, fileMapConfig.ConfigName);

        var externalAppsConfig = new ExternalAppsConfig();
        ConfigManager.Instance.Register(externalAppsConfig, externalAppsConfig.ConfigName);
        ExternalAppRegistry.Instance.Initialize(externalAppsConfig);

        FileMapManager.Instance.Initialize(externalAppsConfig.UseRegistryMapping);

        // ── 6. Feature system ────────────────────────────────────────────────
        FeatureManager.Instance.RegisterFeatures();

        // ── 6a. Voice input — capability probe (model download starts later, off the show path) ──
        var voiceConfig = new VoiceConfig();
        ConfigManager.Instance.Register(voiceConfig, voiceConfig.ConfigName);
        WhisperModelManager.Instance.Initialize(activityManager);

        // Surface download outcomes as messages (global, so they survive the windowless daemon).
        WhisperModelManager.Instance.ModelDownloadCompleted += (_, _) =>
            MessageCenter.Instance.Post(new NotificationItem
            {
                Title     = "Voice ready",
                Body      = "Voice model downloaded — voice input is ready.",
                Severity  = MessageSeverity.Info,
                ShowToast = true,
            });
        WhisperModelManager.Instance.ModelDownloadFailed += (_, msg) =>
            MessageCenter.Instance.Post(new NotificationItem
            {
                Title     = "Voice model download failed",
                Body      = msg,
                Severity  = MessageSeverity.Error,
                ShowToast = true,
                Actions   = [new MessageAction("Retry",
                    new RelayCommand(() => WhisperModelManager.Instance.EnsureModelDownloaded(voiceConfig)), IsPrimary: true)],
            });

        HostCapabilityService.Instance.StartProbe();

        // ── 8. Taskbar JumpList — one entry per WorkContext ─────────────────
        JumpListService.Initialize();

        // ── 9. Single-instance IPC listener — a later click signals us to open a window ──
        _singleInstance.StartListening(name => OpenNewWindow(activityManager, name));

        return voiceConfig;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VoiceManager.Instance.Dispose();
        _singleInstance.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Opens a new shell window. When <paramref name="contextName"/> names a known config
    /// (e.g. forwarded from a taskbar JumpList launch) the window opens in a fresh context
    /// built from that config; otherwise the first config is used.
    /// </summary>
    private static void OpenNewWindow(BackgroundActivityManager activityManager, string? contextName = null)
    {
        var config = ResolveConfig(contextName) ?? WorkContextManager.Instance.Configs[0];
        var ctx    = WorkContextManager.Instance.CreateFromConfig(config);

        ctx.ShellServices!.CreateWindowFactory ??= MakeWindowFactory(activityManager);

        var win = new MainWindow(activityManager, ctx);
        win.Show();
        win.Activate();
    }

    /// <summary>
    /// Returns a factory that creates torn-off or "open in new window" shells, each in a
    /// fresh <see cref="WorkContext"/> built from the first saved config.
    /// </summary>
    private static Func<IWindowHost> MakeWindowFactory(BackgroundActivityManager activityManager)
        => () =>
        {
            var cfg = WorkContextManager.Instance.Configs[0];
            var ctx = WorkContextManager.Instance.CreateFromConfig(cfg);
            var win = new MainWindow(activityManager, ctx, openDefaultTabs: false);
            return (IWindowHost)win.ViewModel;
        };

    /// <summary>Reads a <c>--context "Name"</c> argument, or null if absent.</summary>
    private static string? ParseContextArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], JumpListService.ContextSwitch, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>Resolves a context name to a saved <see cref="WorkContextConfig"/>, or null.</summary>
    private static WorkContextConfig? ResolveConfig(string? name)
        => string.IsNullOrEmpty(name)
            ? null
            : WorkContextManager.Instance.Configs.FirstOrDefault(
                c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private async Task CheckForUpdates()
    {
        try
        {
            var updateFound = await AppUpdater.CheckForUpdatesAsync();
            if (!updateFound) return;

            var release = AppUpdater.LatestRelease;
            if (release is null) return;
            var asset = AppUpdater.GetCompatibleReleaseAsset(release);
            if (asset is null) return;
            string? changeLog = AppUpdater.GetChangelog(true);
            var version = release.TagName ?? release.Name ?? "unknown";

            // Post a persistent update message with an Update action. Whichever window is focused toasts
            // it; if none is open yet (daemon) it waits in the inbox and toasts when a window opens.
            MessageCenter.Instance.Post(new NotificationItem
            {
                Title     = $"Update available — {version}",
                Body      = changeLog ?? string.Empty,
                Severity  = MessageSeverity.Update,
                ShowToast = true,
                Actions   = [new MessageAction("Update", new AsyncRelayCommand(DownloadAndInstallUpdate), IsPrimary: true)],
            });
        }
        catch { }
    }

    public async Task DownloadAndInstallUpdate()
    {
        try
        {
            // 1. Download while the app stays fully usable. Only the install step needs us gone.
            var asset = await AppUpdater.DownloadUpdateAsync();
            if (asset is null) return;

            // 2. Commit to exiting. The installer's script waits for this process to die before it can
            //    replace the in-use binaries; the resident keep-alive would otherwise leave us running
            //    windowless and stall (or corrupt) the install. IsUpdating lifts that keep-alive.
            IsUpdating = true;

            // 3. Drop the single-instance mutex so the relaunched/new version can claim it, and close
            //    every window so nothing holds a lock on the install directory.
            _singleInstance.Dispose();
            foreach (var window in Current.Windows.Cast<Window>().ToList())
                window.Close();

            // 4. Launch the installer and terminate this process so msiexec /qb can replace the now
            //    unlocked files, then relaunch. forceTerminate ⇒ this call does not return.
            await AppUpdater.InstallUpdateAsync(asset, forceTerminate: true, runArguments: null);
        }
        catch { }
    }
}
