using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// Launches Nexaflow.exe before each test and kills it after.
/// UI tests require an interactive desktop session — skip in headless/CI with
/// --filter "TestCategory!=UI". The first launch in a process asks the machine's owner for the mouse and
/// keyboard first (<see cref="UiTakeoverPrompt"/>); CI and <c>NEXAFLOW_UITESTS_NOPROMPT=1</c> skip it.
/// Only one UI test runs at a time across the whole machine (<see cref="UiTestGate"/>) — a whole-suite run
/// is several test hosts, and <c>[DoNotParallelize]</c> only serialises within one.
///
/// Each test runs against an isolated, throwaway config dir (NEXAFLOW_CONFIG_DIR) so it
/// neither depends on nor pollutes the developer's real %APPDATA% config. The app is launched
/// with --uiTest so the first-run / post-update wizard is bypassed and the shell opens
/// straight away — no hunting for the wizard window to click Skip.
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
public abstract class UITestBase
{
    private string _configDir = null!;

    protected Application App { get; private set; } = null!;
    protected UIA3Automation Automation { get; private set; } = null!;
    protected Window MainWindow { get; private set; } = null!;

    /// <summary>The isolated config root for this test (NEXAFLOW_CONFIG_DIR).</summary>
    protected string ConfigDir => _configDir;

    /// <summary>
    /// How long to wait for the shell window after launching. Generous because it costs nothing when the
    /// app is healthy — the wait returns the moment the window appears — and because the machine is not
    /// always healthy when this runs: a preceding test that thrashed the box left the next launch short of
    /// twenty seconds, and the failure it produced ("Shell window did not appear within 20s") reads as a
    /// broken app rather than as a loaded machine. Only a launch that is genuinely stuck should reach this.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>How long to wait for another test host's UI test to finish. The gate is held per test, not
    /// per suite, so a legitimate wait is one other journey — a couple of minutes at worst. A host killed
    /// mid-test leaks its token, and then every later test pays this once: long enough to be a real wait,
    /// short enough not to look like a hang. On timeout we run anyway rather than fail — unserialised is
    /// what this suite did before, a deadlock is not.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How fast the pointer travels. FlaUI glides it at half a pixel a millisecond, so every click across a 1000-pixel
    /// window first spends two seconds getting there. It still moves in steps, so hover and drag see it pass; it only
    /// stops taking its time.
    /// </summary>
    private const double PointerPixelsPerMillisecond = 20;

    private IDisposable? _gate;

    /// <summary>
    /// Override to a page kind (e.g. "SystemInfo") to have the app open that tab on launch via
    /// <c>--openTab</c> — a ribbon-independent, deterministic way to reach any view, including those
    /// that aren't on the default ribbon. Null (default) launches with just the default tabs.
    /// </summary>
    protected virtual string? LaunchTabKind => null;

    /// <summary>The page parameters <see cref="LaunchTabKind"/> opens with, each passed as <c>--tabParam key=value</c>.</summary>
    protected virtual IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string>();

    /// <summary>Environment the app is started with besides its isolated config dir — read after <see cref="SeedConfig"/>,
    /// so a seed can decide it (where the AI models are, say).</summary>
    protected virtual IReadOnlyDictionary<string, string> LaunchEnvironment { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Override to seed files/config into the isolated config dir (<paramref name="configDir"/> =
    /// <c>NEXAFLOW_CONFIG_DIR</c>) BEFORE the app launches — e.g. write a workspace-scoped feature config
    /// under <c>Contexts\Default\&lt;configName&gt;\config_&lt;version&gt;.json</c> to enable a feature. Runs
    /// after the dir is created, before <c>Application.Launch</c>. No-op by default.
    /// </summary>
    protected virtual void SeedConfig(string configDir) { }

    /// <summary>
    /// What a journey owes the machine before it launches the app: the owner's consent, a DPI-aware host so its
    /// clicks land on the pixels it means, and the machine-wide gate so no other host's app is up at the same time.
    /// <para>
    /// The one way in. A journey that called <c>Application.Launch</c> on its own skipped all three — the setup
    /// wizard's did, and so ran through a declined prompt onto a machine someone was using.
    /// </para>
    /// </summary>
    /// <returns>The gate: dispose it once the app is gone.</returns>
    internal static IDisposable TakeMachine()
    {
        UiTakeoverPrompt.EnsureAllowed();
        UiTestGate.EnsureDpiAware();
        return UiTestGate.Acquire(GateTimeout);
    }

    /// <summary>
    /// Why this test cannot run on this machine — the hardware it drives is not plugged in — or null when it can. Asked
    /// before the machine is taken or the app launched, so a test with nothing to drive costs nothing.
    /// </summary>
    protected virtual string? MissingPrerequisite() => null;

    [TestInitialize]
    public void UISetup()
    {
        // Before anything is launched or seeded: these drive the real mouse and keyboard, so ask once
        // whether the machine is free (a no-op on CI and after the first answer), then take it.
        if (MissingPrerequisite() is { } missing) Assert.Inconclusive(missing);
        _gate = TakeMachine();
        Mouse.MovePixelsPerMillisecond = PointerPixelsPerMillisecond;

        _configDir = Path.Combine(TestTemp.Root, "nexaflow-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);

        // Let a derived test seed files/config into the isolated dir BEFORE launch (e.g. enable a
        // workspace-scoped feature so its enabled UI renders instead of the disabled placeholder).
        SeedConfig(_configDir);

        Automation = new UIA3Automation();

        App = Application.Launch(AppLaunchRecipe.StartInfo(_configDir, LaunchTabKind, LaunchTabParams, LaunchEnvironment));

        MainWindow = ResolveShellWindow();
        BringShellToFront();
        OnUISetup();
    }

    /// <summary>
    /// Override in derived classes for feature-specific setup that runs after the
    /// app is launched. Do NOT add a separate [TestInitialize] in derived classes —
    /// MSTest calls both the base and derived [TestInitialize], which would launch
    /// the app twice.
    /// </summary>
    protected virtual void OnUISetup() { }

    [TestCleanup]
    public void UITeardown()
    {
        // Kill() only signals — without waiting, the next test's app launches while this one is still on
        // screen. Two live windows means the newest is not necessarily foremost, so any mouse-driven click
        // can land on the wrong app. Wait for the process to actually go.
        try
        {
            App?.Kill();
            App?.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5));
            for (var waited = 0; App is { HasExited: false } && waited < 5000; waited += 100)
                System.Threading.Thread.Sleep(100);
        }
        catch { /* already exited */ }
        Automation?.Dispose();
        _gate?.Dispose();               // hand the machine to the next host, however this test ended
        _gate = null;

        // The app writes any unhandled exception to a dated crash log in its (isolated) config dir; the
        // handler marks it handled so the app stays up, making the log the only trail. Opening/clicking
        // around the app must not trigger one — surface it as a test failure rather than a silent log.
        var crash = AppLaunchRecipe.ReadCrashLogs(_configDir);

        try { if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true); } catch { }

        if (!string.IsNullOrWhiteSpace(crash))
            Assert.Fail("The app logged an unhandled exception during this UI test — opening/clicking " +
                        $"triggered a crash:{Environment.NewLine}{crash}");
    }

