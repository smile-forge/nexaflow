using CommunityToolkit.Mvvm.Input;

using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
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
using Nexaflow.IO.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

using Nexaflow.Visuals.Common.Theming;

using Nexaflow.AiHost.Client;


using Nexaflow.Providers.AiHost;
using Nexaflow.Localization;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.AiHost.Catalog;
using Nexaflow.IO.Common.Locations;
using Microsoft.Extensions.Logging;

using Nexaflow.Visuals.Common.Viewing;
using Nexaflow.Core.Help;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.IO.Common.Audio;


namespace Nexaflow.Core;

public partial class App : Application
{
    /// <summary>
    /// Where this build stands against its newest release — one per process, shared by the startup check's toast and Options →
    /// About (the session hands it to every window), so an offer outlives the toast that announced it. Made at start-up, once the
    /// logs a failed check is reported to are.
    /// </summary>
    private AppUpdates _updates = null!;

    /// <summary>The process's logs (<see cref="ShellLogging"/>): made first at start-up, before anything that logs, handed to the
    /// session, and ended last, on exit.</summary>
    private ShellLogging _logging = null!;

    /// <summary>What the app itself logs: the steps of its start and end, and the faults its handlers catch.</summary>
    private ILogger<App> _log = null!;

    /// <summary>Whether a fault that reached the UI thread's last-resort handler is still worth handling.</summary>
    private DispatcherFaultPolicy _faultPolicy = null!;

    /// <summary>WPF's binding errors, handed to the logs under <c>--uiTest</c>; null otherwise.</summary>
    private BindingErrorTrace? _bindingErrors;

    private static readonly SingleInstanceService _singleInstance = new();

    /// <summary>
    /// True when this process was launched with <c>--prestart</c> as a windowless login daemon.
    /// The app then stays alive after its last window closes instead of shutting down, so the next
    /// click can show a window instantly. See <see cref="ShellServices.UnregisterWindow"/>.
    /// </summary>
    public static bool IsResident { get; private set; }

    /// <summary>
    /// True once an update install has been kicked off and this process is committed to exiting so the
    /// installer can replace its (otherwise locked) files. From then the updater ends the process itself, once
    /// the script that runs the installer has started, so <see cref="ShellServices.UnregisterWindow"/> does not
    /// shut down when the last window closes: shutting down then would end the process before the updater starts
    /// that script. The resident keep-alive is moot too — the process ends either way.
    /// </summary>
    public static bool IsUpdating { get; private set; }

    /// <summary>
    /// True when launched with <c>--uiTest</c>: a UI journey is driving this window rather than a person.
    /// <para>
    /// It was <c>--skipSetup</c>, named for the first thing it turned off, and it accumulated the rest
    /// anyway — the comments at its other two uses already read "UI-test automation" and "(UI tests)".
    /// Naming it for what it means rather than for one of its effects is what lets the next such case
    /// hang off it instead of arriving as a second flag saying the same thing.
    /// </para>
    /// <para>
    /// What it currently turns off or forces: the first-run / post-update wizard (so the shell opens
    /// straight away and nothing has to hunt for a wizard window to click Skip), eager feature activation
    /// (automation outruns lazy warm-up), and the voice-model download.
    /// </para>
    /// </summary>
    public static bool UiTest { get; private set; }

    /// <summary>
    /// A page kind supplied via <c>--openTab &lt;PageKind&gt;</c>: after the shell's default tab opens, that
    /// page is opened too. A ribbon-independent deep-link into any tab — used by UI tests to reach views
    /// that aren't on the default ribbon (and by future taskbar/URI deep-linking).
    /// </summary>
    public static string? OpenTabKind { get; private set; }

