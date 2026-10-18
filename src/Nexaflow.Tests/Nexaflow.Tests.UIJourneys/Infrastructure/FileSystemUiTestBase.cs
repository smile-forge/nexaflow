using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

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

        Assert.IsNotNull(WaitForId("NewMenuAction", 10),
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

    /// <summary>
    /// True once no on-screen element carries <paramref name="automationId"/> — for asserting that a
    /// modal actually went away. Only meaningful for ids on real controls (which have automation peers);
    /// on a Border or a panel this would "pass" by finding nothing either way.
    /// </summary>
    protected bool WaitForGone(string automationId, int seconds = 4)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)); }
            catch { /* tree churned — retry */ }
            if (el is null || el.IsOffscreen) return true;
            System.Threading.Thread.Sleep(120);
        }
        return false;
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

    /// <summary>Clicks <paramref name="name"/>'s row in the file list, scrolled into view — the selection a page acts on.</summary>
    protected bool SelectInFileList(string name, int seconds = 30)
    {
        var row = FindInFileList(name, seconds);
        if (row is null) return false;

        row.Click();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// <paramref name="name"/>'s cell in the file list, on screen. The list virtualises its rows, so a folder longer than the
    /// window holds only the rows near its scroll position: when the name is not among them, the list is scrolled from the
    /// top a page at a time until it is. How many rows fit depends on the screen the journey runs on, never on the folder. Scoped to the list on purpose: a window-wide search by name also matches the folder tree, whose node may be off screen, where a click lands on the desktop.
    /// </summary>
    protected AutomationElement? FindInFileList(string name, int seconds = 30)
    {
        var list = WaitForId("FileListView", seconds);
        if (list is null) return null;

        AutomationElement? Shown(int wait) => WaitFor(() => list.FindFirstDescendant(cf => cf.ByName(name)), wait);

        var cell = Shown(Math.Min(seconds, 3));
        if (cell is null && list.Patterns.Scroll.PatternOrDefault is { } scroll && scroll.VerticallyScrollable.Value)
        {
            scroll.SetScrollPercent(-1, 0);   // -1: leave the horizontal position as it is
            Wait.UntilInputIsProcessed();
            cell = Shown(1);
            while (cell is null && scroll.VerticalScrollPercent.Value < 99.0)
            {
                scroll.Scroll(FlaUI.Core.Definitions.ScrollAmount.NoAmount, FlaUI.Core.Definitions.ScrollAmount.LargeIncrement);
                Wait.UntilInputIsProcessed();
                cell = Shown(1);
            }
        }
        cell?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        return cell;
    }
}