    /// <summary>
    /// Returns the shell window (AutomationId "MainWindow"). The app is launched with --uiTest, so the
    /// wizard never shows and the shell is the first (and only) top-level window.
    /// </summary>
    /// <summary>
    /// Raises the shell and confirms Windows actually put it in front before anything clicks it.
    /// <para>
    /// A launched app does not reliably come forward: Windows declines the focus change when the process
    /// asking is not itself foreground, so the shell can open behind the editor. It still reports its real
    /// on-screen bounds from there, so FlaUI clicks those coordinates and hits whatever is on top instead
    /// — the test then fails on a missing element, nowhere near the actual cause. Better to say so here.
    /// </para>
    /// </summary>
    private void BringShellToFront()
    {
        try { MainWindow.SetForeground(); } catch { /* UiTestGate forces it below either way */ }

        if (!UiTestGate.BringToForeground(MainWindow.Properties.NativeWindowHandle, TimeSpan.FromSeconds(10)))
            Assert.Inconclusive(
                "The Nexaflow window would not come to the foreground, so every click would land in "
                + "whatever window is on top. Re-run with the desktop free.");
    }

    /// <summary>
    /// Brings the app in front before input is sent the way a user sends it. Keystrokes and clicks go to whichever window
    /// is in front, so input sent while another app holds the foreground lands in that app. False when the app will not
    /// come forward — send nothing then.
    /// </summary>
    protected bool InFront() => UiTestGate.BringToForeground(MainWindow.Properties.NativeWindowHandle, TimeSpan.FromSeconds(5));

    private Window ResolveShellWindow()
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            // A window still being made or already going answers its properties with a COM error: it is not the shell
            // yet, so the next look decides.
            try
            {
                var shell = App.GetAllTopLevelWindows(Automation)
                    .FirstOrDefault(w => w.Properties.AutomationId.ValueOrDefault == "MainWindow");
                if (shell is not null) return shell;
            }
            catch (COMException) { }

            System.Threading.Thread.Sleep(100);
        }

        throw new TimeoutException(
            $"Shell window (AutomationId 'MainWindow') did not appear within {Timeout.TotalSeconds:0}s.");
    }

    /// <summary>
    /// Tries to open a tab that contains <paramref name="targetAutomationId"/> by:
    /// 1. Checking whether the element is already on screen.
    /// 2. Clicking each ribbon button in turn and checking after each click.
    /// 3. If <paramref name="aiInputFallback"/> is supplied, typing that text into
    ///    AiInputBox and pressing Enter — useful for features that the AI input bar
    ///    can route to (e.g. a glob pattern routes to Windows Search).
    /// Returns the found element, or null if none of the attempts succeed.
    /// </summary>
    protected AutomationElement? TryOpenTabWithElement(
        string targetAutomationId,
        string? aiInputFallback = null)
    {
        // 1. Already visible?
        var existing = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(targetAutomationId));
        if (existing is not null && !existing.IsOffscreen) return existing;

        // 2. Ribbon buttons
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        if (ribbon is not null)
        {
            var buttons = ribbon.FindAllDescendants(
                cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
            foreach (var btn in buttons)
            {
                try { btn.AsButton().Invoke(); } catch { continue; }
                System.Threading.Thread.Sleep(600);

                var found = MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId(targetAutomationId));
                if (found is not null && !found.IsOffscreen) return found;
            }
        }

        // 3. AI input bar fallback
        if (aiInputFallback is not null)
        {
            var aiBox = MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("AiInputBox"));
            if (aiBox is not null)
            {
                aiBox.Click();
                System.Threading.Thread.Sleep(200);
                Keyboard.Type(aiInputFallback);
                Keyboard.Press(VirtualKeyShort.RETURN);
                System.Threading.Thread.Sleep(1500);

                var found = MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId(targetAutomationId));
                if (found is not null && !found.IsOffscreen) return found;
            }
        }

        return null;
    }
}