    /// <summary>The page parameters the <see cref="OpenTabKind"/> page opens with: each <c>--tabParam key=value</c>.</summary>
    public static IReadOnlyDictionary<string, string> OpenTabParams { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    /// True when launched with <c>--reset</c>: the relaunch issued by "Reset Config" (Options → About).
    /// <see cref="ShellHost"/> deletes the whole app-data directory before initialising, so the run
    /// starts as a clean first-run. Set by the fresh process; the old one armed it before relaunching.
    /// </summary>
    private static bool _resetRequested;

    /// <summary>
    /// True when launched with <c>--timing</c>, or with <c>NEXAFLOW_STARTUP_TIMING=1</c> set: a harness is profiling the start-up
    /// (<see cref="StartupTimings"/>). The single-instance guard and the setup wizard are bypassed so it can cold-start repeatedly,
    /// and the process ends once its first window has drawn and the timings are reported.
    /// </summary>
    private static bool _profilingStartup;

    /// <summary>The app's version: its assembly's, as the config files are stamped with it.</summary>
    public static string Version => (typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0)).ToString();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool prestart = e.Args.Any(a => string.Equals(a, "--prestart", StringComparison.OrdinalIgnoreCase));
        UiTest = e.Args.Any(a => string.Equals(a, "--uiTest", StringComparison.OrdinalIgnoreCase));
        _resetRequested = e.Args.Any(a => string.Equals(a, "--reset", StringComparison.OrdinalIgnoreCase));

        // Opt-in startup profiling (StartupTimings). --timing bypasses the single-instance guard and the
        // setup wizard so a harness can cold-start repeatedly, but does NOT force the eager feature load the
        // way --uiTest does — so it measures the real lazy-startup path.
        _profilingStartup = e.Args.Any(a => string.Equals(a, "--timing", StringComparison.OrdinalIgnoreCase))
                            || Environment.GetEnvironmentVariable("NEXAFLOW_STARTUP_TIMING") == "1";

        // Where this launch keeps its configuration, and what stays on this machine alone — the session's, and the logs'.
        var folders = AppDataFolders.ForThisProcess();

        // The logs, before anything that writes to them: the exception hooks, the single-instance guard, the steps of start-up.
        _logging     = ShellLogging.Open(folders, Version, UiTest, _profilingStartup);
        _log         = _logging.Factory.CreateLogger<App>();
        _faultPolicy = new DispatcherFaultPolicy(() => DateTimeOffset.Now);

        // Catch stray UI-thread exceptions so one bad event handler can't take the whole shell down.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // …and the two channels that would otherwise swallow a fault whole: a throw on a background thread, and a
        // faulted Task nobody awaited.
        AppDomain.CurrentDomain.UnhandledException     += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException          += OnUnobservedTaskException;

        // A journey fails on a binding WPF could not resolve, which it otherwise tells only a debugger.
        if (UiTest) _bindingErrors = BindingErrorTrace.Attach(_logging.Factory);

        _updates = new AppUpdates(
            new UpdatumReleaseSource(HandOverToInstaller, StrandedMidUpdate),
            action =>
            {
                var dispatcher = Current?.Dispatcher;
                if (dispatcher is null || dispatcher.CheckAccess()) action();
                else dispatcher.BeginInvoke(action);
            },
            (attempt, ex) => _log.LogWarning(ex, "{Attempt}", attempt));



        var openTabIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--openTab", StringComparison.OrdinalIgnoreCase));
        OpenTabKind = openTabIdx >= 0 && openTabIdx + 1 < e.Args.Length ? e.Args[openTabIdx + 1] : null;
        OpenTabParams = TabParams(e.Args);

        // Held by the session's log until the shell config says whether this session keeps one (ShellHost).
        _log.LogInformation("startup{Mode}", prestart ? " as the login daemon (--prestart)"
                                           : UiTest   ? " under UI automation (--uiTest)"
                                           : "");
        _log.StartupMilestone("OnStartup");

#if !DEBUG
        // ── Single-instance guard ──
        // Skipped for a non-prestart timing run so the cold-start harness can loop; a --prestart --timing
        // daemon still acquires it, so a later launch routes its open-window request here (daemon timing).
        if (!(_profilingStartup && !prestart) && !_singleInstance.TryAcquire())
        {
            // A daemon (or an existing window) already owns the instance. A normal launch forwards what it
            // asked for (a --context "Name" from a taskbar JumpList, an --open "path" from Explorer's menu); a
            // --prestart launch must never spawn a window, so it just exits.
            if (!prestart)
                SingleInstanceService.ForwardLaunch(LaunchRequest.Parse(e.Args));
            Shutdown();
            return;
        }
#endif

        IsResident = prestart;

