using Nexaflow.Core.FileActions;
using Nexaflow.Core.Services;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Single-instance guard ────────────────────────────────────────────
        if (!_singleInstance.TryAcquire())
        {
            SingleInstanceService.SignalNewWindow();
            Shutdown();
            return;
        }

        var activityManager = new BackgroundActivityManager();

        // ── 0. Base path — single source of truth for all app-data paths ─────
        ConfigManager.Instance.Initialize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smile", "nexaflow"));

        // ── 1. Shell config ──────────────────────────────────────────────────
        var shellConfig = new ShellConfig();
        ConfigManager.Instance.Register(shellConfig, shellConfig.ConfigName);
        ThemeManager.Apply(shellConfig.Theme);

        // ── 2. WorkContexts config — must come before providers so we know
        //       which provider assemblies each context needs ──────────────────
        var wcConfig = new WorkContextsConfig();
        ConfigManager.Instance.Register(wcConfig, wcConfig.ConfigName);

        // ── 3. Providers — union of all assembly file names across contexts ──
        var allProviderFiles = wcConfig.Contexts
            .SelectMany(c => c.AiConfig.Columns.Select(p => p.AssemblyFileName))
            .Distinct();
        ProviderManager.Instance.Initialize(activityManager);
        ProviderManager.Instance.LoadConfigured(allProviderFiles);

        // ── 4. WorkContextManager — creates per-context AIService instances
        //       and registers all loaded providers into each ─────────────────
        WorkContextManager.Instance.Initialize(wcConfig);

        // ── 5. File map ──────────────────────────────────────────────────────
        var fileMapConfig = new FileMapConfig();
        ConfigManager.Instance.Register(fileMapConfig, fileMapConfig.ConfigName);
        FileMapManager.Instance.Initialize(fileMapConfig.UseRegistryMapping);

        // ── 6. Feature system ────────────────────────────────────────────────
        var defaultCtx = WorkContextManager.Instance.Contexts[0];
        FeatureManager.Instance.RegisterFeatures();

        // ── 7. Torn-off window factory ───────────────────────────────────────
        defaultCtx.ShellServices!.CreateWindowFactory = () =>
        {
            // New shell windows (tearoff or "Open in new Window") use the first available WorkContext for now
            var ctx = WorkContextManager.Instance.Contexts[0];
            var win = new MainWindow(activityManager, ctx, openDefaultTabs: false);
            return (IWindowHost)win.ViewModel;
        };

        // ── 8. Main window ───────────────────────────────────────────────────
        var win = new MainWindow(activityManager, defaultCtx);
        win.Show();

        // ── 9. Single-instance IPC listener ─────────────────────────────────
        _singleInstance.StartListening(() => OpenNewWindow(activityManager));

        if (ConfigManager.Instance.IsFirstRun)
            win.ViewModel.OptionsOpen = true;
        else
        {
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CheckForUpdates(win);
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }

    private static void OpenNewWindow(BackgroundActivityManager activityManager)
    {
        var name = $"Window {WorkContextManager.Instance.Contexts.Count + 1}";
        var ctx  = WorkContextManager.Instance.Create(name);

        ctx.ShellServices!.CreateWindowFactory = () =>
        {
            var c = WorkContextManager.Instance.Contexts[0];
            var w = new MainWindow(activityManager, c, openDefaultTabs: false);
            return (IWindowHost)w.ViewModel;
        };

        var win = new MainWindow(activityManager, ctx);
        win.Show();
    }

    private async Task CheckForUpdates(MainWindow win)
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
            await win.Dispatcher.InvokeAsync(() =>
                win.ViewModel.ShowUpdateToast(version, changeLog));
        }
        catch { }
    }

    public async Task DownloadAndInstallUpdate()
    {
        try { _ = await AppUpdater.DownloadAndInstallUpdateAsync(); }
        catch { }
    }
}
