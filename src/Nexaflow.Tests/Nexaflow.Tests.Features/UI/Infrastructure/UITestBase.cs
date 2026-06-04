using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Nexaflow.Tests.Features.UI.Infrastructure;

/// <summary>
/// Launches Nexacore.exe before each test and kills it after.
/// UI tests require an interactive desktop session — skip in headless/CI with
/// --filter "TestCategory!=UI".
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
        App        = Application.Launch(FindAppExe());
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(15))!;
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
        var withRid  = Path.Combine(coreBase, "win-x64", "Nexacore.exe");
        if (File.Exists(withRid)) return withRid;

        var flat = Path.Combine(coreBase, "Nexacore.exe");
        if (File.Exists(flat)) return flat;

        throw new FileNotFoundException(
            $"Could not locate Nexacore.exe. Tried:\n  {withRid}\n  {flat}");
    }
}
