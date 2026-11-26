using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Nexaflow.AvaloniaShell.Platform;
using Nexaflow.AvaloniaShell.Shell;
using Nexaflow.AvaloniaShell.Theming;
using Nexaflow.AvaloniaShell.Views;
using Nexaflow.Host;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Localization;

namespace Nexaflow.AvaloniaShell;

/// <summary>
/// Opens the session on Avalonia — <see cref="ShellHost"/>, with <see cref="ShellPlatform"/> filled by this toolkit — and its first
/// window, and ends it when the app exits. The counterpart of Nexaflow.Core's <c>App.OnStartup</c>, less what is the WPF app's own:
/// the single-instance guard, the jump list, the resident daemon and the update check.
/// </summary>
internal sealed class ShellLaunch
{
    /// <summary>Where the spike keeps its configuration when no isolated root is named: apart from the WPF app's, which may be running.</summary>
    private const string OwnRoot = "nexaflow-avalonia-spike";

    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;

    /// <summary>The theme the windows draw with now.</summary>
    private ShellTheme? _theme;
    private readonly ShellLogging _logging;
    private readonly ILogger<ShellLaunch> _log;
    private readonly AvaloniaClipboard _clipboard;
    private readonly ShellHost _host;
    private readonly AvaloniaPageViews _views;
    private bool _ended;

    /// <summary>The app's version, which the configuration is stamped with.</summary>
    private static string Version => (typeof(App).Assembly.GetName().Version ?? new Version(0, 1)).ToString();

    /// <summary>Opens the session and shows its first window, on the UI thread Avalonia has just started.</summary>
    public static ShellLaunch Start(Application app, IClassicDesktopStyleApplicationLifetime desktop) => new(app, desktop);

    private ShellLaunch(Application app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
        var folders = Folders();
        _logging = ShellLogging.Open(folders, Version, uiTest: false, profileStartup: false);
        _log     = _logging.Factory.CreateLogger<ShellLaunch>();
        Dispatcher.UIThread.UnhandledException += OnUnhandled;

        var ui = new AvaloniaUiThread(Dispatcher.UIThread);
        _clipboard = new AvaloniaClipboard(ui, desktop);
        AvaloniaPageViews? views = null;
        _host = new ShellHost(
            new ShellHostOptions(folders, Version, Reset: false, UiTest: false, _logging),
            new ShellPlatform(
                ui,
                _clipboard,
                new AvaloniaFilePickers(desktop),
                new AvaloniaThemeHost(_logging.Factory.CreateLogger<AvaloniaThemeHost>()),
                typeof(AvaloniaViewForAttribute),
                typeof(App).Assembly,
                // No fenced block is drawn as anything but its text here: the spike has no markdown renderer.
                _ => false,
                new AvaloniaPictureFitting(ui),
                new WasapiSpeechDevices(),
                () => new ShellChangeWatcher(ui),
                ApplyAppearance,
                (catalog, _, _, logging) => views = new AvaloniaPageViews(catalog, logging.Factory.CreateLogger<AvaloniaPageViews>())),
            new AppUpdates(new NoReleaseChannel(), ui.Post, (attempt, ex) => _log.LogWarning(ex, "{Attempt}", attempt)));
        _views = views ?? throw new InvalidOperationException("The session opened without asking for its page views.");

        // The window a runtime opens into for a tear-off, a "new window" or a workspace rebuild: registered, shown by the session.
        _host.Workspaces.WindowFactory = runtime => new ShellWindow(_host.Windows, _views, ui, runtime, openWorkspaceTabs: false).Model;
        _host.Workspaces.LastWindowClosed += (_, _) => desktop.Shutdown();
        desktop.Exit += (_, _) => End();

        var startup = _host.SavedWorkspaces.Workspaces[0];
        SettleSetup(startup);

        var window = new ShellWindow(_host.Windows, _views, ui, _host.Workspaces.Open(startup), openWorkspaceTabs: true);
        window.Opened += (_, _) => Dispatcher.UIThread.Post(_host.FirstWindowDrawn, DispatcherPriority.Background);
        window.Show();
        _ = _host.StartBackground();
        _log.LogInformation("startup complete; first window shown on {Workspace}", startup.Name);
    }

