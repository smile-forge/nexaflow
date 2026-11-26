using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// What a journey saw at the moment one of its checks failed, kept so that one failure is enough to diagnose it — no rerun
/// to catch it again with better logging. Each capture is the window as it looked (<c>NN-label.png</c>) and a text account
/// (<c>NN-label.txt</c>): the failure, what had focus, and the whole UI Automation tree of the window in the raw view, the
/// view that holds what a search walks past as well as what it finds — collapsed sections, items without containers.
/// <para>
/// A control that was not found is then looked for a while longer, and the failure says which it was: one that turned up
/// late (the journey waited on the wrong thing, or too little) or one that never did (the app never exposed it). The tree
/// is taken before that look, so it is the tree the failed search saw.
/// </para>
/// </summary>
internal sealed class JourneyEvidence(AutomationBase automation, Func<Window> window, Func<string> folder)
{
    /// <summary>How many failures of one test are captured. Later ones are mostly consequences of the first, and a capture
    /// costs a second or two.</summary>
    public const int Captures = 5;

    /// <summary>How long a control that was not found is still looked for after its check gave up.</summary>
    public static readonly TimeSpan LateLook = TimeSpan.FromSeconds(10);

    /// <summary>Past this many elements the tree is cut, so a window listing a huge folder still writes a readable file.</summary>
    private const int MaxElements = 20_000;

    private int _taken;

    /// <summary>
    /// Captures the moment of <paramref name="failure"/> and returns what to add to its message: whether a control the check
    /// missed (<paramref name="missedId"/>) appeared later, and where the capture is. Empty once <see cref="Captures"/> are
    /// taken; a capture that fails says so rather than hiding the failure it was for.
    /// </summary>
    public string Capture(string label, string failure, string? missedId)
    {
        if (_taken >= Captures) return string.Empty;
        var name = $"{++_taken:00}-{Slug(label)}";
        try
        {
            var into = folder();
            var win = window();
            Directory.CreateDirectory(into);
            FlaUI.Core.Capturing.Capture.Rectangle(win.BoundingRectangle).ToFile(Path.Combine(into, name + ".png"));

            var account = new StringBuilder()
                .AppendLine($"Failure: {failure}")
                .AppendLine($"At:      {DateTime.Now:HH:mm:ss.fff}")
                .AppendLine($"Focus:   {Describe(() => automation.FocusedElement())}")
                .AppendLine($"Front:   {ForegroundWindow()}")
                .AppendLine();
            var tree = Tree(win);

            var late = missedId is null ? null : LookAgain(win, missedId);
            if (late is not null) account.Insert(0, $"Later:   {late}{Environment.NewLine}");
            File.WriteAllText(Path.Combine(into, name + ".txt"), account.Append(tree).ToString());

            return (late is null ? string.Empty : $" [{late}]") + $" (evidence: {Path.Combine(into, name)}.png/.txt)";
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $" (no evidence: capturing it threw {ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>Whether <paramref name="automationId"/> turns up within <see cref="LateLook"/>, and when.</summary>
    private static string LookAgain(Window win, string automationId)
    {
        var since = Stopwatch.StartNew();
        while (since.Elapsed < LateLook)
        {
            try
            {
                if (win.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null)
                    return $"'{automationId}' appeared {since.Elapsed.TotalSeconds:F1} s after the check gave up";
            }
            catch (COMException) { }
            Thread.Sleep(100);
        }
        return $"'{automationId}' was still absent {LateLook.TotalSeconds:0} s after the check gave up";
    }

    /// <summary>The window's raw-view tree, fetched in one cached call rather than one cross-process call per property.</summary>
    private string Tree(Window win)
    {
        var library = automation.PropertyLibrary.Element;
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = TrueCondition.Default,
            AutomationElementMode = AutomationElementMode.None,
        };
        request.Add(library.ControlType);
        request.Add(library.Name);
        request.Add(library.AutomationId);
        request.Add(library.BoundingRectangle);
        request.Add(library.IsOffscreen);
        request.Add(library.IsEnabled);

        var text = new StringBuilder();
        var written = 0;
        using (request.Activate())
        {
            if (win.FindFirst(TreeScope.Element, TrueCondition.Default) is { } root) Write(root, 0);
        }
        if (written >= MaxElements) text.AppendLine($"… cut at {MaxElements} elements");
        return text.ToString();

        void Write(AutomationElement element, int depth)
        {
            if (written++ >= MaxElements) return;
            var p = element.Properties;
            text.Append(' ', depth * 2).Append(p.ControlType.ValueOrDefault);
            if (p.Name.ValueOrDefault is { Length: > 0 } n) text.Append(" \"").Append(n.Length > 80 ? n[..80] + "…" : n).Append('"');
            if (p.AutomationId.ValueOrDefault is { Length: > 0 } id) text.Append(" #").Append(id);
            var r = p.BoundingRectangle.ValueOrDefault;
            text.Append($" [{r.X},{r.Y} {r.Width}x{r.Height}]");
            if (p.IsOffscreen.ValueOrDefault) text.Append(" offscreen");
            if (!p.IsEnabled.ValueOrDefault) text.Append(" disabled");
            text.AppendLine();
            foreach (var child in element.CachedChildren) Write(child, depth + 1);
        }
    }

    private static string Describe(Func<AutomationElement> find)
    {
        try
        {
            var e = find();
            return $"{e.Properties.ControlType.ValueOrDefault} \"{e.Properties.Name.ValueOrDefault}\" #{e.Properties.AutomationId.ValueOrDefault}";
        }
        catch (COMException ex) { return $"unreadable ({ex.Message})"; }
    }

    private static string ForegroundWindow()
    {
        var handle = GetForegroundWindow();
        var title = new StringBuilder(256);
        _ = GetWindowText(handle, title, title.Capacity);
        _ = GetWindowThreadProcessId(handle, out var pid);
        string process;
        try { process = Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { process = "gone"; }
        return $"\"{title}\" ({process}, pid {pid})";
    }

    private static string Slug(string label)
    {
        var slug = new string(label.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length > 48 ? slug[..48].TrimEnd('-') : slug;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

