using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Projects;
using Nexaflow.Providers.Aria;
using Nexaflow.Providers.Common;
using System.Windows;

namespace Nexaflow.Core;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the activity manager first so the provider can report
        // activity through it before any window is shown.
        var activityManager = new BackgroundActivityManager();
        LlmProviderRegistry.Register(new AriaLlmProvider(activityManager));

        RegisterFeatures();
        new MainWindow(activityManager).Show();
    }

    /// <summary>
    /// Registers all feature tab factories with <see cref="FeatureManager"/>.
    /// Add a new entry here when introducing a new feature assembly.
    /// </summary>
    private static void RegisterFeatures()
    {
        var fm = FeatureManager.Instance;
        fm.Register(new ConsoleTabRegistration());
        fm.Register(new ProjectsTabRegistration());
        fm.Register(new ProjectDetailTabRegistration());
    }
}
