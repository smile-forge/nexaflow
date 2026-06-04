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
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
public abstract class UITestBase
{
    protected Application App { get; private set; } = null!;
    protected UIA3Automation Automation { get; private set; } = null!;
    protected Window MainWindow { get; private set; } = null!;

    [TestInitialize]
    public void UISetup()
    {
        Automation = new UIA3Automation();
        App = Application.Launch(FindAppExe());
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(15))!;
    }

    [TestCleanup]
    public void UITeardown()
    {
        try { App?.Kill(); } catch { /* already exited */ }
        Automation?.Dispose();
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