        // A later launch hands this one what it asked for. Listening starts first, so a launch never waits behind the start-up below;
        // what it asks is acted on once the UI thread is free of it, when the session is open.
        var ui = new WpfUiThread(Dispatcher);
        _singleInstance.StartListening(ui, launch => { if (_host is { } host) OpenForwardedLaunch(host.Activities, launch); });

        // ── The session + first window — a throw from this sequence ends the launch ──
        // Left to OnDispatcherUnhandledException it would be handled, and the launch would carry on with no window while
        // holding the single-instance guard. A fault raised inside the setup wizard's own message loop still goes there
        // and stays survivable; only what this sequence itself throws is terminal.
        try
        {
            var host = OpenSession(ui, folders);
            var activityManager = host.Activities;
            _log.StartupMilestone("InitializeApp");

            // ── Main window — skipped in --prestart mode (windowless login daemon) ──
            // A --prestart daemon shows nothing now; the wizard runs the first time a window is actually
            // due (OpenForwardedLaunch), so EnsureConfiguredThenCreateWindow gates both paths.
            if (!prestart)
            {
                // Honour --context "Name" from a taskbar JumpList and --open "path" from Explorer's menu.
                ActOnLaunch(activityManager, LaunchRequest.Parse(e.Args), activate: false);
            }
            _log.LogInformation("startup complete; {Window}", prestart ? "windowless until a window is asked for" : "first window created");
        }
        catch (Exception ex)
        {
            new StartupFailure(
                    _log,
                    () => _logging.Crash.CurrentPath,
                    _singleInstance.Dispose,
                    message => MessageBox.Show(message, "Nexaflow", MessageBoxButton.OK, MessageBoxImage.Error),
                    Shutdown)
                .Handle(ex, attended: !prestart && !UiTest && !_profilingStartup);
            return;
        }

        // A resident app keeps a window ready while it has none open (StandbyWindow): the first once the warm-up below has
        // finished, so it is built with every feature's theme and configs in place, then one each time the last window closes.
        if (prestart)
        {
            var standby = new StandbyWindow(_host!.Workspaces, StandbyTarget, ws => BuildFirstWindow(_host!.Activities, ws, openPath: null));
            _host.Workspaces.LastWindowClosed += (_, _) => standby.Schedule(StandbyWindow.AfterLastWindow);
            _standby = standby;
        }

        // The session's background work now that the first window is up: the remaining feature assemblies load and activate off the UI
        // thread, and the sweep of the app's own drafts starts.
        var warmUp = _host!.StartBackground();
        if (_standby is { } ready)
            warmUp.ContinueWith(_ => ready.Schedule(TimeSpan.Zero), TaskScheduler.FromCurrentSynchronizationContext());





