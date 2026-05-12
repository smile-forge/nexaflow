using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Projects;
using System.Windows;

namespace Nexaflow.Core;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterFeatures();
        new MainWindow().Show();
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
