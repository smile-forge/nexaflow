using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// What the agent does on the sandbox's desktop: screenshots, the UI Automation tree, and input sent the way a person sends
/// it — real pointer moves and keystrokes, as the journeys send them. Elements are looked for in every Nexaflow window,
/// whoever launched it, so the app a journey launched can be inspected while it runs.
/// </summary>
internal sealed class DesktopDriver(UIA3Automation automation)
{
    private const string AppProcess = "Nexaflow";
    private const int TreeLineLimit = 5000;
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The whole screen, or Nexaflow's main window, as a PNG at <paramref name="path"/>.</summary>
    public void Screenshot(string path, bool window)
    {
        using var image = window
            ? Capture.Element(AppWindows().FirstOrDefault(w => w.Properties.AutomationId.ValueOrDefault == "MainWindow")
                              ?? throw new SandboxException("No Nexaflow window is open to capture. `launch` one, or take the whole screen."))
            : Capture.Screen();
        image.ToFile(path);
    }

    /// <summary>The control-view tree under <paramref name="target"/>, or under every Nexaflow window, one element a line.</summary>
    public string Tree(ElementTarget? target, int depth)
    {
        var roots = target is null ? AppWindows() : [Find(target).Element];
        if (roots.Count == 0) return "No Nexaflow window is open.";

        var walker = automation.TreeWalkerFactory.GetControlViewWalker();
        var text = new StringBuilder();
        var lines = 0;
        foreach (var root in roots) Write(root, 0);
        if (lines >= TreeLineLimit) text.Append($"… cut at {TreeLineLimit} lines; ask for less with a target or --depth.");
        return text.ToString().TrimEnd();

        void Write(AutomationElement element, int level)
        {
            if (lines++ >= TreeLineLimit) return;
            text.Append(' ', level * 2).AppendLine(Describe(element));
            if (level >= depth) return;
            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                Write(child, level + 1);
        }
    }

    public string Click(ElementTarget target, bool right, bool twice)
    {
        var point = PointOf(target, out var described);
        var button = right ? MouseButton.Right : MouseButton.Left;
        if (twice) Mouse.DoubleClick(point, button);
        else Mouse.Click(point, button);
        return $"{(twice ? "Double-clicked" : right ? "Right-clicked" : "Clicked")} {described} at {point.X},{point.Y}.";
    }

    public string Type(string text, ElementTarget? into)
    {
        var where = "whatever had focus";
        if (into is not null)
        {
            var point = PointOf(into, out where);
            Mouse.Click(point);
            Thread.Sleep(150);
        }
        Keyboard.Type(text);
        return $"Typed {text.Length} character(s) into {where}.";
    }

    public string Key(string chord)
    {
        Keyboard.TypeSimultaneously(KeyChord.Parse(chord));
        return $"Pressed {chord}.";
    }

    /// <summary>Where to click for <paramref name="target"/>, with its window brought in front first — input goes to the front.</summary>
    private Point PointOf(ElementTarget target, out string described)
    {
        if (target.At is { } at)
        {
            described = "the screen";
            return at;
        }

        var (element, window) = Find(target);
        described = Describe(element);
        if (!UiTestGate.BringToForeground(window.Properties.NativeWindowHandle.ValueOrDefault, ForegroundTimeout))
            throw new SandboxException($"The window holding {target} would not come to the front, so the click would land elsewhere.");

        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    /// <summary>The first match on screen in any Nexaflow window — an off-screen match only when there is no other — and its window.</summary>
    private (AutomationElement Element, AutomationElement Window) Find(ElementTarget target)
    {
        var deadline = DateTime.UtcNow + FindTimeout;
        while (true)
        {
            (AutomationElement, AutomationElement)? offscreen = null;
            foreach (var window in AppWindows())
            {
                try
                {
                    var matches = target.AutomationId is { } id
                        ? window.FindAllDescendants(cf => cf.ByAutomationId(id))
                        : window.FindAllDescendants(cf => cf.ByName(target.Name!));
                    foreach (var match in matches)
                    {
                        if (!match.IsOffscreen) return (match, window);
                        offscreen ??= (match, window);
                    }
                }
                catch (COMException) { /* a window closing as it is searched */ }
            }
            if (offscreen is { } fallback) return fallback;
            if (DateTime.UtcNow > deadline)
                throw new SandboxException($"No Nexaflow window holds {target}. `tree` shows what there is.");
            Thread.Sleep(200);
        }
    }

    /// <summary>The top-level windows of every running Nexaflow, popups and menus included.</summary>
    private List<AutomationElement> AppWindows()
    {
        var processes = Process.GetProcessesByName(AppProcess);
        var ids = processes.Select(p => p.Id).ToHashSet();
        foreach (var process in processes) process.Dispose();
        if (ids.Count == 0) return [];

        return automation.GetDesktop().FindAllChildren()
            .Where(w => ids.Contains(w.Properties.ProcessId.ValueOrDefault))
            .ToList();
    }

    private static string Describe(AutomationElement element)
    {
        try
        {
            var properties = element.Properties;
            var line = new StringBuilder(properties.ControlType.ValueOrDefault.ToString());
            if (properties.Name.ValueOrDefault is { Length: > 0 } name) line.Append($" \"{name}\"");
            if (properties.AutomationId.ValueOrDefault is { Length: > 0 } id) line.Append($" #{id}");
            var bounds = properties.BoundingRectangle.ValueOrDefault;
            line.Append($" [{bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height}]");
            if (!properties.IsEnabled.ValueOrDefault) line.Append(" disabled");
            if (properties.IsOffscreen.ValueOrDefault) line.Append(" offscreen");
            return line.ToString();
        }
        catch (COMException)
        {
            return "(gone)";
        }
    }
}