        // Update check — runs in both paths (the daemon finds updates windowless and posts a message;
        // the first window replays it as a toast). Skipped on first run.
        if (!_host!.Config.IsFirstRun)
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CheckForUpdates();
            });
    }

    /// <summary>Each <c>--tabParam key=value</c> in <paramref name="args"/>: the page parameters of the --openTab page.</summary>
    internal static Dictionary<string, string> TabParams(IReadOnlyList<string> args)
    {
        var given = new Dictionary<string, string>();
        for (var i = 0; i + 1 < args.Count; i++)
            if (string.Equals(args[i], "--tabParam", StringComparison.OrdinalIgnoreCase) && args[i + 1].IndexOf('=') is > 0 and var equals)
                given[args[i + 1][..equals]] = args[i + 1][(equals + 1)..];
        return given;
    }

    /// <summary>
    /// Opens the session (<see cref="ShellHost"/>) on WPF — the clipboard, the folder watcher's window and the theme are WPF's — and
    /// wires what the session cannot name: the window a workspace rebuild opens, and the taskbar JumpList.
    /// </summary>
    private ShellHost OpenSession(WpfUiThread ui, AppDataFolders folders)
    {
        var host = new ShellHost(new ShellHostOptions(folders, Version, _resetRequested, UiTest, _logging),
                                 new ShellPlatform(ui, _clipboard = new ShellClipboard(this), new WpfFilePickers(), new WpfThemeHost(), typeof(ViewForAttribute), typeof(App).Assembly, ContentLanguages.Draws, new WpfPictureFitting(), new WasapiSpeechDevices(), ApplyAppearance,
                                                                     (catalog, dependencies, showComponents, logging) => _pageViews = PageViews.FromCatalog(catalog, dependencies, showComponents, logging)),
                                                   _updates);
        _host = host;
        // The pictures help pages show are decoded by WPF, once for every window.
        host.Features.ProvideTool(new HelpPictures(host.Help, host.Language));

        // The window a runtime opens into for a tear-off, a "new window" or a workspace rebuild: the session cannot name MainWindow.
        host.Workspaces.WindowFactory = ws => new MainWindow(host.Windows, _pageViews!, ws, openDefaultTabs: false).ViewModel;

        // The app ends with its last window — unless it stays resident, or an update is ending it once its installer runs, which
        // ending here would cut short.
        host.Workspaces.LastWindowClosed += (_, _) => { if (!IsResident && !IsUpdating) Current.Shutdown(); };

        // Taskbar JumpList — one entry per Workspace.
        JumpListService.Initialize(host.SavedWorkspaces);
        _log.StartupMilestone("Init.JumpList");
        return host;
    }

    /// <summary>
    /// The shell settings that decide how windows look, applied before the first window is built: the theme; whether the theme's
    /// backdrop animates on battery, so a scene that is going to be suppressed is never realised rather than started and torn
    /// down; and the text size — a shell setting pushed onto the shared policy the surfaces read, so no feature has to know Core
    /// holds it, and a text tab restored from the last session paints at the saved size rather than at 13 and then jumping.
    /// </summary>
    private static void ApplyAppearance(ShellConfig shell)
    {
        ThemeManager.Apply(shell.Theme);
        BatteryAnimationGuard.SetDisableOnBattery(shell.DisableAnimationsOnBattery);
        TextTypography.BaseFontSize = shell.TextFontSize;
    }

    /// <summary>The session this process runs: open from the start-up on, ended in <see cref="OnExit"/>.</summary>
    private static ShellHost? _host;

    /// <summary>The clipboard the session reads and writes, listening for its changes until the app exits.</summary>
    private ShellClipboard? _clipboard;

    /// <summary>The views the session's pages are drawn with, made for it as it opens and handed to every window.</summary>
    private static PageViews? _pageViews;
    protected override void OnExit(ExitEventArgs e)
    {
        _log.LogInformation("shutdown begun (exit code {ExitCode})", e.ApplicationExitCode);
        

        // The session ends: every workspace still running, the watches, the models held ahead, the AI host — each wait bounded.
        _host?.DisposeAsync().AsTask().Wait();
        _clipboard?.Dispose();
        _singleInstance.Dispose();

        // The logs end last: a burst the crash log is still counting is written out, and the session's log closed on its last line.
        _bindingErrors?.Detach();
        _log.LogInformation("shutdown complete");
        _logging.Dispose();
        base.OnExit(e);
    }

    /// <summary>Windows is signing out or shutting down. Recorded before anything else, since what follows may be the
    /// process being ended without an <see cref="OnExit"/>.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _log.LogInformation("Windows is ending the session ({Reason})", e.ReasonSessionEnding);
        base.OnSessionEnding(e);
    }

    /// <summary>
    /// Last-resort handler for an unhandled exception on the UI thread: log it, tell the user, and mark it
    /// handled so the shell stays open instead of terminating. A genuinely fatal fault will recur and leave
    /// a trail in the crash log rather than vanishing with the process. What the startup sequence itself throws is
    /// not handled here: with no shell yet to keep open, <see cref="OnStartup"/> ends that launch (<see cref="StartupFailure"/>).
    /// <para>
    /// Handling is not unconditional. An exception thrown from the WPF render pass cannot be recovered by
    /// handling it: the layout stays dirty, WPF re-measures on the next frame and it throws again. When
    /// <see cref="DispatcherFaultPolicy"/> sees that shape handling is withheld and the process terminates, which beats
    /// a live-locked shell writing the same trace 200 times a second.
    /// </para>
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var liveLock = _faultPolicy.Observe(e.Exception);

        // The crash log writes into the active config root (honours NEXAFLOW_CONFIG_DIR) so an isolated run — e.g.
        // a UI test — logs to its own dir instead of the developer's real %APPDATA%, and its teardown can
        // assert the log stayed empty.
        _log.LogError(e.Exception, "unhandled on the UI thread");

        if (liveLock is { } locked)
        {
            _log.LogCritical("the fault above repeated {Repeats} times in {Seconds:F1}s. Handling it is what keeps it recurring (a "
                           + "render-pass fault re-throws on every frame), so the process is being allowed to terminate instead of "
                           + "live-locking.", locked.Repeats, locked.Span.TotalSeconds);
            e.Handled = false;
            return;
        }

        try
        {
            _host?.Inbox.Post(new NotificationItem
            {
                Title     = Str.Get("Shell.Notifications.FaultTitle"),
                Body      = Str.Get("Shell.Notifications.FaultBody"),
                Severity  = MessageSeverity.Error,
                ShowToast = true,
            });
        }
        catch { }

        e.Handled = true;
    }

    /// <summary>
    /// A fault on any thread but the UI one: recorded, so a background failure leaves a trace — the crash log would otherwise
    /// know only dispatcher faults, which is why a customer's log could be 170MB and still not contain the two bugs they were
    /// actually reporting.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) _log.LogCritical(ex, "unhandled on a background thread");
        _logging.Crash.Flush();   // the process is going down; do not leave a burst uncounted
    }

    /// <summary>
    /// A faulted <see cref="Task"/> nobody awaited. Silent by default — the finalizer observes it and the
    /// exception evaporates — so these are logged and then marked observed, leaving behaviour unchanged.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.LogError(e.Exception, "a faulted task nobody awaited");
        e.SetObserved();
    }

    /// <summary>
    /// Acts on a later launch that <see cref="SingleInstanceService"/> handed over, as this process acts on its own,
    /// bringing the window it opens or opens into forward.
    /// </summary>
    private void OpenForwardedLaunch(BackgroundActivityManager activityManager, LaunchRequest launch)
    {
        _log.StartupWindowRequested();   // daemon click→window timing (timed only under --timing)
        ActOnLaunch(activityManager, launch, activate: true);
    }

    /// <summary>
    /// Acts on <paramref name="launch"/>. Without an item it opens a new window in the workspace it names (a taskbar
    /// JumpList entry), or the first one. An item goes to the workspace it names, or else to the one
    /// <see cref="WorkspaceManager.WorkspaceForOpenedPath"/> picks, and opens in that workspace's window — a new window
    /// only when it has none.
    /// </summary>
    private void ActOnLaunch(BackgroundActivityManager activityManager, LaunchRequest launch, bool activate)
    {
        var named = ResolveWorkspace(launch.WorkspaceName);
        if (launch.OpenPath is not { } path)
        {
            EnsureConfiguredThenCreateWindow(activityManager, named ?? _host!.SavedWorkspaces.Workspaces[0], activate);
            return;
        }

        var workspace = named ?? _host!.SavedWorkspaces.WorkspaceForOpenedPath(_host.Workspaces.WithWindows);
        if (_host.Workspaces.Find(workspace)?.ShellServices is { HasWindows: true } shell)
            shell.OpenFromOutside(path);
        else
            EnsureConfiguredThenCreateWindow(activityManager, workspace, activate, path);
    }

    /// <summary>Tracks whether the first window of this process has already run the setup check.</summary>
    private static bool _setupShown;

    /// <summary>The window a resident app keeps ready while none is open; null in an app that is not resident.</summary>
    private static StandbyWindow? _standby;

    /// <summary>
    /// Creates a shell window for <paramref name="workspace"/>, running the first-run / post-update
    /// wizard first the very first time a window is due (so a <c>--prestart</c> daemon shows nothing
    /// until its first real window). Skipping or finishing the wizard always proceeds to the window;
    /// committed steps persist either way. The window opens <paramref name="openPath"/> when one is given,
    /// and its workspace's default tabs otherwise.
    /// </summary>
    private MainWindow EnsureConfiguredThenCreateWindow(
        BackgroundActivityManager activityManager, Workspace workspace, bool activate, string? openPath = null)
    {
        // A standby window stands for a plain launch of its workspace: showing it is all this launch has left to do.
        if (openPath is null && _standby is { } standby && standby.Claim(workspace, out bool drawn) is { } ready)
        {
            _log.StartupMilestone("StandbyShown");
            FollowFirstFrame(ready, drawnBefore: drawn);
            return ready;
        }
        // Any other window opening ends the standby, which stands only while none is open.
        _standby?.Discard();

        // The wizard honours CenterScreen and lands on the active monitor; capture that monitor's work
        // area so the main window opens on the SAME monitor (it otherwise takes the OS default
        // placement, which falls back to the primary monitor).
        Rect? wizardMonitor = null;

        if (!_setupShown)
        {
            _setupShown = true;

            // --uiTest and --timing (profiling) bypass the wizard entirely; the run is still
            // stamped so post-update detection stays consistent.
            if (!UiTest && !_profilingStartup)
            {
                var wizard = SetupWizardViewModel.Build(workspace, ConfigPages(), _host!.Config, _host.Runs, _host.Features, _host.Providers,
                                                                        _host.SavedWorkspaces);
                if (wizard is not null)
                {
                    var wizardWin = new SetupWizardWindow(wizard);
                    // Closing fires while the HWND is still alive, so the monitor is still resolvable.
                    wizardWin.Closing += (_, _) => wizardMonitor = WindowManager.GetWorkAreaForWindow(wizardWin);
                    wizardWin.ShowDialog();   // modal; returns on Finish / Skip / close
                }
            }

            FinishSetup();
        }

        var ws = _host!.Workspaces.Open(workspace);
        _log.StartupMilestone("WorkspaceBootstrapped");

        var win = BuildFirstWindow(activityManager, ws, openPath);
        _log.StartupMilestone("MainWindowBuilt");
        // Back where this workspace's last window was meant to be; the first time, on the wizard's monitor.
        if (workspace.LastWindowPlacement is { IsUsable: true } placement)
            ((IWindowHost)win.ViewModel).OpenAt(placement);
        else
            CenterOnMonitor(win, wizardMonitor);
        win.Show();
        _log.StartupMilestone("WindowShown");
        FollowFirstFrame(win, drawnBefore: false);
        if (activate) win.Activate();
        return win;
    }

    /// <summary>A runtime's first window, unshown: opening <paramref name="openPath"/>, or the workspace's tabs.</summary>
    private static MainWindow BuildFirstWindow(BackgroundActivityManager activityManager, WorkspaceRuntime ws, string? openPath)
        => new(_host!.Windows, _pageViews!, ws, openPath: openPath);

    /// <summary>
    /// What follows a launch's window drawing: models load ahead only then, so none of their loading competes with it, and a
    /// <c>--timing</c> run reports and ends. A window that drew before it was shown — a standby — is followed once the frame
    /// that shows it has drawn.
    /// </summary>
    private void FollowFirstFrame(Window win, bool drawnBefore)
    {
        void Drawn()
        {
            _host?.FirstWindowDrawn();
            if (_logging.Timings is not { } timings) return;
            // Startup profiling: the first window's first render is "time to first window"; a harness cold-starts again.
            timings.Report();
            Current.Shutdown();
        }

        if (!drawnBefore)
        {
            win.ContentRendered += (_, _) => Drawn();
            return;
        }
        // Below render priority: it runs once the frame that shows the window has been laid out and drawn.
        win.Dispatcher.InvokeAsync(Drawn, DispatcherPriority.Background);
    }

    /// <summary>
    /// The workspace a standby window is built for now — the first, which a plain launch opens — or null while none should
    /// stand: a window is open, the app is updating, the launch asked for a page of its own, the user has turned it off
    /// (<see cref="ShellConfig.KeepWindowReady"/>), or the setup wizard is still due, which runs before the first window and
    /// may change what it shows.
    /// </summary>
    private Workspace? StandbyTarget()
    {
    if (IsUpdating || OpenTabKind is not null || _host?.Workspaces.AnyWindowsOpen == true) return null;
        if (_host?.Shell is not { KeepWindowReady: true }) return null;
        if (_host?.SavedWorkspaces.Workspaces is not [var first, ..]) return null;
        return SettleSetupAhead(first) ? first : null;
    }

    /// <summary>
    /// Settles the first-run / post-update setup ahead of the first window when it has nothing to ask, as that window would
    /// settle it, and says whether it is settled — false while a wizard is due, which runs when a window is first asked for.
    /// </summary>
    private bool SettleSetupAhead(Workspace workspace)
    {
        if (_setupShown) return true;
        if (!UiTest && !_profilingStartup && SetupWizardViewModel.Build(workspace, ConfigPages(), _host!.Config, _host.Runs, _host.Features,
                                                                                          _host.Providers, _host.SavedWorkspaces) is not null) return false;
        _setupShown = true;
        FinishSetup();
        return true;
    }

    /// <summary>Ends the setup check before a first window: the run is stamped, so the next launch can tell an update, and
    /// the language the wizard's Shell step may have picked is selected before the window's strings resolve.</summary>
    private static void FinishSetup()
    {
        _host!.Runs.Stamp();
        _host!.Language.Select(_host.Shell.Language);
    }

    /// <summary>What the setup wizard binds a settings page to: for a Core page that shows the session, its view-model over the
    /// session's services — those it hands every window.</summary>
    private static ConfigPageModels ConfigPages() => ConfigPageModels.Drawn(_pageViews!.Registry, _host!.Windows);

    /// <summary>
    /// Centres <paramref name="win"/> within <paramref name="workArea"/> (a monitor's work area in WPF
    /// logical px) so it opens on that monitor. No-op when <paramref name="workArea"/> is null — the
    /// window then keeps the OS default placement.
    /// </summary>
    private static void CenterOnMonitor(Window win, Rect? workArea)
    {
        if (workArea is not { } work) return;
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left = work.Left + Math.Max(0, (work.Width  - win.Width)  / 2);
        win.Top  = work.Top  + Math.Max(0, (work.Height - win.Height) / 2);
    }

    /// <summary>
    /// "Reset Config" (Options → About): delete all configuration and relaunch into first-run. Arms the
    /// config-write suppressor so nothing re-persists during teardown, drops the single-instance mutex so
    /// the relaunched process can claim it, closes every window, and starts a fresh <c>--reset</c> process
    /// that wipes the app-data directory before initialising. Irreversible — only reached after the user
    /// confirms the shell overlay.
    /// </summary>
    internal static void ResetAndRestart()
    {
        _host?.Config.SuppressWrites();

        var exe = Environment.ProcessPath;

        // Release the mutex first so the relaunched instance isn't bounced by the single-instance guard.
        _singleInstance.Dispose();

        foreach (var window in Current.Windows.Cast<Window>().ToList())
            window.Close();

        if (!string.IsNullOrEmpty(exe))
            Process.Start(new ProcessStartInfo(exe, "--reset") { UseShellExecute = false });

        Current.Shutdown();
    }

    /// <summary>Resolves a workspace name to a saved <see cref="Workspace"/>, or null.</summary>
    private static Workspace? ResolveWorkspace(string? name)
        => string.IsNullOrEmpty(name)
            ? null
            : _host!.SavedWorkspaces.Workspaces.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The startup check: asks for a newer release and, when one is offered, posts a persistent update message with an
    /// Update action. Whichever window is focused toasts it; if none is open yet (daemon) it waits in the inbox and toasts
    /// when a window opens.
    /// </summary>
    private async Task CheckForUpdates()
    {
        if (await _updates.CheckAsync() is not { } offer) return;

        _host?.Inbox.Post(new NotificationItem
        {
            Title     = Str.Format("Shell.Notifications.UpdateFormat", offer.Version),
            Body      = offer.Changelog,
            Severity  = MessageSeverity.Update,
            ShowToast = true,
            Actions   = [new MessageAction(Str.Get("Shell.Notifications.Update"), new AsyncRelayCommand(() => _updates.InstallAsync()), IsPrimary: true)],
        });
    }

    /// <summary>
    /// Commits this process to exiting so an update's installer can replace its files. Drops the single-instance mutex so
    /// the relaunched version can claim it, and closes every window so each saves its state and nothing holds a lock on the
    /// install directory; <see cref="IsUpdating"/> stops the last window closing from ending the process before the
    /// updater has started the installer's script.
    /// </summary>
    private static void HandOverToInstaller()
    {
        IsUpdating = true;
        _singleInstance.Dispose();
        foreach (var window in Current.Windows.Cast<Window>().ToList())
            window.Close();
    }

    /// <summary>The installer failed to start after every window closed, and nothing else will end the process: record why, then exit.</summary>
    private void StrandedMidUpdate(Exception ex)
    {
        _log.LogCritical(ex, "the installer did not start after every window closed; the app is exiting");
        _logging.Crash.Flush();
        Current.Shutdown();
    }
}
