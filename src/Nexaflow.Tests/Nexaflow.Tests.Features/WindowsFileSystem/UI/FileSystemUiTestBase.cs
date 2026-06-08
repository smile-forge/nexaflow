using System;
using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// Shared helpers for the file-browser create UI tests: element waits and navigating the
/// file browser to a folder by typing its path into the AI input bar (the existing
/// path-navigation route handled by <c>FileSystemQueryHandler</c>) — so no production
/// startup hook is needed to open a controlled temp folder.
/// </summary>
public abstract class FileSystemUiTestBase : UITestBase
{
    /// <summary>
    /// Navigates the (default) file-browser tab to <paramref name="path"/> via the AI input bar,
    /// then waits for the strip's "New" button to confirm the folder is open.
    /// </summary>
    protected void NavigateFileBrowserTo(string path)
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "File browser tab did not load.");

        var ai = WaitForId("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        System.Threading.Thread.Sleep(150);
        Keyboard.Type(path);
        Keyboard.Press(VirtualKeyShort.RETURN);

        Assert.IsNotNull(WaitForId("New", 10),
            "File browser did not navigate to the target folder (the 'New' button never appeared).");
    }

    protected void SetText(string automationId, string value)
    {
        var box = WaitForId(automationId, 5);
        Assert.IsNotNull(box, $"Text box '{automationId}' not found.");
        box!.AsTextBox().Text = value;
    }

    protected AutomationElement? WaitForId(string automationId, int seconds = 6)
        => WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), seconds);

    protected AutomationElement? WaitForName(string name, int seconds = 6)
        => WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByName(name)), seconds);

    protected static AutomationElement? WaitFor(Func<AutomationElement?> find, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = find(); } catch { /* tree churned — retry */ }
            if (el is not null && !el.IsOffscreen) return el;
            System.Threading.Thread.Sleep(120);
        }
        return null;
    }

    protected static bool WaitForFs(Func<bool> condition, int seconds = 5)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition()) return true;
            System.Threading.Thread.Sleep(150);
        }
        return condition();
    }
}
