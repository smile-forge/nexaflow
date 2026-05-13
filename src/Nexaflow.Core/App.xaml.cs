using Nexaflow.Features.Console;
using Nexaflow.Features.Images;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Web;
using Nexaflow.Providers.Aria;
using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Ollama;
using System.Windows;
using Nexaflow.Core.Services;

namespace Nexaflow.Core;

public partial class App : Application
{
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
    }
}
