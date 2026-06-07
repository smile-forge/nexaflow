using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Nexaflow.Tests.Core.UI.Infrastructure;

/// <summary>
/// Base class for FlaUI UI tests. Launches a fresh Nexacore.exe before each test
/// and kills it after. UI tests are not parallelised — launching multiple app
/// instances simultaneously causes FlaUI window-handle race conditions.
/// Filter out of headless/CI runs: --filter "TestCategory!=UI"
///
/// Each test runs against an isolated, throwaway config dir (NEXAFLOW_CONFIG_DIR) so it
/// neither depends on nor pollutes the developer's real %APPDATA% config. A fresh config
/// means the first-run / post-update setup wizard shows on launch; we skip it to reach the
/// shell window, which is what these tests exercise.
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

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [TestInitialize]
    public void UISetup()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "nexaflow-uitest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);

        Automation = new UIA3Automation();

        var psi = new ProcessStartInfo(FindAppExe()) { UseShellExecute = false };
        psi.EnvironmentVariables["NEXAFLOW_CONFIG_DIR"] = _configDir;   // isolated, fresh
        App = Application.Launch(psi);

        MainWindow = ResolveShellWindow();
    }

    [TestCleanup]
    public void UITeardown()
    {
        try { App?.Kill(); } catch { /* already exited */ }
        Automation?.Dispose();
        try { if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Returns the shell window (AutomationId "MainWindow"), skipping the setup wizard first if it
    /// appears. A fresh config root makes the first-run wizard show modally before the shell, so a
    /// naive GetMainWindow would grab the wizard ("Nexaflow Setup") instead of the shell.
    /// </summary>
    private Window ResolveShellWindow()
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            Window[] windows;
            try { windows = App.GetAllTopLevelWindows(Automation); }
            catch { windows = []; }

            // Shell is up — identified by its window-level AutomationId.
            var shell = windows.FirstOrDefault(w =>
                w.Properties.AutomationId.ValueOrDefault == "MainWindow");
            if (shell is not null) return shell;

            // Setup wizard is up — skip it so the shell can open.
            var wizard = windows.FirstOrDefault(w =>
                string.Equals(w.Title, "Nexaflow Setup", StringComparison.Ordinal));
            var skip = wizard?
                .FindFirstDescendant(cf => cf.ByAutomationId("WizardSkipButton"))?
                .AsButton();
            if (skip is { IsEnabled: true }) skip.Invoke();

            Thread.Sleep(100);
        }

        throw new TimeoutException(
            $"Shell window (AutomationId 'MainWindow') did not appear within {Timeout.TotalSeconds:0}s.");
    }

    /// <summary>
    /// Resolves the Nexacore.exe path from the test binary's output directory.
    /// Tries the RID-qualified layout (win-x64 subdir) first, then falls back to the
    /// flat layout. Both projects must use the same configuration (Debug/Release) and TFM.
    /// </summary>
    private static string FindAppExe()
    {
        var testDir = new DirectoryInfo(AppContext.BaseDirectory); // net10.0-windows
        var tfm     = testDir.Name;
        var config  = testDir.Parent!.Name; // Debug or Release
        // Navigate up: tfm → config → bin → Nexaflow.Tests.Core → Nexaflow.Tests → src
        var srcDir  = testDir.Parent!   // config
                             .Parent!   // bin
                             .Parent!   // Nexaflow.Tests.Core
                             .Parent!   // Nexaflow.Tests
                             .Parent!   // src
                             .FullName;

        var coreBase = Path.Combine(srcDir, "Nexaflow.Core", "bin", config, tfm);

        // With RID subdir (self-contained / single-file publish produces this layout)
        var withRid = Path.Combine(coreBase, "win-x64", "Nexacore.exe");
        if (File.Exists(withRid)) return withRid;

        // Flat layout (framework-dependent)
        var flat = Path.Combine(coreBase, "Nexacore.exe");
        if (File.Exists(flat)) return flat;

        throw new FileNotFoundException(
            $"Could not locate Nexacore.exe. Tried:\n  {withRid}\n  {flat}");
    }
}
