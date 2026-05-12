using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Projects;
using Nexaflow.Providers.Aria;
using Nexaflow.Providers.Common;
using System.Windows;
using Nexaflow.Core.Services;

namespace Nexaflow.Core;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Activity manager needed by providers for status reporting
        var activityManager = new BackgroundActivityManager();
        LlmProviderRegistry.Register("Aria", new AriaLlmProvider(activityManager));

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
    }
}
