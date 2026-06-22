using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
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
using System.Windows.Threading;
using Updatum;
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

    /// <summary>
    /// True when launched with <c>--skipSetup</c>: the first-run / post-update wizard is bypassed and the
    /// shell opens straight away. Used by the UI test harness so it never has to find the wizard window
    /// and click Skip.
    /// </summary>
    public static bool SkipSetup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch stray UI-thread exceptions so one bad event handler can't take the whole shell down.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        bool prestart = e.Args.Any(a => string.Equals(a, "--prestart", StringComparison.OrdinalIgnoreCase));
        SkipSetup = e.Args.Any(a => string.Equals(a, "--skipSetup", StringComparison.OrdinalIgnoreCase));

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
        // A --prestart daemon shows nothing now; the wizard runs the first time a window is actually
        // due (OpenNewWindow), so EnsureConfiguredThenCreateWindow gates both paths.
        if (!prestart)
        {
            // Honour --context "Name" from a taskbar JumpList.
            var startupProfile = ResolveProfile(ParseContextArg(e.Args))
                                 ?? WorkspaceManager.Instance.Profiles[0];
            EnsureConfiguredThenCreateWindow(activityManager, startupProfile, activate: false);
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
        // NEXAFLOW_CONFIG_DIR overrides the default (used by UI tests for an isolated, fresh config
        // root, and handy for portable installs); otherwise %AppData%\Smile\nexaflow.
        var baseDir = Environment.GetEnvironmentVariable("NEXAFLOW_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Smile", "nexaflow");
        ConfigManager.Instance.Initialize(baseDir);

        // ── 1. Shell config ──────────────────────────────────────────────────
        var shellConfig = new ShellConfig();
        ConfigManager.Instance.Register(shellConfig, shellConfig.ConfigName);
        ThemeManager.Apply(shellConfig.Theme);

        var securityConfig = new SecurityConfig();
        ConfigManager.Instance.Register(securityConfig, securityConfig.ConfigName);

        // ── 2. Workspaces config (profiles) — must come before providers so we know
        //       which provider assemblies each profile needs ───────────────────
        var wcConfig = new WorkspacesConfig();
        ConfigManager.Instance.Register(wcConfig, wcConfig.ConfigName);

        // ── 3. Providers — union of all assembly file names across profiles ──
        ProviderManager.Instance.Initialize(activityManager);

        // Load each profile's AI config (stored per-profile on disk) to discover which provider
        // assemblies are needed before loading them. Profile has no runtime fields yet, so use a
        // temporary AiConfig per entry.
        var allProviderFiles = wcConfig.Contexts
            .SelectMany(profile =>
            {
                var ai = new AiConfig();
                ConfigManager.Instance.LoadFrom(WorkspaceManager.ProfileDir(profile.Name), ai, ai.ConfigName);
                return ai.Columns.Select(p => p.AssemblyFileName);
            })
            .Distinct();
        ProviderManager.Instance.LoadConfigured(allProviderFiles);

        // ── 4. WorkspaceManager — loads the profile list (no runtime workspaces yet) ──
        WorkspaceManager.Instance.Initialize(wcConfig);

        // A workspace rebuild (Configure panel) needs to create a replacement window for a fresh
        // workspace; hand WorkspaceManager the factory that knows how to build MainWindow.
        WorkspaceManager.Instance.WindowHostFactory = ws => CreateWorkspaceWindow(activityManager, ws);

        // ── 5. File map + external apps ──────────────────────────────────────
        var fileMapConfig = new FileMapConfig();
        ConfigManager.Instance.Register(fileMapConfig, fileMapConfig.ConfigName);

        var externalAppsConfig = new ExternalAppsConfig();
        ConfigManager.Instance.Register(externalAppsConfig, externalAppsConfig.ConfigName);
        ExternalAppRegistry.Instance.Initialize(externalAppsConfig);

        FileMapManager.Instance.Initialize(externalAppsConfig.UseRegistryMapping, ConfigManager.Instance.BaseDir);

        // ShellNew create-file entries from HKCR — gated on the same registry-mapping option.
        ShellNewRegistry.Instance.Initialize(externalAppsConfig.UseRegistryMapping);

        // Templated-create (user templates) — global config + its appdata template store.
        var templatedCreateConfig = new TemplatedCreateConfig();
        ConfigManager.Instance.Register(templatedCreateConfig, templatedCreateConfig.ConfigName);
        TemplatedCreateRegistry.Instance.Initialize(templatedCreateConfig, ConfigManager.Instance.BaseDir);

        // ── 6. Feature system ────────────────────────────────────────────────
        FeatureManager.Instance.RegisterFeatures();

        // Re-apply the theme now that features are loaded, folding in any feature theme
        // contributions (IThemeContribution) below the active theme. No-op when none contribute.
        ThemeManager.Apply(shellConfig.Theme, FeatureManager.Instance.ThemeContributionUris);

        // ── 6a. Voice input — capability probe (model download starts later, off the show path) ──
        var voiceConfig = new VoiceConfig();
        ConfigManager.Instance.Register(voiceConfig, voiceConfig.ConfigName);
        WhisperModelManager.Instance.Initialize(activityManager);

        // Force the app-global inbox into existence here, on the UI thread, so it captures the app
        // dispatcher even in the windowless --prestart daemon (which never builds a ShellViewModel — its
        // first Post would otherwise come from the background update-check thread).
        _ = MessageCenter.Instance;

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

        // ── 7. About — read-only Options section (registered last so it sorts to the bottom) ──
        var aboutConfig = new AboutConfig();
        ConfigManager.Instance.Register(aboutConfig, aboutConfig.ConfigName);

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
    /// Last-resort handler for an unhandled exception on the UI thread: log it, tell the user, and mark it
    /// handled so the shell stays open instead of terminating. A genuinely fatal fault will recur and leave
    /// a trail in the crash log rather than vanishing with the process.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smile", "nexaflow");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        try
        {
            MessageCenter.Instance.Post(new NotificationItem
            {
                Title     = "Something went wrong",
                Body      = "An unexpected error was caught and logged; the app stayed open. If this keeps happening, please report it.",
                Severity  = MessageSeverity.Error,
                ShowToast = true,
            });
        }
        catch { }

        e.Handled = true;
    }

    /// <summary>
    /// Opens a new shell window. When <paramref name="contextName"/> names a known config
    /// (e.g. forwarded from a taskbar JumpList launch) the window opens in a fresh context
    /// built from that config; otherwise the first config is used.
    /// </summary>
    private static void OpenNewWindow(BackgroundActivityManager activityManager, string? contextName = null)
    {
        var profile = ResolveProfile(contextName) ?? WorkspaceManager.Instance.Profiles[0];
        EnsureConfiguredThenCreateWindow(activityManager, profile, activate: true);
    }

    /// <summary>Tracks whether the first window of this process has already run the setup check.</summary>
    private static bool _setupShown;

    /// <summary>
    /// Creates a shell window for <paramref name="profile"/>, running the first-run / post-update
    /// wizard first the very first time a window is due (so a <c>--prestart</c> daemon shows nothing
    /// until its first real window). Skipping or finishing the wizard always proceeds to the window;
    /// committed steps persist either way.
    /// </summary>
    private static MainWindow EnsureConfiguredThenCreateWindow(
        BackgroundActivityManager activityManager, Profile profile, bool activate)
    {
        // The wizard honours CenterScreen and lands on the active monitor; capture that monitor's work
        // area so the main window opens on the SAME monitor (it otherwise takes the OS default
        // placement, which falls back to the primary monitor).
        Rect? wizardMonitor = null;

        if (!_setupShown)
        {
            _setupShown = true;

            // --skipSetup bypasses the wizard entirely (UI tests); the run is still stamped so post-update
            // detection stays consistent.
            if (!SkipSetup)
            {
                var wizard = SetupWizardViewModel.Build(profile);
                if (wizard is not null)
                {
                    var wizardWin = new SetupWizardWindow(wizard);
                    // Closing fires while the HWND is still alive, so the monitor is still resolvable.
                    wizardWin.Closing += (_, _) => wizardMonitor = WindowManager.GetWorkAreaForWindow(wizardWin);
                    wizardWin.ShowDialog();   // modal; returns on Finish / Skip / close
                }
            }

            StampLastRunVersion();
        }

        var ws = WorkspaceManager.Instance.CreateWorkspace(profile);
        ws.ShellServices!.CreateWindowFactory = MakeWindowFactory(activityManager, ws);

        var win = new MainWindow(activityManager, ws);
        CenterOnMonitor(win, wizardMonitor);
        win.Show();
        if (activate) win.Activate();
        return win;
    }

    /// <summary>
    /// Centres <paramref name="win"/> within <paramref name="workArea"/> (a monitor's work area in WPF
    /// logical px) so it opens on that monitor. No-op when <paramref name="workArea"/> is null — the
    /// window then keeps the OS default placement.
    /// </summary>
    private static void CenterOnMonitor(Window win, Rect? workArea)
    {
        if (workArea is not { } work) return;
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left = work.Left + Math.Max(0, (work.Width  - win.Width)  / 2);
        win.Top  = work.Top  + Math.Max(0, (work.Height - win.Height) / 2);
    }

    /// <summary>Records the current version so the next launch can detect an update (drives What's New).</summary>
    private static void StampLastRunVersion()
    {
        if (ConfigManager.Instance.GetAll().OfType<ShellConfig>().FirstOrDefault() is not { } shell)
            return;
        var current = SetupWizardViewModel.CurrentVersion();
        if (shell.LastRunVersion == current) return;   // unchanged — skip the write
        shell.LastRunVersion = current;
        try { ConfigManager.Instance.Save(shell, shell.ConfigName); } catch { }
    }

    /// <summary>
    /// Returns a factory that creates torn-off / "open in new window" shells in the SAME
    /// <paramref name="owner"/> workspace (so extra windows share its tab registry and services).
    /// </summary>
    private static Func<IWindowHost> MakeWindowFactory(BackgroundActivityManager activityManager, Workspace owner)
        => () =>
        {
            var win = new MainWindow(activityManager, owner, openDefaultTabs: false);
            return (IWindowHost)win.ViewModel;
        };

    /// <summary>
    /// Builds (does not show) the first window for a freshly-created <paramref name="ws"/> and wires
    /// its tear-off factory. Registered as <see cref="WorkspaceManager.WindowHostFactory"/> so a
    /// workspace rebuild (Configure panel) can spin up a replacement window without Core.Services
    /// referencing <see cref="MainWindow"/>.
    /// </summary>
    private static IWindowHost CreateWorkspaceWindow(BackgroundActivityManager activityManager, Workspace ws)
    {
        ws.ShellServices!.CreateWindowFactory = MakeWindowFactory(activityManager, ws);
        var win = new MainWindow(activityManager, ws, openDefaultTabs: false);
        return (IWindowHost)win.ViewModel;
    }

    /// <summary>Reads a <c>--context "Name"</c> argument, or null if absent.</summary>
    private static string? ParseContextArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], JumpListService.ContextSwitch, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>Resolves a profile name to a saved <see cref="Profile"/>, or null.</summary>
    private static Profile? ResolveProfile(string? name)
        => string.IsNullOrEmpty(name)
            ? null
            : WorkspaceManager.Instance.Profiles.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

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