    /// <summary>
    /// This launch's folders: the isolated root <see cref="AppDataFolders.IsolatedRootVariable"/> names, or the spike's own, kept
    /// apart from the WPF app's configuration, which a running Nexaflow is writing.
    /// </summary>
    private static AppDataFolders Folders()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppDataFolders.IsolatedRootVariable)))
            return AppDataFolders.ForThisProcess();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", OwnRoot);
        return new AppDataFolders(Path.Combine(root, "config"), Path.Combine(root, "local"));
    }

    /// <summary>
    /// The first-run and post-update decisions, read from the session. The WPF app asks them in its setup wizard, whose steps are
    /// Core's view-models; the spike has no wizard, so what the wizard would ask is put in the inbox, and the run is stamped as the
    /// WPF app stamps it once its wizard closes.
    /// </summary>
    private void SettleSetup(Workspace startup)
    {
        var agenda = SetupAgenda.Of(startup, _host.Config, _host.Runs, _host.Providers, _host.SavedWorkspaces);
        var asks = new List<string>();
        if (agenda.Welcomes) asks.Add("a welcome");
        if (agenda.Unreadable.Count > 0) asks.Add($"settings that could not be read ({string.Join(", ", agenda.Unreadable)})");
        if (agenda.MandatoryConfigs.Count > 0) asks.Add($"required settings ({string.Join(", ", agenda.MandatoryConfigs.Select(c => c.FriendlyName))})");
        if (agenda.SetsUpWorkspace) asks.Add($"an AI model for '{startup.Name}' (WorkspaceAiSetup)");
        if (agenda.AwaitedDownloads.Count > 0) asks.Add($"{agenda.AwaitedDownloads.Count} model download(s) awaited");

        if (asks.Count > 0)
            _host.Inbox.Post(new NotificationItem
            {
                Title     = "Setup the WPF app would ask for",
                Body      = "This shell has no setup wizard. The session's SetupAgenda asks for: " + string.Join("; ", asks) + ".",
                Severity  = MessageSeverity.Warning,
                ShowToast = true,
            });

        _host.Runs.Stamp();
        _host.Language.Select(_host.Shell.Language);
    }

    /// <summary>
    /// The shell settings that decide how windows look: the theme, read from the WPF app's own theme dictionaries
    /// (<see cref="ShellTheme"/>) — or the one <c>--theme &lt;name&gt;</c> names for this run — with its scenes drawn by this app's ports.
    /// </summary>
    private void ApplyAppearance(ShellConfig shell)
    {
        var option = ThemeAsked() ?? shell.Theme;
        var theme = ShellTheme.Load(option, _logging.Factory.CreateLogger<ShellTheme>());
        theme.Apply(_app, _theme);
        _theme = theme;
    }

    /// <summary>The theme this run was asked to show with <c>--theme &lt;name&gt;</c>, over the one the settings hold; null when none was.</summary>
    private ThemeOption? ThemeAsked()
    {
        var args = _desktop.Args ?? [];
        var at = Array.FindIndex(args, arg => string.Equals(arg, "--theme", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length && Enum.TryParse<ThemeOption>(args[at + 1], ignoreCase: true, out var asked) ? asked : null;
    }

    /// <summary>A fault on the UI thread: recorded, told in the inbox, and handled so the window stays open.</summary>
    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.LogError(e.Exception, "unhandled on the UI thread");
        try
        {
            _host?.Inbox.Post(new NotificationItem
            {
                Title     = Str.Get("Shell.Notifications.FaultTitle"),
                Body      = e.Exception.Message,
                Severity  = MessageSeverity.Error,
                ShowToast = true,
            });
        }
        catch { /* the inbox is not open yet */ }
        e.Handled = true;
    }

    /// <summary>Ends the session in the reverse of its opening, then the clipboard's listener and the logs.</summary>
    private void End()
    {
        if (_ended) return;
        _ended = true;
        _log.LogInformation("shutdown begun");
        _host.DisposeAsync().AsTask().Wait();
        _clipboard.Dispose();
        _log.LogInformation("shutdown complete");
        _logging.Dispose();
    }
}
