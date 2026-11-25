using System.Runtime.InteropServices;
using System.Text;

namespace Nexaflow.Session.Services;

/// <summary>The other applications' windows open on the desktop, as Windows lists them.</summary>
public static class DesktopWindows
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// Titles of visible, titled top-level windows (de-duplicated, capped at <paramref name="cap"/>): which other applications
    /// the person has open, as the assistant is told when allowed. Best-effort — returns what it can read.
    /// </summary>
    public static IReadOnlyList<string> VisibleTitles(int cap = 100)
    {
        var titles = new List<string>();
        var seen   = new HashSet<string>(StringComparer.Ordinal);

        EnumWindows((hWnd, _) =>
        {
            if (titles.Count >= cap) return false;          // stop enumerating
            if (!IsWindowVisible(hWnd)) return true;

            var len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (!string.IsNullOrWhiteSpace(title) && seen.Add(title))
                titles.Add(title);
            return true;
        }, IntPtr.Zero);

        return titles;
    }
}
