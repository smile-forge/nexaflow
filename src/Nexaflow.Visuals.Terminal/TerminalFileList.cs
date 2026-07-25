using System.IO;
using Nexaflow.Visuals.Terminal.Models;

namespace Nexaflow.Visuals.Terminal;

/// <summary>
/// What the terminal's Files panel lists for a directory: files first, then folders, each alphabetical.
/// <para>
/// The order is the point and it is deliberately not Explorer's. The panel exists mainly to drag a path
/// onto the console or the AI bar, and it is a file you nearly always want — putting the folders first
/// would bury them. Kept out of the view-model so the rule is one testable call rather than a loop wrapped
/// around a live shell's current directory.
/// </para>
/// </summary>
public static class TerminalFileList
{
    /// <summary>Lists <paramref name="path"/>. An unreadable or missing directory lists as empty rather
    /// than throwing — the panel sits beside a live shell that can be anywhere, including somewhere it has
    /// just been denied.</summary>
    public static IReadOnlyList<TerminalFsEntry> Enumerate(string? path)
    {
        var entries = new List<TerminalFsEntry>();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return entries;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                entries.Add(new TerminalFsEntry(file, isDirectory: false));
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                entries.Add(new TerminalFsEntry(dir, isDirectory: true));
        }
        catch { /* access denied / transient — keep whatever was enumerated */ }

        return entries;
    }
}
