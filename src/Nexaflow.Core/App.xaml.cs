using Nexaflow.Core.FileActions;
using Nexaflow.Core.Services;
using Nexaflow.Features.Console;
using Nexaflow.Features.Images;
using Nexaflow.Features.Logs;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Text;
using Nexaflow.Features.Web;
using Nexaflow.Providers.Aria;
using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Ollama;
using System.Windows;
using Updatum;

namespace Nexaflow.Core;

public partial class App : Application
{
    private const string RepositoryOwner = "smile-forge";
    private const string RepositoryName = "nexaflow";

    internal static readonly UpdatumManager AppUpdater = new(RepositoryOwner, RepositoryName)
    {
        // All below are optional settings with recommended defaults
        // Regex filter to get the correct asset from running system
        // Defaults would work here too: EntryApplication.GenericRuntimeIdentifier
        AssetRegexPattern = $"^{RepositoryName}_{EntryApplication.GenericRuntimeIdentifier}_v",
        // Specifies the type of windows .exe being used in the assets.
        // It is highly recommended to set this when having .exe assets.
        // - When asset is an installer, set to Installer.
        // - When asset is a single file executable, set to SingleFileExecutable.
        // - When having both asset types, set to Auto.
        InstallUpdateWindowsExeType = UpdatumWindowsExeType.Installer,
        // Displays a basic user interface for MSI package
        // This will show the installer UI installing without any interaction
        InstallUpdateWindowsInstallerArguments = "/qb",
        // Strategy to determine the single file executable name for installation and update
        InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.EntryApplicationName,
        // Fallback name if unable to determine the executable name from the entry application
        // This is safe to omit, but as we are using a fake app, we need to set it
        InstallUpdateSingleFileExecutableName = RepositoryName,
        // Enable codesign verification for macOS .app bundles
        InstallUpdateCodesignMacOSApp = true,
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var activityManager = new BackgroundActivityManager();
        ProviderManager.Instance.Register(typeof(AriaLlmProvider), activityManager);
        ProviderManager.Instance.Register(typeof(OllamaLlmProvider), activityManager);
        ProviderManager.Instance.Register(typeof(ClaudeLlmProvider), activityManager);

        RegisterFeatures();

        // ShellConfig is not in a feature assembly; register manually after providers are ready
        var shellConfig = new ShellConfig();
        ConfigManager.Instance.Register(shellConfig, shellConfig.ConfigName);

        // FileMapConfig and FileMapManager — not in a feature assembly
        var fileMapConfig = new FileMapConfig();
        ConfigManager.Instance.Register(fileMapConfig, fileMapConfig.ConfigName);
        FileMapManager.Instance.Initialize(fileMapConfig.UseRegistryMapping);

        // Apply any persisted provider selections
        if (!string.IsNullOrEmpty(shellConfig.BasicAiProvider))
            LlmProviderRegistry.SetBasicProvider(shellConfig.BasicAiProvider);
        if (!string.IsNullOrEmpty(shellConfig.ConversationAiProvider))
            LlmProviderRegistry.SetConversationProvider(shellConfig.ConversationAiProvider);

        var win = new MainWindow(activityManager);
        win.Show();

        // First run: open Options automatically so the user can configure things
        if (ConfigManager.Instance.IsFirstRun)
            win.ViewModel.OptionsOpen = true;
        else
        {
            Task.Run(async () =>
            {
                // Check for updates shortly after startup, so it doesn't impact launch performance
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CheckForUpdates(win);
            });
        }
    }

    private async Task CheckForUpdates(MainWindow win)
    {
        try
        {
            var updateFound = await AppUpdater.CheckForUpdatesAsync();
            if (!updateFound) return;

            var release = AppUpdater.LatestRelease;

            if (release is null)
            {
                return;
            }
            var asset = AppUpdater.GetCompatibleReleaseAsset(release);
            if (asset is null)
            {
                return;
            }
            string? changeLog = AppUpdater.GetChangelog(true);

            var version = release.TagName ?? release.Name ?? "unknown";
            await win.Dispatcher.InvokeAsync(() =>
                win.ViewModel.ShowUpdateToast(version, changeLog));
        }
        catch
        {
        }
    }

    public async Task DownloadAndInstallUpdate()
    {
        try
        {
            _ = await AppUpdater.DownloadAndInstallUpdateAsync();
        }
        catch
        {
            return;
        }
    }

    /// <summary>
    /// Registers all feature tab factories. Pass any type from the feature assembly;
    /// FeatureManager scans the whole assembly automatically.
    /// </summary>
    private static void RegisterFeatures()
    {
        var fm = FeatureManager.Instance;
        fm.Register(typeof(ConsoleTabRegistration));
        fm.Register(typeof(ProjectsTabRegistration));
        // ProjectDetailTabRegistration is in the same Projects assembly and discovered automatically
        fm.Register(typeof(HtmlTabRegistration));
        fm.Register(typeof(ImageTabRegistration));
        fm.Register(typeof(MarkdownTabRegistration));
        fm.Register(typeof(TextTabRegistration));
        fm.Register(typeof(ScratchpadTabRegistration));
        fm.Register(typeof(LogTabRegistration));
    }
}
