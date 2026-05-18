using Nexaflow.Core.FileActions;
using Nexaflow.Core.Services;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Images;
using Nexaflow.Features.Logs;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Text;
using Nexaflow.Features.Web;
using Nexaflow.Features.WindowsSearch;
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
        AssetRegexPattern = $"^{RepositoryName}_{EntryApplication.GenericRuntimeIdentifier}_v",
        InstallUpdateWindowsExeType = UpdatumWindowsExeType.Installer,
        InstallUpdateWindowsInstallerArguments = "/qb",
        InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.EntryApplicationName,
        InstallUpdateSingleFileExecutableName = RepositoryName,
        InstallUpdateCodesignMacOSApp = true,
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var activityManager = new BackgroundActivityManager();
        ProviderManager.Instance.Register(typeof(AriaLlmProvider), activityManager);
        ProviderManager.Instance.Register(typeof(OllamaLlmProvider), activityManager);
        ProviderManager.Instance.Register(typeof(ClaudeLlmProvider), activityManager);

        var shellConfig = new ShellConfig();
        ConfigManager.Instance.Register(shellConfig, shellConfig.ConfigName);

        var fileMapConfig = new FileMapConfig();
        ConfigManager.Instance.Register(fileMapConfig, fileMapConfig.ConfigName);
        FileMapManager.Instance.Initialize(fileMapConfig.UseRegistryMapping);

        if (!string.IsNullOrEmpty(shellConfig.BasicAiProvider))
            LlmProviderRegistry.SetBasicProvider(shellConfig.BasicAiProvider);
        if (!string.IsNullOrEmpty(shellConfig.ConversationAiProvider))
            LlmProviderRegistry.SetConversationProvider(shellConfig.ConversationAiProvider);

        var aiService = new AIService();
        FeatureManager.Instance.RegisterSingletonService(typeof(IAIService), aiService);

        // Create the application-level shell services before registering features
        // so IShellServices is available for constructor injection
        var shellServices = new ShellServices();
        FeatureManager.Instance.SetShellServices(shellServices);

        RegisterFeatures();

        // Factory for tearoff windows: creates a window, registers it, returns the host
        shellServices.CreateWindowFactory = tab =>
        {
            var win = new MainWindow(activityManager, aiService, shellServices, openDefaultTabs: false);
            // RegisterWindow is called inside MainWindow constructor
            return (IWindowHost)win.ViewModel;
        };

        var win = new MainWindow(activityManager, aiService, shellServices);
        win.Show();

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

    private static void RegisterFeatures()
    {
        var fm = FeatureManager.Instance;
        fm.Register(typeof(ConsoleTabRegistration));
        fm.Register(typeof(ProjectsTabRegistration));
        fm.Register(typeof(HtmlTabRegistration));
        fm.Register(typeof(ImageTabRegistration));
        fm.Register(typeof(MarkdownTabRegistration));
        fm.Register(typeof(TextTabRegistration));
        fm.Register(typeof(ScratchpadTabRegistration));
        fm.Register(typeof(LogTabRegistration));
        fm.Register(typeof(SearchTabRegistration));
        fm.Register(typeof(AIChatTabRegistration));
    }
}
