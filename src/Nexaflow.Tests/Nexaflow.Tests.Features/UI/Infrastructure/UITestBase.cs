using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Nexaflow.Tests.Features.UI.Infrastructure;

/// <summary>
/// Launches Nexaflow.exe before each test and kills it after.
/// UI tests require an interactive desktop session — skip in headless/CI with
/// --filter "TestCategory!=UI".
///
/// Each test runs against an isolated, throwaway config dir (NEXAFLOW_CONFIG_DIR) so it
/// neither depends on nor pollutes the developer's real %APPDATA% config. The app is launched
/// with --skipSetup so the first-run / post-update wizard is bypassed and the shell opens
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

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [TestInitialize]
    public void UISetup()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "nexaflow-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);

        Automation = new UIA3Automation();

        var psi = new ProcessStartInfo(FindAppExe()) { UseShellExecute = false };
        psi.ArgumentList.Add("--skipSetup");                           // bypass the first-run wizard
        psi.EnvironmentVariables["NEXAFLOW_CONFIG_DIR"] = _configDir;   // isolated, fresh
        App = Application.Launch(psi);

        MainWindow = ResolveShellWindow();
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
        try { App?.Kill(); } catch { /* already exited */ }
        Automation?.Dispose();
        try { if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Returns the shell window (AutomationId "MainWindow"). The app is launched with --skipSetup, so the
    /// wizard never shows and the shell is the first (and only) top-level window.
    /// </summary>
    private Window ResolveShellWindow()
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            Window[] windows;
            try { windows = App.GetAllTopLevelWindows(Automation); }
            catch { windows = []; }

            var shell = windows.FirstOrDefault(w =>
                w.Properties.AutomationId.ValueOrDefault == "MainWindow");
            if (shell is not null) return shell;

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

    private static string FindAppExe()
    {
        var testDir = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm     = testDir.Name;
        var config  = testDir.Parent!.Name;
        var srcDir  = testDir.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;

        var coreBase = Path.Combine(srcDir, "Nexaflow.Core", "bin", config, tfm);
        var withRid  = Path.Combine(coreBase, "win-x64", "Nexaflow.exe");
        if (File.Exists(withRid)) return withRid;

        var flat = Path.Combine(coreBase, "Nexaflow.exe");
        if (File.Exists(flat)) return flat;

        throw new FileNotFoundException(
            $"Could not locate Nexaflow.exe. Tried:\n  {withRid}\n  {flat}");
    }
}
