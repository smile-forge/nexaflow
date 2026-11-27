using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Nexaflow.AvaloniaShell;

/// <summary>The Avalonia application: opens the session and its first window once the toolkit is up.</summary>
public partial class App : Application
{
    /// <summary>The session this process runs, open from start-up to exit.</summary>
    private ShellLaunch? _launch;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            _launch = ShellLaunch.Start(this, desktop);
        base.OnFrameworkInitializationCompleted();
    }
}
