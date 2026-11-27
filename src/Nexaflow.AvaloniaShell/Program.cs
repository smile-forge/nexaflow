using Avalonia;
using Avalonia.Controls;

namespace Nexaflow.AvaloniaShell;

/// <summary>Starts the toolkit; the session opens once Avalonia's UI thread runs (<see cref="App"/>).</summary>
internal static class Program
{
    /// <summary>The session ends the app with its last window (<see cref="ShellLaunch"/>), so Avalonia never shuts down on its own.</summary>
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

    /// <summary>The app as Avalonia builds it; its designer reads this too.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
